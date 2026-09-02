namespace CStructSharp.Tests;

using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

/// <summary>Executes every valid/invalid pair published by the Portable language manual.</summary>
[TestClass]
public class ManualLanguageFixtureTests
{
    private static readonly Lazy<ManualFixtureContract> Fixtures = new(LoadFixtures);
    private static readonly Lazy<PortableContract> Portable = new(LoadPortableContract);

    /// <summary>Proves the exact bytes, sizes, alignments, offsets, and values printed by every valid fixture.</summary>
    [TestMethod]
    public void ValidFixtures_PredictBytesOffsetsAndValues()
    {
        foreach (FeaturePair pair in Fixtures.Value.FeaturePairs)
        {
            ValidFixture fixture = pair.Valid;
            var cstruct = new CStruct(
                fixture.Definition,
                (byte)fixture.PointerSize,
                fixture.Aligned,
                fixture.LittleEndian);
            byte[] bytes = Convert.FromHexString(fixture.Bytes);

            Assert.AreEqual(fixture.Alignment, cstruct.GetStructAlignmentInBytes(fixture.Root), pair.Id);
            if (fixture.Size is not null)
            {
                Assert.AreEqual(fixture.Size.Value, cstruct.GetStructSizeInBytes(fixture.Root), pair.Id);
                Assert.AreEqual(fixture.Size.Value, bytes.Length, pair.Id);
            }

            var readOptions = new ReadOptions { DereferencePointers = false, };
            foreach (KeyValuePair<string, long> offset in fixture.Offsets)
            {
                using var input = new MemoryStream(bytes);
                Assert.AreEqual(
                    offset.Value,
                    cstruct.ResolveAddress(input, offset.Key, fixture.Variables, readOptions),
                    $"{pair.Id}: {offset.Key}");
                Assert.AreEqual(0L, input.Position, pair.Id);
            }

            using var parseInput = new MemoryStream(bytes);
            object parsed = cstruct.ParseStream(
                parseInput,
                fixture.Root,
                fixture.Variables,
                readOptions);
            foreach (KeyValuePair<string, string> expected in fixture.Values)
            {
                object? actual = SelectPath(parsed, fixture.Root, expected.Key);
                Assert.AreEqual(expected.Value, FormatValue(actual), $"{pair.Id}: {expected.Key}");
            }

            CollectionAssert.AreEqual(
                bytes,
                cstruct.Serialize(fixture.Root, parsed, fixture.Variables),
                pair.Id);
        }
    }

    /// <summary>Proves every paired unsupported form or bounded read reports its documented stable category.</summary>
    [TestMethod]
    public void InvalidFixtures_ReportStableErrorCategories()
    {
        Dictionary<string, UnsupportedConstruct> unsupportedById = Portable.Value.UnsupportedConstructs.
            ToDictionary(item => item.Id, StringComparer.Ordinal);

        foreach (FeaturePair pair in Fixtures.Value.FeaturePairs)
        {
            InvalidFixture fixture = pair.Invalid;
            CStructErrorCode expectedCode = Enum.Parse<CStructErrorCode>(fixture.ErrorCode);

            if (fixture.Stage == "compile")
            {
                Assert.IsTrue(
                    unsupportedById.TryGetValue(fixture.UnsupportedId, out UnsupportedConstruct? unsupported),
                    $"{pair.Id}: unknown unsupported fixture '{fixture.UnsupportedId}'.");
                CStructLayoutException failure = Assert.Throws<CStructLayoutException>(
                    () => new CStruct(unsupported.Definition),
                    pair.Id);
                Assert.AreEqual(expectedCode, failure.Code, pair.Id);
                continue;
            }

            var cstruct = new CStruct(
                fixture.Definition,
                (byte)fixture.PointerSize,
                fixture.Aligned,
                fixture.LittleEndian);
            using var input = new MemoryStream(Convert.FromHexString(fixture.Bytes));
            CStructException readFailure = Assert.Throws<CStructException>(
                () => cstruct.ParseStream(
                    input,
                    fixture.Root,
                    null,
                    new ReadOptions { MaxStringBytes = fixture.MaxStringBytes, }),
                pair.Id);
            Assert.AreEqual(expectedCode, readFailure.Code, pair.Id);
        }
    }

    private static object? SelectPath(object value, string rootName, string path)
    {
        string[] segments = path.Split('.');
        int start = string.Equals(segments[0], rootName, StringComparison.Ordinal) ? 1 : 0;
        object? current = value;
        for (int index = start; index < segments.Length; index++)
        {
            Match match = Regex.Match(
                segments[index],
                @"\A(?<name>[A-Za-z_][A-Za-z0-9_]*)(?:\[(?<index>[0-9]+)\])?\z",
                RegexOptions.CultureInvariant);
            Assert.IsTrue(match.Success, $"Invalid fixture value path '{path}'.");
            current = SelectMember(current, match.Groups["name"].Value, path);
            if (match.Groups["index"].Success)
            {
                int elementIndex = int.Parse(match.Groups["index"].Value, CultureInfo.InvariantCulture);
                current = SelectIndex(current, elementIndex, path);
            }
        }

        return current;
    }

    private static object? SelectMember(object? value, string name, string path)
    {
        Assert.IsNotNull(value, $"Cannot select '{name}' from null while resolving '{path}'.");
        if (value is IDictionary<string, object?> dictionary)
        {
            Assert.IsTrue(dictionary.TryGetValue(name, out object? selected), $"Missing '{name}' in '{path}'.");
            return selected;
        }

        if (value is IReadOnlyDictionary<string, object?> readOnlyDictionary)
        {
            Assert.IsTrue(
                readOnlyDictionary.TryGetValue(name, out object? selected),
                $"Missing '{name}' in '{path}'.");
            return selected;
        }

        PropertyInfo? property = value.GetType().GetProperty(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
        Assert.IsNotNull(property, $"Missing property '{name}' in '{path}'.");
        return property.GetValue(value);
    }

    private static object? SelectIndex(object? value, int index, string path)
    {
        Assert.IsNotNull(value, $"Cannot index null while resolving '{path}'.");
        if (value is IList list)
        {
            Assert.IsTrue(index < list.Count, $"Index {index} is outside '{path}'.");
            return list[index];
        }

        object?[] items = ((IEnumerable)value).Cast<object?>().ToArray();
        Assert.IsTrue(index < items.Length, $"Index {index} is outside '{path}'.");
        return items[index];
    }

    private static string FormatValue(object? value)
    {
        return value switch
        {
            null => "<null>",
            bool boolean => boolean ? "true" : "false",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };
    }

    private static ManualFixtureContract LoadFixtures()
    {
        return Deserialize<ManualFixtureContract>("manual-fixtures-v1.json");
    }

    private static PortableContract LoadPortableContract()
    {
        return Deserialize<PortableContract>("portable-v1.json");
    }

    private static T Deserialize<T>(string fileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, fileName);
        return JsonSerializer.Deserialize<T>(
                   File.ReadAllText(path),
                   new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ??
               throw new InvalidOperationException($"Fixture contract '{fileName}' is empty.");
    }

    private sealed class ManualFixtureContract
    {
        public FeaturePair[] FeaturePairs { get; init; } = [];
    }

    private sealed class FeaturePair
    {
        public string Id { get; init; } = string.Empty;

        public ValidFixture Valid { get; init; } = new();

        public InvalidFixture Invalid { get; init; } = new();
    }

    private sealed class ValidFixture
    {
        public string Definition { get; init; } = string.Empty;

        public string Root { get; init; } = string.Empty;

        public int PointerSize { get; init; }

        public bool Aligned { get; init; }

        public bool LittleEndian { get; init; }

        public int? Size { get; init; }

        public int Alignment { get; init; }

        public string Bytes { get; init; } = string.Empty;

        public Dictionary<string, int>? Variables { get; init; }

        public Dictionary<string, long> Offsets { get; init; } = new(StringComparer.Ordinal);

        public Dictionary<string, string> Values { get; init; } = new(StringComparer.Ordinal);
    }

    private sealed class InvalidFixture
    {
        public string Stage { get; init; } = string.Empty;

        public string UnsupportedId { get; init; } = string.Empty;

        public string ErrorCode { get; init; } = string.Empty;

        public string Definition { get; init; } = string.Empty;

        public string Root { get; init; } = string.Empty;

        public int PointerSize { get; init; }

        public bool Aligned { get; init; }

        public bool LittleEndian { get; init; }

        public string Bytes { get; init; } = string.Empty;

        public long MaxStringBytes { get; init; }
    }

    private sealed class PortableContract
    {
        public UnsupportedConstruct[] UnsupportedConstructs { get; init; } = [];
    }

    private sealed class UnsupportedConstruct
    {
        public string Id { get; init; } = string.Empty;

        public string Definition { get; init; } = string.Empty;
    }
}
