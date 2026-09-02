---
title: Differences from C
description: Translate a C-looking binary format without assuming unsupported headers, preprocessing, declarations, or ABI rules.
---

# Differences from C

Portable borrows familiar declaration syntax, but a layout is not an ISO C translation unit. CStructSharp does not
run a preprocessor, import headers, or ask a compiler how to place native objects.

The forms below are representative rejected fixtures stored in
[`portable-v1.json`](../contracts/language/portable-v1.json). Tests construct each layout on .NET 8 and .NET 10 and
require `CStructLayoutException` with code `InvalidLayout`.

## Unsupported C forms

| Fixture id | Representative form | Why it is not accepted | Portable approach |
| --- | --- | --- | --- |
| `include-directive` | `#include <stdint.h>` | The core does not search/read translation-unit files | Supply one complete normalized layout string |
| `packing-pragma` | `#pragma pack(push, 1)` | There is no compiler pack stack | Set packed/aligned placement in the constructor |
| `tagged-field-reference` | `struct child value;` | Portable has no separate C tag namespace | Declare `child`, then use `child value;` |
| `forward-declaration` | `struct child;` | Incomplete type identity/storage is unavailable | Supply the complete named declaration |
| `anonymous-member` | Unnamed inline aggregate | Members are not implicitly promoted | Give the inline struct a field name |
| `inline-union` | `union { ... } value;` inside a struct | Only named top-level unions are accepted | Declare `union choice`, then use `choice value;` |
| `multiple-declarators` | `uint8 first, second;` | One field declaration has one name/shape | Write two field declarations |
| `multidimensional-array` | `value[2][3]` | Public paths have one explicit dimension | Use a named row struct or flatten explicitly |
| `general-flexible-array` | `uint16 values[]` | Remaining stream bytes do not define a safe count | Use bounded `values[COUNT]`; empty `[]` is for character strings |
| `qualified-field` | `const uint8 value;` | Qualifier/storage behavior is not silently discarded | Remove layout-neutral qualifiers before construction |
| `multi-token-integer` | `unsigned long value;` | Native data-model widths are not inferred | Use exact `uint32`, `uint64`, and so on |
| `floating-point-field` | `double value;` | Endian/NaN/value rules are not defined | Model reviewed raw integer bits or add a fully specified feature |
| `integer-suffix` | `#define COUNT 1U` | C suffixes/promotions are not implemented | Use an unsuffixed bounded integer |
| `function-pointer` | `uint8 (*callback)(uint8)` | Data-pointer grammar cannot describe/invoke functions | Use fixed unsigned storage only when an opaque address is appropriate |
| `zero-width-bitfield` | `uint8 reserved : 0;` | Native separator/allocation rules vary | Start an explicit new field/storage unit |
| `typedef-array` | `typedef uint8 bytes[4];` | Typedef aliases a name and optional pointer depth, not a declarator | Put `[4]` on the field |
| `typedef-union` | Common C typedef-union form | The supported typedef-struct form is intentionally narrower | Use a named Portable union directly |

Broader unsupported families include booleans/other floating types, full preprocessing, qualifiers, anonymous member
promotion, source packing controls, and named compiler modes. One fixture may represent several equivalent spellings.

## No host ABI inference

ABI means application binary interface: the compiler/target rules for native widths, alignment, calling convention,
and related details. Portable uses explicit binary-format rules instead:

| Native concept | Portable behavior |
| --- | --- |
| C `long` / `unsigned long` width | `long` / `ulong` are always 64-bit |
| Plain `char` signedness | `char` is one unsigned raw code unit |
| `wchar_t` width/locale | `wchar` is one 16-bit UTF-16 code unit |
| Pointer width | Constructor value 1, 2, 4, or 8 |
| Enum backing | Supported explicit integral type; omitted means unsigned byte |
| Struct/union padding | Constructor chooses packed or Portable aligned placement |
| Bitfield allocation | Low-bit-first Portable storage-unit rule |
| Native byte order | Constructor order plus optional field suffix |

The core does not inspect OS, CPU, process bitness, current culture, installed compiler, system headers, target
triple, or native data model. Compiler-comparison files show selected observations only; they do not add MSVC, SysV,
GCC, Clang, LLP64, or LP64 modes.

## The limited #define form

`#define NAME expression` binds an integer expression. It is not textual macro expansion. There are no parameters,
include guards, conditionals, token pasting, stringification, `defined`, header search, compiler built-ins, casts,
`sizeof`, or target macros.

One operation may supply an integer variable that overrides a matching definition. See
[Expressions, defines, and runtime variables](expressions-defines-and-variables.md).

## Other invalid layouts

Portable also rejects duplicate names, unknown types, circular aliases/definitions, recursive by-value composites,
invalid enum backing/ranges, bit widths outside storage, negative/overflowing array counts, and layout dependencies
that cannot produce finite storage.

Recursive structures are possible through pointers to real named declarations and remain subject to traversal
limits. Use the [grammar](grammar.md), [primitive table](primitive-types.md), and
[feature table](operation-matrix.md) for accepted forms.
