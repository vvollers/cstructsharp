namespace CStructSharp.Generators;

/// <summary>What a member of a <c>[CStructMapped]</c> class maps as: decides the code the mapper emits for it.</summary>
internal enum MappedMemberKind
{
    /// <summary>A scalar, string, enum, array, <c>StructValue</c>, <c>UnionValue</c>, runtime <c>Pointer</c>, <c>object</c>, or a mapped class: <c>Get&lt;T&gt;</c> converts it.</summary>
    Converted,

    /// <summary>A <c>List&lt;T&gt;</c>, <c>IList&lt;T&gt;</c>, or <c>ICollection&lt;T&gt;</c>: read as an array and copied into a list.</summary>
    List,

    /// <summary>An <c>IReadOnlyList&lt;T&gt;</c>, <c>IReadOnlyCollection&lt;T&gt;</c>, or <c>IEnumerable&lt;T&gt;</c>: read as an array.</summary>
    ReadOnlyCollection,

    /// <summary>A <c>CStructSharp.Generated.Pointer&lt;T&gt;</c>: the runtime pointer with its target converted.</summary>
    TypedPointer,

    /// <summary>A class that is neither mapped nor a value the reader produces: CSG101.</summary>
    Unsupported,
}
