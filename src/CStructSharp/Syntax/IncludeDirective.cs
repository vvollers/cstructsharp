namespace CStructSharp.Syntax;

using System;

/// <summary>
///     An <c>#include</c> line. The core does not read translation-unit files; the path is recorded on
///     <see cref="CStruct.Includes"/> so a caller that assembles headers itself can see what a definition expected.
/// </summary>
internal sealed class IncludeDirective : CStructElement
{
    public IncludeDirective(string path, bool isSystem)
    {
        this.Name = new Identifier(path);
        this.Path = path;
        this.IsSystem = isSystem;
    }

    public override Identifier Name { get; }

    /// <summary>The text between the delimiters, exactly as written.</summary>
    public string Path { get; }

    /// <summary>Whether the path was written with angle brackets.</summary>
    public bool IsSystem { get; }

    public override bool Equals(CStructElement? other)
    {
        return other is IncludeDirective include && this.Path == include.Path && this.IsSystem == include.IsSystem;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(this.Path, this.IsSystem);
    }

    public override string ToString()
    {
        return $"Include: {(this.IsSystem ? "<" + this.Path + ">" : "\"" + this.Path + "\"")}";
    }
}
