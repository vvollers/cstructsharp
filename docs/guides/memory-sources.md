---
title: Address spaces, mappings, and selected reads
description: Follow a field from its local byte offset through an unsigned address to one or more backing image ranges.
---

# Address spaces, mappings, and selected reads

A memory image is a collection of bytes captured from another program or machine. An address stored in that
image is rarely an offset into the capture file. A process might call a location `0x1000`, while the capture
stores its bytes at file offset `0x9000`. Another process can use `0x1000` for entirely different bytes, and a
third page might not have been captured at all.

This guide introduces the objects that keep those coordinates apart. It ends with a bridge back to the ordinary
stream API, so you can use both families together. The types live in the **CStructSharp** NuGet package;
import `CStructSharp` and `CStructSharp.Memory` in C#.

## Why an address needs an address space

On a desktop operating system, every process sees its own **virtual address space**: a range of numbers from
zero up to a very large maximum, of which only some ranges are backed by real memory. The operating system decides
which physical pages sit behind which virtual pages, and it makes the same decision separately for every process.
Consequently, a number such as `0x1000` has no meaning on its own. It means "the byte the kernel calls `0x1000`",
or "the byte process 7 calls `0x1000`", and those are different bytes. A pointer copied from one space into the
other points at something unrelated.

The library models this with `IMemorySource`. A source is an object that can hand you bytes for an unsigned
address. Its **object identity** is the address space: if you construct two `MappedMemorySource` objects for two
processes, the library treats them as two spaces even when both are labeled `"process"`. The `Id` string exists
for error messages, not for identity. Keep and reuse the same source object while following addresses in one space.

Addresses are `ulong` rather than `long` for a practical reason. Kernel addresses on 64-bit systems commonly start
with `0xffff...`, which is a negative number when read as a signed integer. A stream position cannot represent
them, and arithmetic on a signed value would silently go wrong. Region lengths, on the other hand, are `long`
because a single operation must always be finite and must fit in an allocation.

## The objects and their jobs

| Object | Question it answers | What it does not contain |
| --- | --- | --- |
| `IMemorySource` | Which bytes are available at an unsigned address? | A schema or an implied process attachment |
| `MemoryRegion` | Which finite range of which source may this operation use? | An owned copy of those bytes |
| `MemorySchema` | Where are the fields, and how are their values encoded? | The captured data |
| `MemorySession` | How should this schema read, inspect, or update this region? | An automatic byte cache |
| `MemoryAccessContext` | How much work may this operation perform? | A snapshot or a lock on all sources |

Each object knows only its own question. A region does not know what type lives there; a schema does not know
where its records are; a session does not remember bytes between calls. That separation is why you can compile one
schema and then apply it to a file, a mapped process image, and a hand-made test fixture without changing anything.

## Read four bytes split across two mappings

Our declaration is `struct Record { uint32 value; };`. The sole field starts at local byte offset zero and occupies
four bytes. The example uses little-endian order, so the first byte contributes the lowest eight bits of the
integer. Picture the record sitting at the very end of a 4096-byte page: two bytes on the page ending at `...0fff`,
two bytes on the page starting at `...1000`. The capture stored those two pages in different places.

| Logical address | Image offset | Byte |
| --- | --- | --- |
| `0xffff800000000ffe` | 0 | `78` |
| `0xffff800000000fff` | 1 | `56` |
| `0xffff800000001000` | 4 | `34` |
| `0xffff800000001001` | 5 | `12` |

The logical bytes are consecutive even though the backing bytes are not. Image offsets 2 and 3 exist but belong to
nothing this record uses. The example below builds this arrangement from a `ByteArrayMemorySource` and two
`MemoryMapping` entries, then reads and inspects the field.

[!code-csharp[Read and inspect a split field](../examples/memory-analysis/MemoryTutorialExamples.cs#memory-cross-page)]

`Require` is the runner's assertion helper: it throws if the described result is false. All included snippets run
as part of the [memory examples](../examples/memory-analysis/index.md), so the numbers in this text are checked
against the implementation.

Here is what `MappedMemorySource` does when the session asks it for four bytes at `...0ffe`:

1. It looks up the mapping that contains `...0ffe` using a binary search over its sorted table. The first mapping
   starts at `...0ffe` and is two bytes long, so it can supply at most two bytes.
2. It asks the backing source `image` for two bytes at image offset 0 and copies them into the first half of the
   destination.
3. It looks up `...1000`, finds the second mapping, and asks `image` for two bytes at offset 4.
4. It returns the four bytes in logical order. It has not reinterpreted them; byte order is the schema's business.

The result is `0x12345678U`. `inspection.Selection.Region.Address` is the logical address, while
`inspection.BackingRegions` identifies image ranges `(0, 2)` and `(4, 2)`. Removing the second mapping causes
`Unmapped` at `...1000`, even though the backing image has plenty of bytes elsewhere. A hole is missing
information; it is never interpreted as an integer zero, because a fabricated zero would be indistinguishable from
a genuine one.

## Choose the amount of inspection

The session offers three levels of detail for one path. Pick the cheapest one that answers your question.

| Call | Returns | Reads data? |
| --- | --- | --- |
| `Resolve(region, "Record", "value")` | A `MemorySelection`: the logical region, type, and field metadata | Only if the path crosses a pointer |
| `Read(region, "Record", "value")` | The decoded value | Yes, the selected storage |
| `Inspect(region, "Record", "value")` | The value, the selection, and the ordered backing ranges | Yes, plus mapping lookups |

`Resolve` is useful for address displays and for planning an edit: it tells you where a field is without decoding
it. Resolving an ordinary member is pure arithmetic on schema offsets. Resolving through `.value` on a pointer must
read the pointer bytes first, because the target address is data. `Inspect` is the right call for a viewer that
must show evidence of where bytes came from; `Read` is enough when you only need the value.

Use an empty path to read the root. The managed result types are:

| Selected type | Result |
| --- | --- |
| Scalar | The core codec's managed type, for example `uint` for `uint32` |
| Signed bit slice | `long`, sign-extended from the selected bits |
| Pointer | `StoredPointer` holding the stored bits and their width |
| Struct or union | A `StructValue` dictionary keyed by member name |
| Array | `object?[]` with one element per declared element |

Reading a whole union returns every member's interpretation of the same bytes. That is honest but not a claim
that all interpretations are meaningful; choose a member path when your application knows which one is active.

A selected read touches only the storage its path needs. If a record's second page is missing, reading a field
on the first page still succeeds, while reading the whole record fails. The region bounds the root selection only:
a pointer target is a separate region supplied by the resolver, and it need not lie inside the root region.

## Choose and compose sources

Five source types ship with the library. Each answers the same question, "which bytes are at this address?",
but obtains them differently. They are designed to be stacked.

| Source | Typical use | Ownership and consistency |
| --- | --- | --- |
| `ByteArrayMemorySource` | Small fixtures and editable offline images | Clones its input; `ToArray()` also returns a copy; writes advance its generation |
| `StreamMemorySource` | Read a capture through a seekable file stream | Caller owns and closes the stream; reads restore its position |
| `MappedMemorySource` | Translate a logical range to image ranges | Snapshots the mapping table; backing sources remain caller-owned |
| `CachedMemorySource` | Avoid repeated reads from a stable or versioned source | Retains exact requested ranges within a fixed byte capacity |
| `OverlayMemorySource` | Edit a stable image without changing it | Stores changed bytes separately; see [offline updates](memory-updates.md) |

A realistic stack for a capture file looks like this. Reads enter at the top and are answered by the first layer
that can answer them; every layer forwards the same budget.

```text
CachedMemorySource   "cached"     remembers exact ranges already read
        |
MappedMemorySource   "kernel"     logical address -> file offset
        |
StreamMemorySource   "capture"    file offsets, signed stream positions
        |
FileStream                        owned by your application
```

For a file, open a readable seekable stream and pass it to `new StreamMemorySource("capture", stream)`. Its own
addresses are file offsets and must fit signed stream positions. A mapping above it can still expose those bytes at
addresses above `long.MaxValue`, because translation happens before the stream is touched. The adapter does not
watch the file for external changes, so keep the file stable while analyzing it, and dispose the stream yourself
after all views are finished.

Rules for mapping tables:

- Logical ranges must not overlap; otherwise a lookup would be ambiguous. The constructor rejects overlap.
- Several logical ranges may point at the same backing bytes. This is called **aliasing** and is normal in real
  systems (two processes sharing a library page). Reads allow it; a patch rejects physical overlap within its own
  fragment list so one edit cannot overwrite itself.
- A mapping's backing region can belong to another `MappedMemorySource`. `Describe` returns the next layer only,
  while `Inspect` and patch planning follow all known mapping layers down to the final sources.
- A custom `IMemorySource` is a final source as far as the library can tell, even if it delegates internally.

## Bridge a finite region to the stream APIs

The ordinary `CStruct` reader consumes a `Stream`. Any region can become one: `region.OpenRead()` returns a
read-only, seekable stream whose position zero is the region's starting address and whose length is the region's
length. Disposing the stream closes only the view, never the source or a caller-owned file stream.

This bridge lets the core reader handle runtime-sized layouts within a finite range. For example, an EOF-sized array
stops at the region's end instead of scanning an unbounded address space. The bridge does **not** rewrite pointer
bytes: ordinary stream pointer rules apply when calling core stream APIs, so a stored 64-bit process address will
not be a valid stream position. Use `MemorySession` for unsigned address-space pointers.
`PortableMemorySchema.Create` itself accepts fixed layouts only, not runtime-sized ones.

## Check your understanding

1. Two `MappedMemorySource` objects are both named `"process"` and both map address `0x1000`. Does the library
   treat a pointer read from one as valid in the other?
2. In the worked example, image offsets 2 and 3 hold zero bytes. If you extend the first mapping to four bytes
   and delete the second, what value does `Read` return, and does the library report a problem?
3. A region is `new MemoryRegion(mapped, 0xffff800000000ffe, 4)`. What is `Position` 0 of `region.OpenRead()`,
   and what happens when the core reader seeks to position 4?

Answers: **no**, sources are distinguished by object identity, so the second source is a different address space
and the address must be resolved there explicitly; **`0x00005678`** with no error, because the mapping is
now valid and the library cannot know that offsets 2 and 3 are the wrong bytes, which is why mapping tables must
come from a trustworthy description of the capture; **the logical address `...0ffe`**, and position 4 is the
end of the stream, so a read there returns zero bytes (ordinary EOF) rather than touching `...1002`.

Continue with [schemas and metadata](memory-schemas.md) and [pointer resolution and traversal](memory-traversal.md).
