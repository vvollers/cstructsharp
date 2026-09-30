namespace CStructSharp.Tests;

using System.Text.Json;
using CStructSharp;
using CStructSharp.Diagnostics;
using CStructSharp.Reading;
using CStructSharp.Values;

/// <summary>
///     The memory-backed read cursor serves span, array, and MemoryStream sources; every other stream keeps
///     the delegating path. Both must produce identical values, identical final positions, and identical failures
///     over the whole benchmark fixture corpus.
/// </summary>
[TestClass]
public class SpanStreamParityTests
{
    /// <summary>Every fixture parses identically through the span, MemoryStream, chunked-stream and plan-free paths, ending at the same position.</summary>
    [TestMethod]
    public void EveryFixture_ParsesIdenticallyThroughMemoryAndStreamPaths()
    {
        string directory = TestFixtures.BenchmarkFixtures;
        int compared = 0;
        foreach (string path in Directory.GetFiles(Path.Combine(directory, "cases"), "*.json").Order(StringComparer.Ordinal))
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { MaxDepth = 4096 });
            JsonElement root = document.RootElement;
            if (root.GetProperty("bytes").ValueKind == JsonValueKind.Null || root.GetProperty("byteLength").GetInt64() > 2 * 1024 * 1024)
            {
                continue;
            }

            byte[] bytes = TestFixtures.Materialize(root.GetProperty("bytes"));
            JsonElement options = root.GetProperty("options");
            var layout = new CStruct(
                root.GetProperty("definition").GetString()!,
                options.GetProperty("pointerSize").GetByte(),
                options.GetProperty("aligned").GetBoolean(),
                options.GetProperty("littleEndian").GetBoolean());
            string rootName = root.GetProperty("root").GetString()!;
            ReadOptions readOptions = CreateReadOptions(root.GetProperty("readOptions"));
            string id = root.GetProperty("id").GetString()!;

            OperationOutcome span = OperationOutcome.Of(() => layout.Parse(bytes.AsSpan(), rootName, options: readOptions));
            using var memoryStream = new MemoryStream(bytes, writable: false);
            OperationOutcome memory = OperationOutcome.Of(() => layout.Parse(memoryStream, rootName, options: readOptions));
            using var chunked = new ChunkedMemoryStream(bytes, 7, writable: false);
            OperationOutcome chunkedOutcome = OperationOutcome.Of(() => layout.Parse(chunked, rootName, options: readOptions));

            // The same memory-backed source through the general path only (static read plans disabled).
            using var unplanned = new MemoryStream(bytes, writable: false);
            OperationOutcome unplannedOutcome = OperationOutcome.Of(() => layout.Parse(unplanned, rootName, options: readOptions with { ExecutionPath = ExecutionPath.NoFastPaths }));

            OperationOutcome.AssertSame(span, memory, id + ": span vs MemoryStream");
            OperationOutcome.AssertSame(span, chunkedOutcome, id + ": span vs chunked stream");
            OperationOutcome.AssertSame(span, unplannedOutcome, id + ": plan vs member-by-member engine");
            Assert.AreEqual(chunked.Position, memoryStream.Position, id + ": final position");
            Assert.AreEqual(unplanned.Position, memoryStream.Position, id + ": final position without plan");

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

    /// <summary>The read options a benchmark fixture describes, or the defaults.</summary>
    /// <param name="element">The fixture's options, or JSON null.</param>
    /// <returns>The options.</returns>
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
}
