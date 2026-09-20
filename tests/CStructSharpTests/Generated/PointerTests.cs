namespace CStructSharpTests.Generated;

using System;
using CStructSharp.Generated;
using CStructSharp.Values;

/// <summary>Pins <see cref="Pointer{T}"/>: the runtime pointer's invariants and a lossless conversion both ways.</summary>
[TestClass]
public class PointerTests
{
    /// <summary><c>Pointer&lt;T&gt;</c> keeps the runtime pointer's invariants: null, address-only, and dereferenced states.</summary>
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
        Assert.AreEqual("address", Assert.Throws<ArgumentOutOfRangeException>(() => new Pointer<byte>(-1, 1)).ParamName);
        StringAssert.StartsWith(Assert.Throws<ArgumentOutOfRangeException>(() => new Pointer<byte>(-1, 1)).Message, "Pointer addresses cannot be negative.");
        Assert.AreEqual("depth", Assert.Throws<ArgumentOutOfRangeException>(() => new Pointer<byte>(1, 0)).ParamName);
        StringAssert.StartsWith(Assert.Throws<ArgumentOutOfRangeException>(() => new Pointer<byte>(1, 0)).Message, "Pointer depth must be greater than zero.");
        ArgumentException nullDereferenced = Assert.Throws<ArgumentException>(() => new Pointer<byte>(0, 1, 1, isDereferenced: true));
        Assert.AreEqual("isDereferenced", nullDereferenced.ParamName);
        StringAssert.StartsWith(nullDereferenced.Message, "A null pointer cannot be marked as dereferenced.");
        Assert.AreEqual(string.Empty, new Pointer<string>(4, 1, null, isDereferenced: true).ToString(), "a dereferenced null target prints as empty");
        Assert.AreEqual("0x10", new Pointer<byte>(16, 1).ToString(), "an address prints in hexadecimal");

        var resolved = new Pointer<byte>(4, 1, 0x2A, isDereferenced: true);
        Assert.AreEqual((byte)0x2A, resolved.Value);
        Assert.AreEqual("42", resolved.ToString());
        Assert.AreEqual(resolved, new Pointer<byte>(4, 1, 0x2A, true));
        Assert.IsTrue(resolved == new Pointer<byte>(4, 1, 0x2A, true));
        Assert.IsTrue(resolved != unresolved);
        Assert.AreEqual(resolved.GetHashCode(), new Pointer<byte>(4, 1, 0x2A, true).GetHashCode());
        Assert.IsFalse(resolved.Equals((object)unresolved));

        // Equality compares every part: the same address and depth with a different dereference state or value differ.
        Assert.AreNotEqual(resolved, new Pointer<byte>(4, 1, 0x2A, isDereferenced: false));
        Assert.AreNotEqual(resolved, new Pointer<byte>(4, 1, 0x2B, isDereferenced: true));
        Assert.AreNotEqual(resolved, new Pointer<byte>(4, 2, 0x2A, isDereferenced: true));
        Assert.AreNotEqual(resolved, new Pointer<byte>(5, 1, 0x2A, isDereferenced: true));
        Assert.IsFalse(new Pointer<byte>(4, 1) == new Pointer<byte>(4, 2), "equal addresses with different depths are not equal");
    }

    /// <summary><c>FromPointer</c>/<c>ToPointer</c> round-trip a typed pointer through the runtime's <c>Pointer</c> value.</summary>
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
        Assert.Throws<ArgumentNullException>(() => Pointer<ushort>.FromPointer(runtime, null!));
        Pointer<ushort> typedUnresolved = Pointer<ushort>.FromPointer(unresolved, _ => throw new InvalidOperationException("not called"));
        Assert.IsFalse(typedUnresolved.IsDereferenced);
        Assert.AreEqual(2, typedUnresolved.Depth);
        Pointer backUnresolved = typedUnresolved.ToPointer(_ => throw new InvalidOperationException("not called"));
        Assert.IsNull(backUnresolved.Value);
        Assert.Throws<ArgumentNullException>(() => Pointer<ushort>.FromPointer(null!, value => (ushort)value));
        Assert.Throws<ArgumentNullException>(() => typed.ToPointer(null!));
    }
}
