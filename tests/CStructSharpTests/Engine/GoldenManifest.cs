namespace CStructSharp.Tests;

using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

/// <summary>
///     The golden outcomes of one test class, stored in <c>Engine/Golden/&lt;class&gt;.txt</c>: one section per test (a
///     test method, or one data row of it), each holding either readable outcomes keyed by case, or the SHA-256 of each
///     group of outcomes. The differential tests compare the engine with these outcomes (<see cref="EngineGolden"/>).
/// </summary>
/// <remarks>
///     <para>
///         The file is plain text with line feeds only. After a fixed comment header, <c>@test &lt;id&gt;</c> starts a
///         section; <c>@case &lt;key&gt;</c> starts a readable outcome, whose lines follow, each indented by two spaces
///         (an empty line stays empty); <c>@hash &lt;sha256&gt; &lt;count&gt; &lt;group&gt;</c> records the hash of a
///         group's <c>count</c> outcomes. Sections are sorted by test id and entries keep the order the test produced
///         them, so a regeneration diff shows exactly the outcomes that changed. A blank line separates sections.
///     </para>
///     <para>
///         A group's hash covers each outcome, in order, as its length in UTF-16 code units, a colon, the outcome and a
///         line feed, encoded as UTF-8; the length prefix keeps two different sequences from hashing alike.
///     </para>
/// </remarks>
internal sealed class GoldenManifest
{
    /// <summary>The directive that starts a test's section.</summary>
    public const string TestDirective = "@test ";

    /// <summary>The directive that starts a readable outcome.</summary>
    public const string CaseDirective = "@case ";

    /// <summary>The directive that records a group's hash.</summary>
    public const string HashDirective = "@hash ";

    /// <summary>The indentation of a readable outcome's lines.</summary>
    public const string Indent = "  ";

    /// <summary>The loaded manifests by class name; a class without a file maps to <see langword="null"/>.</summary>
    private static readonly ConcurrentDictionary<string, Lazy<GoldenManifest?>> Loaded = new(StringComparer.Ordinal);

    /// <summary>Creates a manifest.</summary>
    /// <param name="className">The test class's name, without its namespace.</param>
    /// <param name="sections">The sections by test id.</param>
    public GoldenManifest(string className, IEnumerable<GoldenSection> sections)
    {
        this.ClassName = className;
        foreach (GoldenSection section in sections)
        {
            this.Sections.Add(section.TestId, section);
        }
    }

    /// <summary>Gets the directory that holds the manifests: <c>tests/CStructSharpTests/Engine/Golden</c>.</summary>
    public static string Directory => Path.Combine(TestFixtures.RepositoryRoot, "tests", "CStructSharpTests", "Engine", "Golden");

    /// <summary>Gets the test class's name, without its namespace.</summary>
    public string ClassName { get; }

    /// <summary>Gets the sections by test id, in ordinal order.</summary>
    public SortedDictionary<string, GoldenSection> Sections { get; } = new(StringComparer.Ordinal);

    /// <summary>Returns the path of a test class's manifest.</summary>
    /// <param name="className">The test class's name, without its namespace.</param>
    /// <returns>The file path.</returns>
    public static string PathOf(string className) => Path.Combine(Directory, className + ".txt");

    /// <summary>Returns a class's manifest as committed, loading it once per test run.</summary>
    /// <param name="className">The test class's name, without its namespace.</param>
    /// <returns>The manifest, or <see langword="null"/> when the class has none.</returns>
    /// <exception cref="FormatException">The manifest is malformed.</exception>
    public static GoldenManifest? Committed(string className)
        => Loaded.GetOrAdd(className, name => new Lazy<GoldenManifest?>(() => Load(name))).Value;

    /// <summary>Reads a class's manifest from disk.</summary>
    /// <param name="className">The test class's name, without its namespace.</param>
    /// <returns>The manifest, or <see langword="null"/> when the class has none.</returns>
    /// <exception cref="FormatException">The manifest is malformed.</exception>
    public static GoldenManifest? Load(string className)
    {
        string path = PathOf(className);
        return File.Exists(path) ? Parse(className, File.ReadAllText(path)) : null;
    }

    /// <summary>Parses a manifest's text; comment lines before the first section are ignored.</summary>
    /// <param name="className">The test class's name, without its namespace.</param>
    /// <param name="text">The manifest text.</param>
    /// <returns>The manifest.</returns>
    /// <exception cref="FormatException">A line is neither a directive nor part of an outcome, or an entry is malformed.</exception>
    public static GoldenManifest Parse(string className, string text)
    {
        var sections = new List<GoldenSection>();
        string? testId = null;
        bool? hashed = null;
        var entries = new List<GoldenEntry>();
        string? caseKey = null;
        var caseLines = new List<string>();
        string[] lines = text.Split('\n');
        int number = 0;

        // Ends the readable outcome being read: blank lines at its end separate sections and belong to no outcome.
        void EndCase()
        {
            if (caseKey is null)
            {
                return;
            }

            int count = caseLines.Count;
            while (count > 0 && caseLines[count - 1].Length == 0)
            {
                count--;
            }

            entries.Add(GoldenEntry.Readable(caseKey, string.Join("\n", caseLines.Take(count))));
            caseKey = null;
            caseLines.Clear();
        }

        // Ends the section being read.
        void EndSection()
        {
            EndCase();
            if (testId is not null)
            {
                sections.Add(new GoldenSection(testId, hashed ?? false, [.. entries]));
            }

            entries.Clear();
            hashed = null;
        }

        foreach (string line in lines)
        {
            number++;
            if (line.Contains('\r', StringComparison.Ordinal))
            {
                throw new FormatException(className + " line " + number + ": a golden manifest uses line feeds only.");
            }

            if (line.StartsWith(TestDirective, StringComparison.Ordinal))
            {
                EndSection();
                testId = line[TestDirective.Length..];
            }
            else if (line.StartsWith(CaseDirective, StringComparison.Ordinal) || line.StartsWith(HashDirective, StringComparison.Ordinal))
            {
                EndCase();
                bool hash = line.StartsWith(HashDirective, StringComparison.Ordinal);
                if (testId is null || (hashed is { } kind && kind != hash))
                {
                    throw new FormatException(className + " line " + number + ": an entry outside a section, or readable and hashed entries mixed in one section.");
                }

                hashed = hash;
                if (hash)
                {
                    entries.Add(ParseHash(className, number, line[HashDirective.Length..]));
                }
                else
                {
                    caseKey = line[CaseDirective.Length..];
                }
            }
            else if (caseKey is not null && (line.Length == 0 || line.StartsWith(Indent, StringComparison.Ordinal)))
            {
                caseLines.Add(line.Length == 0 ? line : line[Indent.Length..]);
            }
            else if (line.Length > 0 && !(testId is null && line.StartsWith('#')))
            {
                throw new FormatException(className + " line " + number + ": unexpected line '" + line + "'.");
            }
        }

        EndSection();
        return new GoldenManifest(className, sections);
    }

    /// <summary>
    ///     Renders the manifest in its canonical form: the comment header, then each section in test-id order, entries in
    ///     their recorded order, with a line feed after every line.
    /// </summary>
    /// <returns>The manifest text.</returns>
    public string Render()
    {
        var text = new StringBuilder();
        text.Append("# Golden outcomes of ").Append(this.ClassName).Append(": the reference each differential comparison checks the engine against.\n");
        text.Append("# Regenerate only for an intended, explained behaviour change: node tools/quality/engine-golden.mjs record (CONTRIBUTING.md).\n");
        text.Append("# @test starts a test; @case a readable outcome, indented below it; @hash <sha256> <count> <group> hashes a group's outcomes.\n");
        foreach (GoldenSection section in this.Sections.Values)
        {
            text.Append('\n').Append(TestDirective).Append(section.TestId).Append('\n');
            foreach (GoldenEntry entry in section.Entries)
            {
                if (section.Hashed)
                {
                    text.Append(HashDirective).Append(entry.Hash).Append(' ').Append(entry.Count.ToString(CultureInfo.InvariantCulture));
                    text.Append(entry.Key.Length == 0 ? string.Empty : " " + entry.Key).Append('\n');
                    continue;
                }

                text.Append(CaseDirective).Append(entry.Key).Append('\n');
                if (entry.Text!.Length == 0)
                {
                    continue;
                }

                foreach (string line in entry.Text.Split('\n'))
                {
                    text.Append(line.Length == 0 ? string.Empty : Indent + line).Append('\n');
                }
            }
        }

        return text.ToString();
    }

    /// <summary>Parses the operands of a <c>@hash</c> directive: the hash, the count and the (possibly empty) group.</summary>
    /// <param name="className">The class, for error messages.</param>
    /// <param name="number">The line number, for error messages.</param>
    /// <param name="operands">The text after the directive.</param>
    /// <returns>The hashed entry.</returns>
    /// <exception cref="FormatException">The hash is not 64 lowercase hexadecimal digits or the count is not a positive number.</exception>
    private static GoldenEntry ParseHash(string className, int number, string operands)
    {
        string[] parts = operands.Split(' ', 3);
        if (parts.Length < 2 || parts[0].Length != 64 || !parts[0].All(character => char.IsAsciiHexDigitLower(character) || char.IsAsciiDigit(character)) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int count) || count < 1)
        {
            throw new FormatException(className + " line " + number + ": expected '@hash <sha256> <count> <group>'.");
        }

        return GoldenEntry.Hashed(parts.Length == 3 ? parts[2] : string.Empty, parts[0], count);
    }
}
