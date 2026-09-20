namespace CStructSharp.Generators.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CStructSharp;

/// <summary>The benchmark harness's fixture cases (<c>benchmarks/fixtures/cases/*.json</c>): definitions with bytes (inline hex, a data file, or a seeded xorshift stream), options, and an expected error.</summary>
internal static class BenchmarkFixtures
{
    public static string Root { get; } = FindRepositoryRoot();

    public static IReadOnlyList<BenchmarkFixture> Load(long maximumBytes)
    {
        var fixtures = new List<BenchmarkFixture>();
        foreach (string file in Directory.GetFiles(Path.Combine(Root, "benchmarks", "fixtures", "cases"), "*.json").OrderBy(name => name, StringComparer.Ordinal))
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { MaxDepth = 256 });
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("bytes", out JsonElement bytesSpec) || bytesSpec.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            byte[]? bytes = LoadBytes(bytesSpec, maximumBytes);
            if (bytes is null)
            {
                continue;
            }

            JsonElement options = root.GetProperty("options");
            var variables = new Dictionary<string, int>();
            if (root.TryGetProperty("variables", out JsonElement variableElement) && variableElement.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in variableElement.EnumerateObject())
                {
                    variables[property.Name] = property.Value.GetInt32();
                }
            }

            fixtures.Add(new BenchmarkFixture(
                root.GetProperty("id").GetString()!,
                root.GetProperty("definition").GetString()!,
                root.GetProperty("root").GetString()!,
                options.GetProperty("pointerSize").GetInt32(),
                options.GetProperty("aligned").GetBoolean(),
                options.GetProperty("littleEndian").GetBoolean(),
                bytes,
                variables,
                root.TryGetProperty("readOptions", out JsonElement readOptions) && readOptions.ValueKind == JsonValueKind.Object ? ToReadOptions(readOptions) : null,
                root.TryGetProperty("expectedError", out JsonElement error) && error.ValueKind == JsonValueKind.String ? error.GetString() : null));
        }

        return fixtures;
    }

    private static byte[]? LoadBytes(JsonElement spec, long maximumBytes)
    {
        switch (spec.GetProperty("kind").GetString())
        {
        case "hex":
            return Convert.FromHexString(spec.GetProperty("hex").GetString()!);
        case "file":
            {
                string path = Path.Combine(Root, "benchmarks", "fixtures", spec.GetProperty("file").GetString()!);
                return new FileInfo(path).Length <= maximumBytes ? File.ReadAllBytes(path) : null;
            }

        case "xorshift":
            {
                int size = spec.GetProperty("size").GetInt32();
                if (size > maximumBytes)
                {
                    return null;
                }

                // One xorshift32 step per byte, low byte taken - the harness's shared generator.
                uint state = spec.GetProperty("seed").GetUInt32();
                if (state == 0)
                {
                    state = 1;
                }

                var bytes = new byte[size];
                for (int index = 0; index < size; index++)
                {
                    state ^= state << 13;
                    state ^= state >> 17;
                    state ^= state << 5;
                    bytes[index] = (byte)(state & 0xff);
                }

                return bytes;
            }

        default:
            return null;
        }
    }

    private static ReadOptions ToReadOptions(JsonElement element)
    {
        int maxArrayElements = 1_000_000;
        long maxTotalBytesRead = 64 * 1024 * 1024;
        long maxStringBytes = 16 * 1024 * 1024;
        int maxPointerDepth = 64;
        int maxNestingDepth = 256;
        var addressingMode = PointerAddressingMode.Absolute;
        long origin = 0;
        bool dereferencePointers = true;
        foreach (JsonProperty property in element.EnumerateObject())
        {
            switch (property.Name)
            {
            case "maxArrayElements":
                maxArrayElements = property.Value.GetInt32();
                break;
            case "maxTotalBytesRead":
                maxTotalBytesRead = property.Value.GetInt64();
                break;
            case "maxStringBytes":
                maxStringBytes = property.Value.GetInt64();
                break;
            case "maxPointerDepth":
                maxPointerDepth = property.Value.GetInt32();
                break;
            case "maxNestingDepth":
                maxNestingDepth = property.Value.GetInt32();
                break;
            case "addressingMode":
                addressingMode = Enum.Parse<PointerAddressingMode>(property.Value.GetString()!, ignoreCase: true);
                break;
            case "origin":
                origin = property.Value.GetInt64();
                break;
            case "dereferencePointers":
                dereferencePointers = property.Value.GetBoolean();
                break;
            }
        }

        return new ReadOptions
        {
            MaxArrayElements = maxArrayElements,
            MaxTotalBytesRead = maxTotalBytesRead,
            MaxStringBytes = maxStringBytes,
            MaxPointerDepth = maxPointerDepth,
            MaxNestingDepth = maxNestingDepth,
            AddressingMode = addressingMode,
            Origin = origin,
            DereferencePointers = dereferencePointers,
        };
    }

    private static string FindRepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null && !File.Exists(Path.Combine(directory, "CStructSharp.sln")))
        {
            directory = Path.GetDirectoryName(directory);
        }

        return directory ?? throw new InvalidOperationException("The repository root was not found above " + AppContext.BaseDirectory);
    }
}
