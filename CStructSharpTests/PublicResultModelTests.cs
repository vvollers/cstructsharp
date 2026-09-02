namespace CStructSharp.Tests;

/// <summary>Defines the stable null and construction invariants of public semantic result values.</summary>
[TestClass]
public class PublicResultModelTests
{
    /// <summary>A stored null address is explicit and can never claim that a target was followed.</summary>
    [TestMethod]
    public void NullPointer_IsNeverReportedAsDereferenced()
    {
        var cstruct = new CStruct("struct root { uint8 *value; };", pointerSize: 1);

        dynamic parsed = cstruct.ParseStream(new MemoryStream([0]), "root");
        var pointer = (Pointer)parsed.value;

        Assert.IsTrue(pointer.IsNull);
        Assert.IsFalse(pointer.IsDereferenced);
        Assert.IsNull(pointer.Value);
        Assert.IsNull(pointer.Next);
        Assert.IsNull(pointer.Dereference());
        Assert.AreEqual(string.Empty, pointer.ToString());
    }

    /// <summary>Rejects states that contradict the documented address, depth, null, or follow status.</summary>
    [TestMethod]
    public void PointerConstructor_RejectsContradictoryResultStates()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Pointer(-1, null, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Pointer(1, null, 0));
        Assert.Throws<ArgumentException>(() => new Pointer(0, new object(), 1));
        Assert.Throws<ArgumentException>(() => new Pointer(0, null, 1, true));
        Assert.Throws<ArgumentException>(() => new Pointer(1, new object(), 1, false));
        Assert.Throws<ArgumentException>(() => new Pointer(1, null, 1, true));
    }

    /// <summary>Null, unresolved, and followed pointers expose distinct stable result states.</summary>
    [TestMethod]
    public void PointerResult_ExposesThreeUnambiguousStates()
    {
        var cstruct = new CStruct("struct root { uint8 *value; };", pointerSize: 1);

        dynamic nullResult = cstruct.ParseStream(new MemoryStream([0]), "root");
        var nullPointer = (Pointer)nullResult.value;
        Assert.IsTrue(nullPointer.IsNull);
        Assert.IsFalse(nullPointer.IsDereferenced);
        Assert.IsNull(nullPointer.Value);

        dynamic unresolvedResult = cstruct.ParseStream(
            new MemoryStream([1, 42]),
            "root",
            null,
            new ReadOptions { DereferencePointers = false });
        var unresolvedPointer = (Pointer)unresolvedResult.value;
        Assert.IsFalse(unresolvedPointer.IsNull);
        Assert.IsFalse(unresolvedPointer.IsDereferenced);
        Assert.IsNull(unresolvedPointer.Value);

        dynamic dereferencedResult = cstruct.ParseStream(new MemoryStream([1, 42]), "root");
        var dereferencedPointer = (Pointer)dereferencedResult.value;
        Assert.IsFalse(dereferencedPointer.IsNull);
        Assert.IsTrue(dereferencedPointer.IsDereferenced);
        Assert.AreEqual((byte)42, dereferencedPointer.Value);

        var addressInput = new Pointer(1, null, 1);
        Assert.IsFalse(addressInput.IsNull);
        Assert.IsFalse(addressInput.IsDereferenced);
        Assert.IsNull(addressInput.Value);
    }
}
