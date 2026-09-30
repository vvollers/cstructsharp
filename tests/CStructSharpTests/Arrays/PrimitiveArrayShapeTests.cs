namespace CStructSharp.Tests;

using CStructSharp.Values;

/// <summary>
///     Verifies the value shape of a one-dimensional array of a fixed-width number or <c>bool</c>: it is a
///     <see cref="PrimitiveArray{T}"/> wherever it is read - a struct member, a union member view, a promoted union,
///     an empty array, a debug parse, a selected read, a root array and a pointer target - from every source and path.
/// </summary>
[TestClass]
public class PrimitiveArrayShapeTests
{
    /// <summary>The execution paths that must agree.</summary>
    private static readonly ExecutionPath[] Paths = [ExecutionPath.Fastest, ExecutionPath.NoDirectAccess, ExecutionPath.NoFastPaths];

    /// <summary>
    ///     Gets the cases: a name, the definition, the root, the input, the path of the array inside the parsed value
    ///     (a <c>|</c> steps into a union's member views), the element type, and the expected elements.
    /// </summary>
    public static IEnumerable<object[]> Cases =>
    [
        ["struct member", "struct rec { uint8 a; uint16 v[2]; };", "rec", new byte[] { 1, 2, 0, 3, 0, }, "v", typeof(ushort), new object[] { (ushort)2, (ushort)3, }],
        ["union member view", "union u { uint16 w[2]; uint32 d; }; struct rec { u v; };", "rec", new byte[] { 1, 0, 2, 0, }, "v|w", typeof(ushort), new object[] { (ushort)1, (ushort)2, }],
        ["bool union member view", "union u { bool flags[2]; uint16 raw; }; struct rec { u v; };", "rec", new byte[] { 1, 0, }, "v|flags", typeof(bool), new object[] { true, false, }],
        ["promoted union", "struct rec { union { uint8 b[4]; uint32 d; }; };", "rec", new byte[] { 1, 2, 3, 4, }, "b", typeof(byte), new object[] { (byte)1, (byte)2, (byte)3, (byte)4, }],
        ["empty runtime-sized array", "struct rec { uint8 n; uint16 v[n]; uint8 tail; };", "rec", new byte[] { 0, 9, }, "v", typeof(ushort), Array.Empty<object>()],
        ["empty fixed array", "struct rec { uint8 a; uint32 z[0]; uint8 tail; };", "rec", new byte[] { 1, 9, }, "z", typeof(uint), Array.Empty<object>()],
        ["empty bool array", "struct rec { uint8 n; bool f[n]; };", "rec", new byte[] { 0, }, "f", typeof(bool), Array.Empty<object>()],
        ["24-bit array", "struct rec { uint8 n; int24 v[n]; };", "rec", new byte[] { 1, 0xFF, 0xFF, 0xFF, }, "v", typeof(int), new object[] { -1, }],
    ];

    /// <summary>
    ///     Parse from a span and a stream, ParseWithDebug, and ParseAsync return a <see cref="PrimitiveArray{T}"/> of the
    ///     element type holding the expected elements, on every path.
    /// </summary>
    /// <param name="name">The case name.</param>
    /// <param name="definition">The layout source.</param>
    /// <param name="root">The root name.</param>
    /// <param name="data">The input.</param>
    /// <param name="arrayPath">The array's path inside the parsed value.</param>
    /// <param name="elementType">The expected element type.</param>
    /// <param name="expected">The expected elements.</param>
    [TestMethod]
    [DynamicData(nameof(Cases))]
    public void EveryRead_ReturnsAPrimitiveArray(string name, string definition, string root, byte[] data, string arrayPath, Type elementType, object[] expected)
    {
        var layout = new CStruct(definition);
        var problems = new List<string>();
        foreach (ExecutionPath path in Paths)
        {
            var read = new ReadOptions { ExecutionPath = path, };
            var values = new (string Source, StructValue Value)[]
            {
                ("span", layout.Parse(data.AsSpan(), root, null, read)),
                ("stream", layout.Parse(new MemoryStream(data, writable: false), root, null, read)),
                ("debug", layout.ParseWithDebug(data.AsSpan(), root, null, read).Value),
                ("debug stream", layout.ParseWithDebug(new MemoryStream(data, writable: false), root, null, read).Value),
                ("async", layout.ParseAsync(new MemoryStream(data, writable: false), root, null, read).AsTask().GetAwaiter().GetResult()),
            };
            foreach ((string source, StructValue value) in values)
            {
                Check(problems, name + ", " + source + " (" + path + ")", Select(value, arrayPath), elementType, expected);
            }

            if (!arrayPath.Contains('|', StringComparison.Ordinal))
            {
                Check(problems, name + ", ReadValue (" + path + ")", layout.ReadValue(data.AsSpan(), root + "." + arrayPath, null, read), elementType, expected);
            }
        }

        Assert.AreEqual(0, problems.Count, string.Join("\n", problems));
    }

    /// <summary>
    ///     A root array and a pointer target array - including an empty one - are <see cref="PrimitiveArray{T}"/> too.
    /// </summary>
    [TestMethod]
    public void RootAndPointerTargetArrays_ArePrimitiveArrays()
    {
        var problems = new List<string>();
        var rootArray = new CStruct("typedef uint16 pair[2];");
        var pointers = new CStruct("struct rec { uint8 n; uint16 *p @count(n); };", pointerSize: 1);
        foreach (ExecutionPath path in Paths)
        {
            var read = new ReadOptions { ExecutionPath = path, };
            Check(problems, "root array (" + path + ")", rootArray.ReadValue(new byte[] { 1, 0, 2, 0, }.AsSpan(), "pair", null, read), typeof(ushort), [(ushort)1, (ushort)2]);
            StructValue full = pointers.Parse(new byte[] { 2, 2, 7, 0, 8, 0, }.AsSpan(), "rec", null, read);
            Check(problems, "pointer target (" + path + ")", ((Pointer)full["p"]!).Value, typeof(ushort), [(ushort)7, (ushort)8]);
            StructValue empty = pointers.Parse(new byte[] { 0, 2, 0xAA, }.AsSpan(), "rec", null, read);
            Check(problems, "empty pointer target (" + path + ")", ((Pointer)empty["p"]!).Value, typeof(ushort), []);
        }

        Assert.AreEqual(0, problems.Count, string.Join("\n", problems));
    }

    /// <summary>The writers accept a <see cref="PrimitiveArray{T}"/> read from any position, an empty one included.</summary>
    [TestMethod]
    public void Writers_AcceptTheArraysBack()
    {
        foreach (object[] row in Cases)
        {
            var layout = new CStruct((string)row[1]);
            byte[] data = (byte[])row[3];
            StructValue value = layout.Parse(data.AsSpan(), (string)row[2]);
            CollectionAssert.AreEqual(data, layout.Serialize((string)row[2], value), (string)row[0]);
        }
    }

    /// <summary>Records a problem unless the value is a <see cref="PrimitiveArray{T}"/> of the element type with the expected elements.</summary>
    /// <param name="problems">The problems found so far.</param>
    /// <param name="label">The case.</param>
    /// <param name="value">The array value.</param>
    /// <param name="elementType">The expected element type.</param>
    /// <param name="expected">The expected elements.</param>
    private static void Check(List<string> problems, string label, object? value, Type elementType, object[] expected)
    {
        Type shape = typeof(PrimitiveArray<>).MakeGenericType(elementType);
        if (value?.GetType() != shape)
        {
            problems.Add(label + ": expected " + shape.Name + "<" + elementType.Name + ">, got " + (value?.GetType().ToString() ?? "null"));
            return;
        }

        if (OperationOutcome.Render(value) != OperationOutcome.Render(expected))
        {
            problems.Add(label + ": expected " + OperationOutcome.Render(expected) + ", got " + OperationOutcome.Render(value));
        }
    }

    /// <summary>Finds the array in a parsed value: struct members by name, and after a <c>|</c> a union's member view.</summary>
    /// <param name="value">The parsed root.</param>
    /// <param name="arrayPath">The path, such as <c>v|w</c>.</param>
    /// <returns>The array value.</returns>
    private static object? Select(StructValue value, string arrayPath)
    {
        string[] parts = arrayPath.Split('|');
        object? current = value[parts[0]];
        return parts.Length == 1 ? current : ((UnionValue)current!).Members[parts[1]];
    }
}
