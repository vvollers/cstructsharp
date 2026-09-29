namespace CStructSharp.Tests;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

/// <summary>
///     The golden state of one running test: the outcomes it has checked, their keys and groups, and what the committed
///     manifest holds for it. While recording, the scope collects the reference outcomes into a new section; otherwise it
///     compares each outcome with the committed one (a readable outcome at once, a hashed group when the test ends) and
///     at the end reports committed entries the test no longer produced.
/// </summary>
/// <remarks>
///     A readable outcome's key is the enclosing parts (<see cref="Part"/>) joined by <c> / </c>, the caller's key, and
///     <c> #n</c> for its n-th repetition within the test. A hashed group is named by the enclosing parts alone. A
///     recorded test stays readable while its outcomes total at most <see cref="ReadableLimit"/> characters and every
///     line can be stored as it is, unless it is a sweep (a row of a data source member, such as every layout or corpus
///     case); otherwise its groups are hashed.
/// </remarks>
internal sealed class GoldenScope : IDisposable
{
    /// <summary>The largest total, in characters, of a test's outcomes and keys that is stored readably.</summary>
    public const int ReadableLimit = 8 * 1024;

    /// <summary>How many characters of a hashed group's outcomes a failure shows.</summary>
    private const int SampleLimit = 4096;

    /// <summary>The joiner of nested part names and keys.</summary>
    private const string Separator = " / ";

    /// <summary>Guards the scope against a test that checks outcomes from several threads.</summary>
    private readonly object gate = new();

    /// <summary>The enclosing parts, outermost first.</summary>
    private readonly List<string> parts = [];

    /// <summary>How often each readable key has been checked so far.</summary>
    private readonly Dictionary<string, int> occurrences = new(StringComparer.Ordinal);

    /// <summary>The hashed groups in the order the test first reached them.</summary>
    private readonly List<(string Name, IncrementalHash Hash, StringBuilder Sample)> groups = [];

    /// <summary>The hashed groups by name.</summary>
    private readonly Dictionary<string, int> groupIndex = new(StringComparer.Ordinal);

    /// <summary>The counts of each hashed group's outcomes, parallel to <see cref="groups"/>.</summary>
    private readonly List<int> counts = [];

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
    ///     outcome under the same key (readable) or adds it to its group's hash (hashed).
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
    ///     per group; <see langword="null"/> when it checked nothing.
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
                       : new GoldenSection(this.TestId, true, [.. this.groups.Select((group, index) => GoldenEntry.Hashed(group.Name, Hex(group.Hash), this.counts[index]))]);
        }
    }

    /// <summary>
    ///     At the end of a passed test, compares what it checked with its committed section: every hashed group's hash and
    ///     count, and that no committed outcome or group went unchecked (a stale entry) or is missing.
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
                for (int index = 0; index < this.groups.Count; index++)
                {
                    (string name, IncrementalHash hash, StringBuilder sample) = this.groups[index];
                    string actual = Hex(hash);
                    if (!committedSection.ByKey.TryGetValue(name, out GoldenEntry? entry))
                    {
                        failures.Append("no golden hash for the group '").Append(name).Append("'.\n");
                    }
                    else if (entry.Hash != actual || entry.Count != this.counts[index])
                    {
                        failures.Append("the group '").Append(name).Append("' differs from its golden outcomes: ").Append(this.counts[index]).Append(" outcomes hash to ")
                                .Append(actual).Append(", the golden ").Append(entry.Count).Append(" to ").Append(entry.Hash).Append(". The engine's first outcomes:\n")
                                .Append(sample).Append('\n');
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
            foreach ((_, IncrementalHash hash, _) in this.groups)
            {
                hash.Dispose();
            }
        }
    }

    /// <summary>Whether a line can be stored in a manifest as it is: no carriage return, no trailing whitespace, no line feed.</summary>
    /// <param name="line">The line.</param>
    /// <returns><see langword="true"/> when it can.</returns>
    private static bool IsStorableLine(string line)
        => !line.Contains('\r', StringComparison.Ordinal) && !line.Contains('\n', StringComparison.Ordinal) && (line.Length == 0 || !char.IsWhiteSpace(line[^1]));

    /// <summary>Turns a test id or group name into a file name: every character but letters, digits, '-' and '.' becomes '_'.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The file name.</returns>
    private static string FileName(string name) => string.Concat(name.Select(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '.' ? character : '_'));

    /// <summary>Returns a hash's current value as lowercase hexadecimal.</summary>
    /// <param name="hash">The incremental hash; it keeps accumulating afterwards.</param>
    /// <returns>64 hexadecimal digits.</returns>
    private static string Hex(IncrementalHash hash) => Convert.ToHexString(hash.GetCurrentHash()).ToLowerInvariant();

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

    /// <summary>Adds one outcome to its group's hash, as its length, a colon, the outcome and a line feed.</summary>
    /// <param name="group">The group's name.</param>
    /// <param name="text">The normalized outcome.</param>
    private void Hash(string group, string text)
    {
        if (!this.groupIndex.TryGetValue(group, out int index))
        {
            index = this.groups.Count;
            this.groupIndex.Add(group, index);
            this.groups.Add((group, IncrementalHash.CreateHash(HashAlgorithmName.SHA256), new StringBuilder()));
            this.counts.Add(0);
        }

        (_, IncrementalHash hash, StringBuilder sample) = this.groups[index];
        if (EngineGolden.DumpDirectory is { } dump)
        {
            // One file per test and group, so tests running in parallel never share one.
            string directory = Path.Combine(dump, this.ClassName, FileName(this.TestId));
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, (group.Length == 0 ? "test" : FileName(group)) + ".txt"), text + "\n--\n");
        }

        hash.AppendData(Encoding.UTF8.GetBytes(text.Length.ToString(CultureInfo.InvariantCulture) + ":" + text + "\n"));
        this.counts[index]++;
        if (!this.Recording && sample.Length < SampleLimit)
        {
            sample.Append(text.Length > SampleLimit ? text[..SampleLimit] : text).Append("\n--\n");
        }
    }

    /// <summary>Names the test in failure messages.</summary>
    /// <returns>The class and test id.</returns>
    private string Describe() => this.ClassName + "." + this.TestId;

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
