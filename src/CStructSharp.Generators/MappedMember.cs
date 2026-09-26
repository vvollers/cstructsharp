namespace CStructSharp.Generators;

/// <summary>
///     One property of a mapped class, as the transform step extracted it. <see cref="FixedType"/> classifies the
///     property type for the direct members of a layout-bound class (see <c>MappedFixedEmitter</c>): <c>p:uint</c> for a
///     primitive, <c>e:byte</c> for an enum over that underlying type, <c>s</c> for a string, <c>ap:int</c> for an array
///     of a primitive, <c>c</c> (<c>cs</c> for a struct) for a mapped class, <c>ac:</c>/<c>acs:</c> plus the element type
///     for an array of one; empty for anything the direct members do not read.
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
    string FixedType = "");
