namespace CStructSharp.Compilation.Programs;

/// <summary>
///     How one element of an array member is written, decided by checking in this order:
///     unnamed custom-codec padding, a pointer's address, an enum, a struct or union, a fixed-width number, or any other
///     codec value. The <see cref="WriteOpCode.WriteElements"/> and <see cref="WriteOpCode.WriteLeaves"/> steps carry it in
///     their <c>B</c>.
/// </summary>
internal enum WriteElementKind
{
    /// <summary>Unnamed padding of a caller's codec: the element's fixed extent written as zeroes, whatever the codec encodes.</summary>
    Zeroes,

    /// <summary>A pointer: only its stored address is written.</summary>
    Pointer,

    /// <summary>An enum or flag member, through the storage codec <c>A</c>.</summary>
    Enum,

    /// <summary>A struct or union, through the nested program <c>A</c>.</summary>
    Composite,

    /// <summary>A fixed-width number of codec <c>A</c>, converted by the codec's own conversion.</summary>
    Numeric,

    /// <summary>Any other value, through the stream writer of codec <c>A</c> (a caller's codec included).</summary>
    Codec,

    /// <summary>A value of a type that has no writer (<c>void</c>): the write fails (<see cref="WriteOpCode.FailNoWriter"/>).</summary>
    Unwritable,
}
