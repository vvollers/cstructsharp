namespace CStructSharp.Syntax;

using System;

/// <summary>
///     An <c>#include</c> line. The core does not read translation-unit files; the path is recorded on
///     <see cref="CStruct.Includes"/> so a caller that assembles headers itself can see what a definition expected.
/// </summary>
internal sealed class IncludeDirective : CStructElement
{
    /// <summary>Creates a directive for one <c>#include</c> line.</summary>
    /// <param name="path">The text between the delimiters, exactly as written.</param>
    /// <param name="isSystem">
    ///     Whether the path was written as <c>&lt;path&gt;</c> rather than <c>"path"</c>.
    /// </param>
    public IncludeDirective(string path, bool isSystem)
    {
        this.Name = new Identifier(path);
        this.Path = path;
        this.IsSystem = isSystem;
    }

    /// <summary>Gets the included path as the element's name.</summary>
    public override Identifier Name { get; }

    /// <summary>The text between the delimiters, exactly as written.</summary>
    public string Path { get; }

    /// <summary>Whether the path was written with angle brackets.</summary>
    public bool IsSystem { get; }

    /// <summary>Checks whether another element is an include of the same path with the same delimiters.</summary>
    /// <param name="other">The element to compare with.</param>
    /// <returns>
    ///     <see langword="true"/> when <paramref name="other"/> is an include with an equal path and delimiter kind.
    /// </returns>
    public override bool Equals(CStructElement? other)
    {
        return other is IncludeDirective include && this.Path == include.Path && this.IsSystem == include.IsSystem;
    }

    /// <summary>Returns a hash code that matches this directive's equality rules.</summary>
    /// <returns>A hash of the path and the delimiter kind.</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(this.Path, this.IsSystem);
    }

    /// <summary>Returns a short readable description for debugging and logs.</summary>
    /// <returns>The directive as <c>Include: &lt;path&gt;</c> or <c>Include: "path"</c>.</returns>
    public override string ToString()
    {
        return $"Include: {(this.IsSystem ? "<" + this.Path + ">" : "\"" + this.Path + "\"")}";
    }
}
