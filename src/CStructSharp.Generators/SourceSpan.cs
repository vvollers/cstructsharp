namespace CStructSharp.Generators;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

/// <summary>A location in the consumer's source, kept as values so the request record stays equatable.</summary>
internal readonly record struct SourceSpan(string FilePath, int Start, int Length, int StartLine, int StartCharacter, int EndLine, int EndCharacter)
{
    /// <summary>Copies a Roslyn location's file path, character span and line positions into a value.</summary>
    /// <param name="location">The location to snapshot; one outside a syntax tree gets an empty path.</param>
    /// <returns>An equatable span that <see cref="ToLocation"/> turns back into a location.</returns>
    public static SourceSpan From(Location location)
    {
        FileLinePositionSpan lines = location.GetLineSpan();
        return new SourceSpan(
            location.SourceTree?.FilePath ?? string.Empty,
            location.SourceSpan.Start,
            location.SourceSpan.Length,
            lines.StartLinePosition.Line,
            lines.StartLinePosition.Character,
            lines.EndLinePosition.Line,
            lines.EndLinePosition.Character);
    }

    /// <summary>Recreates a location for reporting a diagnostic at this span.</summary>
    /// <returns>A file location with the stored character span and zero-based line positions.</returns>
    public Location ToLocation()
    {
        return Location.Create(
            this.FilePath,
            new TextSpan(this.Start, this.Length),
            new LinePositionSpan(new LinePosition(this.StartLine, this.StartCharacter), new LinePosition(this.EndLine, this.EndCharacter)));
    }
}
