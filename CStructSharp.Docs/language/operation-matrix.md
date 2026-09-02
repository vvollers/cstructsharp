---
title: Language feature and operation table
description: Check how each Portable feature behaves during parse, debug, address, length, serialize, write, update, and selected reads.
---

# Language feature and operation table

Use this table when you know a language feature but need to confirm which operations support it.

- `V` means the behavior is verified without a special limitation.
- `L` means the operation is supported with the limitation explained below.
- `—` means the operation does not make sense for that feature, such as asking a scalar integer for a dynamic length.

The maintained [`feature-operation-matrix.json`](../contracts/quality/feature-operation-matrix.json) contains the
same rows plus exact test methods, round-trip conditions, and limitation text used by repository validators.

## Feature support

| Feature/manual | Parse | Debug | Address | Length | Serialize | Write | Update | Read value | Executable pair |
| --- | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | --- |
| [Fixed primitives](primitive-types.md#fixed-primitives) | V | V | V | — | V | V | V | V | `fixed-primitives` |
| [Character buffers](arrays-and-strings.md#fixed-character-buffers) | V | V | V | V | V | V | V | V | `character-buffers` |
| [Terminated strings](arrays-and-strings.md#terminated-strings) | V | V | V | V | V | V | L | V | `terminated-strings` |
| [Enums](structs-unions-enums-typedefs.md#enums) | V | V | V | — | V | V | V | V | `enums` |
| [Portable bitfields](bitfields.md#portable-bitfields) | V | V | V | — | V | V | V | V | `portable-bitfields` |
| [Fixed arrays](arrays-and-strings.md#fixed-arrays) | V | V | V | V | V | V | V | V | `fixed-arrays` |
| [Runtime expression arrays](arrays-and-strings.md#runtime-expression-arrays) | V | V | V | V | V | V | V | V | `runtime-expression-arrays` |
| [Named nested structs](structs-unions-enums-typedefs.md#named-structs) | V | V | V | — | V | V | V | V | `nested-structs` |
| [Inline structs](structs-unions-enums-typedefs.md#inline-structs) | V | V | V | — | V | V | V | V | `inline-structs` |
| [Typedefs](structs-unions-enums-typedefs.md#typedefs) | V | V | V | — | V | V | V | V | `typedefs` |
| [Unions](structs-unions-enums-typedefs.md#unions) | V | V | V | — | V | V | V | V | `unions` |
| [Pointers](pointers-and-addressing.md#pointers) | L | V | V | — | L | L | V | L | `pointers` |
| [Multi-level pointers](pointers-and-addressing.md#multi-level-pointers) | V | L | V | — | V | V | V | V | `multi-pointers` |
| [Alignment/endian](layout-alignment-and-padding.md#alignment-and-endian) | V | V | V | — | V | V | V | V | `alignment-and-endian-overrides` |
| [Bounded failures](limits-and-diagnostics.md#bounded-failures) | V | V | V | V | V | V | V | V | `bounded-failures` |
| [Invalid layouts](limits-and-diagnostics.md#invalid-layouts) | V | V | V | V | V | V | V | V | `invalid-layouts` |

Each pair in
[`manual-fixtures-v1.json`](../contracts/language/manual-fixtures-v1.json) contains a valid layout with exact bytes,
offsets, and values plus one invalid case and its stable error category. Tests execute the pairs on .NET 8 and
.NET 10.

## Operation meanings

| Column | What the operation does |
| --- | --- |
| Parse | Reads a dynamic root or selected composite from a stream/span/memory |
| Debug | Performs a read and records byte ranges for visited values |
| Address | Finds a path's absolute stream position and restores caller position |
| Length | Counts a fixed/runtime array or terminated string |
| Serialize | Creates an array or writes to a span / `IBufferWriter<byte>` |
| Write | Encodes a root/selected value at the current stream position |
| Update | Finds and replaces storage after staged validation |
| Read value | Returns one direct value or checked typed mapping |

Supported does not mean equally appropriate. Memory input avoids a stream adapter when bytes are already available.
Debugging adds diagnostic work. Updating has validation-before-commit behavior that a direct stream write does not.
Use [Choose an API](../guides/choosing-an-api.md) for those tradeoffs.

## Limited rows

A terminated-string update cannot grow beyond the existing storage plan or move later fields.

Pointer parsing, selected reads, and writes require explicit coordinate and following rules. Serialization writes
addresses; it does not relocate target objects. Multi-level-pointer debugging records pointer storage but does not
add a separate final primitive target range on every route.
