namespace CStructSharp.Tests;

using System.Buffers;
using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>
///     Verifies qualified references (<c>hdr.n</c>) into inline named composites (<c>struct { ... } hdr;</c>): a layout
///     expression reaches their fields exactly as it reaches the fields of a typed member (<c>h hdr;</c>), through every
///     read, address, length, write and update operation, on every execution path.
/// </summary>
[TestClass]
public class InlineQualifiedReferenceTests
{
    /// <summary>The execution paths that must agree.</summary>
    private static readonly ExecutionPath[] Paths = [ExecutionPath.Fastest, ExecutionPath.NoDirectAccess, ExecutionPath.NoFastPaths];

    /// <summary>
    ///     Gets the layouts: a case name, the definition, its input, the value of <c>root.tail</c>, and the path of a
    ///     runtime-sized array whose length a qualified reference sets.
    /// </summary>
    public static IEnumerable<object[]> Layouts =>
    [
        ["typed member", "struct h { uint8 n; }; struct root { h hdr; uint8 v[hdr.n]; uint8 tail; };", new byte[] { 2, 7, 8, 9, }, (byte)9, "root.v"],
        ["inline struct", "struct root { struct { uint8 n; } hdr; uint8 v[hdr.n]; uint8 tail; };", new byte[] { 2, 7, 8, 9, }, (byte)9, "root.v"],
        ["inline struct with a runtime-sized member", "struct root { struct { uint8 n; uint8 d[n]; } hdr; uint8 v[hdr.n]; uint8 tail; };", new byte[] { 1, 5, 7, 9, }, (byte)9, "root.v"],
        ["inline struct two levels deep", "struct root { struct { struct { uint8 n; } b; } a; uint8 v[a.b.n]; uint8 tail; };", new byte[] { 2, 7, 8, 9, }, (byte)9, "root.v"],
        ["inline struct in an array element", "struct item { struct { uint8 n; } hdr; uint8 v[hdr.n]; }; struct root { uint8 count; item items[count]; uint8 tail; };", new byte[] { 2, 1, 5, 2, 6, 7, 9, }, (byte)9, "root.items[1].v"],
        ["conditional on an inline struct", "struct root { struct { uint8 kind; } hdr; if (hdr.kind == 1) { uint16 wide; } else { uint8 narrow; } uint8 tail; };", new byte[] { 1, 0x34, 0x12, 9, }, (byte)9, null!],
        ["typed member naming its own member, inside a named member", "struct h { uint8 n; uint8 k; }; struct mid { h hdr; uint8 v[hdr.n]; }; struct root { mid m; uint8 w[m.hdr.k]; uint8 tail; };", new byte[] { 2, 1, 7, 8, 9, 4, }, (byte)4, "root.m.v"],
        ["inline struct naming its own member, inside a named inline struct", "struct root { struct { struct { uint8 n; uint8 k; } hdr; uint8 v[hdr.n]; } m; uint8 w[m.hdr.k]; uint8 tail; };", new byte[] { 2, 1, 7, 8, 9, 4, }, (byte)4, "root.m.v"],
        ["inline struct naming its own member, inside a named member", "struct mid { struct { uint8 n; uint8 k; } hdr; uint8 v[hdr.n]; }; struct root { mid m; uint8 w[m.hdr.k]; uint8 tail; };", new byte[] { 2, 1, 7, 8, 9, 4, }, (byte)4, "root.m.v"],
        ["named member naming its own member, with no outer reference", "struct h { uint8 n; }; struct mid { h hdr; uint8 v[hdr.n]; }; struct root { mid m; uint8 tail; };", new byte[] { 2, 7, 8, 4, }, (byte)4, "root.m.v"],
    ];

    /// <summary>
    ///     Every operation succeeds on every execution path, reads <c>root.tail</c> after the array or branch the
    ///     qualified reference sized, and returns the same value, address, length or bytes on every path.
    /// </summary>
    /// <param name="name">The case name.</param>
    /// <param name="definition">The layout source.</param>
    /// <param name="data">The input.</param>
    /// <param name="tail">The value of <c>root.tail</c>.</param>
    /// <param name="arrayPath">A runtime-sized array's path, or <see langword="null"/>.</param>
    [TestMethod]
    [DynamicData(nameof(Layouts))]
    public void QualifiedReference_WorksThroughEveryOperationOnEveryPath(string name, string definition, byte[] data, byte tail, string? arrayPath)
    {
        var layout = new CStruct(definition, pointerSize: 1);

        // The value the writes encode; when no path can read it, each write reports that instead of a write outcome.
        StructValue value = OperationOutcome.Of(() => layout.Parse(data.AsSpan(), "root")).Result as StructValue ?? new StructValue();
        var operations = new List<(string Name, Func<ExecutionPath, object?> Run)>
        {
            ("Parse span", path => layout.Parse(data.AsSpan(), "root", null, Read(path))),
            ("Parse stream", path => layout.Parse(new MemoryStream(data, writable: false), "root", null, Read(path))),
            ("Parse sequence", path => layout.Parse(new ReadOnlySequence<byte>(data), "root", null, Read(path))),
            ("ParseWithDebug", path => Describe(layout.ParseWithDebug(data.AsSpan(), "root", null, Read(path)))),
            ("ParseWithDebug stream", path => Describe(layout.ParseWithDebug(new MemoryStream(data, writable: false), "root", null, Read(path)))),
            ("ReadValue root", path => layout.ReadValue(data.AsSpan(), "root", null, Read(path))),
            ("ReadValue tail", path => layout.ReadValue(data.AsSpan(), "root.tail", null, Read(path))),
            ("ReadValue tail stream", path => layout.ReadValue(new MemoryStream(data, writable: false), "root.tail", null, Read(path))),
            ("ResolveAddress", path => layout.ResolveAddress(data.AsSpan(), "root.tail", null, Read(path))),
            ("Serialize", path => layout.Serialize("root", value, null, Write(path))),
            ("Serialize span", path => SerializeToSpan(layout, value, data.Length, Write(path))),
            ("Write", path => WriteToStream(layout, value, Write(path))),
            ("Update", path => UpdateTail(layout, data, path)),
        };
        if (arrayPath is not null)
        {
            operations.Add(("GetArrayLength", path => layout.GetArrayLength(data.AsSpan(), arrayPath, null, Read(path))));
            operations.Add(("ReadValue array", path => layout.ReadValue(data.AsSpan(), arrayPath, null, Read(path))));
        }

        var unexpected = new List<string>();
        foreach ((string operation, Func<ExecutionPath, object?> run) in operations)
        {
            OperationOutcome[] outcomes = Paths.Select(path => OperationOutcome.Of(() => run(path))).ToArray();
            for (int index = 0; index < outcomes.Length; index++)
            {
                if (outcomes[index].Failure is { } failure)
                {
                    unexpected.Add(operation + " (" + Paths[index] + "): " + failure.Message);
                }
            }

            if (unexpected.Count == 0)
            {
                for (int index = 0; index < outcomes.Length - 1; index++)
                {
                    OperationOutcome.AssertSame(outcomes[^1], outcomes[index], name + ", " + operation + " (" + Paths[index] + " vs " + ExecutionPath.NoFastPaths + ")");
                }
            }
        }

        Assert.AreEqual(0, unexpected.Count, name + ":\n" + string.Join("\n", unexpected));
        foreach (ExecutionPath path in Paths)
        {
            Assert.AreEqual(tail, layout.ReadValue(data.AsSpan(), "root.tail", null, Read(path)), name + " (" + path + ")");
            CollectionAssert.AreEqual(data, layout.Serialize("root", value, null, Write(path)), name + " (" + path + ")");
        }
    }

    /// <summary>
    ///     A union's members are published only while the union is read - its views overlap, so none of their values
    ///     remains afterwards - so <c>hdr.n</c> after a union <c>hdr</c> is undefined. An inline union and a typed union
    ///     member fail identically on every path, and so do the resolver's operations.
    /// </summary>
    [TestMethod]
    public void UnionMember_IsNotPublishedAfterTheUnion()
    {
        byte[] data = [2, 7, 8, 9];
        CStruct[] layouts =
        [
            new("union u { uint8 n; uint8 raw; }; struct root { u hdr; uint8 v[hdr.n]; uint8 tail; };"),
            new("struct root { union { uint8 n; uint8 raw; } hdr; uint8 v[hdr.n]; uint8 tail; };"),
        ];
        foreach (CStruct layout in layouts)
        {
            foreach (ExecutionPath path in Paths)
            {
                CStructReadException parse = Assert.ThrowsExactly<CStructReadException>(() => layout.Parse(data.AsSpan(), "root", null, Read(path)));
                StringAssert.Contains(parse.Message, "Undefined expression identifier: hdr.n (field 'v' (uint8), in 'root', offset 1)");
                CStructReadException address = Assert.ThrowsExactly<CStructReadException>(() => layout.ResolveAddress(data.AsSpan(), "root.tail", null, Read(path)));
                StringAssert.Contains(address.Message, "Undefined expression identifier: hdr.n (path 'root.tail', offset 0)");
                CStructReadException length = Assert.ThrowsExactly<CStructReadException>(() => layout.GetArrayLength(data.AsSpan(), "root.v", null, Read(path)));
                StringAssert.Contains(length.Message, "Undefined expression identifier: hdr.n (path 'root.v', offset 0)");
            }
        }
    }

    /// <summary>
    ///     The examples of <c>docs/language/expressions-defines-and-variables.md</c> (a nested field's value): the
    ///     <c>packet</c> input reads two items and is six bytes long, and after a union member neither the qualified nor
    ///     the bare member name is defined.
    /// </summary>
    [TestMethod]
    public void DocumentedExamples_MatchTheImplementation()
    {
        var packet = new CStruct("struct packet { struct { uint8 count; uint8 flags; } hdr; uint16 items[hdr.count]; };");
        byte[] data = [0x02, 0x00, 0x0A, 0x00, 0x0B, 0x00];
        foreach (ExecutionPath path in Paths)
        {
            StructValue value = packet.Parse(data.AsSpan(), "packet", null, Read(path));
            CollectionAssert.AreEqual(new ushort[] { 10, 11, }, ((IEnumerable<object?>)value["items"]!).Cast<ushort>().ToArray(), path.ToString());
            CollectionAssert.AreEqual(data, packet.Serialize("packet", value, null, Write(path)), path.ToString());
        }

        var bare = new CStruct("union u { uint8 n; uint8 raw; }; struct root { u hdr; uint8 v[n]; };");
        CStructReadException failure = Assert.ThrowsExactly<CStructReadException>(() => bare.Parse(new byte[] { 2, 7, 8, }.AsSpan(), "root"));
        StringAssert.Contains(failure.Message, "Undefined expression identifier: n");
    }

    /// <summary>
    ///     The inline struct and the typed member produce the same value, so a layout can switch between the two forms
    ///     without changing what its expressions see.
    /// </summary>
    [TestMethod]
    public void InlineStruct_ReadsLikeTheTypedMember()
    {
        byte[] data = [3, 1, 2, 3, 9];
        var typed = new CStruct("struct h { uint8 n; }; struct root { h hdr; uint8 v[hdr.n]; uint8 tail; };");
        var inline = new CStruct("struct root { struct { uint8 n; } hdr; uint8 v[hdr.n]; uint8 tail; };");
        foreach (ExecutionPath path in Paths)
        {
            Assert.AreEqual(
                OperationOutcome.Render(typed.Parse(data.AsSpan(), "root", null, Read(path))),
                OperationOutcome.Render(inline.Parse(data.AsSpan(), "root", null, Read(path))),
                path.ToString());
        }
    }

    /// <summary>Read options on one execution path.</summary>
    /// <param name="path">The execution path.</param>
    /// <returns>The options.</returns>
    private static ReadOptions Read(ExecutionPath path) => new() { ExecutionPath = path, };

    /// <summary>Write options on one execution path.</summary>
    /// <param name="path">The execution path.</param>
    /// <returns>The options.</returns>
    private static WriteOptions Write(ExecutionPath path) => new() { ExecutionPath = path, };

    /// <summary>Renders a debug parse as its value and its recorded member paths.</summary>
    /// <param name="result">The debug parse result.</param>
    /// <returns>The value and the paths, for comparison.</returns>
    private static object Describe(ParseResult result) => (OperationOutcome.Render(result.Value), string.Join(",", result.Debug.Select(entry => entry.Path)));

    /// <summary>Serializes into a span with room to spare and returns the bytes written.</summary>
    /// <param name="layout">The compiled layout.</param>
    /// <param name="value">The root value.</param>
    /// <param name="length">The expected encoded length.</param>
    /// <param name="options">The write options.</param>
    /// <returns>The written bytes.</returns>
    private static byte[] SerializeToSpan(CStruct layout, StructValue value, int length, WriteOptions options)
    {
        byte[] destination = new byte[length + 3];
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

    /// <summary>Updates <c>root.tail</c> in a copy of the input, which needs the qualified reference to find it.</summary>
    /// <param name="layout">The compiled layout.</param>
    /// <param name="data">The input.</param>
    /// <param name="path">The execution path.</param>
    /// <returns>The updated bytes.</returns>
    private static byte[] UpdateTail(CStruct layout, byte[] data, ExecutionPath path)
    {
        byte[] copy = (byte[])data.Clone();
        layout.Update(copy.AsSpan(), "root.tail", (byte)0x42, null, new UpdateOptions { ExecutionPath = path, });
        return copy;
    }
}
