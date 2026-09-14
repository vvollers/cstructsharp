namespace CStructSharp;

using System;
using System.Collections.Generic;
using CStructSharp.Structure;

/// <summary>A persistent debug path shares parent segments without cloning declarations or path arrays.</summary>
internal sealed class DebugPath(DebugPath? parent, string name)
{
    private readonly string name = name;
    private readonly int length = checked((parent is null ? 0 : parent.length + 1) + name.Length);
    private string? formatted;

    public DebugPath? Parent { get; } = parent;

    /// <summary>Adapts selected-path metadata only when a caller requested debug output.</summary>
    public static DebugPath? FromElements(IEnumerable<CStructElement> elements)
    {
        DebugPath? result = null;
        foreach (CStructElement element in elements)
        {
            result = new DebugPath(result, element.Name.Name);
        }

        return result;
    }

    /// <summary>Formats the path once with one string allocation and no recursive traversal.</summary>
    public override string ToString()
    {
        return this.formatted ??= string.Create(this.length, this, static (characters, path) =>
        {
            int position = characters.Length;
            for (DebugPath? current = path; current is not null; current = current.Parent)
            {
                position -= current.name.Length;
                current.name.AsSpan().CopyTo(characters[position..]);
                if (current.Parent is not null)
                {
                    characters[--position] = '.';
                }
            }
        });
    }
}
