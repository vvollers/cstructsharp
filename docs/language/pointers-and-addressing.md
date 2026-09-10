---
title: Pointers and addressing
description: Interpret stored coordinates with explicit widths, origins, path levels, and traversal limits.
---

# Pointers and addressing

## Pointers

`T *field;` stores an unsigned coordinate that may refer to another value in the same binary source. The coordinate
is data, not a native memory address.

The `CStruct` constructor sets pointer storage to 1, 2, 4, or 8 bytes for the entire layout. Choose the width from the
binary format. A physical .NET stream position must fit a non-negative `Int64`, so an encoded eight-byte value above
`long.MaxValue` is rejected even when pointer following is disabled.

For:

```c
struct root {
    uint8 marker;
    uint16 *target;
};
```

a two-byte packed null pointer looks like:

```text
offset   0    1    2
bytes   AA   00   00
field   marker target pointer storage
value   170  null (stored address 0)
```

Packed size is 3 and the pointer begins at offset 1. In aligned mode the pointer begins at offset 2, byte 1 is
padding, and total size is 4. A stored zero is always null; a relative origin is never added to zero.

## Absolute and relative modes

`ReadOptions.AddressingMode` controls how a nonzero stored coordinate becomes a target:

| Mode | Meaning of stored value | Effective target |
| --- | --- | --- |
| `Absolute` | Stream position | Stored value |
| `Relative` | Offset from a base | Checked `Origin + stored value` |

Before reading, the effective target must be non-negative, fit `Int64`, and lie in the readable stream or supplied
memory region. `Pointer.Address` always exposes the stored number, not the origin-adjusted target.

## Pointer result states

The direct result is `Pointer`:

| State | `Address` | `IsNull` | `IsDereferenced` | `Value` |
| --- | ---: | --- | --- | --- |
| Null | `0` | `true` | `false` | `null` |
| Nonzero, following disabled | `>0` | `false` | `false` | `null` |
| Followed | `>0` | `false` | `true` | Decoded target |

Set `DereferencePointers = false` to inspect coordinates without following them. This keeps a nonzero unresolved
pointer distinct from null.

Serialization writes a coordinate. It does not move or allocate target objects. A scalar pointer or selected pointer
array item may receive `null`, which encodes zero. Null for a pointer collection or non-pointer value is a shape
error.

## Multi-level pointers

`T **field` stores a pointer whose target contains another pointer. Each level has its own encoded coordinate:

```text
root.ptr (depth 2)        next pointer (depth 1)        uint8 target
offset 0: [04 00] ─────► offset 4: [08 00] ─────────► offset 8: [2A]
```

Each `Pointer` object keeps its address, declared remaining depth, follow status, and value. `Next` returns the next
pointer object when present.

A path consumes one level with each `.value`:

- `root.ptr.address` selects the first pointer storage;
- `root.ptr.value.address` selects the second pointer storage; and
- `root.ptr.value.value` selects the final `T`.

`MaxPointerDepth` limits followed levels. Cycle detection combines effective target position with remaining pointer
shape. `MaxPointerTargetBytes` limits one fixed target. If a target is a variable-size terminated string, setting a
fixed-target limit rejects following it because its size is unknown before the scan.

## Struct, union, and array targets

A pointer to a struct uses the same declaration-order traversal as a direct struct. A pointer to a union reads all
bounded overlapping views into `UnionValue`. CStructSharp does not automatically follow pointer members in every
unselected union view; choose an explicit path when that traversal is intended.

Pointer arrays use the configured pointer-width stride. Pointers to supported character shapes may produce the
corresponding terminated string. Every intermediate pointer and target read counts toward the same total-read limit.

## Limits and failure categories

Read-like operations use:

- `MaxPointerDepth` (default 64);
- `MaxPointerTargetBytes` for one fixed target;
- `MaxArrayElements`;
- `MaxStringBytes`;
- `MaxTotalBytesRead`; and
- `MaxNestingDepth`.

Malformed, cyclic, or out-of-range targets produce `ReadFailed`; configured ceilings produce `ReadLimitExceeded`.
Relative address arithmetic during path resolution may produce `InvalidPath`. Writer coordinate/range/shape problems
produce `WriteFailed`.

Common mistakes are using process pointer width, treating a coordinate as native memory, adding the origin to null,
expecting serialization to relocate targets, or consuming the wrong number of `.value` levels. See the
[pointer guide](../guides/pointers.md) for a runnable example and [Paths and selection](paths-and-selection.md) for
the path syntax.
