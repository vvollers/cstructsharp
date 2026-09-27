namespace CStructSharp.Tests;

using System.Text.Json;

/// <summary>Locates the repository's shared fixtures and builds the input bytes a benchmark fixture describes.</summary>
internal static class TestFixtures
{
    /// <summary>Gets the repository root: the first directory above the test output that holds <c>CStructSharp.sln</c>.</summary>
    /// <exception cref="DirectoryNotFoundException">No such directory exists.</exception>
    public static string RepositoryRoot
    {
        get
        {
            string? directory = AppContext.BaseDirectory;
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory, "CStructSharp.sln")))
                {
                    return directory;
                }

                directory = Path.GetDirectoryName(directory);
            }

            throw new DirectoryNotFoundException("CStructSharp.sln not found above " + AppContext.BaseDirectory);
        }
    }

    /// <summary>Gets the benchmark fixture directory, which holds <c>manifest.json</c> and the <c>cases</c> folder.</summary>
    public static string BenchmarkFixtures => Path.Combine(RepositoryRoot, "benchmarks", "fixtures");

    /// <summary>
    ///     The input bytes a benchmark fixture describes: hex text, a file beside the fixtures, or <c>size</c> bytes from
    ///     a 32-bit xorshift generator started at <c>seed</c> (a zero seed starts at 1).
    /// </summary>
    /// <param name="spec">The fixture's input description.</param>
    /// <returns>The bytes.</returns>
    public static byte[] Materialize(JsonElement spec)
    {
        switch (spec.GetProperty("kind").GetString())
        {
        case "hex":
            return Convert.FromHexString(spec.GetProperty("hex").GetString()!);
        case "file":
            return File.ReadAllBytes(Path.Combine(BenchmarkFixtures, spec.GetProperty("file").GetString()!));
        default:
            return Xorshift(spec.GetProperty("seed").GetUInt32(), spec.GetProperty("size").GetInt32());
        }
    }

    /// <summary>Deterministic pseudo-random bytes from a 32-bit xorshift generator (shifts 13, 17, 5).</summary>
    /// <param name="seed">The generator's start; zero is replaced by 1, since xorshift never leaves zero.</param>
    /// <param name="size">The number of bytes.</param>
    /// <returns>The bytes: each is the low byte of the next state.</returns>
    public static byte[] Xorshift(uint seed, int size)
    {
        uint x = seed == 0 ? 1 : seed;
        byte[] result = new byte[size];
        for (int index = 0; index < size; index++)
        {
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            result[index] = (byte)(x & 0xff);
        }

        return result;
    }
}
