namespace CStructSharp.Compilation;

/// <summary>References one canonical type symbol plus pointer shape accumulated from aliases.</summary>
/// <param name="Symbol">The canonical type symbol the name resolves to.</param>
/// <param name="PointerDepth">The number of pointer levels the alias chain adds on top of <paramref name="Symbol"/>; 0 for a plain type.</param>
/// <param name="TerminalName">The type name the alias chain ends at, such as <c>void</c> for <c>PVOID</c>.</param>
internal readonly record struct CompiledTypeReference(
    CompiledTypeSymbol Symbol,
    int PointerDepth,
    string TerminalName);
