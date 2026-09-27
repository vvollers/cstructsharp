namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;

/// <summary>Checks that selected-path failures explain the invalid array or pointer traversal.</summary>
[TestClass]
public class AddressResolutionDiagnosticTests
{
    /// <summary>Invalid path accessors fail with the specific array, scalar or pointer rule they violate.</summary>
    /// <param name="path">The invalid selected path.</param>
    /// <param name="reason">The required diagnostic explanation.</param>
    [TestMethod]
    [DataRow("root.scalar[0]", "Field is not an indexable fixed array: scalar")]
    [DataRow("root.values[0][0]", "Too many array indices for values: expected at most 1, got 2.")]
    [DataRow("root.values[2]", "Array index 2 is out of range for values with length 2.")]
    [DataRow("root.values.child", "An array index is required before traversing: values")]
    [DataRow("root.scalar.child", "Cannot traverse through scalar field: scalar")]
    [DataRow("root.p.address[0]", "Pointer .address must be the terminal path segment.")]
    [DataRow("root.p.address.child", "Pointer .address must be the terminal path segment.")]
    [DataRow("root.p.other", "Expected pointer accessor '.value' or '.address' after: p")]
    [DataRow("root.pp.value.address.child", "Pointer .address must be the terminal path segment.")]
    [DataRow("root.pp.value.value[0]", "Pointer accessors cannot have array indexes.")]
    [DataRow("root.pp.value.other", "Expected another pointer accessor for a multi-level pointer.")]
    [DataRow("root.pp.value.value.child", "Cannot traverse beyond a scalar pointer target.")]
    public void InvalidTraversal_ExplainsTheViolatedRule(string path, string reason)
    {
        var layout = new CStruct("struct target { uint8 value; }; struct root { uint8 scalar; uint8 values[2]; target *p; uint8 **pp; };", pointerSize: 1);
        byte[] source = [1, 2, 3, 8, 9, 0, 0, 0, 42, 10, 11,];

        // p points to byte eight; pp points to a pointer at byte nine, whose target is byte ten.
        CStructPathException failure = Assert.Throws<CStructPathException>(() => layout.ResolveAddress(source, path));
        StringAssert.StartsWith(failure.Message, reason.TrimEnd('.') + " (path ");
        Assert.AreEqual(path, failure.Path);
    }

    /// <summary>A null pointer cannot be traversed to a child, and disabling dereference is diagnosed separately.</summary>
    [TestMethod]
    public void PointerTraversal_ExplainsNullAndDisabledTargets()
    {
        var layout = new CStruct("struct target { uint8 value; }; struct root { target *p; };", pointerSize: 1);

        // The pointer slot exists, but its stored address is the null address.
        CStructPathException nullTarget = Assert.Throws<CStructPathException>(() => layout.ResolveAddress(new byte[1], "root.p.value.value"));
        StringAssert.StartsWith(nullTarget.Message, "Cannot traverse through a null pointer target (path ");

        // Options reject dereference before the stored address needs to be followed.
        CStructPathException disabled = Assert.Throws<CStructPathException>(() => layout.ResolveAddress(new byte[1], "root.p.value", options: new ReadOptions { DereferencePointers = false, }));
        StringAssert.StartsWith(disabled.Message, "Pointer dereference is disabled for the selected path (path ");
    }

    /// <summary>A selected path that revisits an active typed pointer target reports the cycle at its stream address.</summary>
    [TestMethod]
    public void CyclicPointerPath_ReportsTheRepeatedAddress()
    {
        var layout = new CStruct("struct node { node *next; }; struct root { node *p; };", pointerSize: 1);

        // Both pointer slots target the node at byte one; the second dereference revisits it while active.
        CStructReadException failure = Assert.Throws<CStructReadException>(() => layout.ResolveAddress(new byte[] { 1, 1, }, "root.p.value.next.value.next.value"));
        StringAssert.StartsWith(failure.Message, "Cyclic pointer target detected at stream address 1 (path ");
    }
}
