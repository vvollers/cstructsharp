namespace CStructSharp.Tests;

using System.Buffers;
using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>
///     Verifies how anonymous promoted members count toward <c>MaxNestingDepth</c>: a promoted anonymous struct or union
///     is part of the struct that contains it and adds no nesting level, while a named struct or union member adds one.
///     Every read, address, length, write and update operation reaches the same limit on every execution path.
/// </summary>
[TestClass]
public class PromotedMemberNestingTests
{
    /// <summary>A one-byte struct that every layout nests at the second level, below the root.</summary>
    private const string Leaf = "struct leaf { uint8 v; }; ";

    /// <summary>The input every layout reads: <c>a = 1</c>, then the member storage, then the tail.</summary>
    private static readonly byte[] Data = [0x01, 0x02, 0x03, 0x04, 0x05];

    /// <summary>The execution paths that must agree.</summary>
    private static readonly ExecutionPath[] Paths = [ExecutionPath.Fastest, ExecutionPath.NoDirectAccess, ExecutionPath.GeneralOnly];

    /// <summary>
    ///     Gets the layouts: a case name, the definition, the path prefix that reaches <c>inner</c> and <c>items</c>, and
    ///     the structure levels a whole parse needs. Only the named inline struct adds a level between the root and
    ///     <c>leaf</c>.
    /// </summary>
    public static IEnumerable<object[]> Layouts =>
    [
        ["promoted struct", Leaf + "struct root { uint8 a; struct { leaf inner; uint8 items[2]; }; uint8 tail; };", "root", 2],
        ["promoted struct in a runtime-sized parent", Leaf + "struct root { uint8 a; struct { leaf inner; uint8 items[2]; }; uint8 tail[a]; };", "root", 2],
        ["transitively promoted structs", Leaf + "struct root { uint8 a; struct { struct { leaf inner; uint8 items[2]; }; }; uint8 tail; };", "root", 2],
        ["promoted union", Leaf + "struct root { uint8 a; union { leaf inner; uint8 items[1]; }; uint8 tail; };", "root", 2],
        ["struct promoted through a union", Leaf + "struct root { uint8 a; union { struct { leaf inner; uint8 items[2]; }; uint16 w; }; uint8 tail; };", "root", 2],
        ["named inline struct", Leaf + "struct root { uint8 a; struct { leaf inner; uint8 items[2]; } mid; uint8 tail; };", "root.mid", 3],
    ];

    /// <summary>
    ///     With <c>MaxNestingDepth</c> exactly at the levels the layout needs, every operation succeeds and every
    ///     execution path returns the same value, address, length or bytes.
    /// </summary>
    /// <param name="name">The case name.</param>
    /// <param name="definition">The layout source.</param>
    /// <param name="prefix">The path of the struct that declares <c>inner</c> and <c>items</c>.</param>
    /// <param name="levels">The structure levels a whole parse needs.</param>
    [TestMethod]
    [DynamicData(nameof(Layouts))]
    public void AtTheLimit_EveryOperationSucceedsOnEveryPath(string name, string definition, string prefix, int levels)
    {
        var layout = new CStruct(definition, pointerSize: 1);
        var unexpected = new List<string>();
        foreach ((string operation, OperationOutcome[] outcomes) in RunEverywhere(layout, prefix, levels, includeLength: true))
        {
            for (int index = 0; index < outcomes.Length; index++)
            {
                if (outcomes[index].Failure is { } failure)
                {
                    unexpected.Add(operation + " (" + Paths[index] + "): " + failure.Message);
                }
            }
        }

        Assert.AreEqual(0, unexpected.Count, name + " at a limit of " + levels + ":\n" + string.Join("\n", unexpected));
        foreach ((string operation, OperationOutcome[] outcomes) in RunEverywhere(layout, prefix, levels, includeLength: true))
        {
            AssertPathsAgree(name + ", " + operation, outcomes);
        }
    }

    /// <summary>
    ///     One level below the limit, every operation that reaches <c>leaf</c> fails with a limit failure, identically on
    ///     every execution path.
    /// </summary>
    /// <param name="name">The case name.</param>
    /// <param name="definition">The layout source.</param>
    /// <param name="prefix">The path of the struct that declares <c>inner</c> and <c>items</c>.</param>
    /// <param name="levels">The structure levels a whole parse needs.</param>
    [TestMethod]
    [DynamicData(nameof(Layouts))]
    public void BelowTheLimit_EveryOperationFailsOnEveryPath(string name, string definition, string prefix, int levels)
    {
        var layout = new CStruct(definition, pointerSize: 1);
        var unexpected = new List<string>();
        foreach ((string operation, OperationOutcome[] outcomes) in RunEverywhere(layout, prefix, levels - 1, includeLength: false))
        {
            for (int index = 0; index < outcomes.Length; index++)
            {
                if (outcomes[index].Failure is not (CStructReadLimitException or CStructWriteLimitException))
                {
                    unexpected.Add(operation + " (" + Paths[index] + "): " + (outcomes[index].Failure?.Message ?? "succeeded"));
                }
            }

            AssertPathsAgree(name + ", " + operation, outcomes);
        }

        Assert.AreEqual(0, unexpected.Count, name + " at a limit of " + (levels - 1) + ", expected nesting limit failures:\n" + string.Join("\n", unexpected));
    }

    /// <summary>
    ///     A promoted member's nesting is counted the same way at any depth: a chain of named structs whose innermost
    ///     struct holds a promoted struct and a promoted union needs exactly one level per named struct.
    /// </summary>
    [TestMethod]
    public void PromotedMembers_AddNoLevelInsideADeepChain()
    {
        var layout = new CStruct(
            "struct s3 { uint8 v; }; " +
            "struct s2 { struct { s3 x; }; union { s3 y; uint8 z; }; }; " +
            "struct s1 { s2 inner; }; " +
            "struct s0 { s1 inner; };",
            pointerSize: 1);
        byte[] data = [0x07, 0x08];

        foreach (ExecutionPath path in Paths)
        {
            var enough = new ReadOptions { MaxNestingDepth = 4, ExecutionPath = path, };
            StructValue value = layout.Parse(data.AsSpan(), "s0", null, enough);
            Assert.AreEqual((byte)0x08, layout.ReadValue(data.AsSpan(), "s0.inner.inner.y.v", null, enough), path.ToString());
            CollectionAssert.AreEqual(data, layout.Serialize("s0", value, null, new WriteOptions { MaxNestingDepth = 4, ExecutionPath = path, }), path.ToString());

            var tooFew = new ReadOptions { MaxNestingDepth = 3, ExecutionPath = path, };
            try
            {
                _ = layout.Parse(data.AsSpan(), "s0", null, tooFew);
                Assert.Fail(path + ": four named levels must exceed a limit of three");
            }
            catch (CStructReadLimitException)
            {
                // The expected nesting limit failure.
            }
        }
    }

    /// <summary>
    ///     The examples of <c>docs/language/limits-and-diagnostics.md</c> (how nesting depth is counted) and
    ///     <c>structs-unions-enums-typedefs.md</c> (anonymous promoted members): <c>shape</c> reads and writes with two
    ///     levels and fails at <c>origin</c> with one; <c>file_name</c> needs one level.
    /// </summary>
    [TestMethod]
    public void DocumentedExamples_NeedTheDocumentedLevels()
    {
        var shape = new CStruct(
            """
            struct point { int16 x; int16 y; };

            struct shape {
                uint8 kind;
                struct { point origin; uint8 flags; };   // promoted: no level of its own
                union { point centre; uint32 raw; };     // promoted: no level of its own
            };
            """,
            pointerSize: 4);
        byte[] data = [0x01, 0x02, 0x00, 0x03, 0x00, 0x04, 0x05, 0x00, 0x06, 0x00];
        StructValue value = shape.Parse(data.AsSpan(), "shape", null, new ReadOptions { MaxNestingDepth = 2, });
        CollectionAssert.AreEqual(data, shape.Serialize("shape", value, null, new WriteOptions { MaxNestingDepth = 2, }));
        Assert.AreEqual((short)2, shape.ReadValue(data.AsSpan(), "shape.origin.x", null, new ReadOptions { MaxNestingDepth = 2, }));
        Assert.AreEqual(1L, shape.ResolveAddress(data.AsSpan(), "shape.origin.x", null, new ReadOptions { MaxNestingDepth = 2, }));
        CStructReadLimitException failure = Assert.ThrowsExactly<CStructReadLimitException>(() => shape.Parse(data.AsSpan(), "shape", null, new ReadOptions { MaxNestingDepth = 1, }));
        StringAssert.Contains(failure.Message, "field 'origin'");

        var fileName = new CStruct(
            "struct file_name { uint32 attributes; union { struct { uint16 ea_size; uint16 reserved; }; uint32 reparse_tag; }; uint8 name_length; };",
            pointerSize: 4);
        byte[] record = [0x20, 0x00, 0x00, 0x00, 0x34, 0x12, 0x78, 0x56, 0x03];
        StructValue parsed = fileName.Parse(record.AsSpan(), "file_name", null, new ReadOptions { MaxNestingDepth = 1, });
        CollectionAssert.AreEqual(record, fileName.Serialize("file_name", parsed, null, new WriteOptions { MaxNestingDepth = 1, }));
    }

    /// <summary>Runs every operation on every execution path with one nesting limit for reads, writes and update traversal.</summary>
    /// <param name="layout">The compiled layout.</param>
    /// <param name="prefix">The path of the struct that declares <c>inner</c> and <c>items</c>.</param>
    /// <param name="limit">The nesting limit.</param>
    /// <param name="includeLength">Whether to include <c>GetArrayLength</c>, which needs fewer levels in some layouts.</param>
    /// <returns>Each operation's name and its outcome per path, in <see cref="Paths"/> order.</returns>
    private static IEnumerable<(string Operation, OperationOutcome[] Outcomes)> RunEverywhere(CStruct layout, string prefix, int limit, bool includeLength)
    {
        StructValue value = layout.Parse(Data.AsSpan(), "root");
        var operations = new List<(string Name, Func<ExecutionPath, object?> Run)>
        {
            ("Parse span", path => layout.Parse(Data.AsSpan(), "root", null, Read(limit, path))),
            ("Parse stream", path => layout.Parse(new MemoryStream(Data, writable: false), "root", null, Read(limit, path))),
            ("Parse sequence", path => layout.Parse(new ReadOnlySequence<byte>(Data), "root", null, Read(limit, path))),
            ("ParseWithDebug", path => Describe(layout.ParseWithDebug(Data.AsSpan(), "root", null, Read(limit, path)))),
            ("ParseWithDebug stream", path => Describe(layout.ParseWithDebug(new MemoryStream(Data, writable: false), "root", null, Read(limit, path)))),
            ("ReadValue struct", path => layout.ReadValue(Data.AsSpan(), prefix + ".inner", null, Read(limit, path))),
            ("ReadValue scalar", path => layout.ReadValue(Data.AsSpan(), prefix + ".inner.v", null, Read(limit, path))),
            ("ReadValue stream", path => layout.ReadValue(new MemoryStream(Data, writable: false), prefix + ".inner.v", null, Read(limit, path))),
            ("ResolveAddress", path => layout.ResolveAddress(Data.AsSpan(), prefix + ".inner.v", null, Read(limit, path))),
            ("Serialize", path => layout.Serialize("root", value, null, Write(limit, path))),
            ("Serialize span", path => SerializeToSpan(layout, value, Write(limit, path))),
            ("Write", path => WriteToStream(layout, value, Write(limit, path))),
            ("Update", path => UpdateCopy(layout, prefix + ".inner.v", limit, path)),
        };
        if (includeLength)
        {
            operations.Add(("GetArrayLength", path => layout.GetArrayLength(Data.AsSpan(), prefix + ".items", null, Read(limit, path))));
        }

        foreach ((string operationName, Func<ExecutionPath, object?> run) in operations)
        {
            yield return (operationName, Paths.Select(path => OperationOutcome.Of(() => run(path))).ToArray());
        }
    }

    /// <summary>Asserts that every path's outcome equals the general path's.</summary>
    /// <param name="label">The case and operation, for the failure message.</param>
    /// <param name="outcomes">The outcomes in <see cref="Paths"/> order.</param>
    private static void AssertPathsAgree(string label, OperationOutcome[] outcomes)
    {
        OperationOutcome general = outcomes[^1];
        for (int index = 0; index < outcomes.Length - 1; index++)
        {
            OperationOutcome.AssertSame(general, outcomes[index], label + " (" + Paths[index] + " vs " + ExecutionPath.GeneralOnly + ")");
        }
    }

    /// <summary>Read options with one nesting limit on one execution path.</summary>
    /// <param name="limit">The nesting limit.</param>
    /// <param name="path">The execution path.</param>
    /// <returns>The options.</returns>
    private static ReadOptions Read(int limit, ExecutionPath path) => new() { MaxNestingDepth = limit, ExecutionPath = path, };

    /// <summary>Write options with one nesting limit on one execution path.</summary>
    /// <param name="limit">The nesting limit.</param>
    /// <param name="path">The execution path.</param>
    /// <returns>The options.</returns>
    private static WriteOptions Write(int limit, ExecutionPath path) => new() { MaxNestingDepth = limit, ExecutionPath = path, };

    /// <summary>Renders a debug parse as its value and its recorded member paths.</summary>
    /// <param name="result">The debug parse result.</param>
    /// <returns>The value and the paths, for comparison.</returns>
    private static object Describe(ParseResult result) => (OperationOutcome.Render(result.Value), string.Join(",", result.Debug.Select(entry => entry.Path)));

    /// <summary>Serializes into a span with room to spare and returns the bytes written.</summary>
    /// <param name="layout">The compiled layout.</param>
    /// <param name="value">The root value.</param>
    /// <param name="options">The write options.</param>
    /// <returns>The written bytes.</returns>
    private static byte[] SerializeToSpan(CStruct layout, StructValue value, WriteOptions options)
    {
        byte[] destination = new byte[Data.Length + 3];
        int written = layout.Serialize(destination.AsSpan(), "root", value, null, options);
        return destination[..written];
    }

    /// <summary>Writes the root to a new stream and returns its contents.</summary>
    /// <param name="layout">The compiled layout.</param>
    /// <param name="value">The root value.</param>
    /// <param name="options">The write options.</param>
    /// <returns>The stream's bytes.</returns>
    private static byte[] WriteToStream(CStruct layout, StructValue value, WriteOptions options)
    {
        using var stream = new MemoryStream();
        layout.Write(stream, "root", value, null, options);
        return stream.ToArray();
    }

    /// <summary>Updates one member of a copy of <see cref="Data"/> with the limit on writes and on traversal.</summary>
    /// <param name="layout">The compiled layout.</param>
    /// <param name="target">The member path.</param>
    /// <param name="limit">The nesting limit.</param>
    /// <param name="path">The execution path.</param>
    /// <returns>The updated bytes.</returns>
    private static byte[] UpdateCopy(CStruct layout, string target, int limit, ExecutionPath path)
    {
        byte[] copy = (byte[])Data.Clone();
        layout.Update(copy.AsSpan(), target, (byte)0x09, null, new UpdateOptions { MaxNestingDepth = limit, MaxTraversalNestingDepth = limit, ExecutionPath = path, });
        return copy;
    }
}
