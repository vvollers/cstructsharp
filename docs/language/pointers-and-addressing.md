---
title: Pointers and addressing
description: Follow stored pointers with explicit widths, origins, path levels, and limits, and tell storage from targets.
---

# Pointers and addressing

A pointer in a binary file or message is a number that refers to another byte position. It is not a C# reference and
must never be treated as a process memory address.

Three positions are easy to confuse:

1. The *pointer field position* is where the encoded pointer bytes are stored.
2. The *stored address* is the unsigned number decoded from those bytes.
3. The *effective target position* is where CStructSharp reads the pointed-to value. In relative mode, this is the
   configured origin plus the stored address.

The layout and the read options tell CStructSharp how to connect them.

A native C pointer normally addresses a live object in a process. Saving its numeric representation does not save
its target or make the address valid after a restart. The background chapter on
[memory addresses and stored data](../guides/memory-and-stored-data.md) explains virtual memory, file-relative
offsets, and why the pointer width belongs to the format rather than the current operating system.

## Pointers

`T *field;` stores an unsigned coordinate that may refer to another value in the same binary source. The coordinate
is data, not a native memory address.

The `CStruct` constructor sets pointer storage to 1, 2, 4, or 8 bytes for the entire layout. Choose the width from the
binary format; do not copy the bitness of the .NET process. A physical .NET stream position must fit a non-negative
`Int64`, so an encoded eight-byte value above `long.MaxValue` is rejected even when pointer following is disabled.

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

### Follow a simple pointer

This layout stores a one-byte pointer to a one-byte value:

```c
struct root {
    uint8 *target;
};
```

The executable example sets `pointerSize: 1` and reads bytes `01 2A`:

[!code-csharp[Follow a bounded one-byte pointer](../examples/Program.cs#api-reference-pointer-read-options)]

```text
offset       0            1
bytes       01           2A
field       target       pointed-to uint8
stored      address 1 ───────► value 0x2A
```

The returned `Pointer` (from `CStructSharp.Values`) reports `Address = 1`, `IsDereferenced = true`, and
`Value = 0x2A`. The stream contains no object allocation or relocation information; it contains only the
coordinate `1`.

## Absolute and relative modes

`ReadOptions.AddressingMode` controls how a nonzero stored coordinate becomes a target:

| Mode | Meaning of stored value | Effective target |
| --- | --- | --- |
| `Absolute` | Stream position | Stored value |
| `Relative` | Offset from a base | Checked `Origin + stored value` |

Use relative mode only when the format specification says offsets are measured from a known base, such as the
beginning of a record. Set the base with `ReadOptions.Origin`.

<svg class="byte-grid" role="img" viewBox="0 0 668 162" width="668" height="162" xmlns="http://www.w3.org/2000/svg" font-size="12">
  <title>A stored address of 4 targets offset 4 in Absolute mode and offset 12 in Relative mode with Origin 8</title>
  <text x="87" y="18" text-anchor="middle" fill="currentColor" opacity="0.7" font-size="11">0</text>
  <text x="223" y="18" text-anchor="middle" fill="currentColor" opacity="0.7" font-size="11">4</text>
  <text x="359" y="18" text-anchor="middle" fill="currentColor" opacity="0.7" font-size="11">8</text>
  <text x="495" y="18" text-anchor="middle" fill="currentColor" opacity="0.7" font-size="11">12</text>
  <text x="6" y="43" fill="currentColor">bytes</text>
  <rect x="70" y="24" width="34" height="30" fill="none" stroke="currentColor" stroke-opacity="0.35"/>
  <rect x="104" y="24" width="34" height="30" fill="none" stroke="currentColor" stroke-opacity="0.35"/>
  <rect x="138" y="24" width="34" height="30" fill="none" stroke="currentColor" stroke-opacity="0.35"/>
  <rect x="172" y="24" width="34" height="30" fill="none" stroke="currentColor" stroke-opacity="0.35"/>
  <rect x="206" y="24" width="34" height="30" fill="none" stroke="currentColor" stroke-opacity="0.35"/>
  <rect x="240" y="24" width="34" height="30" fill="none" stroke="currentColor" stroke-opacity="0.35"/>
  <rect x="274" y="24" width="34" height="30" fill="none" stroke="currentColor" stroke-opacity="0.35"/>
  <rect x="308" y="24" width="34" height="30" fill="none" stroke="currentColor" stroke-opacity="0.35"/>
  <rect x="342" y="24" width="34" height="30" fill="none" stroke="currentColor" stroke-opacity="0.35"/>
  <rect x="376" y="24" width="34" height="30" fill="none" stroke="currentColor" stroke-opacity="0.35"/>
  <rect x="410" y="24" width="34" height="30" fill="none" stroke="currentColor" stroke-opacity="0.35"/>
  <rect x="444" y="24" width="34" height="30" fill="none" stroke="currentColor" stroke-opacity="0.35"/>
  <rect x="478" y="24" width="34" height="30" fill="none" stroke="currentColor" stroke-opacity="0.35"/>
  <rect x="512" y="24" width="34" height="30" fill="none" stroke="currentColor" stroke-opacity="0.35"/>
  <text x="104" y="43" text-anchor="middle" fill="currentColor">04 00</text>
  <text x="223" y="43" text-anchor="middle" fill="currentColor">2A</text>
  <text x="495" y="43" text-anchor="middle" fill="currentColor">2A</text>
  <text x="6" y="81" fill="currentColor">Absolute</text>
  <rect x="138" y="62" width="34" height="30" fill="none" stroke="currentColor" stroke-opacity="0.35"/>
  <rect x="172" y="62" width="34" height="30" fill="none" stroke="currentColor" stroke-opacity="0.35"/>
  <rect x="240" y="62" width="34" height="30" fill="none" stroke="currentColor" stroke-opacity="0.35"/>
  <rect x="274" y="62" width="34" height="30" fill="none" stroke="currentColor" stroke-opacity="0.35"/>
  <rect x="308" y="62" width="34" height="30" fill="none" stroke="currentColor" stroke-opacity="0.35"/>
  <rect x="342" y="62" width="34" height="30" fill="none" stroke="currentColor" stroke-opacity="0.35"/>
  <rect x="376" y="62" width="34" height="30" fill="none" stroke="currentColor" stroke-opacity="0.35"/>
  <rect x="410" y="62" width="34" height="30" fill="none" stroke="currentColor" stroke-opacity="0.35"/>
  <rect x="444" y="62" width="34" height="30" fill="none" stroke="currentColor" stroke-opacity="0.35"/>
  <rect x="478" y="62" width="34" height="30" fill="none" stroke="currentColor" stroke-opacity="0.35"/>
  <rect x="512" y="62" width="34" height="30" fill="none" stroke="currentColor" stroke-opacity="0.35"/>
  <rect x="71" y="63" width="66" height="28" fill="none" stroke="var(--cstruct-accent, #2563eb)" stroke-width="1.5"/>
  <text x="104" y="81" text-anchor="middle" fill="currentColor">ptr</text>
  <rect x="207" y="63" width="32" height="28" fill="var(--cstruct-accent-soft, #dbeafe)" stroke="var(--cstruct-accent, #2563eb)" stroke-width="1.5"/>
  <text x="223" y="81" text-anchor="middle" fill="currentColor">*</text>
  <text x="6" y="119" fill="currentColor">Relative</text>
  <rect x="138" y="100" width="34" height="30" fill="none" stroke="currentColor" stroke-opacity="0.35"/>
  <rect x="172" y="100" width="34" height="30" fill="none" stroke="currentColor" stroke-opacity="0.35"/>
  <rect x="206" y="100" width="34" height="30" fill="none" stroke="currentColor" stroke-opacity="0.35"/>
  <rect x="240" y="100" width="34" height="30" fill="none" stroke="currentColor" stroke-opacity="0.35"/>
  <rect x="274" y="100" width="34" height="30" fill="none" stroke="currentColor" stroke-opacity="0.35"/>
  <rect x="308" y="100" width="34" height="30" fill="none" stroke="currentColor" stroke-opacity="0.35"/>
  <rect x="376" y="100" width="34" height="30" fill="none" stroke="currentColor" stroke-opacity="0.35"/>
  <rect x="410" y="100" width="34" height="30" fill="none" stroke="currentColor" stroke-opacity="0.35"/>
  <rect x="444" y="100" width="34" height="30" fill="none" stroke="currentColor" stroke-opacity="0.35"/>
  <rect x="512" y="100" width="34" height="30" fill="none" stroke="currentColor" stroke-opacity="0.35"/>
  <rect x="71" y="101" width="66" height="28" fill="none" stroke="var(--cstruct-accent, #2563eb)" stroke-width="1.5"/>
  <text x="104" y="119" text-anchor="middle" fill="currentColor">ptr</text>
  <rect x="343" y="101" width="32" height="28" fill="currentColor" fill-opacity="0.08" stroke="currentColor" stroke-opacity="0.35" stroke-dasharray="3 2"/>
  <text x="359" y="119" text-anchor="middle" fill="currentColor">O</text>
  <rect x="479" y="101" width="32" height="28" fill="var(--cstruct-accent-soft, #dbeafe)" stroke="var(--cstruct-accent, #2563eb)" stroke-width="1.5"/>
  <text x="495" y="119" text-anchor="middle" fill="currentColor">*</text>
  <text x="6" y="150" fill="currentColor" opacity="0.7" font-size="11">Absolute: target = address. Relative: target = Origin (O, here 8) + address. Address 0 stays null.</text>
</svg>

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
pointer distinct from null, and lets an inspection tool display an address without reading untrusted target data.

Serialization writes a coordinate. It does not move, allocate, or relocate target objects, so the application must
already know the correct coordinate. A scalar pointer or selected pointer array item may receive `null`, which
encodes zero. Null for a pointer collection or non-pointer value is a shape error.

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
then the target's `rec.p.value.v` (offset 2). A failure while following a pointer still names the pointer field and
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

[!code-csharp[Read a pointer counted by a later field](../examples/Program.cs#api-reference-counted-pointer)]

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
not restart that depth count, and neither does reading a pointer field of a struct that a path reached through
another pointer. Cycle detection combines effective target position with remaining pointer shape.
`MaxPointerTargetBytes` limits one fixed target. If a target is a variable-size terminated string, setting a
fixed-target limit rejects following it because its size is unknown before the scan.

## Struct, union, and array targets

A pointer to a struct reads the target in declaration order, exactly as a direct struct. A pointer to a union reads
all bounded overlapping views into `UnionValue`. CStructSharp does not automatically follow pointer members in every
unselected union view; choose an explicit path when following one is intended.

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

These limits protect against cycles and against addresses designed to make the reader visit excessive data.
Malformed, cyclic, or out-of-range targets produce `ReadFailed`; configured ceilings produce `ReadLimitExceeded`.
Relative address arithmetic during path resolution may produce `InvalidPath`. Writer coordinate/range/shape problems
produce `WriteFailed`.

When following fails, inspect:

1. pointer width and byte order;
2. absolute versus relative mode and the origin;
3. the stored address and effective target position;
4. whether the stream or supplied memory contains the target;
5. the number of `.value` levels; and
6. pointer depth, target-size, and total-read limits.

Common mistakes are using process pointer width, treating a coordinate as native memory, adding the origin to null,
expecting serialization to relocate targets, or consuming the wrong number of `.value` levels. See
[Paths and selection](paths-and-selection.md) for the path syntax.

For unsigned virtual addresses and mapped images, use the memory APIs described in
[Analyze mapped memory](../guides/memory-analysis.md). Their `StoredPointer` values keep the stored unsigned
`Address` (all 64 bits), and their `.value` paths follow targets explicitly in a caller-selected address space.
