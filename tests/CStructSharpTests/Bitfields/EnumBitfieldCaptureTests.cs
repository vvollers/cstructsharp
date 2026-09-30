namespace CStructSharp.Tests;

using CStructSharp.Values;

/// <summary>
///     Verifies that an enum or flag bitfield named by a later count gives that count its own bits - the value a parse
///     shows - in every operation: the address, length, selected-read and update operations measure the preceding
///     fields through the address resolver, which must capture the field's bits, not its whole storage unit.
/// </summary>
[TestClass]
public class EnumBitfieldCaptureTests
{
    /// <summary>The execution paths that must agree.</summary>
    private static readonly ExecutionPath[] Paths = [ExecutionPath.Fastest, ExecutionPath.NoDirectAccess, ExecutionPath.NoFastPaths];

    /// <summary>
    ///     Gets the cases: a name, the definition, the bitfield packing, the bit allocation, and the input. Each layout
    ///     has a bitfield <c>k</c> sharing its storage unit with another bitfield, then <c>uint8 items[k]</c> and
    ///     <c>uint8 tail</c>, so the whole unit's value differs from <c>k</c>.
    /// </summary>
    public static IEnumerable<object[]> Cases =>
    [
        ["enum in the high bits", "enum kind : uint8 { A = 1, B = 2 }; struct r { uint8 lo : 4; kind k : 4; uint8 items[k]; uint8 tail; };", BitfieldPacking.SysV, BitfieldAllocation.LowBitFirst, new byte[] { 0x21, 7, 8, 9, }],
        ["enum in the low bits", "enum kind : uint8 { A = 1, B = 2 }; struct r { kind k : 4; uint8 hi : 4; uint8 items[k]; uint8 tail; };", BitfieldPacking.SysV, BitfieldAllocation.LowBitFirst, new byte[] { 0x52, 7, 8, 9, }],
        ["high bit first", "enum kind : uint8 { A = 1, B = 2 }; struct r { uint8 lo : 4; kind k : 4; uint8 items[k]; uint8 tail; };", BitfieldPacking.SysV, BitfieldAllocation.HighBitFirst, new byte[] { 0x32, 7, 8, 9, }],
        ["signed enum", "enum kind : int8 { NEG = -1, TWO = 2 }; struct r { uint8 lo : 4; kind k : 4; uint8 items[k]; uint8 tail; };", BitfieldPacking.SysV, BitfieldAllocation.LowBitFirst, new byte[] { 0x21, 7, 8, 9, }],
        ["signed enum with its top bit set", "enum kind : int8 { NEG = -1, TWO = 2 }; struct r { uint8 lo : 4; kind k : 4; uint8 items[k]; uint8 tail; };", BitfieldPacking.SysV, BitfieldAllocation.LowBitFirst, new byte[] { 0xE1, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 0x63, }],
        ["flag", "flag perms : uint8 { X = 1, Y = 2 }; struct r { uint8 lo : 4; perms k : 4; uint8 items[k]; uint8 tail; };", BitfieldPacking.SysV, BitfieldAllocation.LowBitFirst, new byte[] { 0x31, 7, 8, 9, 10, }],
        ["wider enum in a SysV unit", "enum kind : uint16 { A = 1, B = 2 }; struct r { uint8 lo : 4; kind k : 4; uint8 items[k]; uint8 tail; };", BitfieldPacking.SysV, BitfieldAllocation.LowBitFirst, new byte[] { 0x21, 7, 8, 9, }],
        ["wider enum in an MSVC unit", "enum kind : uint16 { A = 1, B = 2 }; struct r { uint8 lo : 4; kind k : 4; uint8 items[k]; uint8 tail; };", BitfieldPacking.Msvc, BitfieldAllocation.LowBitFirst, new byte[] { 0x05, 0x32, 0x00, 7, 8, 9, 10, }],
    ];

    /// <summary>
    ///     <c>GetArrayLength</c>, <c>ResolveAddress</c>, <c>ReadValue</c> of later fields, and <c>Update</c> of the tail
    ///     use the value of <c>k</c> that <c>Parse</c> reads, on every execution path.
    /// </summary>
    /// <param name="name">The case name.</param>
    /// <param name="definition">The layout source.</param>
    /// <param name="packing">The bitfield packing rule.</param>
    /// <param name="allocation">The bit allocation order.</param>
    /// <param name="data">The input.</param>
    [TestMethod]
    [DynamicData(nameof(Cases))]
    public void LaterOperations_CountWithTheBitfieldsOwnBits(string name, string definition, BitfieldPacking packing, BitfieldAllocation allocation, byte[] data)
    {
        var layout = new CStruct(definition, compilationOptions: new CStructCompilationOptions { BitfieldPacking = packing, BitfieldAllocation = allocation, });
        StructValue parsed = layout.Parse(data.AsSpan(), "r");
        int count = ((IEnumerable<object?>)parsed["items"]!).Count();
        byte tail = (byte)parsed["tail"]!;
        long tailOffset = layout.ParseWithDebug(data.AsSpan(), "r").Debug.Single(entry => entry.Path == "r.tail").Start;
        foreach (ExecutionPath path in Paths)
        {
            string label = name + " (" + path + ")";
            var read = new ReadOptions { ExecutionPath = path, };
            Assert.AreEqual(count, layout.GetArrayLength(data.AsSpan(), "r.items", null, read), label + ": GetArrayLength");
            Assert.AreEqual(tailOffset, layout.ResolveAddress(data.AsSpan(), "r.tail", null, read), label + ": ResolveAddress");
            Assert.AreEqual(tail, layout.ReadValue(data.AsSpan(), "r.tail", null, read), label + ": ReadValue");
            Assert.AreEqual(
                OperationOutcome.Render(parsed["items"]),
                OperationOutcome.Render(layout.ReadValue(data.AsSpan(), "r.items", null, read)),
                label + ": ReadValue of the array");

            byte[] updated = (byte[])data.Clone();
            layout.Update(updated.AsSpan(), "r.tail", (byte)0x42, null, new UpdateOptions { ExecutionPath = path, });
            byte[] expected = (byte[])data.Clone();
            expected[tailOffset] = 0x42;
            CollectionAssert.AreEqual(expected, updated, label + ": Update");
        }
    }
}
