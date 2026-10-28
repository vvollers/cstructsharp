namespace CStructSharp.Experiments;

using System.Diagnostics;
using System.Globalization;
using System.Text;
using CStructSharp.Diagnostics;
using CStructSharp.Memory;
using CStructSharp.Memory.Metadata;
using CStructSharp.Values;

/// <summary>Measures complete synthetic memory operations and a deliberately restricted metadata projection prototype.</summary>
internal static class Program
{
    /// <summary>Runs one workload after setup, reports every measured loop's elapsed time, allocation and result checksum.</summary>
    /// <param name="args">Workload name and iteration count; no image path is accepted.</param>
    private static void Main(string[] args)
    {
        string name = args.Length > 0 ? args[0] : "bound";
        int iterations = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 10000;
        var schema = new MemorySchema([
            new("u", "u", MemoryTypeKind.Scalar, 4, scalarType: "uint32"),
            new("b", "b", MemoryTypeKind.Scalar, 1, scalarType: "uint8"),
            new("record", "record", MemoryTypeKind.Struct, 4, [new("value", "u", 0),]),
            new("array", "array", MemoryTypeKind.Array, 4096, elementTypeId: "b", count: 4096),
        ]);
        var session = new MemorySession(schema);
        byte[] bytes = new byte[4096];
        bytes[0] = 42;
        for (int index = 1; index < bytes.Length; index++)
        {
            bytes[index] = (byte)index;
        }

        var region = new MemoryRegion(new ByteArrayMemorySource("synthetic", bytes), 0, bytes.Length);
        var missingRegion = new MemoryRegion(new ByteArrayMemorySource("missing", []), 0, 4);
#if !MEMORY_REFERENCE
        MemoryFieldReader<uint> bound = schema.Bind<uint>("record", "value");
#endif
        byte[] destination = new byte[4096];
        byte[] metadataBytes = Graph(256);
        var metadata = new IsfMetadata(metadataBytes);
        _ = metadata.Import("r0");
        var smallMetadata = new IsfMetadata(Graph(4));
        _ = smallMetadata.Import("r0");

        // Every operation consumes the complete returned value; comparisons retain all allocations and transport work.
        Func<ulong> operation = name switch
        {
            "ordinary" => () => (uint)session.Read(region, "record", "value")!,
            "throw-missing" => () => Missing(session, missingRegion),
#if !MEMORY_REFERENCE
            "bound" => () => bound.Read(session, region),
            "try-missing" => () => bound.TryRead(session, missingRegion, out _, out _) ? 0UL : 1UL,
            "bind" => () => (ulong)schema.Bind<uint>("record", "value").Selection.Type.Size,
            "array-element" => () => ReadArray(session, region, destination, MemoryArrayReadMode.Elementwise),
            "array-bulk" => () => ReadArray(session, region, destination, MemoryArrayReadMode.Bulk),
#endif
            "array-owned" => () => Sum(((PrimitiveArray<byte>)session.Read(region, "array")!).Span),
            "import-first" => () => (ulong)new IsfMetadata(metadataBytes).Import("r0").Schema.Types.Count,
            "import-overlap" => () => (ulong)metadata.Import("r1").Schema.Types.Count,
#if !MEMORY_REFERENCE
            "describe" => () => (ulong)metadata.Describe("r1").Size,
            "prototype" => () => (ulong)ProjectPrototype(metadata, "r1").GetType("isf:user:r1").Size,
            "projection-first" => () => (ulong)new IsfMetadata(metadataBytes).ImportProjection("r0", ["value"]).Schema.Types.Count,
            "projection" => () => (ulong)Project(metadata, "r1").GetType("isf:user:r1").Size,
            "prepare-first-full" => () => Prepare(new IsfMetadata(metadataBytes).Import("r1").Schema, region),
            "prepare-first-projection" => () => Prepare(new IsfMetadata(metadataBytes).ImportProjection("r1", ["value"]).Schema, region),
            "prepare-full" => () => Prepare(metadata.Import("r1").Schema, region),
            "prepare-projection" => () => Prepare(Project(metadata, "r1"), region),
            "prepare-small-full" => () => Prepare(smallMetadata.Import("r1").Schema, region),
            "prepare-small-projection" => () => Prepare(Project(smallMetadata, "r1"), region),
#endif
            _ => throw new ArgumentException("Unknown synthetic workload.", nameof(args)),
        };
        int warmups = args.Length > 2 && args[2] == "cold" ? 0 : Math.Min(iterations, 2000);
        for (int index = 0; index < warmups; index++)
        {
            _ = operation();
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        ulong checksum = 0;
        for (int index = 0; index < iterations; index++)
        {
            checksum += operation();
        }

        double elapsed = Stopwatch.GetElapsedTime(start).TotalNanoseconds / iterations;
        long allocation = (GC.GetAllocatedBytesForCurrentThread() - allocated) / iterations;
        Console.WriteLine(FormattableString.Invariant($"{name}: {elapsed:F1} ns/op, {allocation} B/op, checksum={checksum}, iterations={iterations}, warmups={warmups}"));
        var accounting = new MemoryAccessContext();
        _ = session.Read(region, "record", "value", accounting);
        Console.WriteLine($"ordinary-shape: {accounting.Requests} requests, {accounting.BytesRequested} bytes");
#if !MEMORY_REFERENCE
        accounting = new MemoryAccessContext();
        _ = bound.Read(session, region, accounting);
        Console.WriteLine($"bound-shape: {accounting.Requests} requests, {accounting.BytesRequested} bytes");
        foreach (MemoryArrayReadMode mode in new[] { MemoryArrayReadMode.Elementwise, MemoryArrayReadMode.Bulk })
        {
            accounting = new MemoryAccessContext();
            session.ReadArray<byte>(region, "b", destination, accounting, mode);
            Console.WriteLine($"{mode}-shape: {accounting.Requests} requests, {accounting.BytesRequested} bytes");
        }
#endif
    }

    /// <summary>Measures an expected missing source through the throwing API and consumes its failure category.</summary>
    /// <param name="session">Schema session.</param>
    /// <param name="region">Finite permitted extent backed by unavailable storage.</param>
    /// <returns>One when the expected source failure occurs.</returns>
    private static ulong Missing(MemorySession session, MemoryRegion region)
    {
        try
        {
            _ = session.Read(region, "record", "value");
            return 0;
        }
        catch (MemoryAccessException exception) when (exception.Failure == MemoryFailure.MissingBytes)
        {
            return 1;
        }
    }

#if !MEMORY_REFERENCE
    /// <summary>Decodes every array element into caller-owned memory and consumes the complete result.</summary>
    /// <param name="session">Prepared schema session.</param>
    /// <param name="region">Permitted storage extent.</param>
    /// <param name="destination">Reusable owned result.</param>
    /// <param name="mode">Explicit transport shape.</param>
    /// <returns>Checksum of every result byte.</returns>
    private static ulong ReadArray(MemorySession session, MemoryRegion region, byte[] destination, MemoryArrayReadMode mode)
    {
        session.ReadArray<byte>(region, "b", destination, mode: mode);
        return Sum(destination);
    }
#endif

    /// <summary>Consumes a complete byte array outside any boxing conversions.</summary>
    /// <param name="bytes">Complete result bytes.</param>
    /// <returns>The sum of every byte.</returns>
    private static ulong Sum(ReadOnlySpan<byte> bytes)
    {
        ulong sum = 0;
        foreach (byte value in bytes)
        {
            sum += value;
        }

        return sum;
    }

#if !MEMORY_REFERENCE
    /// <summary>Models preparation followed by repeated selected reads from one imported root.</summary>
    /// <param name="schema">Full or experimentally projected root.</param>
    /// <param name="region">Synthetic input record.</param>
    /// <returns>Checksum of 1000 complete selected values.</returns>
    private static ulong Prepare(MemorySchema schema, MemoryRegion region)
    {
        var session = new MemorySession(schema);
        MemoryFieldReader<uint> reader = schema.Bind<uint>("isf:user:r1", "value");
        ulong sum = 0;
        for (int index = 0; index < 1000; index++)
        {
            sum += reader.Read(session, region);
        }

        return sum;
    }

    /// <summary>Imports the measured selected field through the public, validated field-directed importer.</summary>
    /// <param name="metadata">Parsed metadata with its bounded successful scalar cache.</param>
    /// <param name="root">Root user name.</param>
    /// <returns>The immutable schema for the selected value path.</returns>
    private static MemorySchema Project(IsfMetadata metadata, string root)
        => metadata.ImportProjection(root, ["value"]).Schema;

    /// <summary>Prototypes a one-field projection while retaining root size and pointer identity as explicitly incomplete.</summary>
    /// <remarks>This is an experimental upper bound for one known descriptor shape, not an importer. It cannot establish promoted-path validation, arbitrary declarations or useful availability diagnostics. Those costs are intentionally excluded and must not be claimed as production performance.</remarks>
    /// <param name="metadata">Synthetic parsed document.</param>
    /// <param name="root">Root user name.</param>
    /// <returns>A narrower-validation experimental schema.</returns>
    private static MemorySchema ProjectPrototype(IsfMetadata metadata, string root)
    {
        IsfTypeDescription description = metadata.Describe(root);
        return new MemorySchema([
            new("u32", "u32", MemoryTypeKind.Scalar, 4, scalarType: "uint32"),
            new("isf:user:r2", "r2", MemoryTypeKind.Incomplete, 0, provenance: "Projection prototype: target intentionally unselected."),
            new("pointer", "pointer", MemoryTypeKind.Pointer, 8, elementTypeId: "isf:user:r2"),
            new("isf:user:" + root, root, MemoryTypeKind.Struct, description.Size, [new("value", "u32", 0), new("next", "pointer", 8),]),
        ]);
    }
#endif

    /// <summary>Generates a ring of overlapping root graphs without real capture or profile data.</summary>
    /// <param name="count">Number of roots in the ring.</param>
    /// <returns>UTF-8 ISF metadata.</returns>
    private static byte[] Graph(int count)
    {
        var builder = new StringBuilder("""{"metadata":{"format":"6.2.0"},"base_types":{"u32":{"kind":"int","size":4,"signed":false,"endian":"little"}},"user_types":{ """);
        for (int index = 0; index < count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            builder.Append(CultureInfo.InvariantCulture, $"\"r{index}\":{{\"kind\":\"struct\",\"size\":16,\"fields\":{{\"value\":{{\"offset\":0,\"type\":{{\"kind\":\"base\",\"name\":\"u32\"}}}},\"next\":{{\"offset\":8,\"type\":{{\"kind\":\"pointer\",\"subtype\":{{\"kind\":\"struct\",\"name\":\"r{(index + 1) % count}\"}}}}}}}}}}");
        }

        builder.Append("}}");
        return Encoding.UTF8.GetBytes(builder.ToString());
    }
}
