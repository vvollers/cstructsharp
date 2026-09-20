namespace CStructSharp.Generators;

/// <summary>One property of a mapped class, as the transform step extracted it.</summary>
internal sealed record MappedMember(
    string PropertyName,
    string? LayoutName,
    string TypeName,
    MappedMemberKind Kind,
    string ElementTypeName,
    bool IsNullableValue,
    bool IsInitOnly,
    string UnsupportedTypeName,
    SourceSpan Span);
