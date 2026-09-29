namespace CStructSharp.Tests;

using System.Text;
using CStructSharp.Engine;
using CStructSharp.Fuzzing;

/// <summary>
///     The differential harness: runs one operation twice - once with the interpreter forced
///     (<see cref="EngineSelection.InterpreterOnly"/>) and once with <see cref="EngineSelection.Automatic"/> selection -
///     renders each outcome canonically (<see cref="CanonicalText"/>), and fails with a line diff when the renderings
///     differ. Through each side's <see cref="EngineDiagnostics"/> it also asserts whether the engine ran.
/// </summary>
/// <remarks>
///     Both sides use the same <see cref="ExecutionPath"/>, so a case can compare the engine with the interpreter under
///     every fast-path restriction. Each side runs inside its own recording (<see cref="EngineDiagnostics.Record"/>),
///     which is local to the test's flow, so the assertions hold when MSTest runs tests in parallel.
/// </remarks>
internal static class EngineDifferential
{
    /// <summary>The number of unchanged lines shown around each changed line of a diff.</summary>
    private const int DiffContext = 3;

    /// <summary>
    ///     Asserts that the interpreter and automatic selection produce the same rendering for
    ///     <paramref name="operation"/>, and that the engine ran exactly when <paramref name="expectEngine"/> says so.
    /// </summary>
    /// <param name="operation">The operation.</param>
    /// <param name="expectEngine">
    ///     Whether automatic selection must run the engine for every decision; when <see langword="false"/> it must run
    ///     the engine for none; when <see langword="null"/> either is accepted, which sweeps and corpora use so they
    ///     compare whatever the selector chooses as the engine gains features.
    /// </param>
    /// <param name="path">The execution path both sides use.</param>
    /// <param name="alterAutomatic">
    ///     A test-only change applied to the automatic side's rendering before the comparison, used to prove that the
    ///     harness detects a planted difference; <see langword="null"/> in real cases.
    /// </param>
    /// <returns>The comparison: the shared rendering and both sides' recorders.</returns>
    /// <exception cref="AssertFailedException">The renderings differ, or the engine ran contrary to a non-null <paramref name="expectEngine"/>.</exception>
    public static EngineComparison AssertSame(
        DifferentialOperation operation,
        bool? expectEngine = false,
        ExecutionPath path = ExecutionPath.Fastest,
        Func<string, string>? alterAutomatic = null)
    {
        var interpreter = new EngineSide(EngineSelection.InterpreterOnly, path);
        var automatic = new EngineSide(EngineSelection.Automatic, path);
        string expected = Render(operation, interpreter);
        string actual = Render(operation, automatic);
        if (alterAutomatic is not null)
        {
            actual = alterAutomatic(actual);
        }

        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            Assert.Fail(
                operation.Name + " (" + path + "): the interpreter (-) and automatic selection (+) differ:\n" + Diff(expected, actual));
        }

        // The interpreter side never consults the engine; both sides decide the same number of times, or one of them
        // took a path the other did not.
        EngineDiagnostics forced = interpreter.Diagnostics;
        EngineDiagnostics selected = automatic.Diagnostics;
        Assert.AreEqual(0, forced.EngineRuns + forced.Declines, operation.Name + ": the interpreter side asked the engine");
        Assert.AreEqual(forced.InterpreterSelections, selected.Decisions, operation.Name + ": the sides made different numbers of decisions");
        if (expectEngine == true)
        {
            Assert.IsTrue(selected.EngineRuns > 0, operation.Name + ": the engine did not run");
            Assert.AreEqual(0, selected.Declines, operation.Name + ": the engine declined: " + string.Join("; ", selected.RecentDeclines));
        }
        else if (expectEngine == false)
        {
            Assert.AreEqual(0, selected.EngineRuns, operation.Name + ": the engine ran although the case expects the interpreter");
        }

        return new EngineComparison(expected, forced, selected);
    }

    /// <summary>Runs the operation for one side inside a recording into the side's recorder and returns its rendering.</summary>
    /// <param name="operation">The operation.</param>
    /// <param name="side">The side.</param>
    /// <returns>The canonical rendering.</returns>
    public static string Render(DifferentialOperation operation, EngineSide side)
    {
        var output = new CanonicalText();
        using (EngineDiagnostics.Record(side.Diagnostics))
        {
            operation.Run(side, output);
        }

        return output.ToString();
    }

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
