namespace CStructSharpTests;

using System.Text.Json;
using CStructSharp;
using CStructSharp.Diagnostics;
using CStructSharp.Reading;
using CStructSharp.Tests;
using CStructSharp.Values;

/// <summary>
///     The memory-backed read cursor serves span, array, and MemoryStream sources; every other stream keeps
///     the delegating path. Both must produce identical values, identical final positions, and identical failures
///     over the whole benchmark fixture corpus.
/// </summary>
[TestClass]
public class MemoryCursorParityTests
{
    /// <summary>Every fixture parses identically through the span, MemoryStream, chunked-stream and plan-free paths, ending at the same position.</summary>
    [TestMethod]
    public void EveryFixture_ParsesIdenticallyThroughMemoryAndStreamPaths()
    {
        string directory = FindFixtureDirectory();
        int compared = 0;
        foreach (string path in Directory.GetFiles(Path.Combine(directory, "cases"), "*.json").Order(StringComparer.Ordinal))
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { MaxDepth = 4096 });
            JsonElement root = document.RootElement;
            if (root.GetProperty("bytes").ValueKind == JsonValueKind.Null || root.GetProperty("byteLength").GetInt64() > 2 * 1024 * 1024)
            {
                continue;
            }

            byte[] bytes = Materialize(directory, root.GetProperty("bytes"));
            JsonElement options = root.GetProperty("options");
            var layout = new CStruct(
                root.GetProperty("definition").GetString()!,
                options.GetProperty("pointerSize").GetByte(),
                options.GetProperty("aligned").GetBoolean(),
                options.GetProperty("littleEndian").GetBoolean());
            string rootName = root.GetProperty("root").GetString()!;
            ReadOptions readOptions = CreateReadOptions(root.GetProperty("readOptions"));
            string id = root.GetProperty("id").GetString()!;

            (object? spanResult, string? spanError) = Try(() => layout.Parse(bytes.AsSpan(), rootName, options: readOptions));
            using var memoryStream = new MemoryStream(bytes, writable: false);
            (object? memoryResult, string? memoryError) = Try(() => layout.Parse(memoryStream, rootName, options: readOptions));
            using var chunked = new ChunkedMemoryStream(bytes, 7, writable: false);
            (object? chunkedResult, string? chunkedError) = Try(() => layout.Parse(chunked, rootName, options: readOptions));

            // The same memory-backed source through the general reader only (static read plans disabled).
            using var unplanned = new MemoryStream(bytes, writable: false);
            StaticReadPlan.DisabledForTesting = true;
            (object? unplannedResult, string? unplannedError) = Try(() => layout.Parse(unplanned, rootName, options: readOptions));
            StaticReadPlan.DisabledForTesting = false;

            Assert.AreEqual(spanError, memoryError, id);
            Assert.AreEqual(spanError, chunkedError, id);
            Assert.AreEqual(spanError, unplannedError, id);
            Assert.AreEqual(chunked.Position, memoryStream.Position, id + ": final position");
            Assert.AreEqual(unplanned.Position, memoryStream.Position, id + ": final position without plan");
            if (spanError is null)
            {
                Assert.IsTrue(StructurallyEqual(spanResult, memoryResult), id + ": span vs MemoryStream");
                Assert.IsTrue(StructurallyEqual(spanResult, chunkedResult), id + ": span vs chunked stream");
                Assert.IsTrue(StructurallyEqual(spanResult, unplannedResult), id + ": plan vs general reader");
            }

            compared++;
        }

        Assert.IsTrue(compared >= 40, $"only {compared} fixtures compared");
    }

    /// <summary>A memory-backed operation writes its final position back to the caller's stream, on success and on failure.</summary>
    [TestMethod]
    public void MemoryStreamPosition_IsWrittenBack_OnSuccessAndFailure()
    {
        var layout = new CStruct("struct root { uint16 a; uint32 b; uint8 tail[count]; }; #define count 3");
        using var success = new MemoryStream(new byte[] { 1, 0, 2, 0, 0, 0, 7, 8, 9, 0xFF }, writable: false);
        success.Position = 0;
        _ = layout.Parse(success, "root");
        Assert.AreEqual(9L, success.Position);

        using var failure = new MemoryStream(new byte[] { 1, 0, 2, 0, 0, 0, 7 }, writable: false);
        Assert.ThrowsExactly<CStructReadException>(() => layout.Parse(failure, "root"));
        Assert.IsTrue(failure.Position >= 6, $"position after failure was {failure.Position}");

        using var resolve = new MemoryStream(new byte[] { 1, 0, 2, 0, 0, 0, 7, 8, 9 }, writable: false);
        resolve.Position = 0;
        Assert.AreEqual(6L, layout.ResolveAddress(resolve, "root.tail"));
        Assert.AreEqual(0L, resolve.Position, "ResolveAddress restores the position");
    }

    private static (object? Result, string? Error) Try(Func<object> parse)
    {
        try
        {
            return (parse(), null);
        }
        catch (CStructException exception)
        {
            return (null, exception.GetType().Name);
        }
    }

    private static bool StructurallyEqual(object? left, object? right)
    {
        switch (left)
        {
        case null:
            return right is null;
        case StructValue leftStruct:
            {
                if (right is not StructValue rightStruct)
                {
                    return false;
                }

                IDictionary<string, object?> l = leftStruct;
                IDictionary<string, object?> r = rightStruct;
                return l.Count == r.Count && l.All(pair => r.TryGetValue(pair.Key, out object? other) && StructurallyEqual(pair.Value, other));
            }

        case UnionValue leftUnion:
            return right is UnionValue rightUnion && leftUnion.UnionName == rightUnion.UnionName &&
                   leftUnion.Members.Count == rightUnion.Members.Count &&
                   leftUnion.Members.All(pair => StructurallyEqual(pair.Value, rightUnion.Members[pair.Key]));
        case Pointer leftPointer:
            return right is Pointer rightPointer && leftPointer.Address == rightPointer.Address && StructurallyEqual(leftPointer.Value, rightPointer.Value);
        case EnumValueResult leftEnum:
            return right is EnumValueResult rightEnum && leftEnum.Value == rightEnum.Value && leftEnum.Name == rightEnum.Name;
        case System.Collections.IList leftList:
            {
                if (right is not System.Collections.IList rightList || leftList.Count != rightList.Count)
                {
                    return false;
                }

                for (int index = 0; index < leftList.Count; index++)
                {
                    if (!StructurallyEqual(leftList[index], rightList[index]))
                    {
                        return false;
                    }
                }

                return true;
            }

        default:
            return left.Equals(right);
        }
    }

    private static ReadOptions CreateReadOptions(JsonElement element)
    {
        var defaults = new ReadOptions();
        if (element.ValueKind == JsonValueKind.Null)
        {
            return defaults;
        }

        return new ReadOptions
        {
            AddressingMode = element.TryGetProperty("addressingMode", out JsonElement mode) && mode.ValueKind == JsonValueKind.String && mode.GetString() == "Relative" ? PointerAddressingMode.Relative : PointerAddressingMode.Absolute,
            MaxArrayElements = element.TryGetProperty("maxArrayElements", out JsonElement elements) && elements.ValueKind == JsonValueKind.Number ? elements.GetInt32() : defaults.MaxArrayElements,
            MaxTotalBytesRead = element.TryGetProperty("maxTotalBytesRead", out JsonElement total) && total.ValueKind == JsonValueKind.Number ? total.GetInt64() : defaults.MaxTotalBytesRead,
            MaxStringBytes = element.TryGetProperty("maxStringBytes", out JsonElement strings) && strings.ValueKind == JsonValueKind.Number ? strings.GetInt64() : defaults.MaxStringBytes,
            MaxPointerDepth = element.TryGetProperty("maxPointerDepth", out JsonElement depth) && depth.ValueKind == JsonValueKind.Number ? depth.GetInt32() : defaults.MaxPointerDepth,
        };
    }

    private static byte[] Materialize(string directory, JsonElement spec)
    {
        switch (spec.GetProperty("kind").GetString())
        {
        case "hex":
            return Convert.FromHexString(spec.GetProperty("hex").GetString()!);
        case "file":
            return File.ReadAllBytes(Path.Combine(directory, spec.GetProperty("file").GetString()!));
        default:
            {
                uint x = spec.GetProperty("seed").GetUInt32();
                int size = spec.GetProperty("size").GetInt32();
                byte[] result = new byte[size];
                if (x == 0)
                {
                    x = 1;
                }

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
    }

    private static string FindFixtureDirectory()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null)
        {
            string candidate = Path.Combine(directory, "benchmarks", "fixtures", "manifest.json");
            if (File.Exists(candidate))
            {
                return Path.GetDirectoryName(candidate)!;
            }

            directory = Path.GetDirectoryName(directory);
        }

        throw new DirectoryNotFoundException("benchmarks/fixtures/manifest.json not found");
    }
}
