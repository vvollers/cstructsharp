namespace CStructSharp.Generators;

using Microsoft.CodeAnalysis;

/// <summary>
///     The incremental source generator behind <c>[CStructLayout]</c> and <c>[CStructMapped]</c>. This first
///     version registers nothing; it exists so the Core sources are proven to compile on netstandard2.0 inside a
///     Roslyn component before any generation is written.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class CStructLayoutGenerator : IIncrementalGenerator
{
    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Registered in Phase 3.
    }
}
