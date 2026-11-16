namespace CStructSharp.Generators;

/// <summary>
///     One property of a mapped class, as the transform step extracted it. <see cref="FixedType"/> classifies the
///     property type for the direct members of a layout-bound class (see <c>MappedFixedEmitter</c>).
/// </summary>
/// <param name="PropertyName">The C# property name.</param>
/// <param name="LayoutName">The field name given by the property's member attribute; <see langword="null"/> to map by property name.</param>
/// <param name="TypeName">The fully qualified property type, with a nullable value type unwrapped.</param>
/// <param name="Kind">How the property maps, which decides the code emitted for it.</param>
/// <param name="ElementTypeName">The fully qualified element or pointer target type of a collection or typed pointer; empty otherwise.</param>
/// <param name="IsNullableValue">Whether the property type is a nullable value type (<c>T?</c>).</param>
/// <param name="IsInitOnly">Whether the property's setter is <c>init</c>-only.</param>
/// <param name="UnsupportedTypeName">The type CSG101 reports for an <see cref="MappedMemberKind.Unsupported"/> member; empty otherwise.</param>
/// <param name="Span">The property's source location, for diagnostics.</param>
/// <param name="FixedType">The type's classification for the direct members of a layout-bound class.</param>
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
