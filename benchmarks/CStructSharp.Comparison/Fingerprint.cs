namespace CStructSharp.Comparison;

using System.Numerics;

/// <summary>
///     An order-sensitive 64-bit summary of a record's members. Every deserialize benchmark reads each member once
///     into a fingerprint, so lazy readers (views, FlatBuffers) do the same work as readers that build an object,
///     and verification can check that every reader saw the same values in the same order.
/// </summary>
/// <remarks>
///     One rotate and one exclusive-or per member keeps the consumer cheap (a few nanoseconds per record) and equal
///     for every benchmark. It is a check value, not a hash with collision guarantees.
/// </remarks>
public struct Fingerprint
{
    private ulong value;

    /// <summary>Gets the summary of the members added so far.</summary>
    public readonly ulong Value => this.value;

    /// <summary>Adds an integer member, widened to 64 bits.</summary>
    /// <param name="member">The member's value.</param>
    public void AddInteger(long member) => this.value = BitOperations.RotateLeft(this.value, 7) ^ (ulong)member;

    /// <summary>Adds a <see cref="float" /> member by its exact bit pattern.</summary>
    /// <param name="member">The member's value.</param>
    public void AddSingle(float member) => this.AddInteger(BitConverter.SingleToInt32Bits(member));

    /// <summary>Adds a <see cref="double" /> member by its exact bit pattern.</summary>
    /// <param name="member">The member's value.</param>
    public void AddDouble(double member) => this.AddInteger(BitConverter.DoubleToInt64Bits(member));
}
