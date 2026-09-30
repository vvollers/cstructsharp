namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;

/// <summary>
///     Checks that a path operation reads and charges each byte before its target once: a variable-length value the path
///     passes (terminated text, LEB128) is not read again to find where it ends, and a whole terminated array the operation
///     reads is scanned by that read alone. The smallest total read budget an operation succeeds with shows the charge.
/// </summary>
[TestClass]
public class PathByteChargeTests
{
    /// <summary>
    ///     <c>struct s { cstring name; uint8 x; }</c> over <c>61 62 00 07</c>: reading <c>s.x</c> needs the budget a parse of
    ///     the whole struct needs, and resolving its address and updating it need one byte less, the target's own byte.
    /// </summary>
    [TestMethod]
    public void TextBeforeTheTarget_IsChargedOnce()
    {
        var layout = new CStruct("struct s { cstring name; uint8 x; };");
        byte[] data = [0x61, 0x62, 0x00, 0x07];

        // A parse consumes the four bytes, each charged once.
        int parse = MinimumBudget(budget => layout.Parse(data, "s", options: Read(budget)));
        Assert.AreEqual(4, parse);
        Assert.AreEqual(parse, MinimumBudget(budget => layout.ReadValue(data, "s.x", options: Read(budget))));
        Assert.AreEqual(parse - 1, MinimumBudget(budget => layout.ResolveAddress(new MemoryStream(data), "s.x", options: Read(budget))));
        Assert.AreEqual(parse - 1, MinimumBudget(budget => layout.Update(new MemoryStream([.. data]), "s.x", (byte)8, options: new UpdateOptions { MaxTraversalBytesRead = budget, })));
    }

    /// <summary>
    ///     <c>struct s { uleb128 v; uint8 x; }</c> over <c>81 01 07</c>: reading <c>s.x</c> past a two-byte LEB128 value needs
    ///     the budget a parse of the whole struct needs.
    /// </summary>
    [TestMethod]
    public void Leb128BeforeTheTarget_IsChargedOnce()
    {
        var layout = new CStruct("struct s { uleb128 v; uint8 x; };");
        byte[] data = [0x81, 0x01, 0x07];

        Assert.AreEqual(
            MinimumBudget(budget => layout.Parse(data, "s", options: Read(budget))),
            MinimumBudget(budget => layout.ReadValue(data, "s.x", options: Read(budget))));
    }

    /// <summary>
    ///     <c>struct g { cstring name; uint8 a[]; }</c> over <c>61 62 00 01 02 00</c>: the length of <c>g.a</c> (2) is charged
    ///     the text once and the array's three bytes once.
    /// </summary>
    [TestMethod]
    public void ArrayLengthAfterText_IsChargedOnce()
    {
        var layout = new CStruct("struct g { cstring name; uint8 a[]; };");
        byte[] data = [0x61, 0x62, 0x00, 0x01, 0x02, 0x00];

        Assert.AreEqual(2, layout.GetArrayLength(new MemoryStream(data), "g.a"));

        // The text is charged its bytes through the terminator (3); a struct of the text alone over the same
        // bytes is charged the same. The array adds its three bytes, scanned once.
        int text = MinimumBudget(budget => new CStruct("struct h { cstring name; };").Parse(data, "h", options: Read(budget)));
        Assert.AreEqual(3, text);
        Assert.AreEqual(text + 3, MinimumBudget(budget => layout.GetArrayLength(new MemoryStream(data), "g.a", options: Read(budget))));
    }

    /// <summary>
    ///     A whole terminated array read through a path is scanned once, by the read: <c>struct one { uint8 a[]; }</c> over
    ///     <c>01 02 00 09</c> reads <c>one.a</c> with the budget a parse of the struct needs - the three bytes it consumes,
    ///     since the scan charges nothing and the read charges each byte once - and the bare root <c>uint8[]</c> with the
    ///     same budget.
    /// </summary>
    [TestMethod]
    public void TerminatedArrayTarget_IsScannedOnce()
    {
        var layout = new CStruct("struct one { uint8 a[]; };");
        byte[] data = [0x01, 0x02, 0x00, 0x09];

        int parse = MinimumBudget(budget => layout.Parse(data, "one", options: Read(budget)));
        Assert.AreEqual(3, parse);
        Assert.AreEqual(parse, MinimumBudget(budget => layout.ReadValue(data, "one.a", options: Read(budget))));
        Assert.AreEqual(parse, MinimumBudget(budget => layout.ReadValue(data, "uint8[]", options: Read(budget))));
    }

    /// <summary>
    ///     <c>struct p { n* ptr; }</c> whose pointer addresses <c>struct n { uint8 a[]; }</c> over <c>01 02 00</c> right after
    ///     it: a parse needs the pointer's bytes and the target's three, each charged once.
    /// </summary>
    [TestMethod]
    public void PointerTargetArray_IsChargedOnce()
    {
        var layout = new CStruct("struct n { uint8 a[]; }; struct p { n* ptr; };");
        byte[] address = new byte[layout.PointerSize];
        address[0] = layout.PointerSize;
        byte[] data = [.. address, 0x01, 0x02, 0x00];

        Assert.AreEqual(layout.PointerSize + 3, MinimumBudget(budget => layout.Parse(data, "p", options: Read(budget))));
    }

    /// <summary>Read options with a total read budget.</summary>
    /// <param name="budget">The budget in bytes.</param>
    /// <returns>The options.</returns>
    private static ReadOptions Read(int budget) => new() { MaxTotalBytesRead = budget, };

    /// <summary>
    ///     Returns the smallest total read budget the operation succeeds with: every smaller budget must fail with a read-limit
    ///     failure, which the one just below the result is asserted to do.
    /// </summary>
    /// <param name="operation">The operation, given the budget in bytes to run with.</param>
    /// <returns>The smallest budget in bytes.</returns>
    private static int MinimumBudget(Action<int> operation)
    {
        for (int budget = 0; budget <= 64; budget++)
        {
            try
            {
                operation(budget);
            }
            catch (CStructReadLimitException)
            {
                // Too small a budget: the next one is tried.
                continue;
            }

            if (budget > 0)
            {
                int below = budget - 1;
                Assert.Throws<CStructReadLimitException>(() => operation(below));
            }

            return budget;
        }

        Assert.Fail("No budget up to 64 bytes let the operation succeed.");
        return -1;
    }
}
