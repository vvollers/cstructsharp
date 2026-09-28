namespace CStructSharp.FixtureTool;

using System.Text.Json;

/// <summary>
///     Shared fixture materialization used by the fixture tool and the BenchmarkDotNet suite: locates the fixture
///     directory, reads documents, and reproduces byte payloads exactly as generate-fixtures.mjs and the JS harness do.
/// </summary>
public static class FixtureLoader
{
    /// <summary>
    ///     Walks up from a starting directory until it finds <c>benchmarks/fixtures/manifest.json</c>.
    /// </summary>
    /// <param name="start">The directory to search from; null starts at the application base directory.</param>
    /// <returns>The full path of the <c>benchmarks/fixtures</c> directory.</returns>
    /// <exception cref="DirectoryNotFoundException">No ancestor directory contains the fixture manifest.</exception>
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

    /// <summary>Reads one fixture from <c>cases/&lt;id&gt;.json</c>.</summary>
    /// <param name="fixtureDirectory">The fixture directory that contains the <c>cases</c> folder.</param>
    /// <param name="id">The fixture id, which is also its file name without extension.</param>
    /// <returns>The deserialized fixture.</returns>
    /// <exception cref="InvalidDataException">The fixture file deserializes to null.</exception>
    public static FixtureDocument Load(string fixtureDirectory, string id)
    {
        string path = Path.Combine(fixtureDirectory, "cases", id + ".json");
        return JsonSerializer.Deserialize<FixtureDocument>(File.ReadAllText(path), FixtureDocument.SerializerOptions)
               ?? throw new InvalidDataException("Fixture is empty: " + path);
    }

    /// <summary>Lazily reads every fixture under <c>cases/</c> in ordinal file-name order.</summary>
    /// <param name="fixtureDirectory">The fixture directory that contains the <c>cases</c> folder.</param>
    /// <returns>The fixtures, read one at a time as the sequence is enumerated.</returns>
    /// <exception cref="InvalidDataException">A fixture file deserializes to null.</exception>
    public static IEnumerable<FixtureDocument> LoadAll(string fixtureDirectory)
    {
        foreach (string path in Directory.GetFiles(Path.Combine(fixtureDirectory, "cases"), "*.json").Order(StringComparer.Ordinal))
        {
            yield return JsonSerializer.Deserialize<FixtureDocument>(File.ReadAllText(path), FixtureDocument.SerializerOptions)
                         ?? throw new InvalidDataException("Fixture is empty: " + path);
        }
    }

    /// <summary>Materializes the payload. Seeded generators mirror generate-fixtures.mjs xorshiftBytes exactly.</summary>
    /// <param name="fixtureDirectory">The fixture directory that <c>file</c> payload paths are relative to.</param>
    /// <param name="fixture">The fixture whose byte description is materialized.</param>
    /// <returns>A new array holding the fixture's input payload.</returns>
    /// <exception cref="InvalidDataException">
    ///     The fixture has no payload, a required generator field is missing, or the generator kind is unknown.
    /// </exception>
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

    /// <summary>
    ///     Generates deterministic pseudo-random bytes with a 32-bit xorshift (shifts 13, 17, 5), keeping the low byte
    ///     of each state. The sequence matches xorshiftBytes in generate-fixtures.mjs byte for byte.
    /// </summary>
    /// <param name="seed">The initial state; zero becomes one because xorshift never leaves the zero state.</param>
    /// <param name="size">The number of bytes to produce.</param>
    /// <returns>A new array of <paramref name="size"/> generated bytes.</returns>
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

    /// <summary>Compiles the fixture's layout source with its pointer size, alignment and byte order.</summary>
    /// <param name="fixture">The fixture whose definition and options are compiled.</param>
    /// <returns>The compiled layout.</returns>
    public static CStruct CreateLayout(FixtureDocument fixture)
    {
        return new CStruct(
            fixture.Definition,
            fixture.Options.PointerSize,
            fixture.Options.Aligned,
            fixture.Options.LittleEndian);
    }

    /// <summary>
    ///     Builds the read options for a fixture. Limits the fixture leaves unset keep their library defaults, and an
    ///     addressing mode other than <c>Relative</c> (case-insensitive) selects absolute pointer addressing.
    /// </summary>
    /// <param name="fixture">The fixture whose optional read settings are applied.</param>
    /// <returns>Default read options when the fixture has none; otherwise options with its overrides applied.</returns>
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
