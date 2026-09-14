namespace CStructSharp.FixtureTool;

using System.Text.Json;

/// <summary>
///     Shared fixture materialization used by the fixture tool and the BenchmarkDotNet suite: locates the fixture
///     directory, reads documents, and reproduces byte payloads exactly as generate-fixtures.mjs and the JS harness do.
/// </summary>
public static class FixtureLoader
{
    public static string FindFixtureDirectory(string? start = null)
    {
        string? directory = start ?? AppContext.BaseDirectory;
        while (directory is not null)
        {
            string candidate = Path.Combine(directory, "benchmarks", "fixtures", "manifest.json");
            if (File.Exists(candidate))
            {
                return Path.GetDirectoryName(candidate)!;
            }

            directory = Path.GetDirectoryName(directory);
        }

        throw new DirectoryNotFoundException("benchmarks/fixtures/manifest.json was not found above " + (start ?? AppContext.BaseDirectory));
    }

    public static FixtureDocument Load(string fixtureDirectory, string id)
    {
        string path = Path.Combine(fixtureDirectory, "cases", id + ".json");
        return JsonSerializer.Deserialize<FixtureDocument>(File.ReadAllText(path), FixtureDocument.SerializerOptions)
               ?? throw new InvalidDataException("Fixture is empty: " + path);
    }

    public static IEnumerable<FixtureDocument> LoadAll(string fixtureDirectory)
    {
        foreach (string path in Directory.GetFiles(Path.Combine(fixtureDirectory, "cases"), "*.json").Order(StringComparer.Ordinal))
        {
            yield return JsonSerializer.Deserialize<FixtureDocument>(File.ReadAllText(path), FixtureDocument.SerializerOptions)
                         ?? throw new InvalidDataException("Fixture is empty: " + path);
        }
    }

    /// <summary>Materializes the payload. Seeded generators mirror generate-fixtures.mjs xorshiftBytes exactly.</summary>
    public static byte[] MaterializeBytes(string fixtureDirectory, FixtureDocument fixture)
    {
        FixtureBytes bytes = fixture.Bytes ?? throw new InvalidDataException("Fixture has no bytes: " + fixture.Id);
        switch (bytes.Kind)
        {
        case "hex":
            return Convert.FromHexString(bytes.Hex ?? string.Empty);
        case "file":
            return File.ReadAllBytes(Path.Combine(fixtureDirectory, bytes.File ?? throw new InvalidDataException("Missing file name.")));
        case "xorshift":
            return XorshiftBytes(bytes.Seed ?? throw new InvalidDataException("Missing seed."), bytes.Size ?? throw new InvalidDataException("Missing size."));
        default:
            throw new InvalidDataException("Unknown byte generator kind: " + bytes.Kind);
        }
    }

    public static byte[] XorshiftBytes(uint seed, int size)
    {
        uint x = seed == 0 ? 1u : seed;
        byte[] result = new byte[size];
        for (int i = 0; i < size; i++)
        {
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            result[i] = (byte)(x & 0xff);
        }

        return result;
    }

    public static CStruct CreateLayout(FixtureDocument fixture)
    {
        return new CStruct(
            fixture.Definition,
            fixture.Options.PointerSize,
            fixture.Options.Aligned,
            fixture.Options.LittleEndian);
    }

    public static ReadOptions CreateReadOptions(FixtureDocument fixture)
    {
        FixtureReadOptions? source = fixture.ReadOptions;
        var defaults = new ReadOptions();
        if (source is null)
        {
            return defaults;
        }

        return new ReadOptions
        {
            AddressingMode = string.Equals(source.AddressingMode, "Relative", StringComparison.OrdinalIgnoreCase)
                                 ? PointerAddressingMode.Relative
                                 : PointerAddressingMode.Absolute,
            MaxArrayElements = source.MaxArrayElements ?? defaults.MaxArrayElements,
            MaxTotalBytesRead = source.MaxTotalBytesRead ?? defaults.MaxTotalBytesRead,
            MaxStringBytes = source.MaxStringBytes ?? defaults.MaxStringBytes,
            MaxPointerDepth = source.MaxPointerDepth ?? defaults.MaxPointerDepth,
        };
    }
}
