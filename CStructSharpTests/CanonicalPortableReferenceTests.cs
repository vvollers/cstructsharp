namespace CStructSharp.Tests;

using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

/// <summary>Executes the machine-readable tables and byte predictions published by the canonical Portable reference.</summary>
[TestClass]
public class CanonicalPortableReferenceTests
{
    private static readonly Lazy<PortableContract> Contract = new(LoadContract);

    /// <summary>Every fixed and terminated spelling agrees with the runtime's width, alignment, and natural CLR value.</summary>
    [TestMethod]
    public void PrimitiveTables_AgreeWithCompiledCodecs()
    {
        foreach (FixedPrimitive primitive in Contract.Value.FixedPrimitives)
        {
            var cstruct = new CStruct($"struct root {{ {primitive.Spelling} value; }}", pointerSize: 1);
            Assert.AreEqual(primitive.Bytes, cstruct.GetStructSizeInBytes("root"), primitive.Spelling);
            Assert.AreEqual(primitive.Alignment, cstruct.GetStructAlignmentInBytes("root"), primitive.Spelling);

            IDictionary<string, object?> parsed = cstruct.ParseStream(
                new MemoryStream(new byte[primitive.Bytes]),
                "root");
            object value = parsed["value"] ??
                           throw new AssertFailedException($"{primitive.Spelling} returned null.");
            Assert.AreEqual(primitive.Clr, value.GetType().Name, primitive.Spelling);
        }

        foreach (TerminatedPrimitive primitive in Contract.Value.TerminatedPrimitives)
        {
            var cstruct = new CStruct($"struct root {{ {primitive.Spelling} value; }}", pointerSize: 1);
            Assert.AreEqual(primitive.Alignment, cstruct.GetStructAlignmentInBytes("root"), primitive.Spelling);

            byte[] terminator = GetTerminatorBytes(primitive);
            IDictionary<string, object?> parsed = cstruct.ParseStream(new MemoryStream(terminator), "root");
            Assert.AreEqual(string.Empty, parsed["value"], primitive.Spelling);
        }
    }

    /// <summary>Published sizes, alignments, offsets, and canonical byte images execute exactly as documented.</summary>
    [TestMethod]
    public void LayoutExamples_PredictCompiledBytesAndOffsets()
    {
        foreach (LayoutExample example in Contract.Value.LayoutExamples)
        {
            var cstruct = new CStruct(
                example.Definition,
                (byte)example.PointerSize,
                example.Aligned,
                example.LittleEndian);
            byte[] bytes = Convert.FromHexString(example.Bytes);

            Assert.AreEqual(example.Size, cstruct.GetStructSizeInBytes(example.Root), example.Id);
            Assert.AreEqual(example.Alignment, cstruct.GetStructAlignmentInBytes(example.Root), example.Id);
            Assert.AreEqual(example.Size, bytes.Length, example.Id);

            foreach (KeyValuePair<string, long> offset in example.Offsets)
            {
                using var addressInput = new MemoryStream(bytes);
                Assert.AreEqual(offset.Value, cstruct.ResolveAddress(addressInput, offset.Key), example.Id);
                Assert.AreEqual(0L, addressInput.Position, example.Id);
            }

            using var parseInput = new MemoryStream(bytes);
            object parsed = cstruct.ParseStream(
                parseInput,
                example.Root,
                null,
                new ReadOptions { DereferencePointers = false });
            foreach (KeyValuePair<string, string> expected in example.Values)
            {
                object? actual = SelectPath(parsed, example.Root, expected.Key);
                Assert.AreEqual(expected.Value, FormatValue(actual), $"{example.Id}: {expected.Key}");
            }

            CollectionAssert.AreEqual(bytes, cstruct.Serialize(example.Root, parsed), example.Id);
        }
    }

    /// <summary>The contract never implies an implemented compiler-specific profile.</summary>
    [TestMethod]
    public void Contract_ListsPortableAsTheOnlyShippedProfile()
    {
        Assert.AreEqual("Portable", Contract.Value.Profile);
        CollectionAssert.AreEqual(new[] { "Portable", }, Contract.Value.ShippedProfiles);
    }

    /// <summary>Representative valid-C syntax outside Portable fails before any operation can touch a stream.</summary>
    [TestMethod]
    public void UnsupportedConstructs_FailDuringLayoutConstruction()
    {
        foreach (UnsupportedConstruct item in Contract.Value.UnsupportedConstructs)
        {
            CStructLayoutException failure = Assert.Throws<CStructLayoutException>(
                () => new CStruct(item.Definition),
                item.Id);
            Assert.AreEqual(CStructErrorCode.InvalidLayout, failure.Code, item.Id);
            Assert.IsFalse(string.IsNullOrWhiteSpace(failure.Message), item.Id);
        }
    }

    /// <summary>Returns the smallest byte sequence containing one complete terminator for a handler.</summary>
    private static byte[] GetTerminatorBytes(TerminatedPrimitive primitive)
    {
        if (!primitive.Encoding.Contains("UTF-16", StringComparison.Ordinal))
        {
            return primitive.Terminator == "LF" ? [0x0A,] : [0x00,];
        }

        if (primitive.Terminator == "NUL")
        {
            return [0x00, 0x00,];
        }

        return primitive.Endian == "big" ? [0x00, 0x0A,] : [0x0A, 0x00,];
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
            Assert.IsTrue(match.Success, $"Invalid canonical value path '{path}'.");
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

    /// <summary>Loads the exact checked-in contract copied beside the test assembly.</summary>
    private static PortableContract LoadContract()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "portable-v1.json");
        return JsonSerializer.Deserialize<PortableContract>(
                   File.ReadAllText(path),
                   new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ??
               throw new InvalidOperationException("The canonical Portable contract is empty.");
    }

    private sealed class PortableContract
    {
        public string Profile { get; init; } = string.Empty;

        public string[] ShippedProfiles { get; init; } = [];

        public FixedPrimitive[] FixedPrimitives { get; init; } = [];

        public TerminatedPrimitive[] TerminatedPrimitives { get; init; } = [];

        public LayoutExample[] LayoutExamples { get; init; } = [];

        public UnsupportedConstruct[] UnsupportedConstructs { get; init; } = [];
    }

    private sealed class FixedPrimitive
    {
        public string Spelling { get; init; } = string.Empty;

        public int Bytes { get; init; }

        public int Alignment { get; init; }

        public string Clr { get; init; } = string.Empty;
    }

    private sealed class TerminatedPrimitive
    {
        public string Spelling { get; init; } = string.Empty;

        public string Encoding { get; init; } = string.Empty;

        public string Terminator { get; init; } = string.Empty;

        public string Endian { get; init; } = string.Empty;

        public int Alignment { get; init; }
    }

    private sealed class LayoutExample
    {
        public string Id { get; init; } = string.Empty;

        public string Definition { get; init; } = string.Empty;

        public string Root { get; init; } = string.Empty;

        public int PointerSize { get; init; }

        public bool LittleEndian { get; init; }

        public bool Aligned { get; init; }

        public int Size { get; init; }

        public int Alignment { get; init; }

        public Dictionary<string, long> Offsets { get; init; } = new(StringComparer.Ordinal);

        public Dictionary<string, string> Values { get; init; } = new(StringComparer.Ordinal);

        public string Bytes { get; init; } = string.Empty;
    }

    private sealed class UnsupportedConstruct
    {
        public string Id { get; init; } = string.Empty;

        public string Definition { get; init; } = string.Empty;
    }
}
