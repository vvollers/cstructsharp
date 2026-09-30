namespace CStructSharp.Tests;

using System.Text;
using CStructSharp.Fuzzing;

/// <summary>
///     The golden harness of the engine tests: runs one operation, renders its outcome canonically
///     (<see cref="CanonicalText"/>), and checks the rendering against its golden outcome (<see cref="EngineGolden"/>),
///     which fails with a line diff when they differ.
/// </summary>
/// <remarks>
///     The case's key is the operation's name and execution path, so a case can be checked under every fast-path
///     restriction. The outcome records what an operation returns, writes and reports, not how many operations reached
///     the compiled engine: a test about that routing counts them itself with <c>EngineDiagnostics.Record</c>.
/// </remarks>
internal static class EngineDifferential
{
    /// <summary>The number of unchanged lines shown around each changed line of a diff.</summary>
    private const int DiffContext = 3;

    /// <summary>Asserts that the operation reproduces its golden outcome under the execution path.</summary>
    /// <param name="operation">The operation.</param>
    /// <param name="path">The execution path of the run.</param>
    /// <param name="alter">
    ///     A test-only change applied to the run's rendering before the check, used to prove that the harness detects a
    ///     planted difference; <see langword="null"/> in real cases.
    /// </param>
    /// <returns>The run's rendering.</returns>
    /// <exception cref="AssertFailedException">The outcome differs from the golden one.</exception>
    public static string AssertGolden(
        GoldenOperation operation,
        ExecutionPath path = ExecutionPath.Fastest,
        Func<string, string>? alter = null)
    {
        string actual = Render(operation, path);
        if (alter is not null)
        {
            actual = alter(actual);
        }

        EngineGolden.Check(operation.Name + " (" + path + ")", actual);
        return actual;
    }

    /// <summary>
    ///     Asserts that the operation reproduces its golden outcome under <see cref="ExecutionPath.NoFastPaths"/> (the
    ///     engine's member-by-member work) and then under <see cref="ExecutionPath.Fastest"/>, and that both runs render
    ///     the same outcome: the fast paths must not change what an operation returns, writes or reports.
    /// </summary>
    /// <param name="operation">The operation.</param>
    /// <param name="label">The case, for the failure message.</param>
    /// <exception cref="AssertFailedException">An outcome differs from its golden one, or the two paths disagree.</exception>
    public static void AssertPathsAgree(GoldenOperation operation, string label)
    {
        string memberByMember = AssertGolden(operation, ExecutionPath.NoFastPaths);
        string fastest = AssertGolden(operation, ExecutionPath.Fastest);
        if (!string.Equals(memberByMember, fastest, StringComparison.Ordinal))
        {
            Assert.Fail(label + ", " + operation.Name + ": the member-by-member path (-) and the fast paths (+) differ:\n" + Diff(memberByMember, fastest));
        }
    }

    /// <summary>Runs the operation under the execution path and the invariant culture, and returns its rendering.</summary>
    /// <param name="operation">The operation.</param>
    /// <param name="path">The execution path of the run.</param>
    /// <returns>The canonical rendering.</returns>
    private static string Render(GoldenOperation operation, ExecutionPath path)
    {
        return EngineGolden.Invariant(
            () =>
            {
                var output = new CanonicalText();
                operation.Run(path, output);
                return output.ToString();
            });
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
