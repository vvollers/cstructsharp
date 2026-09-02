namespace CStructSharp.Fuzzing;

using System.Globalization;
using System.Text;
using System.Text.Json;

/// <summary>Loads the bounded, versioned seed corpus used by the managed fuzz harness.</summary>
public sealed class FuzzCorpus
{
    /// <summary>Gets the corpus schema version.</summary>
    public int SchemaVersion { get; init; }

    /// <summary>Gets the stable hexadecimal mutation seed.</summary>
    public string Seed { get; init; } = string.Empty;

    /// <summary>Gets the default number of generated mutations per target.</summary>
    public int IterationsPerTarget { get; init; }

    /// <summary>Gets the hard maximum size of one generated input.</summary>
    public int MaxInputBytes { get; init; }

    /// <summary>Gets the resource budgets applied by every target.</summary>
    public FuzzLimits Limits { get; init; } = new();

    /// <summary>Gets the retained target-specific seed cases.</summary>
    public FuzzTargetCorpus[] Targets { get; init; } = [];

    /// <summary>Loads and minimally validates a corpus document.</summary>
    public static FuzzCorpus Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        FuzzCorpus corpus = JsonSerializer.Deserialize<FuzzCorpus>(
                                File.ReadAllText(path),
                                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ??
                            throw new InvalidDataException("The managed fuzz corpus is empty.");
        if (corpus.SchemaVersion != 1 ||
            corpus.IterationsPerTarget <= 0 ||
            corpus.MaxInputBytes <= 0 ||
            corpus.Targets.Length == 0)
        {
            throw new InvalidDataException("The managed fuzz corpus has invalid required metadata.");
        }

        _ = corpus.GetSeed();
        foreach (FuzzTargetCorpus target in corpus.Targets)
        {
            if (string.IsNullOrWhiteSpace(target.Id) || target.Seeds.Length == 0)
            {
                throw new InvalidDataException("Every managed fuzz target requires an id and retained seeds.");
            }

            foreach (FuzzSeed seed in target.Seeds)
            {
                byte[] bytes = seed.Decode();
                if (bytes.Length == 0 || bytes.Length > corpus.MaxInputBytes)
                {
                    throw new InvalidDataException(
                        $"Fuzz seed '{target.Id}/{seed.Id}' has an invalid decoded length.");
                }
            }
        }

        return corpus;
    }

    /// <summary>Parses the stable hexadecimal seed.</summary>
    public ulong GetSeed()
    {
        if (!this.Seed.StartsWith("0x", StringComparison.Ordinal) ||
            !ulong.TryParse(
                this.Seed.AsSpan(2),
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out ulong value))
        {
            throw new InvalidDataException("The managed fuzz seed is not a hexadecimal UInt64.");
        }

        return value;
    }
}

/// <summary>Defines the bounded public-operation limits shared by fuzz targets.</summary>
public sealed class FuzzLimits
{
    public int MaxDefinitionLength { get; init; }

    public int MaxLayoutNestingDepth { get; init; }

    public int MaxExpressionNestingDepth { get; init; }

    public int MaxExpressionTokens { get; init; }

    public int MaxArrayElements { get; init; }

    public long MaxStringBytes { get; init; }

    public long MaxTotalBytesRead { get; init; }

    public int MaxNestingDepth { get; init; }

    public int MaxPointerDepth { get; init; }

    public long MaxPointerTargetBytes { get; init; }

    public long MaxTotalBytesWritten { get; init; }
}

/// <summary>Groups the retained seeds for one named fuzz target.</summary>
public sealed class FuzzTargetCorpus
{
    public string Id { get; init; } = string.Empty;

    public FuzzSeed[] Seeds { get; init; } = [];
}

/// <summary>Stores one UTF-8 or hexadecimal retained seed.</summary>
public sealed class FuzzSeed
{
    public string Id { get; init; } = string.Empty;

    public string Encoding { get; init; } = string.Empty;

    public string Data { get; init; } = string.Empty;

    public byte[] Decode()
    {
        return this.Encoding switch
        {
            "utf8" => System.Text.Encoding.UTF8.GetBytes(this.Data),
            "hex" => Convert.FromHexString(this.Data),
            _ => throw new InvalidDataException($"Fuzz seed '{this.Id}' has an unknown encoding."),
        };
    }
}
