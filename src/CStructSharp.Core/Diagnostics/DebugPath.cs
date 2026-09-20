namespace CStructSharp.Diagnostics;

using System;
using System.Collections.Generic;
using CStructSharp.Syntax;

/// <summary>A persistent debug path shares parent segments without cloning declarations or path arrays.</summary>
internal sealed class DebugPath(DebugPath? parent, string name)
{
    private readonly string name = name;
    private readonly int length = checked((parent is null ? 0 : parent.length + 1) + name.Length);
    private string? formatted;

    public DebugPath? Parent { get; } = parent;

    /// <summary>Adapts selected-path metadata only when a caller requested debug output.</summary>
    public static DebugPath? FromNames(IEnumerable<string> names)
    {
        DebugPath? result = null;
        foreach (string name in names)
        {
            result = new DebugPath(result, name);
        }

        return result;
    }

    /// <summary>Formats the path once with one string allocation and no recursive traversal.</summary>
    public override string ToString()
    {
        if (this.formatted is not null)
        {
            return this.formatted;
        }

        // Fill from the end so the walk up the parent chain needs no reversal.
        var characters = new char[this.length];
        int position = characters.Length;
        for (DebugPath? current = this; current is not null; current = current.Parent)
        {
            position -= current.name.Length;
            current.name.CopyTo(0, characters, position, current.name.Length);
            if (current.Parent is not null)
            {
                characters[--position] = '.';
            }
        }

        return this.formatted = new string(characters);
    }
}
