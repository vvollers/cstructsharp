namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;
using CStructSharp.Introspection;
using CStructSharp.Values;

/// <summary>
///     One layout and input of a corpus the differential harness runs over (<see cref="EngineCorpora"/>): how to
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
    private static readonly ExecutionPath[] CorpusPaths = [ExecutionPath.Fastest, ExecutionPath.GeneralOnly];

    /// <summary>
    ///     Runs the case through the harness under <see cref="ExecutionPath.Fastest"/> and
    ///     <see cref="ExecutionPath.GeneralOnly"/>: the root is read from memory, a multi-segment sequence, and streams
    ///     (the sources must agree with each other), read as a value, debug-parsed, its update layout captured, and a few of
    ///     its paths resolved;
    ///     when the interpreter reads a value, that value is written back to a new array, a span of the input's
    ///     length, a stream and a buffer writer with small windows, and each selected member's value is written on its own
    ///     through its path to a stream and a new array.
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
        object? value = Attempt(() => layout.ReadValue(this.Data, root, this.Variables, EngineSelections.InterpreterOnly(read)));
        string[] addresses = [.. this.DebugPaths(layout, root, read), .. this.Paths ?? []];
        EngineInput[] sources = detailed
                                    ? [EngineInput.Span, EngineInput.Sequence, EngineInput.Stream, EngineInput.ChunkedStream1, EngineInput.ChunkedStream7]
                                    : [EngineInput.Span, EngineInput.Sequence, EngineInput.Stream, EngineInput.ChunkedStream7];
        if (detailed)
        {
            // The layout an update compares reads with every fast path off, so one capture covers both execution paths.
            EngineLayoutCapture.AssertSame(this.Id, layout, this.Data, EngineInput.ChunkedStream3, root, this.Variables, read);
        }

        foreach (ExecutionPath path in CorpusPaths)
        {
            var renderings = new List<(EngineInput Input, string Rendering)>();
            foreach (EngineInput input in sources)
            {
                DifferentialOperation operation = union
                                                      ? EngineOperations.ReadValue(layout, this.Data, input, root, this.Variables, read)
                                                      : EngineOperations.Parse(layout, this.Data, input, root, this.Variables, read);
                renderings.Add((input, Same(operation, path)));
            }

            EngineAgreement.AssertSourcesAgree(this.Id + " (" + path + ")", renderings);
            Same(EngineOperations.ReadValue(layout, this.Data, EngineInput.Memory, root, this.Variables, read), path);
            if (detailed)
            {
                Same(EngineOperations.ParseWithDebug(layout, this.Data, EngineInput.Span, root, this.Variables, read), path);
                Same(EngineOperations.ReadValueWithDebug(layout, this.Data, EngineInput.ChunkedStream3, root, this.Variables, read), path);
            }

            foreach (string address in addresses)
            {
                Same(EngineOperations.ResolveAddress(layout, this.Data, EngineInput.Span, address, this.Variables, read), path);
            }

            foreach (string selected in this.Paths ?? [])
            {
                Same(EngineOperations.ReadValue(layout, this.Data, EngineInput.Stream, selected, this.Variables, read), path);
                Same(EngineOperations.GetArrayLength(layout, this.Data, EngineInput.Span, selected, this.Variables, read), path);
            }

            if (value is not null)
            {
                Same(EngineOperations.Serialize(layout, root, value, this.Variables, this.Write), path);
                Same(EngineOperations.SerializeToSpan(layout, this.Data.Length, root, value, this.Variables, this.Write), path);
                Same(EngineOperations.Write(layout, [0xAA, 0xAA], 1, root, value, this.Variables, this.Write), path);
                Same(EngineOperations.SerializeToWindows(layout, 3, root, value, this.Variables, this.Write), path);

                // Each selected member written on its own from the value the read selects there.
                foreach (string selected in this.Paths ?? [])
                {
                    if (Attempt(() => layout.ReadValue(this.Data, selected, this.Variables, EngineSelections.InterpreterOnly(read))) is { } member)
                    {
                        Same(EngineOperations.Write(layout, [0xAA, 0xAA], 1, selected, member, this.Variables, this.Write), path);
                        Same(EngineOperations.Serialize(layout, selected, member, this.Variables, this.Write), path);
                    }
                }
            }
        }

        return true;
    }

    /// <summary>Returns the case's identifier, which test data rows display.</summary>
    /// <returns>The identifier.</returns>
    public override string ToString() => this.Id;

    /// <summary>
    ///     Compares one operation through the harness, requiring the engine to run it exactly when the operation expects
    ///     it: a whole-root read of a root whose program is eligible (the eligibility report's property,
    ///     <see cref="EngineExpectations"/>).
    /// </summary>
    /// <param name="operation">The operation.</param>
    /// <param name="path">The execution path both sides use.</param>
    /// <returns>The shared rendering.</returns>
    private static string Same(DifferentialOperation operation, ExecutionPath path) => EngineDifferential.AssertSame(operation, path: path).Rendering;

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
    ///     The paths of the first, middle, and last debug records of the interpreter's debug parse of the root, which
    ///     the run resolves as addresses; none when that parse fails.
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

        var records = Attempt(() => layout.ReadValueWithDebug(this.Data, root, this.Variables, EngineSelections.InterpreterOnly(read))) as ReadResult;
        if (records is null || records.Debug.Count == 0)
        {
            return [];
        }

        IReadOnlyList<DebugData> debug = records.Debug;
        return new[] { debug[0].Path, debug[debug.Count / 2].Path, debug[^1].Path, }.Distinct(StringComparer.Ordinal).ToArray();
    }
}
