namespace CStructSharp.Generators.Tests;

using System;
using System.IO;
using System.Linq;
using System.Reflection;

/// <summary>
///     Golden-file comparison for generated code: <c>Snapshots/&lt;name&gt;.g.cs</c> must equal the generated
///     text byte for byte after line-ending normalization. <c>UPDATE_SNAPSHOTS=1</c> rewrites the file instead of
///     failing; a rewrite is a reviewed diff in the commit and a log entry (plan Appendix F).
/// </summary>
internal static class Snapshot
{
    private static readonly string Directory = typeof(Snapshot).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(attribute => attribute.Key == "SnapshotDirectory")
        .Value!;

    public static void Match(string name, string generated)
    {
        string path = Path.Combine(Directory, name + ".g.cs");
        string normalized = Normalize(generated);
        if (Environment.GetEnvironmentVariable("UPDATE_SNAPSHOTS") == "1")
        {
            File.WriteAllText(path, normalized);
            return;
        }

        Assert.IsTrue(File.Exists(path), $"Snapshot '{path}' does not exist; run with UPDATE_SNAPSHOTS=1 to create it.\n\nGenerated:\n{normalized}");
        string expected = Normalize(File.ReadAllText(path));
        if (expected != normalized)
        {
            int index = 0;
            while (index < expected.Length && index < normalized.Length && expected[index] == normalized[index])
            {
                index++;
            }

            int line = expected.Take(index).Count(character => character == '\n') + 1;
            Assert.Fail($"Snapshot '{name}' differs at line {line}.\n--- expected ---\n{Excerpt(expected, index)}\n--- generated ---\n{Excerpt(normalized, index)}\n\nRun with UPDATE_SNAPSHOTS=1 after reviewing the change.");
        }
    }

    /// <summary>Line endings and the generator version (which changes with every release) are not part of what a snapshot pins.</summary>
    private static string Normalize(string text)
    {
        string unified = text.Replace("\r\n", "\n").TrimEnd('\n') + "\n";
        return System.Text.RegularExpressions.Regex.Replace(unified, "GeneratedCode\\(\"CStructSharp\", \"[^\"]*\"\\)", "GeneratedCode(\"CStructSharp\", \"<version>\")");
    }

    private static string Excerpt(string text, int index)
    {
        int start = Math.Max(0, index - 200);
        int length = Math.Min(text.Length - start, 400);
        return text.Substring(start, length);
    }
}
