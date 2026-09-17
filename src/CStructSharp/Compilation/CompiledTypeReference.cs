namespace CStructSharp.Compilation;

/// <summary>References one canonical type symbol plus pointer shape accumulated from aliases.</summary>
internal readonly record struct CompiledTypeReference(
    CompiledTypeSymbol Symbol,
    int PointerDepth,
    string TerminalName);
