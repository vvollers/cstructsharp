---
title: Synthetic memory analysis
description: Run an independent mapped-memory consumer with unsigned pointers, bounded traversal, and offline patches.
---

# Synthetic memory analysis

This runnable project is the executable companion to the [memory guide series](../../guides/memory-analysis.md).
It has two parts. `MemoryTutorialExamples.cs` contains the nine snippets that the guides include verbatim, each
ending in assertions that check the numbers the guides quote. `Program.cs` then runs a longer scenario that
combines the same ideas into one small analysis. Nothing in it needs a real capture, profile, or live process:
the program constructs its own metadata and byte image, so you can read every input byte in the source.

Use `using CStructSharp.Memory;` for sources, schemas, and sessions, and `using CStructSharp.Memory.Metadata;`
for the importers.

## Run it

From the repository root:

```powershell
dotnet run --project docs/examples/memory-analysis -c Release -f net10.0
dotnet run --project docs/examples/memory-analysis -c Release -f net8.0
```

Success starts with `Nine memory guide examples passed.` and continues with the scenario's output. Any failed
assertion exits the program with an error. The documentation validation script runs both commands, so the guides
cannot drift from the implementation without a failing build.

## The nine guide snippets

Each snippet is a `#region` that a guide includes with `[!code-csharp[...]]`, so the code you see in an article
is exactly the code being tested. `Require` is a one-line assertion helper that throws with the failing
description.

| Snippet | Guide | What it checks |
| --- | --- | --- |
| A four-byte field split across two mappings, plus a finite stream view | [Address spaces](../../guides/memory-sources.md) | Logical address, two backing fragments, stream position zero |
| Explicit padding and a signed three-bit update | [Schemas](../../guides/memory-schemas.md) | Sign extension to -1, neighbor bits preserved on write |
| A synthetic ISF type with a member at offset four | [Schemas](../../guides/memory-schemas.md) | Imported offsets are used, not inferred |
| A synthetic BTF table with explicit member placement | [Schemas](../../guides/memory-schemas.md) | Bit offset 32 becomes byte offset 4 |
| A pointer encoded as a displacement from its containing record | [Traversal](../../guides/memory-traversal.md) | Stored bits stay 16 while the target resolves to 24 |
| A sentinel list and embedded-member address calculation | [Traversal](../../guides/memory-traversal.md) | Two data nodes, sentinel excluded, `ContainingRecord` arithmetic |
| A two-fragment overlay patch and physical-image export | [Updates](../../guides/memory-updates.md) | Preview is read-only, original unchanged, export keeps gaps |
| Creation of a union from a chosen member or raw bytes | [Updates](../../guides/memory-updates.md) | Explicit interpretation, exact raw storage |
| Cache accounting, generation invalidation, missing mappings, cancellation | [Reliability](../../guides/memory-reliability.md) | Zero backing bytes on a hit, `Unmapped` versus `OperationCanceledException` |

## The combined scenario

`Program.cs` models a tiny operating-system kernel keeping a circular list of tasks. Reading it top to bottom
follows the order of the guides.

1. **Metadata and image.** An ISF document describes a 16-byte `task` with a `pid` at offset 0 and a `next`
   pointer at offset 8. A 40 KiB `ByteArrayMemorySource` plays the role of the capture file.
2. **A kernel address space.** A `MappedMemorySource` places two 4096-byte pages at `0xffff800000000000` and the
   page after it, backed by image offsets `0x2000` and `0x9000`. The pages are adjacent to the kernel and far apart
   in the file, exactly the situation the first guide describes.
3. **Records that cross a page.** `WriteTask` serializes each record and writes it with `MemoryPatch.Create`. The
   task with PID 42 starts at `Kernel + 4094`, so its `pid` straddles the page boundary.
4. **A sentinel walk with one budget.** `MemoryWalker.SentinelList` follows the list head through PIDs 42 and 99
   and back to the head. The shared context reports 32 physical bytes and 20 requests for the walk and two selected
   PID reads: three pointer reads and two PID reads, with the cross-page PID costing two mapping lookups.
5. **The same number, two spaces.** A second `MappedMemorySource` for "process 7" maps the same numeric address to
   a different image offset. Reading `pid` through each source gives 99 and 0 respectively, showing that an address
   only means something together with its source.
6. **A tagged tree.** The child callback strips the low tag bit from an entry before returning a region, and the
   walker reports `Complete` with two nodes.
7. **Corrupt and missing input.** A callback that always returns the first node produces `RepeatedNode` after one
   node; removing the second page produces `Unavailable` with an `Unmapped` failure. Neither invents a sentinel or a
   zero pointer.
8. **An offline patch.** An `OverlayMemorySource` over the image and a second mapping table on top of it let the
   program change PID 42 to 123. The patch has two fragments because the field crosses the page boundary, the read
   after commit returns 123, and the original image is byte-for-byte unchanged.
9. **A cache over the copy.** A cold read requests four backing bytes; the warm read requests zero.

Read the [overview](../../guides/memory-analysis.md) for the coordinate systems, metadata subset, ownership
rules, and update failure behavior that the scenario relies on.
