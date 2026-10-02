namespace CStructSharp.Tests;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
///     The golden state of one running test: the outcomes it has checked, their keys and groups, and what the committed
///     manifest holds for it. While recording, the scope collects the reference outcomes into a new section; otherwise it
///     compares each outcome with the committed one (a readable outcome at once, a hashed group when the test ends) and
///     at the end reports committed entries the test no longer produced.
/// </summary>
/// <remarks>
///     A readable outcome's key is the enclosing parts (<see cref="Part"/>) joined by <c> / </c>, the caller's key, and
///     <c> #n</c> for its n-th repetition within the test. A hashed group is named by the enclosing parts alone and
///     stores its outcomes' hash and a summary that counts them by kind (<see cref="KindOf"/>), so a failing group shows
///     which kinds of outcome changed and an example of each. A recorded test stays readable while its outcomes total
///     at most <see cref="ReadableLimit"/> characters and every line can be stored as it is, unless it is a sweep (a row
///     of a data source member, such as every layout or corpus case); otherwise its groups are hashed.
/// </remarks>
internal sealed partial class GoldenScope : IDisposable
{
    /// <summary>The largest total, in characters, of a test's outcomes and keys that is stored readably.</summary>
    public const int ReadableLimit = 8 * 1024;

    /// <summary>The kind of an outcome that names no exception (<see cref="KindOf"/>).</summary>
    public const string SuccessKind = "ok";

    /// <summary>The most characters of a name's readable prefix that <see cref="FileName"/> keeps.</summary>
    internal const int FileNamePrefixLimit = 80;

    /// <summary>How many characters of an example outcome a failure shows.</summary>
    private const int SampleLimit = 2048;

    /// <summary>The joiner of nested part names and keys.</summary>
    private const string Separator = " / ";

    /// <summary>Guards the scope against a test that checks outcomes from several threads.</summary>
    private readonly object gate = new();

    /// <summary>The enclosing parts, outermost first.</summary>
    private readonly List<string> parts = [];

    /// <summary>How often each readable key has been checked so far.</summary>
    private readonly Dictionary<string, int> occurrences = new(StringComparer.Ordinal);

    /// <summary>The hashed groups in the order the test first reached them.</summary>
    private readonly List<Group> groups = [];

    /// <summary>The hashed groups by name.</summary>
    private readonly Dictionary<string, Group> groupIndex = new(StringComparer.Ordinal);

    /// <summary>The readable outcomes recorded so far, while the test may still be stored readably.</summary>
    private readonly List<GoldenEntry> readable = [];

    /// <summary>The readable keys compared so far.</summary>
    private readonly HashSet<string> consumed = new(StringComparer.Ordinal);

    /// <summary>Returns the committed section of the test, or <see langword="null"/> when there is none.</summary>
    private readonly Func<GoldenSection?> committed;

    /// <summary>The committed section, once looked up.</summary>
    private GoldenSection? section;

    /// <summary>Whether <see cref="section"/> has been looked up.</summary>
    private bool loaded;

    /// <summary>The characters of the outcomes and keys recorded so far.</summary>
    private long size;

    /// <summary>Whether every outcome recorded so far can be stored readably.</summary>
    private bool storable;

    /// <summary>Creates the scope of one test.</summary>
    /// <param name="className">The test class's name, without its namespace.</param>
    /// <param name="testId">The test's id (<see cref="GoldenTestIds.Of(TestContext)"/>).</param>
    /// <param name="recording">Whether the scope records reference outcomes instead of comparing with committed ones.</param>
    /// <param name="sweep">Whether the test is a sweep, whose recorded outcomes are always hashed.</param>
    /// <param name="committed">Returns the test's committed section, or <see langword="null"/>; called at most once.</param>
    public GoldenScope(string className, string testId, bool recording, bool sweep, Func<GoldenSection?> committed)
    {
        this.ClassName = className;
        this.TestId = testId;
        this.Recording = recording;
        this.storable = !sweep;
        this.committed = committed;
    }

    /// <summary>Gets the test class's name, without its namespace.</summary>
    public string ClassName { get; }

    /// <summary>Gets the test's id.</summary>
    public string TestId { get; }

    /// <summary>Gets whether the scope records reference outcomes.</summary>
    public bool Recording { get; }

    /// <summary>Gets whether the test has checked at least one outcome.</summary>
    public bool Used { get; private set; }

    /// <summary>
    ///     Returns an outcome's kind for a hashed group's summary: the simple name of the first exception type the outcome
    ///     names (a failure, such as <c>CStructReadException</c>), or <see cref="SuccessKind"/> when it names none.
    /// </summary>
    /// <param name="outcome">The normalized outcome.</param>
    /// <returns>The kind: one word.</returns>
    public static string KindOf(string outcome) => ExceptionName().Match(outcome) is { Success: true, } match ? match.Value : SuccessKind;

    /// <summary>
    ///     Enters a named part of the test, such as one layout variant of a sweep; outcomes checked inside it are keyed
    ///     and grouped under the name until the returned handle is disposed.
    /// </summary>
    /// <param name="name">The part's name: one line, without leading or trailing whitespace.</param>
    /// <returns>The handle that leaves the part.</returns>
    /// <exception cref="ArgumentException">The name cannot be stored in a manifest.</exception>
    public IDisposable Part(string name)
    {
        if (!IsStorableLine(name) || name.Length == 0 || char.IsWhiteSpace(name[0]))
        {
            throw new ArgumentException("A golden part name is one non-empty line without surrounding whitespace: '" + name + "'.", nameof(name));
        }

        lock (this.gate)
        {
            this.parts.Add(name);
            return new PartHandle(this, this.parts.Count);
        }
    }

    /// <summary>
    ///     Checks one outcome: records it as the reference while recording; otherwise compares it with the committed
    ///     outcome under the same key (readable) or adds it to its group's hash and summary (hashed).
    /// </summary>
    /// <param name="key">The outcome's key within the current part: one line, such as an operation's name.</param>
    /// <param name="outcome">The outcome's canonical rendering.</param>
    /// <exception cref="AssertFailedException">The outcome differs from the committed one, or none is committed.</exception>
    public void Check(string key, string outcome)
    {
        string text = EngineGolden.Normalize(outcome);
        lock (this.gate)
        {
            this.Used = true;
            string group = string.Join(Separator, this.parts);
            string caseKey = group.Length == 0 ? key : group + Separator + key;
            int occurrence = this.occurrences.GetValueOrDefault(caseKey) + 1;
            this.occurrences[caseKey] = occurrence;
            if (occurrence > 1)
            {
                caseKey += " #" + occurrence.ToString(CultureInfo.InvariantCulture);
            }

            if (this.Recording)
            {
                // Every outcome is hashed as well, so the test can still switch to hashed groups when a later outcome
                // makes it too large (or unstorable) to keep readable.
                this.Hash(group, text);
                this.size += caseKey.Length + text.Length;
                this.storable &= this.size <= ReadableLimit && IsStorableLine(caseKey) && text.Split('\n').All(IsStorableLine);
                if (this.storable)
                {
                    this.readable.Add(GoldenEntry.Readable(caseKey, text));
                }
                else
                {
                    this.readable.Clear();
                }

                return;
            }

            GoldenSection committedSection = this.Committed() ?? throw new AssertFailedException(
                                                 this.Describe() + " has no golden outcomes; record them if the test is new (CONTRIBUTING.md).");
            if (committedSection.Hashed)
            {
                this.Hash(group, text);
                return;
            }

            if (!committedSection.ByKey.TryGetValue(caseKey, out GoldenEntry? entry))
            {
                throw new AssertFailedException(this.Describe() + ": no golden outcome for '" + caseKey + "'; record it if the case is new (CONTRIBUTING.md).");
            }

            this.consumed.Add(caseKey);
            if (!string.Equals(entry.Text, text, StringComparison.Ordinal))
            {
                throw new AssertFailedException(caseKey + ": the golden outcome (-) and the engine (+) differ:\n" + EngineDifferential.Diff(entry.Text!, text));
            }
        }
    }

    /// <summary>
    ///     Returns the section this test recorded: readable when its outcomes are small and storable, otherwise one hash
    ///     and summary per group; <see langword="null"/> when it checked nothing.
    /// </summary>
    /// <returns>The section, or <see langword="null"/>.</returns>
    public GoldenSection? Recorded()
    {
        lock (this.gate)
        {
            if (!this.Used)
            {
                return null;
            }

            return this.storable
                       ? new GoldenSection(this.TestId, false, [.. this.readable])
                       : new GoldenSection(this.TestId, true, [.. this.groups.Select(group => GoldenEntry.Hashed(group.Name, group.Hex(), group.Count, group.Summary()))]);
        }
    }

    /// <summary>
    ///     At the end of a passed test, compares what it checked with its committed section: every hashed group's hash,
    ///     count and summary, and that no committed outcome or group went unchecked (a stale entry) or is missing.
    /// </summary>
    /// <exception cref="AssertFailedException">A group differs, or the section holds stale entries or lacks a group.</exception>
    public void Verify()
    {
        lock (this.gate)
        {
            GoldenSection? committedSection = this.Committed();
            if (committedSection is null)
            {
                // A test that checks nothing needs no section; one that checked something failed at its first check.
                return;
            }

            var failures = new StringBuilder();
            if (committedSection.Hashed)
            {
                foreach (Group group in this.groups)
                {
                    if (!committedSection.ByKey.TryGetValue(group.Name, out GoldenEntry? entry))
                    {
                        failures.Append("no golden hash for the group '").Append(group.Name).Append("'.\n");
                    }
                    else if (entry.Hash != group.Hex() || entry.Count != group.Count || entry.Summary != group.Summary())
                    {
                        failures.Append("the group '").Append(group.Name).Append("' differs from its golden outcomes:\n")
                                .Append("  golden: ").Append(entry.Count).Append(" outcomes (").Append(entry.Summary).Append("), hash ").Append(entry.Hash).Append('\n')
                                .Append("  engine: ").Append(group.Describe()).Append('\n')
                                .Append(group.Examples(entry.Summary!));
                    }
                }
            }

            string[] stale = [.. committedSection.Entries.Select(entry => entry.Key).Where(key => committedSection.Hashed ? !this.groupIndex.ContainsKey(key) : !this.consumed.Contains(key))];
            if (stale.Length > 0)
            {
                failures.Append("stale golden entries the test no longer produces (record the golden outcomes again): ").Append(string.Join(", ", stale.Select(key => "'" + key + "'"))).Append('\n');
            }

            if (failures.Length > 0)
            {
                throw new AssertFailedException(this.Describe() + ":\n" + failures);
            }
        }
    }

    /// <summary>Releases the hashes of the scope's groups.</summary>
    public void Dispose()
    {
        lock (this.gate)
        {
            foreach (Group group in this.groups)
            {
                group.Hash.Dispose();
            }
        }
    }

    /// <summary>Whether a line can be stored in a manifest as it is: no carriage return, no trailing whitespace, no line feed.</summary>
    /// <param name="line">The line.</param>
    /// <returns><see langword="true"/> when it can.</returns>
    private static bool IsStorableLine(string line)
        => !line.Contains('\r', StringComparison.Ordinal) && !line.Contains('\n', StringComparison.Ordinal) && (line.Length == 0 || !char.IsWhiteSpace(line[^1]));

    /// <summary>
    ///     Turns a test id or group name into a unique, portable file name: a readable prefix, where every character but
    ///     ASCII letters, digits, '-' and '.' becomes '_' and at most <see cref="FileNamePrefixLimit"/> characters are kept,
    ///     then '-' and the first 8 lowercase hex digits of the SHA-256 of the name's UTF-8 bytes.
    /// </summary>
    /// <remarks>
    ///     The prefix alone loses information: <c>primitive/int16&lt;</c> and <c>primitive/int16&gt;</c> would share a
    ///     file, and on a case-insensitive file system so would names that differ only in case. Tests run in parallel, so
    ///     a shared dump file mixes their outcomes or fails with an <see cref="IOException"/>. The hash of the exact name
    ///     keeps distinct names apart and is the same on every run and target framework.
    /// </remarks>
    /// <param name="name">The name.</param>
    /// <returns>The file name, made of ASCII letters, digits, '-', '.' and '_' only.</returns>
    internal static string FileName(string name)
    {
        // Keeps portable characters of the capped prefix and replaces every other character with an underscore.
        string prefix = string.Concat(name.Take(FileNamePrefixLimit).Select(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '.' ? character : '_'));
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name)), 0, 4).ToLowerInvariant();
        return prefix + "-" + hash;
    }

    /// <summary>Matches the simple name of an exception type, such as <c>CStructReadException</c>.</summary>
    /// <returns>The expression.</returns>
    [GeneratedRegex(@"\b[A-Z][A-Za-z0-9]*Exception\b", RegexOptions.CultureInvariant)]
    private static partial Regex ExceptionName();

    /// <summary>Returns the test's committed section, looking it up once.</summary>
    /// <returns>The section, or <see langword="null"/>.</returns>
    private GoldenSection? Committed()
    {
        if (!this.loaded)
        {
            this.section = this.committed();
            this.loaded = true;
        }

        return this.section;
    }

    /// <summary>Adds one outcome to its group's hash and summary, and to the dump directory when one is set.</summary>
    /// <param name="name">The group's name.</param>
    /// <param name="text">The normalized outcome.</param>
    private void Hash(string name, string text)
    {
        if (!this.groupIndex.TryGetValue(name, out Group? group))
        {
            group = new Group(name);
            this.groupIndex.Add(name, group);
            this.groups.Add(group);
        }

        if (EngineGolden.DumpDirectory is { } dump)
        {
            // One file per test and group, so tests running in parallel never share one.
            string directory = Path.Combine(dump, this.ClassName, FileName(this.TestId));
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, (name.Length == 0 ? "test" : FileName(name)) + ".txt"), text + "\n--\n");
        }

        group.Add(text, keepExamples: !this.Recording);
    }

    /// <summary>Names the test in failure messages.</summary>
    /// <returns>The class and test id.</returns>
    private string Describe() => this.ClassName + "." + this.TestId;

    /// <summary>
    ///     One hashed group of a test's outcomes: their SHA-256, their number, how many there are of each kind, and (while
    ///     comparing) the first outcome of each kind as the example a failure shows.
    /// </summary>
    private sealed class Group
    {
        /// <summary>The number of outcomes of each kind, in ordinal kind order.</summary>
        private readonly SortedDictionary<string, int> kinds = new(StringComparer.Ordinal);

        /// <summary>The first outcome of each kind, kept only while comparing.</summary>
        private readonly Dictionary<string, string> firstOfKind = new(StringComparer.Ordinal);

        /// <summary>Creates an empty group.</summary>
        /// <param name="name">The group's name: its enclosing parts, or empty for the whole test.</param>
        public Group(string name) => this.Name = name;

        /// <summary>Gets the group's name.</summary>
        public string Name { get; }

        /// <summary>Gets the hash of the outcomes so far.</summary>
        public IncrementalHash Hash { get; } = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        /// <summary>Gets the number of outcomes so far.</summary>
        public int Count { get; private set; }

        /// <summary>
        ///     Adds one outcome: to the hash, as its length in UTF-16 code units, a colon, the outcome and a line feed (the
        ///     length prefix keeps two different sequences from hashing alike), and to its kind's count.
        /// </summary>
        /// <param name="text">The normalized outcome.</param>
        /// <param name="keepExamples">Whether to keep the first outcome of each kind for a failure message.</param>
        public void Add(string text, bool keepExamples)
        {
            this.Hash.AppendData(Encoding.UTF8.GetBytes(text.Length.ToString(CultureInfo.InvariantCulture) + ":" + text + "\n"));
            this.Count++;
            string kind = KindOf(text);
            this.kinds[kind] = this.kinds.GetValueOrDefault(kind) + 1;
            if (keepExamples)
            {
                this.firstOfKind.TryAdd(kind, text.Length > SampleLimit ? text[..SampleLimit] + "\n..." : text);
            }
        }

        /// <summary>Returns the hash's current value as lowercase hexadecimal; the hash keeps accumulating afterwards.</summary>
        /// <returns>64 hexadecimal digits.</returns>
        public string Hex() => Convert.ToHexString(this.Hash.GetCurrentHash()).ToLowerInvariant();

        /// <summary>
        ///     Returns the summary: each kind and its number of outcomes, <see cref="SuccessKind"/> first and the failure
        ///     kinds in ordinal order, such as <c>ok 12, CStructReadException 3</c>.
        /// </summary>
        /// <returns>The summary: one line.</returns>
        public string Summary() => string.Join(", ", this.Kinds().Select(kind => kind + " " + this.kinds[kind].ToString(CultureInfo.InvariantCulture)));

        /// <summary>Describes the group for a failure message: its count, summary and hash.</summary>
        /// <returns>The description.</returns>
        public string Describe() => this.Count.ToString(CultureInfo.InvariantCulture) + " outcomes (" + this.Summary() + "), hash " + this.Hex();

        /// <summary>
        ///     Returns the engine's first outcome of each kind whose count differs from the golden summary; when every
        ///     kind kept its count, the first outcome of every kind. Each example is headed by its kind.
        /// </summary>
        /// <param name="golden">The golden summary.</param>
        /// <returns>The examples, each line ending with a line feed.</returns>
        public string Examples(string golden)
        {
            Dictionary<string, string> counts = golden.Split(", ").Select(item => item.Split(' ')).Where(item => item.Length == 2).ToDictionary(item => item[0], item => item[1], StringComparer.Ordinal);
            string[] changed = [.. this.Kinds().Where(kind => counts.GetValueOrDefault(kind) != this.kinds[kind].ToString(CultureInfo.InvariantCulture))];
            string[] shown = changed.Length > 0 ? changed : [.. this.Kinds()];
            var examples = new StringBuilder(changed.Length > 0 ? "  the engine's first outcome of each kind whose count changed:\n" : "  the engine's first outcome of each kind:\n");
            foreach (string kind in shown)
            {
                examples.Append("  [").Append(kind).Append("]\n").Append(this.firstOfKind[kind]).Append('\n');
            }

            return examples.ToString();
        }

        /// <summary>Returns the kinds seen so far: <see cref="SuccessKind"/> first, then the failure kinds in ordinal order.</summary>
        /// <returns>The kinds.</returns>
        private IEnumerable<string> Kinds() => this.kinds.Keys.OrderBy(kind => kind == SuccessKind ? 0 : 1);
    }

    /// <summary>Leaves a part when disposed, restoring the parts that enclosed it.</summary>
    private sealed class PartHandle : IDisposable
    {
        /// <summary>The scope whose part this is.</summary>
        private readonly GoldenScope scope;

        /// <summary>The number of parts while this one is entered; disposing trims the stack back to one fewer.</summary>
        private readonly int depth;

        /// <summary>Creates the handle.</summary>
        /// <param name="scope">The scope.</param>
        /// <param name="depth">The number of parts while this one is entered.</param>
        public PartHandle(GoldenScope scope, int depth)
        {
            this.scope = scope;
            this.depth = depth;
        }

        /// <summary>Leaves the part (and any part a caller forgot to leave inside it).</summary>
        public void Dispose()
        {
            lock (this.scope.gate)
            {
                int keep = Math.Min(this.depth - 1, this.scope.parts.Count);
                this.scope.parts.RemoveRange(keep, this.scope.parts.Count - keep);
            }
        }
    }
}
