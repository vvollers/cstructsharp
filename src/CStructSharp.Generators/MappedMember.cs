namespace CStructSharp.Generators;

/// <summary>
///     One property of a mapped class, as the transform step extracted it. <see cref="FixedType"/> classifies the
///     property type for the direct members of a layout-bound class (see <c>MappedFixedEmitter</c>).
/// </summary>
internal sealed record MappedMember(
    string PropertyName,
    string? LayoutName,
    string TypeName,
    MappedMemberKind Kind,
    string ElementTypeName,
    bool IsNullableValue,
    bool IsInitOnly,
    string UnsupportedTypeName,
    SourceSpan Span,
    FixedType FixedType);

/// <summary>What a property type is to the direct members of a layout-bound class.</summary>
internal enum FixedTypeShape
{
    /// <summary>A type the direct members do not read or write.</summary>
    None,

    /// <summary>A primitive number or <c>bool</c>; <see cref="FixedType.Name"/> is its C# keyword.</summary>
    Primitive,

    /// <summary>An enum; <see cref="FixedType.Name"/> is the keyword of its underlying type.</summary>
    Enum,

    /// <summary>A <see cref="string"/>.</summary>
    String,

    /// <summary>A one-dimensional array of a primitive; <see cref="FixedType.Name"/> is the element's keyword.</summary>
    PrimitiveArray,

    /// <summary>A mapped class or struct.</summary>
    Mapped,

    /// <summary>A one-dimensional array of a mapped type; <see cref="FixedType.Name"/> is the element's fully qualified name.</summary>
    MappedArray,
}

/// <summary>
///     The classification of a mapped property's type for the direct members of a layout-bound class, as a value
///     so the incremental pipeline can compare it.
/// </summary>
/// <param name="Shape">What the type is.</param>
/// <param name="Name">The keyword or element type name the shape needs; empty otherwise.</param>
/// <param name="IsValueType">Whether a mapped type, or a mapped array's element, is a struct (which cannot be null).</param>
internal sealed record FixedType(FixedTypeShape Shape, string Name, bool IsValueType)
{
    /// <summary>A type the direct members do not read or write.</summary>
    public static FixedType None { get; } = new(FixedTypeShape.None, string.Empty, false);
}
