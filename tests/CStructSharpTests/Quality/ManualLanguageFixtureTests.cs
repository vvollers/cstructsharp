namespace CStructSharp.Tests;

using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using CStructSharp.Diagnostics;

/// <summary>Executes every valid/invalid pair published by the Portable language manual.</summary>
[TestClass]
public class ManualLanguageFixtureTests
{
    private static readonly Lazy<ManualFixtureContract> Fixtures = new(LoadFixtures);
    private static readonly Lazy<PortableContract> Portable = new(LoadPortableContract);

    /// <summary>
    ///     Every valid teaching fixture supplies its own definition, options, bytes, and predicted values and offsets.
    /// </summary>
    /// <remarks>
    ///     The library must match those predictions and reproduce the expected serialization. The test keeps
    ///     explanatory examples tied to executable behavior rather than relying on handwritten calculations alone.
    /// </remarks>
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
                fixture.LittleEndian,
                CreateCompilationOptions(fixture.Compilation));
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
            object parsed = cstruct.ReadValue(
                parseInput,
                fixture.Root,
                fixture.Variables,
                readOptions)!;
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

    /// <summary>Every valid feature example reads the same bytes through <c>ReadValueAsync</c> as through <c>ReadValue(Stream)</c>, and both leave the stream after the value.</summary>
    [TestMethod]
    public async Task ValidFixtures_ReadTheSameAsynchronously()
    {
        foreach (FeaturePair pair in Fixtures.Value.FeaturePairs)
        {
            ValidFixture fixture = pair.Valid;
            var cstruct = new CStruct(fixture.Definition, (byte)fixture.PointerSize, fixture.Aligned, fixture.LittleEndian, CreateCompilationOptions(fixture.Compilation));
            byte[] bytes = Convert.FromHexString(fixture.Bytes);
            var readOptions = new ReadOptions { DereferencePointers = false, };
            using var sync = new MemoryStream(bytes);
            using var async = new MemoryStream(bytes, 0, bytes.Length, writable: false, publiclyVisible: false);
            object expected = cstruct.ReadValue(sync, fixture.Root, fixture.Variables, readOptions)!;
            object actual = (await cstruct.ReadValueAsync(async, fixture.Root, fixture.Variables, readOptions))!;
            CollectionAssert.AreEqual(cstruct.Serialize(fixture.Root, expected, fixture.Variables), cstruct.Serialize(fixture.Root, actual, fixture.Variables), pair.Id);
            Assert.AreEqual(sync.Position, async.Position, pair.Id);
        }
    }

    /// <summary>Every valid feature example serializes to the same bytes through <c>WriteAsync</c> as through <c>Write(Stream)</c>.</summary>
    [TestMethod]
    public async Task ValidFixtures_WriteTheSameAsynchronously()
    {
        foreach (FeaturePair pair in Fixtures.Value.FeaturePairs)
        {
            ValidFixture fixture = pair.Valid;
            var cstruct = new CStruct(fixture.Definition, (byte)fixture.PointerSize, fixture.Aligned, fixture.LittleEndian, CreateCompilationOptions(fixture.Compilation));
            byte[] bytes = Convert.FromHexString(fixture.Bytes);
            object parsed = cstruct.ReadValue(bytes, fixture.Root, fixture.Variables, new ReadOptions { DereferencePointers = false, })!;
            using var sync = new MemoryStream();
            cstruct.Write(sync, fixture.Root, parsed, fixture.Variables);
            using var async = new MemoryStream();
            await cstruct.WriteAsync(async, fixture.Root, parsed, fixture.Variables);
            CollectionAssert.AreEqual(sync.ToArray(), async.ToArray(), pair.Id);
            Assert.AreEqual(sync.Position, async.Position, pair.Id);
        }
    }

    /// <summary>
    ///     Each valid feature example has a paired invalid form or bounded-read failure.
    /// </summary>
    /// <remarks>
    ///     The test checks whether rejection happens during construction or reading and whether its stable error code
    ///     matches the fixture. This documents both what is supported and how the corresponding mistake is reported.
    /// </remarks>
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
                () => cstruct.Parse(
                    input,
                    fixture.Root,
                    null,
                    new ReadOptions { MaxStringBytes = fixture.MaxStringBytes, }),
                pair.Id);
            Assert.AreEqual(expectedCode, readFailure.Code, pair.Id);
        }
    }

    /// <summary>The compilation options a fixture names (the constructor defaults when it names none).</summary>
    private static CStructCompilationOptions? CreateCompilationOptions(CompilationFixture? compilation)
    {
        if (compilation is null)
        {
            return null;
        }

        return new CStructCompilationOptions
        {
            CLongWidth = compilation.CLongWidth ?? 64,
            DefaultEnumStorage = compilation.DefaultEnumStorage,
            BitfieldAllocation = compilation.BitfieldAllocation is null
                                     ? BitfieldAllocation.LowBitFirst
                                     : Enum.Parse<BitfieldAllocation>(compilation.BitfieldAllocation),
        };
    }

    /// <summary>
    ///     Resolves a dotted value path such as <c>root.items[1].tag</c> in a parsed result. The test fails when a
    ///     segment is malformed or names a missing member or element.
    /// </summary>
    /// <param name="value">The parsed root value.</param>
    /// <param name="rootName">The root type name, which the path may repeat as its first segment.</param>
    /// <param name="path">The dotted path, with optional zero-based element indexes.</param>
    /// <returns>The selected value, which may be null.</returns>
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

    /// <summary>
    ///     Selects a named member from a dictionary result or, for other objects, from a public property whose name
    ///     matches without regard to case. The test fails when the member is missing.
    /// </summary>
    /// <param name="value">The container to select from.</param>
    /// <param name="name">The member name.</param>
    /// <param name="path">The full value path, used in failure messages.</param>
    /// <returns>The member value.</returns>
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

    /// <summary>
    ///     Selects an element by zero-based index from a list or other sequence; the test fails when it is out of
    ///     range.
    /// </summary>
    /// <param name="value">The sequence to index.</param>
    /// <param name="index">The zero-based element index.</param>
    /// <param name="path">The full value path, used in failure messages.</param>
    /// <returns>The element value.</returns>
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

    /// <summary>Formats a value the way the manual fixtures writes expected values.</summary>
    /// <param name="value">The value to format.</param>
    /// <returns>
    ///     Invariant-culture text, with lowercase Booleans and <c>&lt;null&gt;</c> for a null value.
    /// </returns>
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

    /// <summary>Loads the manual's feature fixture pairs from <c>manual-fixtures-v1.json</c>.</summary>
    /// <returns>The fixture contract.</returns>
    private static ManualFixtureContract LoadFixtures()
    {
        return Deserialize<ManualFixtureContract>("manual-fixtures-v1.json");
    }

    /// <summary>Loads the unsupported-construct list from <c>portable-v1.json</c>.</summary>
    /// <returns>The part of the Portable contract these tests read.</returns>
    private static PortableContract LoadPortableContract()
    {
        return Deserialize<PortableContract>("portable-v1.json");
    }

    /// <summary>
    ///     Deserializes a JSON contract copied beside the test assembly, matching property names without regard to
    ///     case.
    /// </summary>
    /// <typeparam name="T">The contract type to create.</typeparam>
    /// <param name="fileName">The file name in the test output directory.</param>
    /// <returns>The deserialized contract.</returns>
    /// <exception cref="InvalidOperationException">The file contains JSON <c>null</c>.</exception>
    private static T Deserialize<T>(string fileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, fileName);
        return JsonSerializer.Deserialize<T>(
                   File.ReadAllText(path),
                   new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ??
               throw new InvalidOperationException($"Fixture contract '{fileName}' is empty.");
    }

    /// <summary>The manual fixture file: one valid and invalid pair per documented feature.</summary>
    private sealed class ManualFixtureContract
    {
        public FeaturePair[] FeaturePairs { get; init; } = [];
    }

    /// <summary>A documented feature's valid example and its paired invalid form.</summary>
    private sealed class FeaturePair
    {
        public string Id { get; init; } = string.Empty;

        public ValidFixture Valid { get; init; } = new();

        public InvalidFixture Invalid { get; init; } = new();
    }

    /// <summary>
    ///     A valid example: definition, options, bytes, and the size, alignment, offsets, and values it predicts.
    /// </summary>
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

        public CompilationFixture? Compilation { get; init; }
    }

    /// <summary>The <see cref="CStructCompilationOptions"/> members a fixture may set; absent members keep their defaults.</summary>
    private sealed class CompilationFixture
    {
        public int? CLongWidth { get; init; }

        public string? DefaultEnumStorage { get; init; }

        public string? BitfieldAllocation { get; init; }
    }

    /// <summary>
    ///     An invalid form that fails at construction (stage <c>compile</c>, named by unsupported-construct id) or
    ///     while reading its bytes.
    /// </summary>
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

    /// <summary>The part of <c>portable-v1.json</c> that lists unsupported constructs.</summary>
    private sealed class PortableContract
    {
        public UnsupportedConstruct[] UnsupportedConstructs { get; init; } = [];
    }

    /// <summary>A declaration outside the Portable subset that construction must reject.</summary>
    private sealed class UnsupportedConstruct
    {
        public string Id { get; init; } = string.Empty;

        public string Definition { get; init; } = string.Empty;
    }
}
