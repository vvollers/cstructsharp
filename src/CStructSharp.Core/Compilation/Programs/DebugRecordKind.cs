namespace CStructSharp.Compilation.Programs;

/// <summary>
///     What a <see cref="ReadOpCode.DebugRecord"/> step records about the value the read step before it produced, as the
///     interpreter records the same value in a debug parse.
/// </summary>
internal enum DebugRecordKind
{
    /// <summary>The value itself, from the marked start to the position after the read.</summary>
    Value,

    /// <summary>
    ///     An enum or flag result's number (<see cref="System.Numerics.BigInteger"/>) rather than the result, which is what
    ///     the interpreter records for an enum member.
    /// </summary>
    EnumNumber,

    /// <summary>
    ///     A bitfield's value over its whole storage unit: from the unit's start to its end, although the position returns
    ///     to the unit's start while bits of the unit remain.
    /// </summary>
    Bitfield,
}
