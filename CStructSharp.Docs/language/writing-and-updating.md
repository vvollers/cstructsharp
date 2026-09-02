---
title: Writing and updating
description: Encode complete values, limit output, and replace existing storage with explicit pointer and union rules.
---

# Writing and updating

`Serialize` creates a new encoded region. `WriteStream` writes one at the current destination position.
`UpdateStream` finds and replaces storage that already exists.

All three use the prepared layout to check value shape, numeric range, arrays, strings, enums, unions, pointers, and
configured limits.

## Struct, array, union, and pointer inputs

A whole struct write needs every required field. It may come from a dictionary, `ExpandoObject`, readable POCO, or a
dynamic object returned by parsing.

Selecting a fixed array field requires the complete collection with exactly the declared count. Selecting one indexed
item requires only that element and uses the element codec.

A whole union write accepts `UnionValue`:

- an unchanged parsed value or `UnionValue.FromRaw` writes the complete raw storage;
- `UnionValue.FromMember` and `WithSelectedMember` select one declared member;
- a new selected-member write starts with zero-filled union storage; and
- a wrong union name, raw length, or member name fails before union bytes are submitted.

Legacy dictionary, expando, or POCO values cannot stand in for a whole union because they do not state which member
or raw bytes to preserve. `Members` and raw storage are read-only snapshots. `WithoutSelection` returns to raw
pass-through when a raw snapshot exists.

Pointer input supplies a stored/effective coordinate according to write options. Serialization does not allocate or
relocate target objects. Null is a value only for a scalar pointer and encodes address zero. Null for a primitive,
enum, string, array, struct, union, or pointer collection is `WriteFailed`.

## Write limits

`WriteOptions` defaults to:

- `MaxArrayElements = 1,000,000`;
- `MaxStringBytes = 16 MiB`;
- `MaxTotalBytesWritten = 64 MiB`; and
- `MaxNestingDepth = 256`.

The array limit applies to one array. A single-pass enumerable is consumed only far enough to prove that its
count/limit was exceeded; it is not materialized without a bound.

The string limit includes a terminated string's marker or a fixed character buffer's padding. The total-byte limit
counts submitted bytes and new stream extent, including gaps created beyond the original stream length. Rewriting
shared bitfield or union storage counts each physical write.

Negative byte/array limits or a non-positive nesting limit are argument errors rejected before output begins.
Exceeding a configured write ceiling raises `CStructWriteLimitException`.

`WriteStream` is not transactional. It checks each next write before exceeding a limit, but a later field failure may
leave earlier fields in the destination.

## Update validation before destination writes

`UpdateStream` first locates the selected path by reading existing data. `UpdateOptions` therefore has separate
`MaxTraversal*` limits for pointer depth/target bytes, strings, total bytes read, and nesting, in addition to the
inherited write limits used for the replacement.

A traversal limit produces `CStructReadLimitException` before destination bytes change.

After locating the target, the writer runs once against a bounded copy-on-write view. That view:

- reads unchanged baseline bytes within traversal limits;
- records replacement ranges without extending the stream;
- lets later writes overlay earlier staged writes; and
- reports every library-detectable path, shape, range, conversion, pointer, union, preservation, and write-limit
  error before the destination receives a write.

On one of those validation failures, content and length remain unchanged and the original position is restored.

After validation, changed ranges are combined and committed in increasing address order. A physical destination can
accept a prefix and then throw. CStructSharp stops, keeps that cause in `CStructWriteException`, does not attempt an
unreliable generic rollback, and restores position on a best-effort basis. Successful and validation-failed updates
restore the original position.

## Pointer updates

`UpdateStream(path + ".address")` changes the stored pointer coordinate. `UpdateStream(path + ".value")` follows one
level and writes the target.

An existing non-null target is required by default. Set `RequireExistingPointerTarget = false` only when writing at
address zero is intentionally allowed.

Each `.value` consumes one declared level. Stopping before the final level writes the next pointer; consuming every
level writes the final value.

Write callers supply a physical target address. In relative mode, CStructSharp checks
`targetAddress - WriteOptions.Origin`, requires a positive stored result that fits the configured pointer width, and
encodes it. Address zero is written directly as null. A non-null target equal to the origin is rejected because its
relative form would also be zero.

Negative addresses/offsets, arithmetic overflow, width overflow, and non-integer values fail before pointer bytes are
changed.

## Union and bitfield updates

Replacing a whole union clears existing storage by default (`ClearUnionStorage = true`) before writing a selected
member. Set it to `false` only when a selected-member update should start from the existing full region. An
unselected raw `UnionValue` replaces the complete region exactly.

A path selecting one union member is surgical and preserves bytes outside that member. Updating a bitfield reads the
complete shared storage unit and changes only the selected bits.

## Paths and dynamic lengths

Paths use dot-separated names and at most one unpadded non-negative decimal index per segment, for example
`root.items[2].value`. Empty segments, signs, trailing text, repeated brackets, and indices on non-arrays produce
`CStructPathException`.

`GetDynamicArrayLength` accepts fixed/runtime arrays, unsized character strings, and named terminated strings. It
returns array element counts or decoded string character/code-unit counts and restores the original stream position.

Path resolution retains array, bitfield, pointer-depth, alignment, and union information rather than reducing every
selection to a raw offset. An aligned update uses the already resolved target position and does not align it twice.

Use the [writing guide](../guides/writing-and-serialization.md) and
[update guide](../guides/updating-existing-data.md) for ordinary application workflows.
