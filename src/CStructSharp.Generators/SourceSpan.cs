namespace CStructSharp.Generators;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

/// <summary>A location in the consumer's source, kept as values so the request record stays equatable.</summary>
internal readonly record struct SourceSpan(string FilePath, int Start, int Length, int StartLine, int StartCharacter, int EndLine, int EndCharacter)
{
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

    public Location ToLocation()
    {
        return Location.Create(
            this.FilePath,
            new TextSpan(this.Start, this.Length),
            new LinePositionSpan(new LinePosition(this.StartLine, this.StartCharacter), new LinePosition(this.EndLine, this.EndCharacter)));
    }
}
