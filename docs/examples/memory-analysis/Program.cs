namespace CStructSharp.Examples.MemoryAnalysis;

using System.Text;
using CStructSharp.Memory;
using CStructSharp.Memory.Metadata;

/// <summary>Builds an independent toy image, walks it, and patches an offline copy without decoding primitives in the application.</summary>
/// <remarks>
/// The scenario models a tiny kernel that keeps a circular list of tasks. Its steps follow the order of the memory
/// guides: metadata and image, a mapped kernel address space, records that cross a page, a budgeted sentinel walk,
/// the same numeric address in two spaces, a tagged tree, corrupt and missing input, an offline patch, and a cache.
/// The application never decodes integers itself; the imported schema and the session do that.
/// </remarks>
internal static class Program
{
    /// <summary>The first logical address of the toy kernel: a high 64-bit value that no signed stream position could hold.</summary>
    private const ulong Kernel = 0xffff800000000000;

    /// <summary>A 16-byte task with a PID at offset 0 and a self-referencing pointer at offset 8, written as ISF 6.2.0.</summary>
    private const string Metadata = """
        { "metadata": { "format": "6.2.0" },
          "base_types": { "u32": { "kind": "int", "size": 4, "signed": false, "endian": "little" } },
          "user_types": { "task": { "kind": "struct", "size": 16, "fields": {
            "pid": { "offset": 0, "type": { "kind": "base", "name": "u32" } },
            "next": { "offset": 8, "type": { "kind": "pointer", "subtype": { "kind": "struct", "name": "task" } } }
          } } }, "enums": {}, "symbols": {} }
        """;

    /// <summary>Runs the example and fails if inspection, traversal, or byte preservation disagrees with the fixture.</summary>
    private static void Main()
    {
        CStructSharp.Docs.Examples.MemoryTutorialExamples.Run();
        Console.WriteLine("Ten memory guide examples passed.");

        // Step 1: metadata describes the task layout; a 40 KiB byte array plays the role of the capture file.
        MetadataImportResult imported = IsfMetadata.Import(Encoding.UTF8.GetBytes(Metadata), "task");
        var session = new MemorySession(imported.Schema);
        var image = new ByteArrayMemorySource("physical image", new byte[0xa000]);

        // Step 2: two pages that are adjacent in the kernel come from image offsets far apart in the file.
        var kernel = new MappedMemorySource("kernel", [
            new(Kernel, new MemoryRegion(image, 0x2000, 4096)),
            new(Kernel + 4096, new MemoryRegion(image, 0x9000, 4096)),
        ]);

        // Step 3: head -> PID 42 -> PID 99 -> head. The PID 42 record starts two bytes before the page boundary.
        WriteTask(session, imported.RootTypeId, kernel, Kernel, 0, Kernel + 4094);
        WriteTask(session, imported.RootTypeId, kernel, Kernel + 4094, 42, Kernel + 128);
        WriteTask(session, imported.RootTypeId, kernel, Kernel + 128, 99, Kernel);

        // Step 4: one context bounds the whole walk and the selected reads that follow it.
        var context = new MemoryAccessContext(maxBytes: 4096, maxRequests: 200);

        // A sentinel is a list head whose address marks completion, not an ordinary task.
        MemoryWalkResult tasks = MemoryWalker.SentinelList(new MemoryRegion(kernel, Kernel, 16), (node, budget) =>
        {
            StoredPointer next = (StoredPointer)session.Read(node, imported.RootTypeId, "next", budget)!;
            return new MemoryRegion(kernel, next.Bits, 16);
        }, maxNodes: 10, context: context);
        Require(tasks.Stop == MemoryWalkStop.Sentinel && tasks.Nodes.Count == 2, "Sentinel traversal failed.");
        foreach (MemoryRegion node in tasks.Nodes)
        {
            Console.WriteLine($"task 0x{node.Address:x}: pid={session.Read(node, imported.RootTypeId, "pid", context)}");
        }

        // Step 5: the same numeric coordinate in a process space resolves to different physical bytes.
        var process = new MappedMemorySource("process 7", [new(Kernel, new MemoryRegion(image, 0x2080, 16)),]);
        Require((uint)session.Read(new MemoryRegion(process, Kernel, 16), imported.RootTypeId, "pid")! == 99,
            "Process translation must select its own physical record.");
        Require((uint)session.Read(new MemoryRegion(kernel, Kernel, 16), imported.RootTypeId, "pid")! == 0,
            "Kernel translation must retain the sentinel at the same coordinate.");
        Console.WriteLine($"list read budget: {context.BytesRequested} bytes, {context.Requests} requests");

        // Step 6: a tagged entry uses its low bit as a flag. Classification belongs to the application.
        ulong taggedChild = (Kernel + 128) | 1;
        MemoryWalkResult tree = MemoryWalker.Tree(tasks.Nodes[0], (node, _) =>
            node.Address == Kernel + 4094 ? new[] { new MemoryRegion(kernel, taggedChild & ~1UL, 16), } : Array.Empty<MemoryRegion>(), maxNodes: 4);
        Require(tree.Stop == MemoryWalkStop.Complete && tree.Nodes.Count == 2, "Tagged tree failed.");
        Require(MemoryWalker.ContainingRecord(Kernel + 136, 8) == Kernel + 128, "Embedded link arithmetic failed.");

        // Step 7: a corrupt link repeats a task instead of returning to the sentinel.
        MemoryWalkResult corrupt = MemoryWalker.SentinelList(new MemoryRegion(kernel, Kernel, 16),
            (_, _) => tasks.Nodes[0], maxNodes: 10);
        Require(corrupt.Stop == MemoryWalkStop.RepeatedNode && corrupt.Nodes.Count == 1,
            "A corrupt cycle must terminate without inventing a sentinel.");

        // Removing the second page makes a cross-page pointer read unavailable, not a zero pointer.
        var missingPage = new MappedMemorySource("missing page", [new(Kernel, new MemoryRegion(image, 0x2000, 4096)),]);
        MemoryWalkResult unavailable = MemoryWalker.SentinelList(new MemoryRegion(missingPage, Kernel, 16), (node, budget) =>
        {
            StoredPointer next = (StoredPointer)session.Read(node, imported.RootTypeId, "next", budget)!;
            return new MemoryRegion(missingPage, next.Bits, 16);
        }, maxNodes: 10);
        Require(unavailable.Stop == MemoryWalkStop.Unavailable && unavailable.Failure?.Failure == MemoryFailure.Unmapped,
            "Missing memory must retain its structured failure.");
        Console.WriteLine("Corrupt cycle and missing page stopped with distinct diagnostics.");

        // Step 8: an overlay under a second mapping table lets the edit be previewed as file fragments.
        byte[] original = image.ToArray();
        var overlay = new OverlayMemorySource("offline copy", image);
        var copy = new MappedMemorySource("copied kernel", [
            new(Kernel, new MemoryRegion(overlay, 0x2000, 4096)),
            new(Kernel + 4096, new MemoryRegion(overlay, 0x9000, 4096)),
        ]);
        var selected = new MemoryRegion(copy, Kernel + 4094, 16);
        MemoryPatch patch = session.PlanUpdate(selected, imported.RootTypeId, "pid", 123U);
        Require(patch.Fragments.Count == 2, "Expected a field spanning two pages.");
        patch.Commit();
        Require((uint)session.Read(selected, imported.RootTypeId, "pid")! == 123, "Updated read failed.");
        Require(original.AsSpan().SequenceEqual(image.ToArray()), "Original image changed.");
        Console.WriteLine("Two-fragment offline patch verified; original image preserved.");

        // Step 9: a cache in front of the edited copy answers the repeated read without touching the image.
        var cached = new CachedMemorySource("cached copy", copy);
        var cachedRecord = new MemoryRegion(cached, selected.Address, selected.Length);
        var cold = new MemoryAccessContext();
        var warm = new MemoryAccessContext();
        Require((uint)session.Read(cachedRecord, imported.RootTypeId, "pid", cold)! == 123, "Cold cache result differs.");
        Require((uint)session.Read(cachedRecord, imported.RootTypeId, "pid", warm)! == 123, "Warm cache result differs.");
        Require(cold.BytesRequested == 4 && warm.BytesRequested == 0, "Cache accounting must distinguish backing bytes from hits.");
        Console.WriteLine($"cache: cold {cold.BytesRequested} bytes/{cold.Requests} requests; warm {warm.BytesRequested} bytes/{warm.Requests} requests");
    }

    /// <summary>Creates one zero-padded record using the imported codec and writes only its mapped extent.</summary>
    private static void WriteTask(MemorySession session, string type, IMemorySource source, ulong address, uint pid, ulong next)
    {
        byte[] bytes = session.Serialize(type, new Dictionary<string, object?> { ["pid"] = pid, ["next"] = new StoredPointer(next), });
        MemoryPatch.Create(new MemoryRegion(source, address, bytes.Length), bytes).Commit();
    }

    /// <summary>Turns an example invariant into an executable failure.</summary>
    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
