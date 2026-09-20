namespace CStructSharpTests.Generated;

using System;
using CStructSharp.Generated;
using CStructSharp.Values;

/// <summary>Pins <see cref="Pointer{T}"/>: the runtime pointer's invariants and a lossless conversion both ways.</summary>
[TestClass]
public class PointerTests
{
    [TestMethod]
    public void Invariants_MatchTheRuntimePointer()
    {
        var unresolved = new Pointer<byte>(4, 1);
        Assert.AreEqual(4L, unresolved.Address);
        Assert.AreEqual(1, unresolved.Depth);
        Assert.IsFalse(unresolved.IsDereferenced);
        Assert.IsFalse(unresolved.IsNull);
        Assert.AreEqual("0x4", unresolved.ToString());
        Assert.IsTrue(new Pointer<byte>(0, 2).IsNull);
        Assert.AreEqual(default, new Pointer<byte>(4, 1, 9, isDereferenced: false).Value, "an unresolved pointer carries no target");
        Assert.Throws<ArgumentOutOfRangeException>(() => new Pointer<byte>(-1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Pointer<byte>(1, 0));
        Assert.Throws<ArgumentException>(() => new Pointer<byte>(0, 1, 1, isDereferenced: true));

        var resolved = new Pointer<byte>(4, 1, 0x2A, isDereferenced: true);
        Assert.AreEqual((byte)0x2A, resolved.Value);
        Assert.AreEqual("42", resolved.ToString());
        Assert.AreEqual(resolved, new Pointer<byte>(4, 1, 0x2A, true));
        Assert.IsTrue(resolved == new Pointer<byte>(4, 1, 0x2A, true));
        Assert.IsTrue(resolved != unresolved);
        Assert.AreEqual(resolved.GetHashCode(), new Pointer<byte>(4, 1, 0x2A, true).GetHashCode());
        Assert.IsFalse(resolved.Equals((object)unresolved));
    }

    [TestMethod]
    public void Conversions_RoundTripThroughTheRuntimePointer()
    {
        var runtime = new Pointer(8, (ushort)7, 1, isDereferenced: true);
        Pointer<ushort> typed = Pointer<ushort>.FromPointer(runtime, value => (ushort)value);
        Assert.AreEqual((ushort)7, typed.Value);
        Assert.AreEqual(8L, typed.Address);
        Pointer back = typed.ToPointer(value => value);
        Assert.AreEqual(runtime.Address, back.Address);
        Assert.AreEqual(runtime.Value, back.Value);
        Assert.IsTrue(back.IsDereferenced);

        var unresolved = new Pointer(8, null, 2);
        Pointer<ushort> typedUnresolved = Pointer<ushort>.FromPointer(unresolved, _ => throw new InvalidOperationException("not called"));
        Assert.IsFalse(typedUnresolved.IsDereferenced);
        Assert.AreEqual(2, typedUnresolved.Depth);
        Pointer backUnresolved = typedUnresolved.ToPointer(_ => throw new InvalidOperationException("not called"));
        Assert.IsNull(backUnresolved.Value);
        Assert.Throws<ArgumentNullException>(() => Pointer<ushort>.FromPointer(null!, value => (ushort)value));
        Assert.Throws<ArgumentNullException>(() => typed.ToPointer(null!));
    }
}
