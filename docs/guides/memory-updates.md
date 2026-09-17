---
title: Create records and patch offline memory
description: Serialize explicit layouts, review physical fragments, preserve original images, and handle partial commit failures.
---

# Create records and patch offline memory

Editing a memory image is different from editing a text file. The bytes around the value you change can carry
meaning that the schema does not describe: padding that a program happens to rely on, unused bits of a flags
word, or union storage whose active interpretation you do not know. A careless write destroys that information.
This guide explains how the memory APIs create new records, stage edits to existing ones, and let you review exactly
which file bytes an edit will touch before anything is written.

## Creating and updating follow different preservation rules

`MemorySession.Serialize` creates a brand-new record. It starts from zero-filled storage, encodes the members you
supply, and returns an owned byte array. Padding and unselected union bytes are zero because nothing existed before.

`MemorySession.PlanUpdate` changes a record that already exists. It first reads the current storage of the selected
field, then encodes the new value over a copy of those bytes. Anything the new value does not cover survives: a bit
slice keeps its neighboring bits, a struct keeps its padding gaps.

The result of planning is a `MemoryPatch`, a description of the proposed write. `Commit` performs it. Keeping the
stages separate lets an editor show the affected backing ranges and let a user confirm before bytes change.

## Supply values in their declared shape

| Type | Input to `Serialize` or `PlanUpdate` |
| --- | --- |
| Scalar | A value accepted by its core codec, for example `42U` for `uint32` |
| Signed bit slice | An integer within the slice's range |
| Struct | An `IReadOnlyDictionary<string, object?>` with a value for every member |
| Array | An `IList` with exactly the declared number of elements |
| Pointer | A `StoredPointer` with the declared width |
| Union | An exact-size `byte[]`, or a `MemoryUnionSelection` choosing one member |

For a new struct, provide every member, including explicit null pointers as `new StoredPointer(0, width)`. The
serializer creates bytes for one record only. It does not allocate pointer targets or choose their addresses; the
relative-pointer [example](memory-traversal.md) shows two independently serialized records placed in one image
by the application.

## Choose a union interpretation

A union overlays members on the same bytes. When you read a union, the result dictionary contains every member's
interpretation of that storage. Writing all of them back would make the outcome depend on which member happened to
be encoded last, so the writer refuses to guess. You either name one member or supply the raw bytes.

[!code-csharp[Create a union from one member or exact bytes](../examples/memory-analysis/MemoryTutorialExamples.cs#memory-union)]

Choosing `number` writes `78 56 34 12` for `0x12345678U` in little-endian order. A `MemoryUnionSelection` clears
the union's storage before encoding the chosen member, so bytes outside a smaller member become zero. That also
applies when replacing an entire existing union through `PlanUpdate`. When neighboring bytes matter, update a
specific member path instead, or supply the exact raw bytes you want. For a promoted (anonymous) union inside a
struct, use its descriptor field name with an explicit selection rather than a dictionary of overlapping promoted
members.

## Preserve the original with an overlay

An `OverlayMemorySource` is a copy-on-write layer: it reads every byte from an unchanged backing source and stores
only the bytes you have written in a small dictionary keyed by address. The original capture stays exactly as it
was, and the overlay can be discarded if an experiment goes wrong. Because it only stores differences, an overlay
over a multi-gigabyte image costs memory proportional to the edit, not the image.

Where you put the overlay in the stack decides what a patch preview shows. Placing it **below** the mapping layer
means the overlay's addresses are image offsets, so previews identify physical file positions. Placing it above the
mapping means previews show logical addresses instead. In the arrangement below, a field at logical address
`0x1000` occupies image bytes 0–1 and 4–5; bytes 2–3 belong to something else.

```text
MappedMemorySource   "virtual copy"   0x1000 -> overlay 0..1, 0x1002 -> overlay 4..5
        |
OverlayMemorySource  "edits"          changed bytes only
        |
ByteArrayMemorySource "original"      never modified
```

[!code-csharp[Preview, commit, and export an offline edit](../examples/memory-analysis/MemoryTutorialExamples.cs#memory-offline-patch)]

The patch has two fragments, one per mapping. Each `MemoryPatchFragment` has a `Region` in the final source, a
`Generation`, an `Expected` byte array captured while planning, and a `Replacement` byte array. The array
properties return copies, so a preview UI cannot accidentally change a prepared patch. After `PlanUpdate` the old
value is still visible; after `Commit`, the overlay shows the new value and `original` still holds `78` at offset 0.

Exporting the overlay through a finite region gives `44 33 AA BB 22 11`: the image with the edit applied and
its unrelated bytes intact. Exporting the four-byte logical record instead would give `44 33 22 11`. Choose the
region whose coordinate system matches the output you need.

`maxChangedBytes` limits the number of distinct addresses the overlay retains, not the sum of all writes; writing
the same address again replaces its entry. The backing image must remain stable, and the overlay checks its
generation to detect otherwise. An overlay cannot create bytes where its backing source has a hole, and it does not
extend or resize the source.

## What planning and commit guarantee

Planning does five things: it resolves known mapping layers into physical fragments, checks that those fragments do
not overlap each other, records each source's generation, reads the expected bytes, and stages the replacement
bytes. A selected scalar update does not rewrite its enclosing record. A bitfield update preserves neighboring bits.
A complete struct update encodes its members over a copy of the existing storage, preserving gaps; only a fresh
`Serialize` leaves gaps zero.

The overlap check exists because of aliasing. If two logical ranges map to the same physical bytes, one logical
edit could write the same file bytes twice with different values. Rejecting overlapping fragments makes that
impossible within a single patch.

Before its first write, `Commit` checks every fragment: the source must implement `IWritableMemorySource`, its
generation must match, and its current bytes must equal `Expected`. Any mismatch raises `StaleSource`, and the
patch must be planned again from the current state. These validation reads spend the supplied context. A patch can
be previewed against a read-only source, but commit then fails because that source cannot write. A cache is
read-only too; plan updates through the writable source or mapping beneath it.

**Commit is not an atomic transaction.** There is no lock spanning arbitrary sources, so a later write can fail
after earlier writes succeeded, and a budget or cancellation can also interrupt the write phase. Such failures
raise `MemoryPatchCommitException`: `CompletedBytes` counts confirmed earlier fragments, `FragmentIndex` identifies
the fragment whose completion is uncertain, and `InnerException` records the cause. That fragment may be partly
changed. Validation failures before writing keep their ordinary exception types, because nothing was written.

Do not retry the same patch blindly, and do not assume that zero completed bytes means no mutation occurred.
Inspect or discard the affected offline copy, then re-plan from a known state. A disposable overlay makes recovery
simple because the original capture is untouched. Generation checks detect reported changes; they do not freeze a
live source.

For raw replacements that need no schema, `MemoryPatch.Create(region, replacement)` provides the same fragment
planning and commit behavior. The replacement length must equal the selected region length exactly.

## Check your understanding

1. A record has a `uint8 flags` field where bit 7 is undocumented but set. You call `PlanUpdate` on a 3-bit slice
   at bits 0–2. Does bit 7 survive? Would it survive if you rebuilt the record with `Serialize` instead?
2. You plan a patch, then another part of your program writes to the same `ByteArrayMemorySource`. What happens
   when you call `Commit`, and why is that the desired behavior?
3. Two mappings expose the same two image bytes at logical `0x1000` and again at `0x1002`. A four-byte field at
   logical `0x1000` therefore covers both mappings. What happens when you plan an update to it?

Answers: **yes**, `PlanUpdate` reads the storage unit and changes only the selected bits; **no**, `Serialize`
starts from zeroes and only knows the members you pass. **`Commit` throws `MemoryAccessException` with
`StaleSource`** before writing anything, because the source's generation advanced; the expected bytes may no
longer describe the image, and writing over an unknown state could corrupt it. **Planning throws
`ArgumentException`** because the two fragments overlap physically; one edit must not write the same file bytes
twice.

See [reliability and ownership](memory-reliability.md) for budgets, cancellation, and source implementation rules.
