---
title: Bounded discovery over sparse memory
description: Recognize candidates, retain readable evidence and bound transport and graph exploration.
---

# Bounded discovery over sparse memory

A signature identifies a candidate. It does not prove that an object is live, structurally valid or belongs to a particular process. Keep recognition, structural checks and independent corroboration separate. Report unavailable evidence separately from a failed predicate.

## Prepare recognition and field checks

A C struct places members in declaration order, with padding determined by layout settings. This example has two four-byte unsigned fields at offsets 0 and 4:

```c
struct Header { uint32 tag @magic(tag == 7); uint32 reserved; };
```

Bytes `07 00 00 00 00 00 00 00` in little-endian order hold tag 7 and reserved 0. Compile the declaration once, call `PrepareMatcher("Header")` once and reuse the returned matcher. Its `Matches(bytes)` decodes only fixed fields needed by the selected expressions. Execution rents local typed slots; it retains no input or caller variables. Ordinary runtime `Matches` uses the same lazily cached plan. Preparation and first execution have distinct costs.

Inspect `PreparedMatcher.Rules` before treating recognition as coverage of all constraints. Each rule states whether it is checked and why an unchecked rule is excluded. Evaluable magic rules take precedence over expect rules; supports rules do not participate. Runtime-sized placement, nested conditions and conditional arms retain the documented recognition limits. Missing caller variables throw; short required storage returns false. A successful recognition does not validate the entire record.

For application checks, bind a scalar with `schema.Bind<uint>("Header", "reserved")` and construct `MemoryFieldProbe<uint>("reserved-zero", reader, value => value == 0)`. `Evaluate` returns Match, Mismatch or Unavailable. Supply a `MemoryValidity` whenever some bytes in a presentation buffer are unknown. A zero-filled unread reserved field is Unavailable, never Match. Callbacks run only after required bytes are known, and their exceptions propagate. Shared predicates must be safe for concurrent use.

## Preserve partial storage and ownership

`MemoryRegion.ReadPartial` copies into caller storage and returns BytesRead, IsComplete and the storage Failure. Positive short reads continue at the next byte. The unread suffix stays untouched. Cancellation and exhausted budgets terminate instead of becoming ordinary storage failures. Required extents use an unsigned start and a finite length, so a range can include ulong.MaxValue without representing an exclusive endpoint of 2^64.

`MemoryValidity` owns a bounded normalized list of known byte intervals, measured from buffer offset zero. It merges adjacency, accepts no out-of-buffer intervals and never infers known zeroes. A buffer-native scalar reader requires only its selected field extent, not the whole root record; it cannot follow pointers. Explicit schema-bound snapshot reads reject readers from another schema instance.

`schema.BindBytes` prepares a fixed array of one-byte scalar storage. The reader returns exact signed or unsigned storage bits, into caller-owned memory or as a borrowed span over a stable buffer. It deliberately bypasses element codecs. It imposes no encoding, terminator, trimming or truncation policy. The application must choose those policies explicitly; use ordinary scalar reads when custom codec callbacks are part of the meaning. A missing terminator is not silently a complete string.

## Scan sparse ranges without joining holes

`MemoryScan.Run` accepts finite ordered, disjoint regions and a callback over borrowed windows. Regions in the same source may be adjacent; unrelated source objects never join, even if their diagnostic IDs match. Generation changes stop the operation with SourceChanged. Windows and overlap are explicitly bounded. Bytes are valid only for the callback's duration; copy complete findings before returning.

A window exposes its source, unsigned Address, Bytes and the owned start interval. Test candidate starts only inside OwnedOffset through OwnedOffset + OwnedLength. Overlap provides context and is reused between windows; it does not transfer candidate ownership. Align starts using logical addresses, rather than window offset zero. This emits a boundary-spanning candidate once. No window combines bytes across a hole.

Positive short reads are retried with bounded adaptive requests. UnavailableStride is an explicit assumption: use 1 for arbitrary byte holes; page-sized strides are appropriate only when unavailability is page-granular. The scanner retains later readable islands. ReadableBytes counts newly copied bytes, UnavailableBytes counts skipped bytes, and Stop distinguishes exhaustion, a visitor stop, the window limit and a source change. A stop or unknown bytes makes a search incomplete, including when no findings were produced.

## Capture selected fields explicitly

`MemoryReadSet` prepares fixed paths and exposes their exact descriptors in Fields. It does not follow pointers. Scratch size, field count and allowed coalescing gap are bounded at preparation. Capture defaults to `Elementwise` request order; Coalesced is opt-in because it changes request sizes and budget accounting. A missing merged gap triggers independent selected-field retries. A failure in padding cannot invalidate a later independently readable field.

`MemorySnapshot` owns captured bytes and validity, retains the original region/source identity and reports GenerationBefore and GenerationAfter. Invalid padding is never evidence. Matching generations do not promise an atomic snapshot of a live source. Keep an operation's context shared across transport, decoding, callbacks and retries; cache hits and local decoding still charge work and check cancellation.

## Describe backing and explore bounded graphs

`IMemoryMappingSource` is an optional capability for static or dynamic logical views. `MemoryRegion.DescribeBacking` and inspection follow it on demand and return terminal fragments in logical order. Source object identity, coordinate and generation matter; a label alone does not identify a process or address space. Describing a mapping neither reads payload nor guarantees it matches an earlier live observation. Patch planning continues to require static mapped-source rules.

`MemoryGraph.Run` streams breadth-first or depth-first traversal in supplied root and edge order. The application supplies canonical identity, including process/source/generation and pointer-tag policy where necessary. Duplicate edges charge work. Node, edge, depth and pending-frontier limits are independent; the returned stop reason and counts describe partial exploration. The visitor can skip a branch or stop at the preferred first result. Cancellation, budget exhaustion and callback exceptions propagate. Linux page tables, allocation rules and cryptographic validation belong in the application.

The [executable memory example](../examples/memory-analysis/index.md) checks recognition, unknown zeroes, coalesced capture and sparse-window ownership alongside the mapped-memory tutorial.
