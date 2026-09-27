namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;

/// <summary>Checks precise diagnostics when switch labels cannot form a valid static dispatch table.</summary>
[TestClass]
public class SwitchDiagnosticBoundaryTests
{
    /// <summary>Invalid labels distinguish failed constant evaluation from a repeated evaluated value.</summary>
    /// <param name="cases">The invalid switch arms.</param>
    /// <param name="reason">The expected explanation of the invalid labels.</param>
    [TestMethod]
    [DataRow("case (2147483647+1): {}", "Cannot evaluate switch case constant:")]
    [DataRow("case 2: {} case (1+1): {}", "Duplicate switch case value: 2")]
    public void InvalidLabels_ReportTheirStaticRestriction(string cases, string reason)
    {
        string definition = "struct root { uint8 tag; switch (tag) { " + cases + " } };";

        // Invalid dispatch metadata must be rejected before reading any input.
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => new CStruct(definition));

        StringAssert.Contains(failure.Message, reason);
    }

    /// <summary>
    ///     A switch whose arms declare no members is still a group of its body, so its labels are validated wherever it
    ///     is nested: in an if or switch arm, or in an inline struct.
    /// </summary>
    /// <param name="members">The struct members around the invalid, empty switch.</param>
    [TestMethod]
    [DataRow("uint8 tag; if (tag) { switch (tag) { case 1: {} case (0+1): {} } }")]
    [DataRow("uint8 tag; switch (tag) { case 2: { switch (tag) { case 1: {} case (0+1): {} } } }")]
    [DataRow("uint8 tag; struct { uint8 inner; switch (inner) { case 1: {} case (0+1): {} } } nested;")]
    [DataRow("uint8 tag; if (tag) { struct { uint8 inner; switch (inner) { case 1: {} case (0+1): {} } } nested; }")]
    public void InvalidLabels_InNestedEmptySwitchAreRejected(string members)
    {
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => new CStruct("struct root { " + members + " };"));
        StringAssert.Contains(failure.Message, "Duplicate switch case value: 1");
    }
}
