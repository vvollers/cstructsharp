---
title: Analyze mapped memory
description: Learn why captured memory needs address spaces, mappings, metadata, and bounded work, then follow the worked guide series.
---

# Analyze mapped memory

This series teaches the `CStructSharp.Memory` namespace: a set of types for reading, following, and editing C-style
records inside a **memory image**. It assumes you can already parse a byte array with `CStruct`. It does not assume
operating-systems or forensics experience; every concept is introduced before it is used.

## What a memory image is, and why it needs its own tools

A memory image is a copy of another program's memory, saved as bytes. Familiar examples are a crash dump written
when a program fails, a snapshot of a virtual machine, a firmware image read from a device, or the saved state of
an emulator. Analysts read such images to find out what the program was doing: which tasks existed, which buffers
were allocated, what a linked list contained at the moment of the capture.

The bytes in an image were produced by a running program, so they follow that program's rules rather than a file
format designed for exchange. Three of those rules make a plain stream parser insufficient.

1. **Addresses are not file offsets.** A running process names its memory with *virtual addresses* chosen by the
   operating system. A kernel address such as `0xffff800000000ffe` needs all 64 bits, while stream positions in
   .NET are signed `long` values. The image file stores that page somewhere else again, at its own offset.
2. **Consecutive addresses can be stored far apart.** Memory is managed in pages, typically 4096 bytes. Two pages
   that are neighbors in the process can be neighbors in the file, or thousands of bytes apart, or absent because
   they were never captured. A four-byte integer can start in one page and end in another.
3. **Layout came from a compiler.** The program's structures were placed by its compiler for its platform,
   with padding between members and sometimes bit-packed fields. An analysis machine with a different compiler
   cannot recompute those positions; it must be told them. Debuggers and forensic tools distribute this
   information as *type metadata*, for example BTF for the Linux kernel or ISF for Volatility.

The memory APIs address exactly these three problems. They translate unsigned addresses through mapping tables,
accept explicit member placement from metadata, follow pointers only when asked, and refuse to invent bytes that
the image does not contain. They do **not** attach to a live process, locate operating-system symbols, or walk
hardware page tables; your application supplies the bytes and the mapping information.

## The mental model

Reading one field passes through a small stack of objects. Each layer answers one question and knows nothing
about the others, which is what lets the same schema serve a file, a mapped process image, and a test fixture.

```text
"Record.value"          the path you ask for
        |
  MemorySession         where is that field, and how are its bytes decoded?
        |               uses a MemorySchema (offsets, sizes, codecs)
  MemoryRegion          which finite range of which source may this operation touch?
        |
  MappedMemorySource    which backing range holds logical address 0xffff800000000ffe?
        |               a sorted table of MemoryMapping entries
  ByteArrayMemorySource / StreamMemorySource / your own IMemorySource
                        the bytes themselves, in their own coordinate system
```

A `MemoryAccessContext` travels alongside every call in that stack. It counts requested bytes and requests, checks
a cancellation token, and stops an operation that would otherwise run forever on corrupt input.

| Term | Meaning in this series |
| --- | --- |
| Address space | A set of bytes named by unsigned addresses. Two processes are two address spaces even when they use the same numbers. |
| Source | An object implementing `IMemorySource`; the address space you are reading. Object identity, not the `Id` label, distinguishes sources. |
| Region | A source, an unsigned start address, and a finite length in bytes. It names a range; it does not copy or guarantee the bytes. |
| Mapping | One rule of the form "logical addresses *A* to *A+n* come from backing region *R*". |
| Schema | Validated type descriptors: sizes, member offsets, bit slices, pointer widths, and the codecs used to decode scalars. |
| Session | The object that applies a schema to a region: it resolves paths, reads, inspects, serializes, and plans updates. |
| Budget | A `MemoryAccessContext`, shared by everything that participates in one logical operation. |
| Generation | A counter a source advances when its bytes change, so caches and patches can notice they are out of date. |

## A first complete example

Consider `struct Record { uint32 value; };`. Little-endian bytes `78 56 34 12` represent `0x12345678`. In this
image, the first two bytes are the last two bytes of one page and the other two bytes come from a different part
of the file. The record sits at the very end of a page in the process, so its logical address is
`0xffff800000000ffe` and the page boundary falls at `...1000`.

```csharp
using CStructSharp;
using CStructSharp.Memory;

var layout = new CStruct("struct Record { uint32 value; };");
var session = new MemorySession(PortableMemorySchema.Create(layout, "Record"));
var image = new ByteArrayMemorySource("image", new byte[] { 0x78, 0x56, 0, 0, 0x34, 0x12 });
ulong address = 0xffff800000000ffe;
var mapped = new MappedMemorySource("kernel", new[] {
    new MemoryMapping(address, new MemoryRegion(image, 0, 2)),
    new MemoryMapping(address + 2, new MemoryRegion(image, 4, 2)),
});
var region = new MemoryRegion(mapped, address, 4);
var budget = new MemoryAccessContext(maxBytes: 1024, maxRequests: 100);
MemoryInspection result = session.Inspect(region, "Record", "value", budget);
// result.Value is 0x12345678U. result.BackingRegions lists image offsets 0 and 4, two bytes each.
```

Read the code from the bottom of the mental model upward. `image` holds six bytes at offsets 0 to 5. `mapped`
says that logical addresses `...0ffe` and `...0fff` come from image offsets 0 and 1, and that `...1000` and
`...1001` come from image offsets 4 and 5. The `region` selects four logical bytes. The `session` knows from the
schema that `value` is a four-byte little-endian integer at offset zero. `Inspect` returns both the decoded value
and the two backing ranges it used.

Three coordinates appear in this one read, and the library keeps them apart on purpose:

| Coordinate | Value here | Who owns it |
| --- | --- | --- |
| Field offset | 0 | The schema: relative to the start of the record |
| Logical address | `0xffff800000000ffe` | The mapped source: the address the program used |
| Backing offset | 0 and 4 | The image: positions in the capture file |

Removing the second mapping makes the read fail with `MemoryAccessException` whose `Failure` is `Unmapped` and
whose `Address` is `0xffff800000001000`. The library never fills a missing page with zeroes, because a zero that
was never captured would look exactly like a real value of zero. Session failures also keep the requested `Path`
and the logical root `LogicalRegion`, so an error message can show both the process address and the file offset.

Exercise: swap the two backing ranges so that logical `...0ffe` comes from image offset 4. The result becomes
`0x56781234`. Changing mappings changes which bytes the layout sees; it does not change byte order or offsets.

## Follow the worked guides

Each guide builds on the previous one and includes executable C# snippets that the
[example runner](../examples/memory-analysis/index.md) verifies on both supported .NET versions. Every guide ends
with exercises and answers.

1. [Address spaces, mappings, and selected reads](memory-sources.md): distinguish field offsets, virtual addresses,
   and backing-file positions; choose a source type; read a value split across two mappings.
2. [Memory schemas and metadata import](memory-schemas.md): describe padding and signed bit slices by hand, then
   import ISF and BTF metadata instead of writing descriptors yourself.
3. [Stored pointers and bounded traversal](memory-traversal.md): keep pointer bits separate from their
   interpretation, resolve relative pointers, and walk circular lists and trees with explicit stopping conditions.
4. [Create records and patch offline memory](memory-updates.md): serialize new records, choose union
   interpretations, preview which file bytes an edit touches, and keep the original image unchanged.
5. [Memory budgets, caching, and source contracts](memory-reliability.md): interpret work counters, diagnose
   missing data, cache safely, and implement your own source with clear ownership rules.

## When to use these APIs, and when not to

Use the memory APIs when a pointer needs all 64 bits, when logical pages map to separate file ranges, or when
compiler metadata supplies explicit member placement. Use the ordinary stream APIs, described in
[memory addresses and stored data](memory-and-stored-data.md), for self-contained files whose offsets are
positions in that file. The two families cooperate: `region.OpenRead()` turns any finite region into a stream so
the core reader can parse runtime-sized layouts inside it.

The memory types compile into the main **CStructSharp** NuGet package with no additional dependencies. Import
`CStructSharp.Memory` for sources, schemas, and sessions, and `CStructSharp.Memory.Metadata` for the importers.

## Import coverage

Metadata import turns an external description of types into a `MemorySchema`. Each importer accepts a documented
subset and fails explicitly on anything else rather than guessing a layout.

| Input | Supported value descriptions | Explicit limits |
| --- | --- | --- |
| Compiled Portable layout | Fixed scalars, named structs/unions, typedefs, arrays, pointers, bitfields, promoted storage | No inferred native ABI; no conditional or runtime-sized projection |
| BTF v1 | Integers, floats with core codecs, pointers, arrays, structs, unions, enum/ENUM64, qualifiers/typedefs, member bit slices, split tables | Caller supplies pointer width and root ID; no ELF extraction or symbol discovery; functions/forward declarations are address-only |
| ISF 6.2.0 | Base integers/floats/bool, user structs/classes/unions, arrays, enums, pointers, bitfields | Caller supplies root, pointer width, and default order; no profile matching, relocations, symbol evaluation, or older schema guessing |

BTF's magic number determines the metadata's byte order. Split BTF receives a previously loaded `BtfMetadata` as
its base; IDs and string offsets keep their base identity. ISF respects explicit base-type endianness. Import
follows reachable type references from the chosen root and records address-only types in `Diagnostics`.

The source specifications are the [Linux BTF documentation](https://docs.kernel.org/bpf/btf.html) and the
[Volatility ISF 6.2.0 schema](https://github.com/volatilityfoundation/volatility3/blob/develop/volatility3/schemas/schema-6.2.0.json).
The tests and examples in this repository use independently authored synthetic data, not operating-system captures
or profiles.

## Run the examples

```sh
dotnet run --project docs/examples/memory-analysis -c Release -f net10.0
dotnet run --project docs/examples/memory-analysis -c Release -f net8.0
```

The [synthetic consumer](../examples/memory-analysis/index.md) runs the nine guide snippets and then a longer
scenario that combines them: a task list across two pages, the same numeric address in two address spaces, a tagged
tree, a corrupt cycle, a missing page, and an offline patch. It needs no external capture or profile.

## Glossary

- **ABI** (application binary interface): the platform rules a compiler follows for sizes, alignment, and padding.
  Metadata records the result of those rules so an analyzer does not need to reproduce them.
- **BTF** (BPF Type Format): compact binary type metadata emitted for the Linux kernel and its modules.
- **Copy-on-write overlay**: a source that reads from an unchanged original and stores only the bytes you changed.
- **ISF** (Intermediate Symbol Format): JSON type and symbol metadata used by the Volatility 3 framework.
- **Page**: the unit in which an operating system maps memory, commonly 4096 bytes.
- **Sentinel**: a special list node whose address marks the end of a circular list; it holds no data item.
- **Virtual address**: the address a process uses; the operating system maps it to physical memory or to nothing.
