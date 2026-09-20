namespace CStructSharp.Generators.Tests;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

/// <summary>The language contract's manual fixtures (<c>contracts/language/manual-fixtures-v1.json</c>): one valid definition per accepted declaration shape.</summary>
internal static class ManualFixtures
{
    public static IReadOnlyList<ManualFixture> Load()
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(System.AppContext.BaseDirectory, "manual-fixtures-v1.json")));
        var fixtures = new List<ManualFixture>();
        foreach (JsonElement pair in document.RootElement.GetProperty("featurePairs").EnumerateArray())
        {
            JsonElement valid = pair.GetProperty("valid");
            JsonElement compilation = valid.TryGetProperty("compilation", out JsonElement options) ? options : default;
            fixtures.Add(new ManualFixture(
                pair.GetProperty("id").GetString()!,
                valid.GetProperty("definition").GetString()!,
                valid.GetProperty("root").GetString()!,
                valid.GetProperty("pointerSize").GetInt32(),
                valid.GetProperty("aligned").GetBoolean(),
                valid.GetProperty("littleEndian").GetBoolean(),
                valid.TryGetProperty("bytes", out JsonElement bytes) ? bytes.GetString() : null,
                valid.TryGetProperty("variables", out JsonElement variables) ? variables.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.GetInt32()) : new Dictionary<string, int>(),
                compilation.ValueKind == JsonValueKind.Object && compilation.TryGetProperty("bitfieldAllocation", out JsonElement allocation) ? allocation.GetString() : null,
                compilation.ValueKind == JsonValueKind.Object && compilation.TryGetProperty("cLongWidth", out JsonElement width) ? width.GetInt32() : 0,
                compilation.ValueKind == JsonValueKind.Object && compilation.TryGetProperty("defaultEnumStorage", out JsonElement storage) ? storage.GetString() : null));
        }

        return fixtures;
    }

    /// <summary>The <c>[CStructLayout]</c> attribute arguments that reproduce the fixture's options.</summary>
    public static string AttributeArguments(ManualFixture fixture)
    {
        string arguments = "Root = \"" + fixture.Root + "\", PointerSize = " + fixture.PointerSize + ", Aligned = " + (fixture.Aligned ? "true" : "false") + ", LittleEndian = " + (fixture.LittleEndian ? "true" : "false");
        if (fixture.BitfieldAllocation is not null)
        {
            arguments += ", BitfieldAllocation = BitfieldAllocation." + fixture.BitfieldAllocation;
        }

        if (fixture.CLongWidth != 0)
        {
            arguments += ", CLongWidth = " + fixture.CLongWidth;
        }

        if (fixture.DefaultEnumStorage is not null)
        {
            arguments += ", DefaultEnumStorage = \"" + fixture.DefaultEnumStorage + "\"";
        }

        return arguments;
    }
}
