namespace CStructSharp.Compilation.Programs;

/// <summary>A slot a bare name is published to under one qualified prefix.</summary>
/// <param name="Prefix">The active prefix, including its final dot (<c>hdr.</c>).</param>
/// <param name="Slot">The slot of <c>Prefix + name</c>.</param>
internal readonly record struct QualifiedTarget(string Prefix, int Slot);
