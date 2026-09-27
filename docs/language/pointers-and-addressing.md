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

## When pointer targets are read

A struct is read in two passes. The first pass reads every field in declaration order; a pointer field contributes
only its stored coordinate. The second pass follows the struct's pointers, in declaration order, once its last field
is read. Pointers inside a nested struct are followed when that nested struct is complete, and pointers of an
anonymous promoted struct are followed with the struct it is promoted into.

Two things follow from this rule:

- A pointer target can depend on any field of the same struct, including one declared after the pointer. This is
  what makes [counted pointer targets](#counted-pointer-targets) work.
- When the input has several problems, the first one reported is the first problem in the struct's own fields. A
  truncated field after a pointer is reported before a pointer whose target lies outside the input.

For:

```c
struct box { uint8 v; };
struct rec {
    box *p;
    uint8 tail;
};
```

with a one-byte pointer and input `02 07 09`, `ParseWithDebug` lists `rec.p` (offset 0), then `rec.tail` (offset 1),
then the target's `rec.p.v` (offset 2). A failure while following a pointer still names the pointer field and
reports the offset just after its coordinate, as it would if the pointer were followed in place. Pointers in a union
view are never followed, so this rule does not apply to them.

## Counted pointer targets

In C, a pointer does not know how many values it points at. Interfaces therefore store the count in another field.
The PKCS#11 interface for cryptographic tokens, for example, pairs `CK_BYTE_PTR pIv` with `CK_ULONG ulIvLen`. Recent
GCC and Clang releases can record such a pairing with the
[`counted_by` attribute](https://gcc.gnu.org/onlinedocs/gcc/Common-Variable-Attributes.html). CStructSharp uses a
`@count(N)` suffix on the pointer declarator:

```c
struct params {
    uint8 *iv @count(iv_len);
    uint8 iv_len;
    uint8 data[3];
};
```

`N` is an ordinary expression, evaluated like a [runtime array length](arrays-and-strings.md#runtime-expression-arrays)
once the struct's fields are read. It may name any field of the same struct, including one declared after the
pointer, as well as definitions and caller variables. The target is `N` consecutive values of the pointed-to type,
starting at the pointer's target coordinate.

With a one-byte pointer and input `02 03 A1 A2 A3`:

```text
offset   0          1        2    3    4
bytes   02         03       A1   A2   A3
field   iv         iv_len   data[0..2]
value   target 2   3        the iv target: A1 A2 A3
```

`iv` stores coordinate 2 and `iv_len` is 3, so `iv.Value` holds the three bytes at offsets 2, 3, and 4. The target
overlaps `data` in this example; a real format usually stores it elsewhere.

The target's value depends on its element type:

| Element type | `Pointer.Value` |
| --- | --- |
| `char`, `wchar` | One string of `N` characters, decoded as a character buffer of that length |
| Fixed-width number | An array of `N` numbers |
| Struct, union, enum, or other type | A list of `N` values, read one after another |

A null pointer has no target, whatever its count. A negative count fails the read, and the count is checked against
`MaxArrayElements`. `MaxPointerTargetBytes` applies to the whole target: `N` times the element size, so a target whose
element has no fixed size is refused while that limit is set. The count applies to the final level of a multi-level
pointer. It is rejected at construction on a field that is not a pointer, on an array of pointers, and on a
`void *`, which has no element type.

Writing works as for every pointer: serialization, writes, and updates store the coordinate and never place or resize
the target. A path cannot select a counted target (`params.iv.value`), because its count may name a field that path
resolution does not read; read the containing struct instead. `params.iv.address` still selects the stored
coordinate. The [memory API](../guides/memory-schemas.md) projects one value per pointer and refuses a counted
pointer.

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

`MaxPointerDepth` limits followed levels. A selected read counts both the `.value` levels in its path and any
additional pointers it follows while decoding the selected value. Selecting a pointer field or array element does
not restart that depth count. Cycle detection combines effective target position with remaining pointer
shape. `MaxPointerTargetBytes` limits one fixed target. If a target is a variable-size terminated string, setting a
fixed-target limit rejects following it because its size is unknown before the scan.

## Struct, union, and array targets

A pointer to a struct uses the same declaration-order traversal as a direct struct. A pointer to a union reads all
bounded overlapping views into `UnionValue`. CStructSharp does not automatically follow pointer members in every
unselected union view; choose an explicit path when that traversal is intended.

Pointer arrays use the configured pointer-width stride; their elements are followed in index order after the
containing struct's fields. Pointers to supported character shapes may produce the corresponding terminated string,
and a [counted target](#counted-pointer-targets) is an array of the pointed-to type. Every intermediate pointer and
target read counts toward the same total-read limit.

## Limits and failure categories

Read-like operations use:

- `MaxPointerDepth` (default 64);
- `MaxPointerTargetBytes` for one fixed target, or for all elements of a counted target;
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
