namespace CStructSharp.Fuzzing;

using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

/// <summary>Runs retained seeds and deterministic bounded mutations with stable replay coordinates.</summary>
public sealed class FuzzSession
{
    private readonly FuzzCorpus corpus;
    private readonly FuzzTargets targets;

    /// <summary>Creates a session over a reviewed corpus, with targets bounded by the corpus limits.</summary>
    /// <param name="corpus">The corpus that supplies seeds, limits and retained inputs.</param>
    /// <exception cref="ArgumentNullException"><paramref name="corpus"/> is null.</exception>
    public FuzzSession(FuzzCorpus corpus)
    {
        this.corpus = corpus ?? throw new ArgumentNullException(nameof(corpus));
        this.targets = new FuzzTargets(corpus.Limits);
    }

    private static ulong DeriveTargetSeed(ulong seed, string targetName)
    {
        ulong value = seed ^ 0xCBF29CE484222325UL;
        foreach (byte item in Encoding.UTF8.GetBytes(targetName))
        {
            value ^= item;
            value *= 0x100000001B3UL;
        }

        return value;
    }

    private static byte[] Mutate(byte[] basis, StableFuzzRandom random, int maximumLength)
    {
        var bytes = new List<byte>(basis.Take(maximumLength));
        int operations = 1 + random.NextInt(8);
        for (int operation = 0; operation < operations; operation++)
        {
            switch (random.NextInt(6))
            {
            case 0 when bytes.Count > 0:
                int flipIndex = random.NextInt(bytes.Count);
                bytes[flipIndex] ^= (byte)(1 << random.NextInt(8));
                break;
            case 1 when bytes.Count > 0:
                bytes[random.NextInt(bytes.Count)] = random.NextByte();
                break;
            case 2 when bytes.Count < maximumLength:
                bytes.Insert(random.NextInt(bytes.Count + 1), random.NextByte());
                break;
            case 3 when bytes.Count > 1:
                bytes.RemoveAt(random.NextInt(bytes.Count));
                break;
            case 4 when bytes.Count > 1:
                int first = random.NextInt(bytes.Count);
                int second = random.NextInt(bytes.Count);
                (bytes[first], bytes[second]) = (bytes[second], bytes[first]);
                break;
            case 5 when bytes.Count > 0 && bytes.Count < maximumLength:
                int source = random.NextInt(bytes.Count);
                int count = Math.Min(1 + random.NextInt(8), bytes.Count - source);
                count = Math.Min(count, maximumLength - bytes.Count);
                bytes.InsertRange(random.NextInt(bytes.Count + 1), bytes.GetRange(source, count));
                break;
            }
        }

        return bytes.ToArray();
    }

    /// <summary>Runs one or every target and returns a deterministic outcome report.</summary>
    /// <param name="targetName">The target to run, or <c>all</c> to run every target in ordinal name order.</param>
    /// <param name="iterations">The mutations per target, or null for the corpus's iteration count.</param>
    /// <param name="seed">The run seed, or null for the corpus's seed.</param>
    /// <param name="maxInputBytes">The longest input, at most the corpus limit, or null for that limit.</param>
    /// <returns>A report with the seed, the limits used and one outcome summary per target.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     The iteration count is negative, or the input limit is not positive or exceeds the corpus limit.
    /// </exception>
    public FuzzReport Run(
        string targetName = "all",
        int? iterations = null,
        ulong? seed = null,
        int? maxInputBytes = null)
    {
        int effectiveIterations = iterations ?? this.corpus.IterationsPerTarget;
        int effectiveMaxInputBytes = maxInputBytes ?? this.corpus.MaxInputBytes;
        ArgumentOutOfRangeException.ThrowIfNegative(effectiveIterations);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(effectiveMaxInputBytes);
        if (effectiveMaxInputBytes > this.corpus.MaxInputBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxInputBytes),
                "A run cannot exceed the reviewed corpus input limit.");
        }

        ulong effectiveSeed = seed ?? this.corpus.GetSeed();
        string[] selectedNames = targetName == "all"
                                     ? FuzzTargets.Names
                                     : [targetName,];
        var reports = new List<FuzzTargetReport>(selectedNames.Length);
        foreach (string selectedName in selectedNames.Order(StringComparer.Ordinal))
        {
            FuzzTargetCorpus targetCorpus = this.corpus.Targets.Single(
                item => string.Equals(item.Id, selectedName, StringComparison.Ordinal));
            reports.Add(
                this.RunTarget(
                    this.targets.Resolve(selectedName),
                    targetCorpus,
                    effectiveIterations,
                    effectiveSeed,
                    effectiveMaxInputBytes));
        }

        return new FuzzReport
        {
            SchemaVersion = 1,
            Seed = "0x" + effectiveSeed.ToString("X16", CultureInfo.InvariantCulture),
            IterationsPerTarget = effectiveIterations,
            MaxInputBytes = effectiveMaxInputBytes,
            Targets = reports.ToArray(),
        };
    }

    /// <summary>Executes one exact input for replay or an external byte-oriented fuzz engine.</summary>
    /// <param name="targetName">The name of the target that receives the input.</param>
    /// <param name="input">The input bytes, at most the corpus input limit.</param>
    /// <returns>A report with the single target's outcome for this input.</returns>
    /// <exception cref="ArgumentException"><paramref name="targetName"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The input exceeds the corpus input limit.</exception>
    public FuzzReport RunSingle(string targetName, byte[] input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);
        ArgumentNullException.ThrowIfNull(input);
        if (input.Length > this.corpus.MaxInputBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input),
                "The replay input exceeds the reviewed corpus input limit.");
        }

        FuzzTarget target = this.targets.Resolve(targetName);
        using IncrementalHash digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int successes = 0;
        int documentedFailures = 0;
        this.ExecuteCase(
            target,
            input,
            "single-input",
            this.corpus.GetSeed(),
            0,
            digest,
            ref successes,
            ref documentedFailures);
        return new FuzzReport
        {
            SchemaVersion = 1,
            Seed = this.corpus.Seed,
            IterationsPerTarget = 0,
            MaxInputBytes = this.corpus.MaxInputBytes,
            Targets =
            [
                new FuzzTargetReport
                {
                    Id = targetName,
                    SeedCases = 1,
                    MutationCases = 0,
                    Successes = successes,
                    DocumentedFailures = documentedFailures,
                    Digest = Convert.ToHexString(digest.GetHashAndReset()),
                },
            ],
        };
    }

    private FuzzTargetReport RunTarget(
        FuzzTarget target,
        FuzzTargetCorpus targetCorpus,
        int iterations,
        ulong seed,
        int maxInputBytes)
    {
        ulong targetSeed = DeriveTargetSeed(seed, target.Name);
        var random = new StableFuzzRandom(targetSeed);
        using IncrementalHash digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int successes = 0;
        int documentedFailures = 0;

        for (int index = 0; index < targetCorpus.Seeds.Length; index++)
        {
            byte[] input = targetCorpus.Seeds[index].Decode();
            this.ExecuteCase(
                target,
                input,
                targetCorpus.Seeds[index].Id,
                seed,
                index,
                digest,
                ref successes,
                ref documentedFailures);
        }

        for (int iteration = 0; iteration < iterations; iteration++)
        {
            FuzzSeed basis = targetCorpus.Seeds[random.NextInt(targetCorpus.Seeds.Length)];
            byte[] input = Mutate(basis.Decode(), random, maxInputBytes);
            this.ExecuteCase(
                target,
                input,
                "mutation",
                seed,
                iteration,
                digest,
                ref successes,
                ref documentedFailures);
        }

        return new FuzzTargetReport
        {
            Id = target.Name,
            SeedCases = targetCorpus.Seeds.Length,
            MutationCases = iterations,
            Successes = successes,
            DocumentedFailures = documentedFailures,
            Digest = Convert.ToHexString(digest.GetHashAndReset()),
        };
    }

    private void ExecuteCase(
        FuzzTarget target,
        byte[] input,
        string source,
        ulong seed,
        int iteration,
        IncrementalHash digest,
        ref int successes,
        ref int documentedFailures)
    {
        byte outcome;
        try
        {
            target.Execute(input);
            outcome = 0;
            successes++;
        }
        catch (Exception exception) when (target.IsDocumentedFailure(exception))
        {
            outcome = 1;
            documentedFailures++;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw new FuzzFailureException(
                target.Name,
                seed,
                iteration,
                source,
                input,
                exception);
        }

        byte[] targetBytes = Encoding.UTF8.GetBytes(target.Name);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(length, input.Length);
        digest.AppendData(targetBytes);
        digest.AppendData(length);
        digest.AppendData(input);
        digest.AppendData([outcome,]);
    }
}
