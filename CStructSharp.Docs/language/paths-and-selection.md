---
title: Paths, array indices, and pointer access
description: Select a root, nested field, array item, union view, or pointer level with the public path syntax.
---

# Paths, array indices, and pointer access

A path tells CStructSharp which part of a prepared layout an operation should use. It is not a C expression or
JSONPath. It starts with an exported root name and follows fields with dots:

```text
root
root.header.kind
root.items[2].value
root.choice.large
root.pointer.address
root.pointer.value.code
```

Paths and layout names are case-sensitive.

## Segments and indices

Each dot-separated part is a *segment*. A segment may contain zero or more array indices, one per repeated `[...]`
in the field's own declaration (`root.matrix[2][3]`, not comma-separated). Each index is zero or an unpadded
positive decimal integer that fits `Int32`.

| Path | Result |
| --- | --- |
| `root.items[0]` | Valid first element |
| `root.items[12].value` | Valid nested field |
| `root.items[01]` | `InvalidPath`; leading zero |
| `root.items[-1]` | `InvalidPath`; signs are not allowed |
| `root.items[0x1]` | `InvalidPath`; decimal only |
| `root.items[1][2]` | `InvalidPath`; `items` has only one dimension |
| `root..value` | `InvalidPath`; empty segment |

The selected field must actually be an array, and each index must be below its own dimension's evaluated count.
Runtime arrays need the same variable values as parsing/writing.

Fixed `char[N]` and `wchar[N]` elements can be selected as raw code units. Terminated strings are selected as whole
fields and do not expose character indices.

The exact syntax appears in [Public path EBNF](grammar.md#public-path-ebnf).

## Multidimensional arrays

A field declared with more than one dimension (`uint8 matrix[3][4]`, LANG-05) accepts up to that many indices in one
segment, outermost first, matching the declaration's own bracket order:

```text
root.matrix          ──► every row, as a nested list of rows of columns
root.matrix[2]        ──► the third row alone, as a list of columns
root.matrix[2][3]      ──► one scalar/struct element
```

Supplying fewer indices than the field has dimensions selects the corresponding lower-dimensional sub-array rather
than one element - `root.matrix[2]` is exactly the third row, not an error, and behaves like an ordinary
one-dimensional array field from that point on (it can itself be indexed further, has its own
`GetDynamicArrayLength`, and so on). Traversing into a nested field or pointer accessor still requires every
dimension to be indexed first (`root.grid[1].member` is rejected the same way `root.grid.member` is for a plain
array with no index at all; `root.grid[1][2].member` is fine). Supplying more indices than the field has dimensions is
rejected. A fixed table of fixed-width strings (`char names[10][32]`) selects one row at a time as a whole string,
exactly like a one-dimensional `char[32]` field, with only the outer dimension nesting.

See [Arrays and strings](arrays-and-strings.md#multidimensional-arrays) for the declaration syntax and its one
restriction: only the outermost dimension may ever be a runtime expression, and this slice does not yet support
even that for two or more dimensions.

## Structs and unions

A struct segment follows normal sequential placement. A union member begins at the union address, so every member's
first byte position is the same. If that member is a struct, its own child fields then advance within the selected
view.

Selecting a whole struct/union and selecting one scalar are different result shapes. Use `Parse`/`ParseStream` for a
composite or `ReadValue` for any single direct value.

## Pointer accessors

After a pointer field:

- `.address` selects the pointer storage and reads/writes its encoded coordinate;
- `.value` follows one declared level and selects the target.

```text
root.ptr.address       ──► bytes containing the first stored pointer
root.ptr.value         ──► first target (or next Pointer for T **)
root.ptr.value.value   ──► final target for T **
```

The words `address` and `value` are special only immediately after a pointer. An ordinary non-pointer field may use
either name normally.

## Operations that accept paths

| Operation | What a path selects |
| --- | --- |
| `Parse` / `ParseStream` | A root or selected composite |
| `ReadValue` / `ReadValue<T>` | A root, nested object, scalar, or array item |
| `ParseStreamWithDebug` | A value plus ranges visited while reading it |
| `ResolveAddress` | The absolute stream position of the selected storage/target |
| `GetDynamicArrayLength` | A fixed/runtime array or terminated string |
| `Serialize` / `WriteStream` | The value shape to encode |
| `UpdateStream` | Existing storage to locate and replace |

The [feature table](operation-matrix.md#feature-support) gives exact support and limitations for each language
feature.

An invalid selector produces `CStructPathException` / `InvalidPath`. A valid path that encounters truncated data,
bad text, or an invalid pointer while locating the target produces a read error instead. Traversal may also reach a
configured read limit.

## Positions and origins

For stream operations, the stream's position at entry is the root's starting point where documented. Address results
are absolute stream positions and therefore include a nonzero starting position.

Read-only address/length inspection and update discovery restore the caller's visible position. A successful normal
read advances through the selected value.

Span and memory coordinates start at zero within the region passed to the method. If the region is a slice, paths and
pointers cannot see bytes before or after that slice.

Common mistakes are omitting the root, using a leading-zero index, indexing terminated text, confusing a pointer's
stored number with its storage position, or using one `.value` for a multi-level pointer.
