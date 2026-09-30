namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;
using CStructSharp.Introspection;
using CStructSharp.Values;

/// <summary>
///     One layout and input of a corpus the golden harness runs over (<see cref="EngineCorpora"/>): how to
///     compile the layout, which composite to read, the input, and the options the corpus prescribes.
/// </summary>
/// <param name="Id">The case's identifier within its corpus.</param>
/// <param name="Compile">Compiles the layout; a <see cref="CStructLayoutException"/> means the case has nothing to read.</param>
/// <param name="Root">The composite or path to read, or <see langword="null"/> for the first struct or union.</param>
/// <param name="Data">The input bytes.</param>
/// <param name="Variables">The caller variables, or <see langword="null"/> for none.</param>
/// <param name="Read">The read options, or <see langword="null"/> for the defaults.</param>
/// <param name="Write">The write options, or <see langword="null"/> for the defaults.</param>
/// <param name="Paths">Further paths to read, resolve, and measure, such as a fuzzed path.</param>
internal sealed record EngineCorpusCase(
    string Id,
    Func<CStruct> Compile,
    string? Root,
    byte[] Data,
    IReadOnlyDictionary<string, int>? Variables = null,
    ReadOptions? Read = null,
    WriteOptions? Write = null,
    string[]? Paths = null)
{
    /// <summary>The largest input whose debug parse and one-byte-chunked stream read the corpus run includes.</summary>
    private const int DetailedInputLimit = 16 * 1024;

    /// <summary>The execution paths every corpus case runs under.</summary>
    private static readonly ExecutionPath[] CorpusPaths = [ExecutionPath.Fastest, ExecutionPath.NoFastPaths];

    /// <summary>
    ///     Runs the case through the harness under <see cref="ExecutionPath.Fastest"/> and
    ///     <see cref="ExecutionPath.NoFastPaths"/>: the root is read from memory, a multi-segment sequence, and streams
    ///     (the sources must agree with each other), read as a value, debug-parsed, its update layout captured, and a few of
    ///     its paths resolved; when a read gives a value, that value is written back to a new array, a span of
    ///     the input's length, a stream and a buffer writer with small windows, and each selected member's value is written
    ///     on its own through its path to a stream and a new array; the root and every member a debug record or selected path
    ///     names are updated in place with the value they hold, in a span and (for the selected paths) a stream.
    /// </summary>
    /// <returns>Whether the layout compiled and had a composite to read.</returns>
    public bool Run()
    {
        CStruct layout;
        try
        {
            layout = this.Compile();
        }
        catch (CStructLayoutException)
        {
            return false;
        }

        LayoutDeclarationInfo? declaration = this.Root is null
                                                 ? layout.Layout.Declarations.FirstOrDefault(item => item.Kind is LayoutDeclarationKind.Struct or LayoutDeclarationKind.Union)
                                                 : layout.Layout.Declarations.FirstOrDefault(item => item.Name == this.Root);
        string? root = this.Root ?? declaration?.Name;
        if (root is null)
        {
            return false;
        }

        // A union root has no Parse; its sources are compared through ReadValue instead.
        bool union = declaration?.Kind == LayoutDeclarationKind.Union;
        ReadOptions read = this.Read ?? new ReadOptions();
        bool detailed = this.Data.Length <= DetailedInputLimit;
        object? value = Attempt(() => layout.ReadValue(this.Data, root, this.Variables, read));
        string[] addresses = [.. this.DebugPaths(layout, root, read), .. this.Paths ?? []];
        EngineInput[] sources = detailed
                                    ? [EngineInput.Span, EngineInput.Sequence, EngineInput.Stream, EngineInput.ChunkedStream1, EngineInput.ChunkedStream7]
                                    : [EngineInput.Span, EngineInput.Sequence, EngineInput.Stream, EngineInput.ChunkedStream7];
        if (detailed)
        {
            // The layout an update compares reads with every fast path off, so one capture covers both execution paths.
            EngineLayoutCapture.AssertGolden(this.Id, layout, this.Data, EngineInput.ChunkedStream3, root, this.Variables, read);
        }

        foreach (ExecutionPath path in CorpusPaths)
        {
            var renderings = new List<(EngineInput Input, string Rendering)>();
            foreach (EngineInput input in sources)
            {
                GoldenOperation operation = union
                                                      ? EngineOperations.ReadValue(layout, this.Data, input, root, this.Variables, read)
                                                      : EngineOperations.Parse(layout, this.Data, input, root, this.Variables, read);
                renderings.Add((input, Golden(operation, path)));
            }

            EngineAgreement.AssertSourcesAgree(this.Id + " (" + path + ")", renderings);
            Golden(EngineOperations.ReadValue(layout, this.Data, EngineInput.Memory, root, this.Variables, read), path);
            if (detailed)
            {
                Golden(EngineOperations.ParseWithDebug(layout, this.Data, EngineInput.Span, root, this.Variables, read), path);
                Golden(EngineOperations.ReadValueWithDebug(layout, this.Data, EngineInput.ChunkedStream3, root, this.Variables, read), path);
            }

            foreach (string address in addresses)
            {
                Golden(EngineOperations.ResolveAddress(layout, this.Data, EngineInput.Span, address, this.Variables, read), path);
            }

            foreach (string selected in this.Paths ?? [])
            {
                Golden(EngineOperations.ReadValue(layout, this.Data, EngineInput.Stream, selected, this.Variables, read), path);
                Golden(EngineOperations.GetArrayLength(layout, this.Data, EngineInput.Span, selected, this.Variables, read), path);
            }

            if (value is not null)
            {
                Golden(EngineOperations.Serialize(layout, root, value, this.Variables, this.Write), path);
                Golden(EngineOperations.SerializeToSpan(layout, this.Data.Length, root, value, this.Variables, this.Write), path);
                Golden(EngineOperations.Write(layout, [0xAA, 0xAA], 1, root, value, this.Variables, this.Write), path);
                Golden(EngineOperations.SerializeToWindows(layout, 3, root, value, this.Variables, this.Write), path);

                // Each selected member written on its own from the value the read selects there.
                foreach (string selected in this.Paths ?? [])
                {
                    if (Attempt(() => layout.ReadValue(this.Data, selected, this.Variables, read)) is { } member)
                    {
                        Golden(EngineOperations.Write(layout, [0xAA, 0xAA], 1, selected, member, this.Variables, this.Write), path);
                        Golden(EngineOperations.Serialize(layout, selected, member, this.Variables, this.Write), path);
                        Golden(EngineOperations.Update(layout, this.Data, EngineInput.Stream, selected, member, this.Variables), path);
                    }
                }

                // Updates in place, with the value each target holds: the root, and every member a debug record names.
                if (detailed)
                {
                    Golden(EngineOperations.Update(layout, this.Data, EngineInput.Span, root, value, this.Variables), path);
                    foreach (string address in addresses)
                    {
                        if (Attempt(() => layout.ReadValue(this.Data, address, this.Variables, read)) is { } member)
                        {
                            Golden(EngineOperations.Update(layout, this.Data, EngineInput.Span, address, member, this.Variables), path);
                        }
                    }
                }
            }
        }

        return true;
    }

    /// <summary>
    ///     Runs every public read, write and update of the case and returns how many ran: a whole root read, debug parse and
    ///     record sequence from memory and a stream, a selected read, address and length of each path, the root written to
    ///     every destination, and each path written and updated with the value it holds. Every root the corpora declare
    ///     compiles into the engine's read and write programs, and every operation - a root spelled at run time whose count
    ///     names a caller-only variable included - reaches the engine once and either succeeds or fails with the library's
    ///     own failure, never because the engine has no program for what it runs.
    /// </summary>
    /// <returns>The number of operations run, 0 when the layout does not compile or has nothing to read.</returns>
    /// <exception cref="AssertFailedException">A root has no program, or an operation found no program to run.</exception>
    public int RunEveryOperation()
    {
        CStruct layout;
        try
        {
            layout = this.Compile();
        }
        catch (CStructLayoutException)
        {
            return 0;
        }

        LayoutDeclarationInfo? declaration = this.Root is null
                                                 ? layout.Layout.Declarations.FirstOrDefault(item => item.Kind is LayoutDeclarationKind.Struct or LayoutDeclarationKind.Union)
                                                 : layout.Layout.Declarations.FirstOrDefault(item => item.Name == this.Root);
        string? root = this.Root ?? declaration?.Name;
        if (root is null)
        {
            return 0;
        }

        // A root spelled at run time is registered when its path is first parsed; a root the layout cannot declare has
        // nothing to run.
        _ = Attempt(() => layout.ParsePath(root));
        if (!layout.Compilation.ModelQueries.TryGetCompiledDeclaration(root, out _))
        {
            return 0;
        }

        // Every root the corpora declare compiles into the engine's programs.
        Assert.IsNotNull(layout.Compilation.GetRootReadProgram(root), this.Id + ": the engine has no program for " + root);
        ReadOptions read = this.Read ?? new ReadOptions();
        byte[] data = this.Data;
        IReadOnlyDictionary<string, int>? variables = this.Variables;
        int ran = 0;

        // Runs one operation; it must not fail because the engine found no program, and a whole-root or path operation that
        // the direct fixed-root path does not take reaches the engine.
        void Required(string label, Action operation)
        {
            ran++;
            try
            {
                operation();
            }
            catch (InvalidOperationException missing) when (missing.Message.StartsWith("The compiled engine", StringComparison.Ordinal))
            {
                Assert.Fail(this.Id + " " + label + ": " + missing.Message);
            }
            catch (Exception exception) when (exception is not UnitTestAssertException)
            {
                // The operation fails as the golden outcomes of Run record; only a missing program matters here.
            }
        }

        bool detailed = data.Length <= DetailedInputLimit;
        string[] addresses = [.. this.DebugPaths(layout, root, read), .. this.Paths ?? []];
        Required("Parse", () => layout.Parse(data.AsSpan(), root, variables, read));
        Required("Parse(Stream)", () => layout.Parse(new MemoryStream(data), root, variables, read));
        Required("ParseAsync", () => layout.ParseAsync(new MemoryStream(data), root, variables, read).AsTask().GetAwaiter().GetResult());
        Required("ReadValue", () => layout.ReadValue(data, root, variables, read));
        Required("ParseMany", () => layout.ParseMany(data.AsMemory(), root, variables, read).Take(4).ToList());
        if (detailed)
        {
            Required("ParseWithDebug", () => layout.ParseWithDebug(data, root, variables, read));
            Required("ReadValueWithDebug(Stream)", () => layout.ReadValueWithDebug(new MemoryStream(data), root, variables, read));
        }

        foreach (string address in addresses)
        {
            Required("ReadValue " + address, () => layout.ReadValue(data, address, variables, read));
            Required("ResolveAddress " + address, () => layout.ResolveAddress(new MemoryStream(data), address, variables, read));
            Required("GetArrayLength " + address, () => layout.GetArrayLength(data, address, variables, read));
        }

        // A root spelled at run time whose count names a caller variable no expression of the layout reads.
        var callerOnly = new Dictionary<string, int> { ["ZZ_CALLER_COUNT"] = 2, };
        foreach ((string spelled, object elements) in (ValueTuple<string, object>[])[("uint8[ZZ_CALLER_COUNT]", new byte[] { 1, 2, }), ("uint16[ZZ_CALLER_COUNT]", new ushort[] { 3, 4, })])
        {
            Required("ReadValue " + spelled, () => layout.ReadValue(data, spelled, callerOnly, read));
            Required("ReadValueWithDebug " + spelled, () => layout.ReadValueWithDebug(data, spelled, callerOnly, read));
            Required("GetArrayLength " + spelled, () => layout.GetArrayLength(data, spelled, callerOnly, read));
            Required("Serialize " + spelled, () => layout.Serialize(spelled, elements, callerOnly, this.Write));
            Required("Write " + spelled, () => layout.Write(new MemoryStream(), spelled, elements, callerOnly, this.Write));
            Required("Update " + spelled, () => layout.Update((byte[])data.Clone(), spelled, elements, callerOnly, new UpdateOptions()));
        }

        object? value = Attempt(() => layout.ReadValue(data, root, variables, read));
        if (value is null)
        {
            return ran;
        }

        Assert.IsNull(layout.Compilation.GetRootWriteProgram(root).Reason, this.Id + ": the engine cannot write " + root);

        Required("Serialize", () => layout.Serialize(root, value, variables, this.Write));
        Required("Serialize(Span)", () => layout.Serialize(new byte[data.Length + 8].AsSpan(), root, value, variables, this.Write));
        Required("Serialize(IBufferWriter)", () => layout.Serialize(new System.Buffers.ArrayBufferWriter<byte>(), root, value, variables, this.Write));
        Required("Write", () => layout.Write(new MemoryStream(), root, value, variables, this.Write));
        Required("WriteAsync", () => layout.WriteAsync(new MemoryStream(), root, value, variables, this.Write).AsTask().GetAwaiter().GetResult());
        Required("Write(UpdateOptions)", () => layout.Write(new MemoryStream(), root, value, variables, new UpdateOptions()));
        Required("Update", () => layout.Update((byte[])data.Clone(), root, value, variables, new UpdateOptions()));
        Required("UpdateAsync", () => layout.UpdateAsync(new MemoryStream((byte[])data.Clone()), root, value, variables, new UpdateOptions()).AsTask().GetAwaiter().GetResult());
        foreach (string address in addresses)
        {
            if (Attempt(() => layout.ReadValue(data, address, variables, read)) is not { } member)
            {
                continue;
            }

            Required("Write " + address, () => layout.Write(new MemoryStream(), address, member, variables, this.Write));
            Required("Update " + address, () => layout.Update((byte[])data.Clone(), address, member, variables, new UpdateOptions()));
            Required("Update(Stream) " + address, () => layout.Update(new MemoryStream((byte[])data.Clone()), address, member, variables, new UpdateOptions { ClearUnionStorage = false, }));
        }

        return ran;
    }

    /// <summary>Returns the case's identifier, which test data rows display.</summary>
    /// <returns>The identifier.</returns>
    public override string ToString() => this.Id;

    /// <summary>Checks one operation's outcome against its golden outcome through the harness.</summary>
    /// <param name="operation">The operation.</param>
    /// <param name="path">The execution path the run uses.</param>
    /// <returns>The rendering, for the source-agreement check.</returns>
    private static string Golden(GoldenOperation operation, ExecutionPath path) => EngineDifferential.AssertGolden(operation, path: path).Rendering;

    /// <summary>Returns the result of <paramref name="call"/>, or <see langword="null"/> when it throws.</summary>
    /// <param name="call">The call.</param>
    /// <returns>The result or <see langword="null"/>.</returns>
    private static object? Attempt(Func<object?> call)
    {
        try
        {
            return call();
        }
        catch (Exception exception) when (exception is not UnitTestAssertException)
        {
            return null;
        }
    }

    /// <summary>
    ///     The paths of the first, middle, and last debug records of the debug parse of the
    ///     root, which the run resolves as addresses; none when that parse fails.
    /// </summary>
    /// <param name="layout">The compiled layout.</param>
    /// <param name="root">The root to parse.</param>
    /// <param name="read">The case's read options.</param>
    /// <returns>Up to three distinct paths.</returns>
    private string[] DebugPaths(CStruct layout, string root, ReadOptions read)
    {
        if (this.Data.Length > DetailedInputLimit)
        {
            return [];
        }

        var records = Attempt(() => layout.ReadValueWithDebug(this.Data, root, this.Variables, read)) as ReadResult;
        if (records is null || records.Debug.Count == 0)
        {
            return [];
        }

        IReadOnlyList<DebugData> debug = records.Debug;
        return new[] { debug[0].Path, debug[debug.Count / 2].Path, debug[^1].Path, }.Distinct(StringComparer.Ordinal).ToArray();
    }
}
