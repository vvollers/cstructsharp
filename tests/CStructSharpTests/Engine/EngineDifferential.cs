namespace CStructSharp.Tests;

using System.Globalization;
using System.Text;
using CStructSharp.Engine;
using CStructSharp.Fuzzing;

/// <summary>
///     The differential harness: runs one operation with <see cref="EngineSelection.Automatic"/> selection, renders its
///     outcome canonically (<see cref="CanonicalText"/>), and checks the rendering against the golden reference
///     (<see cref="EngineGolden"/>), which fails with a line diff when they differ. Through the run's
///     <see cref="EngineDiagnostics"/> it also asserts whether the engine ran: by default as the operation expects
///     (<see cref="DifferentialOperation.Engine"/>).
/// </summary>
/// <remarks>
///     <para>
///         The golden outcome is the interpreter's: a recording run (and a comparing one,
///         <see cref="EngineGolden.ComparesInterpreter"/>) also runs the operation with the interpreter forced
///         (<see cref="EngineSelection.InterpreterOnly"/>), requires the two renderings and their numbers of
///         general-path decisions to agree, and checks the interpreter's rendering (a recording records it). The
///         stored outcome ends with that number of decisions, so an ordinary run also notices the engine taking a general
///         path the interpreter did not.
///     </para>
///     <para>
///         The case's key is the operation's name and execution path, so a case can be checked under every fast-path
///         restriction. Each run happens inside its own recording (<see cref="EngineDiagnostics.Record"/>), which is local
///         to the test's flow, so the assertions hold when MSTest runs tests in parallel.
///     </para>
/// </remarks>
internal static class EngineDifferential
{
    /// <summary>The number of unchanged lines shown around each changed line of a diff.</summary>
    private const int DiffContext = 3;

    /// <summary>
    ///     Asserts that automatic selection reproduces the golden rendering of <paramref name="operation"/>, and that the
    ///     engine ran exactly when <paramref name="expectEngine"/> (or, when it is <see langword="null"/>, the operation's
    ///     own <see cref="DifferentialOperation.Engine"/>) says so. When the run compares with the interpreter
    ///     (<see cref="EngineGolden.ComparesInterpreter"/>), it asserts that the interpreter renders the same and checks
    ///     (or, while recording, records) the interpreter's rendering instead.
    /// </summary>
    /// <param name="operation">The operation.</param>
    /// <param name="expectEngine">
    ///     Whether automatic selection must run the engine for every decision the operation makes; when
    ///     <see langword="false"/> it must run the engine for none; when <see langword="null"/> the operation's own
    ///     expectation applies, and an operation that expects nothing accepts either. An operation whose general path is
    ///     never reached (a direct fixed-root read, a pre-cancelled call) makes no decision and passes either way.
    /// </param>
    /// <param name="path">The execution path of the run.</param>
    /// <param name="alterAutomatic">
    ///     A test-only change applied to the automatic run's rendering before the comparison, used to prove that the
    ///     harness detects a planted difference; <see langword="null"/> in real cases.
    /// </param>
    /// <returns>The comparison: the rendering and the automatic run's recorder.</returns>
    /// <exception cref="AssertFailedException">
    ///     The rendering differs from the golden one (or, while recording, from the interpreter's), or the engine ran
    ///     contrary to the expectation.
    /// </exception>
    public static EngineComparison AssertSame(
        DifferentialOperation operation,
        bool? expectEngine = null,
        ExecutionPath path = ExecutionPath.Fastest,
        Func<string, string>? alterAutomatic = null)
    {
        string key = operation.Name + " (" + path + ")";
        EngineSide? interpreter = EngineGolden.ComparesInterpreter ? new EngineSide(EngineSelection.InterpreterOnly, path) : null;
        string? expected = interpreter is null ? null : Render(operation, interpreter);
        var automatic = new EngineSide(EngineSelection.Automatic, path);
        string actual = Render(operation, automatic);
        if (alterAutomatic is not null)
        {
            actual = alterAutomatic(actual);
        }

        EngineDiagnostics selected = automatic.Diagnostics;
        if (interpreter is null)
        {
            EngineGolden.Check(key, WithDecisions(actual, selected.Decisions));
        }
        else
        {
            // The golden outcome is the interpreter's; it is checked before the comparison, so a planted difference
            // consumes the same key in every mode.
            EngineDiagnostics forced = interpreter.Diagnostics;
            EngineGolden.Check(key, WithDecisions(expected!, forced.InterpreterSelections));
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                Assert.Fail(key + ": the interpreter (-) and automatic selection (+) differ:\n" + Diff(expected!, actual));
            }

            // The interpreter side never consults the engine; both sides decide the same number of times, or one of them
            // took a path the other did not.
            Assert.AreEqual(0, forced.EngineRuns + forced.Declines, operation.Name + ": the interpreter side asked the engine");
            Assert.AreEqual(forced.InterpreterSelections, selected.Decisions, operation.Name + ": the sides made different numbers of decisions");
        }

        expectEngine ??= operation.Engine;
        if (expectEngine == true)
        {
            Assert.AreEqual(0, selected.Declines, operation.Name + ": the engine declined: " + string.Join("; ", selected.RecentDeclines));
            Assert.AreEqual(selected.Decisions, selected.EngineRuns, operation.Name + ": the engine did not run every decision");
        }
        else if (expectEngine == false)
        {
            Assert.AreEqual(0, selected.EngineRuns, operation.Name + ": the engine ran although the case expects the interpreter");
        }

        return new EngineComparison(actual, selected);
    }

    /// <summary>
    ///     Runs the operation for one side inside a recording into the side's recorder, under the invariant culture, and
    ///     returns its rendering.
    /// </summary>
    /// <param name="operation">The operation.</param>
    /// <param name="side">The side.</param>
    /// <returns>The canonical rendering.</returns>
    public static string Render(DifferentialOperation operation, EngineSide side)
    {
        return EngineGolden.Invariant(
            () =>
            {
                var output = new CanonicalText();
                using (EngineDiagnostics.Record(side.Diagnostics))
                {
                    operation.Run(side, output);
                }

                return output.ToString();
            });
    }

    /// <summary>Appends the number of general-path decisions to a rendering, which the golden outcome stores with it.</summary>
    /// <param name="rendering">The rendering.</param>
    /// <param name="decisions">The number of decisions the run made.</param>
    /// <returns>The golden outcome.</returns>
    private static string WithDecisions(string rendering, long decisions)
        => rendering + "decisions = " + decisions.ToString(CultureInfo.InvariantCulture) + "\n";

    /// <summary>
    ///     A line diff of two renderings: unchanged lines start with two spaces, lines only in
    ///     <paramref name="expected"/> with <c>- </c>, lines only in <paramref name="actual"/> with <c>+ </c>; runs of
    ///     unchanged lines far from a change are elided.
    /// </summary>
    /// <param name="expected">The reference rendering.</param>
    /// <param name="actual">The compared rendering.</param>
    /// <returns>The diff.</returns>
    public static string Diff(string expected, string actual)
    {
        string[] left = expected.Split('\n');
        string[] right = actual.Split('\n');

        // Common leading and trailing lines are unchanged; only the middle needs the quadratic alignment.
        int prefix = 0;
        while (prefix < left.Length && prefix < right.Length && string.Equals(left[prefix], right[prefix], StringComparison.Ordinal))
        {
            prefix++;
        }

        int suffix = 0;
        while (suffix < left.Length - prefix && suffix < right.Length - prefix &&
               string.Equals(left[left.Length - 1 - suffix], right[right.Length - 1 - suffix], StringComparison.Ordinal))
        {
            suffix++;
        }

        var lines = new List<(char Kind, string Text)>();
        lines.AddRange(left.Take(prefix).Select(line => (' ', line)));
        lines.AddRange(Align(left[prefix..(left.Length - suffix)], right[prefix..(right.Length - suffix)]));
        lines.AddRange(left.Skip(left.Length - suffix).Select(line => (' ', line)));

        var diff = new StringBuilder();
        bool elided = false;
        for (int index = 0; index < lines.Count; index++)
        {
            bool nearChange = false;
            for (int near = Math.Max(0, index - DiffContext); near <= Math.Min(lines.Count - 1, index + DiffContext) && !nearChange; near++)
            {
                nearChange = lines[near].Kind != ' ';
            }

            if (!nearChange)
            {
                if (!elided)
                {
                    diff.Append("  ...\n");
                    elided = true;
                }

                continue;
            }

            elided = false;
            diff.Append(lines[index].Kind).Append(' ').Append(lines[index].Text).Append('\n');
        }

        return diff.ToString();
    }

    /// <summary>
    ///     Aligns two line lists by their longest common subsequence: unchanged lines are marked with a space, lines only
    ///     in <paramref name="left"/> with <c>-</c>, lines only in <paramref name="right"/> with <c>+</c>. Lists too long to
    ///     align are reported as entirely removed and added.
    /// </summary>
    /// <param name="left">The reference lines.</param>
    /// <param name="right">The compared lines.</param>
    /// <returns>The aligned lines.</returns>
    private static List<(char Kind, string Text)> Align(string[] left, string[] right)
    {
        var lines = new List<(char Kind, string Text)>();
        if ((long)left.Length * right.Length > 4_000_000)
        {
            lines.AddRange(left.Select(line => ('-', line)));
            lines.AddRange(right.Select(line => ('+', line)));
            return lines;
        }

        // lcs[i, j] is the length of the longest common subsequence of left[i..] and right[j..].
        var lcs = new int[left.Length + 1, right.Length + 1];
        for (int i = left.Length - 1; i >= 0; i--)
        {
            for (int j = right.Length - 1; j >= 0; j--)
            {
                lcs[i, j] = string.Equals(left[i], right[j], StringComparison.Ordinal) ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        int l = 0;
        int r = 0;
        while (l < left.Length || r < right.Length)
        {
            if (l < left.Length && r < right.Length && string.Equals(left[l], right[r], StringComparison.Ordinal))
            {
                lines.Add((' ', left[l++]));
                r++;
            }
            else if (l < left.Length && (r == right.Length || lcs[l + 1, r] >= lcs[l, r + 1]))
            {
                // On a tie the removed line comes first, so a changed line reads as "- old" then "+ new".
                lines.Add(('-', left[l++]));
            }
            else
            {
                lines.Add(('+', right[r++]));
            }
        }

        return lines;
    }
}
