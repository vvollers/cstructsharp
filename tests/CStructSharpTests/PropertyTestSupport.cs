namespace CStructSharpTests;

using System.Globalization;

/// <summary>Provides deterministic generation, shrinking, and replay diagnostics without adding a test-only runtime dependency.</summary>
internal static class PropertyTestSupport
{
    /// <summary>Runs a generated property and minimizes the first counterexample before failing the test.</summary>
    /// <typeparam name="T">The generated case model.</typeparam>
    /// <param name="propertyName">A stable diagnostic name for the property.</param>
    /// <param name="seed">The fixed generator seed printed in replay diagnostics.</param>
    /// <param name="trials">The number of generated cases to execute.</param>
    /// <param name="generate">Creates the next case from the stable random source.</param>
    /// <param name="assertion">Throws when a generated case violates the property.</param>
    /// <param name="shrink">Produces simpler candidates in deterministic preference order.</param>
    /// <param name="format">Creates a stable replay description and shrink-cycle key.</param>
    public static void Check<T>(
        string propertyName,
        ulong seed,
        int trials,
        Func<StableRandom, T> generate,
        Action<T> assertion,
        Func<T, IEnumerable<T>> shrink,
        Func<T, string> format)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(trials);
        var random = new StableRandom(seed);

        for (int trial = 0; trial < trials; trial++)
        {
            T value = generate(random);
            Exception? failure = CaptureFailure(value, assertion);
            if (failure is null)
            {
                continue;
            }

            (T minimal, Exception minimalFailure, int shrinkSteps) =
                Minimize(value, failure, assertion, shrink, format);
            Assert.Fail(
                $"""
                 Property '{propertyName}' failed.
                 Replay: seed=0x{seed.ToString("X16", CultureInfo.InvariantCulture)}, trial={trial}, trials={trials}.
                 Shrink steps: {shrinkSteps}.
                 Minimal counterexample: {format(minimal)}
                 Failure: {minimalFailure}
                 """);
        }
    }

    private static (T Value, Exception Failure, int Steps) Minimize<T>(
        T original,
        Exception originalFailure,
        Action<T> assertion,
        Func<T, IEnumerable<T>> shrink,
        Func<T, string> format)
    {
        T current = original;
        Exception currentFailure = originalFailure;
        int steps = 0;
        var visited = new HashSet<string>(StringComparer.Ordinal) { format(original), };

        while (steps < 128)
        {
            bool foundSmaller = false;
            foreach (T candidate in shrink(current).Take(256))
            {
                string description = format(candidate);
                if (!visited.Add(description))
                {
                    continue;
                }

                Exception? candidateFailure = CaptureFailure(candidate, assertion);
                if (candidateFailure is null)
                {
                    continue;
                }

                current = candidate;
                currentFailure = candidateFailure;
                steps++;
                foundSmaller = true;
                break;
            }

            if (!foundSmaller)
            {
                break;
            }
        }

        return (current, currentFailure, steps);
    }

    private static Exception? CaptureFailure<T>(T value, Action<T> assertion)
    {
        try
        {
            assertion(value);
            return null;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return exception;
        }
    }

    /// <summary>Uses a small fixed algorithm so a recorded seed produces the same corpus on every supported TFM and OS.</summary>
    internal sealed class StableRandom
    {
        private ulong state;

        /// <summary>
        /// Initializes a new instance of the <see cref="StableRandom"/> class.
        /// </summary>
        /// <param name="seed">The retained nonzero corpus seed; zero selects a fixed fallback state.</param>
        public StableRandom(ulong seed)
        {
            this.state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
        }

        /// <summary>Returns the next stable pseudorandom Boolean.</summary>
        /// <returns>A value derived from the low bit of the next generator word.</returns>
        public bool NextBoolean()
        {
            return (this.NextUInt64() & 1) != 0;
        }

        /// <summary>Returns the next stable pseudorandom integer in a half-open range.</summary>
        /// <param name="exclusiveMaximum">The positive upper bound, which is not included.</param>
        /// <returns>A value greater than or equal to zero and less than <paramref name="exclusiveMaximum"/>.</returns>
        public int NextInt(int exclusiveMaximum)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(exclusiveMaximum);
            return (int)(this.NextUInt64() % (uint)exclusiveMaximum);
        }

        /// <summary>Advances the fixed xorshift generator and returns one 64-bit word.</summary>
        /// <returns>The next deterministic pseudorandom word.</returns>
        public ulong NextUInt64()
        {
            ulong value = this.state;
            value ^= value >> 12;
            value ^= value << 25;
            value ^= value >> 27;
            this.state = value;
            return value * 0x2545F4914F6CDD1DUL;
        }
    }
}
