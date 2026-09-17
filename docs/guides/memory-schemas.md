---
title: Memory schemas and metadata import
description: Describe fixed native layouts, preserve padding and bit slices, and import reachable ISF or BTF types.
---

# Memory schemas and metadata import

A schema describes how bytes represent values: which member starts at which offset, how many bytes it occupies,
and which codec turns those bytes into a number. For a file format you usually write that description yourself.
For a memory image the description already exists, because a compiler made those decisions when it built the
program. This guide shows how to state such decisions explicitly and how to import them from two common metadata
formats.

## Why a memory schema takes offsets instead of computing them

Consider this C declaration compiled for a typical 64-bit platform:

```c
struct sample { uint8_t state; uint32_t count; };
```

The compiler does not place `count` at offset 1. Its platform's **application binary interface** (ABI) requires a
four-byte integer to sit at an address divisible by four, so it inserts three padding bytes and places `count` at
offset 4. The struct is eight bytes long. A different compiler, a `#pragma pack` directive, or a different CPU can
produce a different answer, and a bit-packed member such as `int flag : 3` adds compiler-specific bit placement on
top. [How C structs occupy memory](native-c-memory.md) explains these rules in depth.

An analyzer running on your machine cannot safely reproduce another platform's decisions from type names alone.
`MemorySchema` therefore never guesses. Every composite member carries an explicit byte offset, every type carries
an explicit size that includes padding, and bit-packed members carry an explicit bit offset and width. Where the
Portable language computes placement from declaration order, a memory schema records placement as given.

Compile a schema once and reuse it in sessions that inspect many records or several address spaces. Validation
happens at construction, so a bad description fails before any source is read.

## Three ways to obtain a schema

1. **Project an existing layout.** `PortableMemorySchema.Create(layout, "Root")` converts a compiled `CStruct`
   with a fixed layout into descriptors. It copies the compiled offsets, pointer width, byte order, and codecs; it
   does not parse the declaration again. Use this when your own Portable declaration is already the authority.
2. **Write descriptors by hand.** Construct `MemoryTypeDefinition` and `MemoryField` objects when your application
   knows the placement, for example from a hardware manual or a debugger.
3. **Import metadata.** Feed BTF or ISF to the importers and use the returned `MetadataImportResult.Schema` and
   `RootTypeId`. Use this for operating-system structures whose placement was recorded by the build.

All three routes produce the same `MemorySchema` and support the same session operations. The Portable projection
rejects conditional and runtime-sized members, because a memory schema must know every size in advance. For those
layouts, open a bounded [stream view](memory-sources.md) and use the core API.

## The building blocks of a schema

A schema is a graph of `MemoryTypeDefinition` nodes connected by IDs. Each definition has a `Kind`:

| Kind | Meaning | Extra information carried |
| --- | --- | --- |
| `Scalar` | An integer, float, boolean, or enum decoded by a core codec | `ScalarType` names the codec, for example `"uint32"` |
| `Pointer` | An unsigned stored address | `Size` is the pointer width; `ElementTypeId` names the target, or null for opaque |
| `Struct` | Members at explicit offsets that must not overlap | `Fields` |
| `Union` | Members at explicit offsets that deliberately overlap | `Fields` |
| `Array` | A fixed number of equally sized elements | `ElementTypeId` and `Count`; `Size` is the total byte length |
| `Incomplete` | A type with identity but no readable representation, such as a forward declaration | Nothing; size is zero |

Every definition has an `Id` and a `Name`, and they serve different purposes. `Id` is the reference key used by
fields and by session calls. `Name` is a display string. Native metadata often contains several distinct types with
the same name, so the importers generate unique IDs such as `btf:7` and keep the original name for display.

A `MemoryField` connects a containing struct or union to a member: a `Name`, a `TypeId`, a byte `Offset` relative
to the start of the containing record, and optionally a bit slice (`BitOffset`, `BitWidth`, `Signed`). A field
marked `Promoted` makes the members of an anonymous nested struct or union visible in its parent, mirroring how C
lets you write `record.flag` when `flag` lives in an unnamed inner union.

The `MemorySchema` constructor validates the whole graph before it returns:

- every referenced ID must exist, and a scalar's declared size must equal its codec's size;
- every member must fit inside its container, and an array's size must equal element size times count;
- struct members must not overlap (two bit slices may share one storage unit as long as their bits are disjoint);
- a type may not contain itself by value, directly or through other types, because that would need infinite
  storage. A pointer back to the same type is fine: the pointer has a finite size.

## Describe a padded record with a signed bit slice

Suppose metadata says a record is eight bytes long. A three-bit signed state occupies bits 1 through 3 of byte
zero; a four-byte count starts at offset 4. Bytes 1 through 3 are padding. We describe the state's storage unit
as a one-byte scalar, even though the selected value uses only three of its bits.

| Byte offset | Input | Meaning |
| --- | --- | --- |
| 0 | `8F` | State bits are `111`; the neighboring bits are also present |
| 1–3 | `AA BB CC` | Padding, preserved by an update |
| 4–7 | `2A 00 00 00` | Little-endian count 42 |

[!code-csharp[Explicit offsets and signed bit updates](../examples/memory-analysis/MemoryTutorialExamples.cs#memory-explicit-layout)]

Byte `8F` is `1000 1111` in binary. Bits are numbered from the least significant end, so bit 0 is the rightmost
digit. Bits 1 through 3 are `111`. A three-bit two's-complement number covers -4 to 3:

| Bits | Unsigned | Signed |
| --- | --- | --- |
| `011` | 3 | 3 |
| `100` | 4 | -4 |
| `110` | 6 | -2 |
| `111` | 7 | -1 |

Reading `state` therefore returns -1. Updating it to -2 stores `110` in bits 1 through 3, changing `8F` to `8D`;
bit 0, the high four bits, the padding, and the count remain unchanged. Bit offsets count from the least
significant bit of the decoded storage integer. Byte order decides how a multi-byte storage integer is assembled
from its bytes; it does not change what bit index zero means.

Two rules from this example are worth remembering. Reads sign-extend a signed slice and writes check the signed
range, so `-5` is rejected rather than wrapped. And an update reads the storage unit first, so bits that are not
part of the selected slice survive untouched; the [update guide](memory-updates.md) builds on this.

## Import a small ISF description

**ISF** (Intermediate Symbol Format) is the JSON metadata format used by the Volatility 3 memory-forensics
framework. Its symbol tables are generated from a kernel's debug information and describe base types, user types
with member offsets, enums, and symbol addresses. This importer consumes the value-type subset: it turns types
into descriptors. Choosing the profile that matches a capture, and finding the address of a record, remain the
application's job. An imported type says how a record is laid out, not where one is.

The following independently authored metadata places `value` at offset 4 in an eight-byte record. The first four
bytes are outside the selected member, so their values do not affect the result, 7.

[!code-csharp[Import and inspect a synthetic ISF record](../examples/memory-analysis/MemoryTutorialExamples.cs#memory-isf)]

Use `using CStructSharp.Memory.Metadata;` and `using System.Text;` for the importer and the UTF-8 conversion.
Take `RootTypeId` from the result rather than guessing the importer's ID spelling. Review `Diagnostics` alongside
`Schema.Types` when deciding which imported types can be read by value; a successful import can retain address-only
types as pointer targets without making those targets readable.

ISF import requires format `6.2.0`. It supports base integers, floats and booleans, structs, classes, unions,
arrays, enums, pointers, and member bitfields. Pass the target's pointer width explicitly when it is not eight
bytes. A base type's explicit endianness overrides the schema default. `maxBytes`, `maxTypes`, and cancellation
bound the import; symbol evaluation, relocations, profile matching, and guesses about older formats are outside
its scope.

## Import BTF and split tables

**BTF** (BPF Type Format) is a compact binary type description emitted for the Linux kernel and its modules. A
BTF blob has a small header, a table of numbered type records, and a string table. Each record has a kind (integer,
pointer, struct, and so on), a size or referenced type, and a variable-length payload such as struct members with
their bit offsets. Tools usually read the blob from the `.BTF` section of a kernel image or from
`/sys/kernel/btf`; this API does neither. You obtain the bytes and pass them in.

First supply the blob to `new BtfMetadata(bytes)`, which validates and indexes it. Then `FindType("task")`
locates a unique named type and `Import(typeId, pointerSize: 8)` compiles that type and everything it references.
`FindType` rejects duplicate names rather than picking one, because two kernel types can share a name while having
different layouts. When names are ambiguous, use the numeric ID from the metadata producer.

The blob's magic number tells the parser its byte order (`9F EB` for little-endian, `EB 9F` for big-endian). The
target's pointer width is a separate setting, because BTF describes types, not the machine word size.

Here is a complete synthetic blob with two types: a four-byte unsigned integer and an eight-byte record whose
member starts at byte 4. BTF stores member offsets in bits, so the record says 32 and the importer produces byte
offset 4. The comments identify each section; applications normally receive these bytes from a build tool rather
than writing them by hand.

[!code-csharp[Import an independently encoded BTF record](../examples/memory-analysis/MemoryTutorialExamples.cs#memory-btf)]

The decoded value is 9; the first four image bytes are padding. Because the blob was written independently of the
importer, the example checks that offsets are interpreted rather than inferred.

A **split BTF** table extends a base table; the kernel uses this so each module only ships the types it adds.
Construct the base `BtfMetadata` first, then pass it as `baseMetadata` when constructing the split table. Base
type IDs and string offsets keep their identity. Do not concatenate the blobs or renumber IDs yourself.

Supported representations are integers, supported floating-point sizes, arrays, pointers, structs, unions, enums
and ENUM64, qualifiers such as `const`, typedefs, and member bit slices. Function and forward-declaration targets
remain address-only. Older BTF encoded bitfields inside the integer type itself; the importer normalizes such
legacy slices when they are used as members, but does not offer them as standalone scalars. Any other reachable
value kind fails explicitly instead of producing a guessed layout.

## Semantic metadata versus compiled storage views

`Schema.Types`, `GetType`, and `GetField` present the imported model: real names, IDs, offsets, bit slices, and
`Provenance` strings that say where a definition came from. Use these in an analyzer's user interface.
`GetField` looks up immediate members; session paths additionally resolve uniquely promoted members.

To decode bytes, the schema also compiles a `CompiledLayout`: an ordinary Portable `CStruct` in which every
metadata type becomes a union of byte arrays placed at the recorded offsets. This trick lets the memory APIs reuse
the core's scalar and bitfield codecs without a second decoder. Its generated names such as `m0` and `f0` are
placement labels, not metadata names. `GetCompiledName(id)` connects a semantic type to its view when you need to
inspect the compilation. Do not show these generated names to users as if the metadata had supplied them.

## Check your understanding

1. Metadata describes `struct pair { uint8 a; uint8 b; }` with `b` at offset 0 and `a` at offset 0. The schema
   constructor throws. What is wrong, and how would you describe a type where both really do share byte 0?
2. A four-byte storage unit holds a signed 5-bit slice at bit offset 12. What is the range of values the slice can
   store, and what does an update to 20 do?
3. `FindType("list_head")` throws `ArgumentException` saying the name is ambiguous. What does that tell you about
   the blob, and what should your program do?

Answers: **struct members may not overlap**; declare the type as a `Union`, whose members are expected to share
storage, or give the fields different offsets. **-16 to 15**, so an update to 20 throws
`ArgumentOutOfRangeException` before any byte is staged. **The blob contains two or more types named
`list_head`**, perhaps one from the base table and one from a module; ask the metadata producer for the numeric
ID you want and call `Import` with that ID instead of a name.

The [overview](memory-analysis.md#import-coverage) links the source format specifications and summarizes their
limits. Next, use the schema to [follow pointers](memory-traversal.md) or [create and update records](memory-updates.md).
