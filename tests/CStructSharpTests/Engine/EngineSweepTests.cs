namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;
using CStructSharp.Values;
using SweepLayout = EngineSweepLayouts.SweepLayout;
using Variant = EngineSweepLayouts.Variant;

/// <summary>
///     Sweeps the differential harness over the representative layouts of <see cref="EngineSweepLayouts"/>, packed and
///     aligned, under <see cref="ExecutionPath.Fastest"/> and <see cref="ExecutionPath.GeneralOnly"/> (the path the
///     compiled engine replaces): every truncation of the input, every byte budget up to the operation's natural total,
///     every other limit around the value the input needs, the read options that change decoding, caller variables,
///     every input source, and every destination. Each comparison runs the interpreter against automatic engine
///     selection; the sources must also agree with each other, which checks the interpreter against itself.
/// </summary>
/// <remarks>
///     Every sweep is exhaustive over its range (no sampling). The ranges are small because the inputs are: an element,
///     string or pointer-target limit sweep covers every value from 0 to the input length plus two, and a nesting or
///     pointer-depth sweep every value from 0 to 4; each range includes the needed value minus one, the needed value,
///     and one more, since no layout needs more elements or bytes than its input length, or more than three levels.
/// </remarks>
[TestClass]
public class EngineSweepTests
{
    /// <summary>The execution paths every sweep runs under.</summary>
    private static readonly ExecutionPath[] SweepPaths = [ExecutionPath.Fastest, ExecutionPath.GeneralOnly];

    /// <summary>The memory forms a read sweep runs over, before the stream forms.</summary>
    private static readonly EngineInput[] MemoryInputs = [EngineInput.Span, EngineInput.ByteArray, EngineInput.Memory, EngineInput.Sequence];

    /// <summary>Gets the sweep layout names as data rows.</summary>
    public static IEnumerable<object[]> Layouts => EngineSweepLayouts.Names;

    /// <summary>
    ///     Every prefix of the input, from empty to complete, reads identically through every source: span, array,
    ///     memory, multi-segment sequence, hidden and exposed memory streams, 1-, 3- and 7-byte chunked streams, a file,
    ///     and asynchronously; the debug parse, a selected value, an address, and an array length agree too.
    /// </summary>
    /// <param name="name">The sweep layout.</param>
    [TestMethod]
    [DynamicData(nameof(Layouts))]
    public void Truncations_ReadIdenticallyFromEverySource(string name)
    {
        foreach (Variant variant in EngineSweepLayouts.Both(name))
        {
            SweepLayout source = variant.Source;
            for (int length = 0; length <= variant.Data.Length; length++)
            {
                byte[] prefix = variant.Data[..length];
                foreach (ExecutionPath path in SweepPaths)
                {
                    var renderings = new List<(EngineInput Input, string Rendering)>();
                    foreach (EngineInput input in MemoryInputs.Concat(EngineStreams.All))
                    {
                        renderings.Add((input, Same(EngineOperations.Parse(variant.Layout, prefix, input, "rec", source.Variables, variant.BaseRead(input)), path)));
                    }

                    EngineAgreement.AssertSourcesAgree(variant.Name + " length " + length + " (" + path + ")", renderings);

                    Same(EngineOperations.ParseAsync(variant.Layout, prefix, EngineInput.Stream, "rec", source.Variables, variant.BaseRead()), path);
                    Same(EngineOperations.ParseAsync(variant.Layout, prefix, EngineInput.ChunkedStream3, "rec", source.Variables, variant.BaseRead()), path);
                    Same(EngineOperations.ParseWithDebug(variant.Layout, prefix, EngineInput.Span, "rec", source.Variables, variant.BaseRead()), path);
                    Same(EngineOperations.ParseWithDebug(variant.Layout, prefix, EngineInput.ChunkedStream1, "rec", source.Variables, variant.BaseRead()), path);
                    Same(EngineOperations.ReadValue(variant.Layout, prefix, EngineInput.Span, source.Paths[0], source.Variables, variant.BaseRead()), path);
                    Same(EngineOperations.ReadValue(variant.Layout, prefix, EngineInput.Stream, source.Paths[0], source.Variables, variant.BaseRead()), path);
                    Same(EngineOperations.ResolveAddress(variant.Layout, prefix, EngineInput.Span, source.Paths[^1], source.Variables, variant.BaseRead()), path);
                    Same(EngineOperations.ResolveAddress(variant.Layout, prefix, EngineInput.ChunkedStream3, source.Paths[^1], source.Variables, variant.BaseRead()), path);
                    if (source.ArrayPath is not null)
                    {
                        Same(EngineOperations.GetArrayLength(variant.Layout, prefix, EngineInput.Span, source.ArrayPath, source.Variables, variant.BaseRead()), path);
                    }
                }
            }
        }
    }

    /// <summary>
    ///     Every <see cref="ReadOptions.MaxTotalBytesRead"/> from 1 to one past the parse's natural total,
    ///     <see cref="WriteOptions.MaxTotalBytesWritten"/> from 1 to one past the write's, and every update's write
    ///     budget and <see cref="UpdateOptions.MaxTraversalBytesRead"/> fail or succeed identically.
    /// </summary>
    /// <param name="name">The sweep layout.</param>
    [TestMethod]
    [DynamicData(nameof(Layouts))]
    public void ByteBudgets_FailIdenticallyAtEveryLimit(string name)
    {
        foreach (Variant variant in EngineSweepLayouts.Both(name))
        {
            SweepLayout source = variant.Source;
            byte[] data = variant.Data;
            IReadOnlyDictionary<string, int>? variables = source.Variables;
            int readTotal = Smallest(budget => Succeeds(() => variant.Layout.Parse(data.AsSpan(), "rec", variables, Interpreter(variant.BaseRead() with { MaxTotalBytesRead = budget, }))), (16 * data.Length) + 64);
            int writeTotal = Smallest(budget => Succeeds(() => variant.Layout.Serialize("rec", variant.Value, variables, Interpreter(new WriteOptions { MaxTotalBytesWritten = budget, }))), (16 * data.Length) + 64);
            foreach (ExecutionPath path in SweepPaths)
            {
                for (long budget = 1; budget <= readTotal + 1; budget++)
                {
                    ReadOptions read = variant.BaseRead() with { MaxTotalBytesRead = budget, };
                    Same(EngineOperations.Parse(variant.Layout, data, EngineInput.Span, "rec", variables, read), path);
                    Same(EngineOperations.Parse(variant.Layout, data, EngineInput.Stream, "rec", variables, read), path);
                    Same(EngineOperations.Parse(variant.Layout, data, EngineInput.ChunkedStream3, "rec", variables, read), path);
                    Same(EngineOperations.Parse(variant.Layout, data, EngineInput.Sequence, "rec", variables, read), path);
                    Same(EngineOperations.ParseWithDebug(variant.Layout, data, EngineInput.Span, "rec", variables, read), path);
                    Same(EngineOperations.ReadValue(variant.Layout, data, EngineInput.Span, source.Paths[0], variables, read), path);
                    Same(EngineOperations.ResolveAddress(variant.Layout, data, EngineInput.Span, source.Paths[^1], variables, read), path);
                }

                for (long budget = 1; budget <= writeTotal + 1; budget++)
                {
                    var write = new WriteOptions { MaxTotalBytesWritten = budget, };
                    Same(EngineOperations.Serialize(variant.Layout, "rec", variant.Value, variables, write), path);
                    Same(EngineOperations.SerializeToSpan(variant.Layout, data.Length, "rec", variant.Value, variables, write), path);
                    Same(EngineOperations.Write(variant.Layout, new byte[4], 1, "rec", variant.Value, variables, write), path);
                    Same(EngineOperations.SerializeToWindows(variant.Layout, 3, "rec", variant.Value, variables, write), path);
                }

                foreach ((string target, object value) in source.Updates)
                {
                    for (long budget = 1; budget <= readTotal + 1; budget++)
                    {
                        var update = new UpdateOptions { MaxTotalBytesWritten = budget, MaxTraversalBytesRead = budget, };
                        Same(EngineOperations.Update(variant.Layout, data, EngineInput.Span, target, value, variables, update), path);
                        Same(EngineOperations.Update(variant.Layout, data, EngineInput.Stream, target, value, variables, update), path);
                        Same(EngineOperations.Update(variant.Layout, data, EngineInput.Span, target, value, variables, new UpdateOptions { MaxTotalBytesWritten = budget, }), path);
                        Same(EngineOperations.Update(variant.Layout, data, EngineInput.Span, target, value, variables, new UpdateOptions { MaxTraversalBytesRead = budget, }), path);
                    }
                }
            }
        }
    }

    /// <summary>
    ///     Every element, string, nesting, pointer-depth and pointer-target limit - read, write, and update traversal -
    ///     from 0 (an invalid limit) to the input length plus two fails or succeeds identically.
    /// </summary>
    /// <param name="name">The sweep layout.</param>
    [TestMethod]
    [DynamicData(nameof(Layouts))]
    public void Limits_FailIdenticallyAroundTheNeededValue(string name)
    {
        foreach (Variant variant in EngineSweepLayouts.Both(name))
        {
            SweepLayout source = variant.Source;
            byte[] data = variant.Data;
            IReadOnlyDictionary<string, int>? variables = source.Variables;
            (string target, object replacement) = source.Updates[0];
            int top = data.Length + 2;
            foreach (ExecutionPath path in SweepPaths)
            {
                // Runs the reads, the write, and the update of one set of limits.
                void Compare(ReadOptions read, WriteOptions? write, UpdateOptions? update)
                {
                    Same(EngineOperations.Parse(variant.Layout, data, EngineInput.Span, "rec", variables, read), path);
                    Same(EngineOperations.Parse(variant.Layout, data, EngineInput.ChunkedStream3, "rec", variables, read), path);
                    Same(EngineOperations.ReadValue(variant.Layout, data, EngineInput.Span, source.Paths[0], variables, read), path);
                    Same(EngineOperations.ResolveAddress(variant.Layout, data, EngineInput.Span, source.Paths[^1], variables, read), path);
                    if (write is not null)
                    {
                        Same(EngineOperations.Serialize(variant.Layout, "rec", variant.Value, variables, write), path);
                    }

                    if (update is not null)
                    {
                        Same(EngineOperations.Update(variant.Layout, data, EngineInput.Span, target, replacement, variables, update), path);
                    }
                }

                ReadOptions baseRead = variant.BaseRead();
                for (int limit = 0; limit <= top; limit++)
                {
                    Compare(baseRead with { MaxArrayElements = limit, }, new WriteOptions { MaxArrayElements = limit, }, new UpdateOptions { MaxTraversalArrayElements = limit, });
                    Compare(baseRead with { MaxStringBytes = limit, }, new WriteOptions { MaxStringBytes = limit, }, new UpdateOptions { MaxTraversalStringBytes = limit, });
                    Compare(baseRead with { MaxPointerTargetBytes = limit, }, null, new UpdateOptions { MaxTraversalPointerTargetBytes = limit, });
                    Compare(baseRead, new WriteOptions { MaxStringBytes = limit, MaxArrayElements = Math.Max(limit, 1), }, new UpdateOptions { MaxStringBytes = limit, MaxArrayElements = limit, });
                }

                for (int depth = 0; depth <= 4; depth++)
                {
                    Compare(baseRead with { MaxNestingDepth = depth, }, new WriteOptions { MaxNestingDepth = depth, }, new UpdateOptions { MaxTraversalNestingDepth = depth, });
                    Compare(baseRead with { MaxPointerDepth = depth, }, null, new UpdateOptions { MaxTraversalPointerDepth = depth, });
                    Compare(baseRead, null, new UpdateOptions { MaxNestingDepth = depth, });
                }
            }
        }
    }

    /// <summary>
    ///     A selected read of every sweep path returns the value the parse holds at that path, and the length of the
    ///     sweep's array is the element count the parse read, on the fast and general paths. The selected operations
    ///     measure the fields before their target through the address resolver, so this checks the resolver's captures
    ///     against the reader's, which the implementation comparisons cannot: both sides share the resolver. A path the
    ///     parse has no value at (an inactive branch) and a path no selected read accepts (the target of an
    ///     <c>@count</c> pointer) are skipped.
    /// </summary>
    /// <param name="name">The sweep layout.</param>
    [TestMethod]
    [DynamicData(nameof(Layouts))]
    public void SelectedReads_MatchTheParse(string name)
    {
        foreach (Variant variant in EngineSweepLayouts.Both(name))
        {
            SweepLayout source = variant.Source;
            foreach (ExecutionPath path in SweepPaths)
            {
                ReadOptions read = variant.BaseRead() with { ExecutionPath = path, };
                foreach (string selected in source.Paths)
                {
                    if (!TrySelect(variant.Value, selected, out object? expected))
                    {
                        continue;
                    }

                    OperationOutcome actual = OperationOutcome.Of(() => variant.Layout.ReadValue(variant.Data.AsSpan(), selected, source.Variables, read));
                    if (actual.Failure is CStructPathException)
                    {
                        continue;
                    }

                    Assert.IsNull(actual.Failure, variant.Name + " " + selected + " (" + path + "): " + actual.Failure?.Message);
                    Assert.AreEqual(OperationOutcome.Render(expected), OperationOutcome.Render(actual.Result), variant.Name + " " + selected + " (" + path + ")");
                }

                if (source.ArrayPath is not null)
                {
                    Assert.IsTrue(TrySelect(variant.Value, source.ArrayPath, out object? array), variant.Name + " " + source.ArrayPath);
                    var elements = (System.Collections.IEnumerable)array!;
                    Assert.AreEqual(elements.Cast<object?>().Count(), variant.Layout.GetArrayLength(variant.Data.AsSpan(), source.ArrayPath, source.Variables, read), variant.Name + " length (" + path + ")");
                }
            }
        }
    }

    /// <summary>
    ///     The writers bind the caller's data identically on the fast and general paths, in every destination: the
    ///     promoted-member layouts under <see cref="UnknownMemberPolicy.Reject"/> (the parsed value, the same members in a
    ///     dictionary, and a dictionary with an undeclared key), and a dictionary root whose nested struct and array
    ///     elements are mapped instances, packed and aligned.
    /// </summary>
    [TestMethod]
    public void WriteBinding_AgreesAcrossExecutionPaths()
    {
        var reject = new WriteOptions { UnknownMembers = UnknownMemberPolicy.Reject, };
        var cases = new List<(string Name, CStruct Layout, object Value, int Length)>();
        foreach (Variant variant in EngineSweepLayouts.Both("promoted").Concat(EngineSweepLayouts.Both("promoted-union")))
        {
            int length = variant.Data.Length;
            var dictionary = variant.Value.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            cases.Add((variant.Name + " parsed", variant.Layout, variant.Value, length));
            cases.Add((variant.Name + " dictionary", variant.Layout, dictionary, length));
            cases.Add((variant.Name + " unknown key", variant.Layout, new Dictionary<string, object?>(dictionary) { ["zz"] = 1, }, length));
        }

        foreach (bool aligned in (bool[])[false, true])
        {
            var mapped = new CStruct("struct inner { uint8 a; }; struct rec { uint16 kind; inner nested; inner items[2]; };", aligned: aligned);
            var value = new Dictionary<string, object?>
            {
                ["kind"] = (ushort)1,
                ["nested"] = new SharpEdgeOptionTests.InnerPoco { A = 5, },
                ["items"] = new object[] { new SharpEdgeOptionTests.InnerPoco { A = 6, }, new SharpEdgeOptionTests.InnerPoco { A = 7, }, },
            };
            cases.Add(("nested mapped" + (aligned ? "/aligned" : "/packed"), mapped, value, mapped.GetStructSizeInBytes("rec")));
        }

        foreach ((string name, CStruct layout, object value, int length) in cases)
        {
            var operations = new List<DifferentialOperation>
            {
                EngineOperations.Serialize(layout, "rec", value, null, reject),
                EngineOperations.SerializeToSpan(layout, length + 3, "rec", value, null, reject),
                EngineOperations.SerializeToWindows(layout, 3, "rec", value, null, reject),
                EngineOperations.Write(layout, new byte[length + 4], 2, "rec", value, null, reject),
            };
            foreach (DifferentialOperation operation in operations)
            {
                string general = Same(operation, ExecutionPath.GeneralOnly);
                string fastest = Same(operation, ExecutionPath.Fastest);
                if (!string.Equals(general, fastest, StringComparison.Ordinal))
                {
                    Assert.Fail(name + ", " + operation.Name + ": the general path (-) and the fast paths (+) differ:\n" + EngineDifferential.Diff(general, fastest));
                }
            }
        }
    }

    /// <summary>
    ///     Every nesting limit from 0 to 4 - read, write, and update traversal - gives the same outcome on the fast paths
    ///     as on the general path, so a layout exactly at the limit (with anonymous promoted members, which add no level)
    ///     is accepted or rejected by both.
    /// </summary>
    /// <param name="name">The sweep layout.</param>
    [TestMethod]
    [DynamicData(nameof(Layouts))]
    public void NestingLimits_AgreeAcrossExecutionPaths(string name)
    {
        foreach (Variant variant in EngineSweepLayouts.Both(name))
        {
            SweepLayout source = variant.Source;
            byte[] data = variant.Data;
            IReadOnlyDictionary<string, int>? variables = source.Variables;
            (string target, object replacement) = source.Updates[0];
            for (int depth = 0; depth <= 4; depth++)
            {
                ReadOptions read = variant.BaseRead() with { MaxNestingDepth = depth, };
                var operations = new List<DifferentialOperation>
                {
                    EngineOperations.Parse(variant.Layout, data, EngineInput.Span, "rec", variables, read),
                    EngineOperations.Parse(variant.Layout, data, EngineInput.Stream, "rec", variables, read),
                    EngineOperations.ParseWithDebug(variant.Layout, data, EngineInput.Span, "rec", variables, read),
                    EngineOperations.ReadValue(variant.Layout, data, EngineInput.Span, source.Paths[0], variables, read),
                    EngineOperations.ResolveAddress(variant.Layout, data, EngineInput.Span, source.Paths[^1], variables, read),
                    EngineOperations.Serialize(variant.Layout, "rec", variant.Value, variables, new WriteOptions { MaxNestingDepth = depth, }),
                    EngineOperations.Update(variant.Layout, data, EngineInput.Span, target, replacement, variables, new UpdateOptions { MaxNestingDepth = depth, MaxTraversalNestingDepth = depth, }),
                };
                if (source.ArrayPath is not null)
                {
                    operations.Add(EngineOperations.GetArrayLength(variant.Layout, data, EngineInput.Span, source.ArrayPath, variables, read));
                }

                foreach (DifferentialOperation operation in operations)
                {
                    string general = Same(operation, ExecutionPath.GeneralOnly);
                    string fastest = Same(operation, ExecutionPath.Fastest);
                    if (!string.Equals(general, fastest, StringComparison.Ordinal))
                    {
                        Assert.Fail(variant.Name + " depth " + depth + ", " + operation.Name + ": the general path (-) and the fast paths (+) differ:\n" + EngineDifferential.Diff(general, fastest));
                    }
                }
            }
        }
    }

    /// <summary>
    ///     Fixed text trimming, pointer dereferencing, and absolute and relative addressing from several origins -
    ///     including one past the input and one that overflows - read identically from memory and from streams that
    ///     start at their beginning or past it.
    /// </summary>
    /// <param name="name">The sweep layout.</param>
    [TestMethod]
    [DynamicData(nameof(Layouts))]
    public void ReadOptions_DecodeIdentically(string name)
    {
        (PointerAddressingMode Mode, long Origin)[] addressing =
        [
            (PointerAddressingMode.Absolute, 0), (PointerAddressingMode.Relative, 0), (PointerAddressingMode.Relative, EngineStreams.ExposedStart),
            (PointerAddressingMode.Relative, -3), (PointerAddressingMode.Relative, 64), (PointerAddressingMode.Relative, long.MaxValue),
        ];
        foreach (Variant variant in EngineSweepLayouts.Both(name))
        {
            SweepLayout source = variant.Source;
            foreach (ExecutionPath path in SweepPaths)
            {
                foreach (bool trim in (bool[])[false, true])
                {
                    foreach (bool dereference in (bool[])[true, false])
                    {
                        foreach ((PointerAddressingMode mode, long origin) in addressing)
                        {
                            var read = new ReadOptions { TrimFixedText = trim, DereferencePointers = dereference, AddressingMode = mode, Origin = origin, };
                            foreach (EngineInput input in (EngineInput[])[EngineInput.Span, EngineInput.Sequence, EngineInput.Stream, EngineInput.ExposedStream, EngineInput.ChunkedStream7])
                            {
                                Same(EngineOperations.Parse(variant.Layout, variant.Data, input, "rec", source.Variables, read), path);
                            }

                            Same(EngineOperations.ParseWithDebug(variant.Layout, variant.Data, EngineInput.Span, "rec", source.Variables, read), path);
                            Same(EngineOperations.ReadValueWithDebug(variant.Layout, variant.Data, EngineInput.ExposedStream, source.Paths[0], source.Variables, read), path);
                            Same(EngineOperations.ReadValue<string>(variant.Layout, variant.Data, EngineInput.Span, source.Paths[1 % source.Paths.Length], source.Variables, read), path);
                            foreach (string selected in source.Paths)
                            {
                                Same(EngineOperations.ResolveAddress(variant.Layout, variant.Data, EngineInput.ExposedStream, selected, source.Variables, read), path);
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    ///     Caller variables read and write identically when absent, when they supply the names the layout's expressions
    ///     use, when they only add unrelated names, and when a used name takes values at and near the <see cref="int"/>
    ///     bounds, which 128-bit expression arithmetic must carry without overflow.
    /// </summary>
    /// <param name="name">The sweep layout.</param>
    [TestMethod]
    [DynamicData(nameof(Layouts))]
    public void CallerVariables_ReadAndWriteIdentically(string name)
    {
        int[] extremes = [int.MinValue, int.MinValue + 1, -2, -1, 0, 1, 2, int.MaxValue - 1, int.MaxValue];
        foreach (Variant variant in EngineSweepLayouts.Both(name))
        {
            SweepLayout source = variant.Source;
            var sets = new List<IReadOnlyDictionary<string, int>?>
            {
                null,
                new Dictionary<string, int>(),
                new Dictionary<string, int> { ["unrelated"] = 5, ["zz_top"] = int.MaxValue, },
            };
            if (source.Variables is not null)
            {
                sets.Add(source.Variables);
                sets.Add(new Dictionary<string, int>(source.Variables) { ["unrelated"] = int.MinValue, });
            }

            foreach (string used in source.Names ?? [])
            {
                foreach (int extreme in extremes)
                {
                    sets.Add(new Dictionary<string, int>(source.Variables ?? new Dictionary<string, int>()) { [used] = extreme, });
                }
            }

            (string target, object replacement) = source.Updates[0];
            foreach (ExecutionPath path in SweepPaths)
            {
                foreach (IReadOnlyDictionary<string, int>? variables in sets)
                {
                    Same(EngineOperations.Parse(variant.Layout, variant.Data, EngineInput.Span, "rec", variables, variant.BaseRead()), path);
                    Same(EngineOperations.Parse(variant.Layout, variant.Data, EngineInput.ChunkedStream7, "rec", variables, variant.BaseRead()), path);
                    Same(EngineOperations.ParseWithDebug(variant.Layout, variant.Data, EngineInput.Span, "rec", variables, variant.BaseRead()), path);
                    Same(EngineOperations.ResolveAddress(variant.Layout, variant.Data, EngineInput.Span, source.Paths[^1], variables, variant.BaseRead()), path);
                    if (source.ArrayPath is not null)
                    {
                        Same(EngineOperations.GetArrayLength(variant.Layout, variant.Data, EngineInput.Span, source.ArrayPath, variables, variant.BaseRead()), path);
                    }

                    Same(EngineOperations.Serialize(variant.Layout, "rec", variant.Value, variables), path);
                    Same(EngineOperations.Update(variant.Layout, variant.Data, EngineInput.Span, target, replacement, variables), path);
                }
            }
        }
    }

    /// <summary>
    ///     Record sequences of one record, three records, and three records followed by a truncated fourth read
    ///     identically from memory, a multi-segment sequence, and every stream form, and the sources agree.
    /// </summary>
    /// <param name="name">The sweep layout.</param>
    [TestMethod]
    [DynamicData(nameof(Layouts))]
    public void RecordSequences_ReadIdenticallyFromEverySource(string name)
    {
        foreach (Variant variant in EngineSweepLayouts.Both(name))
        {
            byte[] data = variant.Data;
            byte[][] sequences = [data, [.. data, .. data, .. data], [.. data, .. data, .. data, .. data[..(data.Length / 2)]]];
            foreach (ExecutionPath path in SweepPaths)
            {
                for (int index = 0; index < sequences.Length; index++)
                {
                    var renderings = new List<(EngineInput Input, string Rendering)>();
                    foreach (EngineInput input in ((EngineInput[])[EngineInput.Memory, EngineInput.Sequence]).Concat(EngineStreams.All))
                    {
                        renderings.Add((input, Same(EngineOperations.ParseMany(variant.Layout, sequences[index], input, "rec", variant.Source.Variables, variant.BaseRead(input)), path)));
                    }

                    // Memory and sequence records are each their own region for stored addresses, while the stream form
                    // counts from the stream's first byte (documented), so pointer records only agree within each kind.
                    EngineAgreement.AssertSourcesAgree(variant.Name + " sequence " + index + " (" + path + ")", renderings, memoryMatchesStreams: !variant.Source.HasPointers);
                }
            }
        }
    }

    /// <summary>
    ///     Writes and updates produce identical bytes and failures in every destination: a new array; spans of 0, 5,
    ///     n-1, n and n+3 bytes filled with 0xCC; streams that already hold bytes, written from their start and from
    ///     inside them, synchronously and asynchronously; a growing buffer writer and buffer writers with 1-, 3- and
    ///     7-byte windows; and in-place updates of a span and a stream.
    /// </summary>
    /// <param name="name">The sweep layout.</param>
    [TestMethod]
    [DynamicData(nameof(Layouts))]
    public void Destinations_WriteIdentically(string name)
    {
        foreach (Variant variant in EngineSweepLayouts.Both(name))
        {
            SweepLayout source = variant.Source;
            IReadOnlyDictionary<string, int>? variables = source.Variables;
            int n = variant.Layout.Serialize("rec", variant.Value, variables).Length;
            byte[] prefill = Enumerable.Repeat((byte)0xAA, n + 4).ToArray();
            foreach (ExecutionPath path in SweepPaths)
            {
                Same(EngineOperations.Serialize(variant.Layout, "rec", variant.Value, variables), path);
                foreach (int capacity in (int[])[0, 5, n - 1, n, n + 3])
                {
                    Same(EngineOperations.SerializeToSpan(variant.Layout, capacity, "rec", variant.Value, variables), path);
                }

                Same(EngineOperations.SerializeToBufferWriter(variant.Layout, "rec", variant.Value, variables), path);
                foreach (int window in (int[])[1, 3, 7])
                {
                    Same(EngineOperations.SerializeToWindows(variant.Layout, window, "rec", variant.Value, variables), path);
                }

                foreach (long start in (long[])[0, 3, n + 4])
                {
                    Same(EngineOperations.Write(variant.Layout, prefill, start, "rec", variant.Value, variables), path);
                }

                Same(EngineOperations.WriteAsync(variant.Layout, prefill, 3, "rec", variant.Value, variables), path);
                foreach ((string target, object value) in source.Updates)
                {
                    Same(EngineOperations.Update(variant.Layout, variant.Data, EngineInput.Span, target, value, variables), path);
                    Same(EngineOperations.Update(variant.Layout, variant.Data, EngineInput.Stream, target, value, variables), path);
                    Same(EngineOperations.UpdateAsync(variant.Layout, variant.Data, target, value, variables), path);
                    Same(EngineOperations.Update(variant.Layout, variant.Data[..(variant.Data.Length - 1)], EngineInput.Span, target, value, variables), path);
                    Same(EngineOperations.Update(variant.Layout, variant.Data, EngineInput.Span, target, value, variables, new UpdateOptions { ClearUnionStorage = false, }), path);
                }
            }
        }
    }

    /// <summary>
    ///     Compares one operation through the harness, requiring the engine to run it exactly when the operation expects
    ///     it (a whole-root read of an eligible root, <see cref="EngineExpectations"/>).
    /// </summary>
    /// <param name="operation">The operation.</param>
    /// <param name="path">The execution path both sides use.</param>
    /// <returns>The shared rendering.</returns>
    private static string Same(DifferentialOperation operation, ExecutionPath path) => EngineDifferential.AssertSame(operation, path: path).Rendering;

    /// <summary>
    ///     Finds the value at a sweep path (<c>rec.items[1].b</c>, <c>rec.p.value</c>) in a parsed value: struct and union
    ///     members by name, array elements by index, and a pointer's <c>value</c> and <c>address</c> accessors.
    /// </summary>
    /// <param name="root">The parsed root value.</param>
    /// <param name="path">The path, starting with the root's name.</param>
    /// <param name="value">The value at the path.</param>
    /// <returns>Whether the parsed value has the path; a member of an inactive branch is absent.</returns>
    private static bool TrySelect(StructValue root, string path, out object? value)
    {
        object? current = root;
        value = null;
        foreach (string segment in path.Split('.').Skip(1))
        {
            int bracket = segment.IndexOf('[', StringComparison.Ordinal);
            string member = bracket < 0 ? segment : segment[..bracket];
            switch (current)
            {
            case Pointer pointer when member == "value":
                current = pointer.Value;
                break;
            case Pointer pointer when member == "address":
                current = pointer.Address;
                break;
            case UnionValue union when union.Members.TryGetValue(member, out object? view):
                current = view;
                break;
            case StructValue parent when parent.TryGetValue(member, out object? child):
                current = child;
                break;
            default:
                return false;
            }

            if (bracket >= 0)
            {
                int index = int.Parse(segment[(bracket + 1)..^1], System.Globalization.CultureInfo.InvariantCulture);
                current = ((System.Collections.IEnumerable)current!).Cast<object?>().ElementAt(index);
            }
        }

        value = current;
        return true;
    }

    /// <summary>Returns <paramref name="options"/> restricted to the interpreter, for computing a sweep's range.</summary>
    /// <param name="options">The read options.</param>
    /// <returns>The restricted options.</returns>
    private static ReadOptions Interpreter(ReadOptions options) => EngineSelections.InterpreterOnly(options);

    /// <summary>Returns <paramref name="options"/> restricted to the interpreter, for computing a sweep's range.</summary>
    /// <param name="options">The write options.</param>
    /// <returns>The restricted options.</returns>
    private static WriteOptions Interpreter(WriteOptions options) => EngineSelections.InterpreterOnly(options);

    /// <summary>Returns whether <paramref name="call"/> completes without throwing.</summary>
    /// <param name="call">The call.</param>
    /// <returns><see langword="true"/> when it completes.</returns>
    private static bool Succeeds(Func<object?> call)
    {
        try
        {
            _ = call();
            return true;
        }
        catch (Exception exception) when (exception is not UnitTestAssertException)
        {
            return false;
        }
    }

    /// <summary>The smallest budget from 1 to <paramref name="maximum"/> for which <paramref name="succeeds"/> holds, or <paramref name="maximum"/>.</summary>
    /// <param name="succeeds">Whether an operation with the budget completes.</param>
    /// <param name="maximum">The largest budget tried.</param>
    /// <returns>The operation's natural total.</returns>
    private static int Smallest(Func<long, bool> succeeds, int maximum)
    {
        for (int budget = 1; budget < maximum; budget++)
        {
            if (succeeds(budget))
            {
                return budget;
            }
        }

        return maximum;
    }
}
