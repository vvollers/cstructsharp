namespace CStructSharp.Tests;

using System.Globalization;
using System.Text;
using CStructSharp.Engine;
using CStructSharp.Fuzzing;

/// <summary>
///     The differential harness: runs one operation, renders its outcome canonically (<see cref="CanonicalText"/>), and
///     checks the rendering against the golden reference (<see cref="EngineGolden"/>), which fails with a line diff when
///     they differ.
/// </summary>
/// <remarks>
///     <para>
///         The stored outcome ends with the number of operations that reached the compiled engine
///         (<see cref="EngineDiagnostics"/>), so a run also notices an operation that takes a fast path it did not take
///         before, or the other way round.
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

    /// <summary>Asserts that the operation reproduces its golden rendering under the execution path.</summary>
    /// <param name="operation">The operation.</param>
    /// <param name="path">The execution path of the run.</param>
    /// <param name="alter">
    ///     A test-only change applied to the run's rendering before the comparison, used to prove that the harness detects
    ///     a planted difference; <see langword="null"/> in real cases.
    /// </param>
    /// <returns>The comparison: the rendering and the run's recorder.</returns>
    /// <exception cref="AssertFailedException">The rendering differs from the golden one.</exception>
    public static EngineComparison AssertSame(
        DifferentialOperation operation,
        ExecutionPath path = ExecutionPath.Fastest,
        Func<string, string>? alter = null)
    {
        string key = operation.Name + " (" + path + ")";
        var side = new EngineSide(path);
        string actual = Render(operation, side);
        if (alter is not null)
        {
            actual = alter(actual);
        }

        EngineGolden.Check(key, WithOperations(actual, side.Diagnostics.Runs));
        return new EngineComparison(actual, side.Diagnostics);
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

    /// <summary>
    ///     Appends the number of operations that reached the compiled engine to a rendering, which the golden outcome stores
    ///     with it under its established <c>decisions</c> label.
    /// </summary>
    /// <param name="rendering">The rendering.</param>
    /// <param name="operations">The number of operations the run sent to the engine.</param>
    /// <returns>The golden outcome.</returns>
    private static string WithOperations(string rendering, long operations)
        => rendering + "decisions = " + operations.ToString(CultureInfo.InvariantCulture) + "\n";

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
