namespace CStructSharp.Generators;

using Microsoft.CodeAnalysis;

/// <summary>A type that contains the attributed class, outermost first.</summary>
internal readonly record struct ContainingType(string Keyword, string Name)
{
    /// <summary>
    ///     The declaration keyword that re-declares a type as a partial type around the generated code: the attributed
    ///     type itself, or each type that contains it. An interface stays an interface, since a partial declaration must
    ///     use the same kind as the user's own declaration.
    /// </summary>
    /// <param name="type">The attributed type or one of its containing types.</param>
    /// <returns><c>class</c>, <c>struct</c>, <c>record</c>, <c>record struct</c>, or <c>interface</c>.</returns>
    public static string KeywordOf(INamedTypeSymbol type)
    {
        return type.TypeKind switch
        {
            TypeKind.Struct => type.IsRecord ? "record struct" : "struct",
            TypeKind.Interface => "interface",
            _ => type.IsRecord ? "record" : "class",
        };
    }
}
