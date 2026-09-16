---
title: Differences from C
description: Translate a C-looking binary format without assuming unsupported headers, preprocessing, declarations, or ABI rules.
---

# Differences from C

Portable borrows familiar declaration syntax, but a layout is not an ISO C translation unit. CStructSharp does not
run a preprocessor, import headers, or ask a compiler how to place native objects.

The forms below are representative rejected fixtures stored in
[`portable-v1.json`](../../contracts/language/portable-v1.json). Tests construct each layout on .NET 8 and .NET 10 and
require `CStructLayoutException` with code `InvalidLayout`.

## Unsupported C forms

For a small translation, a C header might describe `struct packet { unsigned short kind; unsigned int length; };`.
Before translating, confirm the format specification says those fields are 16 and 32 bits and stored without padding.
Then use:

```c
struct packet {
    uint16 kind;
    uint32 length;
};
```

With packed placement, kind occupies offsets 0–1 and length occupies offsets 2–5. With Portable aligned placement,
length starts at offset 4 and the total size is 8. Choose the rule stated by the format, not by the host computer.
Since the header vocabulary (`unsigned short`, `DWORD`, `__u32`, ...) is built in, the translation is usually the
original text with its `#include` lines left in place (they are recorded, not resolved) and its `#pragma pack`
lines honored as composite alignment clamps. This example establishes one format's widths; it does not prove
equivalence with every native compiler.

| Fixture id | Representative form | Why it is not accepted | Portable approach |
| --- | --- | --- | --- |
| `include-without-delimiters` | `#include stdint.h` | An include path must be quoted or angle-bracketed to be recorded | Write `#include <stdint.h>`; the path is recorded on `CStruct.Includes` and never read |
| `unknown-preprocessor-directive` | `#error stop` | Only `#define`, `#undef`, `#include`, `#pragma`, `#ifdef`, `#ifndef`, `#else`, and `#endif` are recognized | Remove the line |
| `unterminated-conditional` | `#ifdef X` with no `#endif` | Every conditional must be closed in the same source | Add the matching `#endif` |
| `text-constant-in-expression` | `#define MAGIC "CD001"` then `value[MAGIC]` | A text, byte, bare, or macro constant is published on `CStruct.Constants` but has no integer value | Use an integer `#define` in expressions |
| `tag-kind-mismatch` | `struct root { union child value; };` where `child` is a `struct` | A tag keyword is checked against the referenced declaration's actual kind | Use the matching keyword, or omit it and write `child value;` |
| `tag-alias-kind-mismatch` | `typedef union tag alias;` where `tag` is a `struct` | The same kind check applies to a tag alias | Use the matching keyword |
| `duplicate-typedef-tag` | `typedef struct shared {...} a; typedef struct shared {...} b;` | A typedef's tag is a global type, as in C, and cannot be declared twice | Give each body its own tag, or use the anonymous form |
| `pointer-to-typedef-array` | `typedef uint16 pair[2]; pair *value;` | The language has no pointer-to-array storage | Point at a struct that wraps the array |
| `unsized-typedef-array` | `typedef uint16 open[];` | An array typedef needs a count in every dimension | Give the alias a count, or declare the unsized array on a `char`/`wchar` field |
| `inline-union` | `union { ... } value;` inside a struct | Only named top-level unions are accepted | Declare `union choice`, then use `choice value;` |
| `runtime-sized-multidimensional-array` | `uint8 values[count][3];` | Only the outermost dimension of a multidimensional array may be a runtime expression; a fixed `value[2][3]` is supported (see [Arrays and strings](arrays-and-strings.md#multidimensional-arrays)) | Make every dimension a compile-time-fixed count, or flatten the runtime-sized dimension into a single-dimension array |
| `data-sized-array-of-dynamic-element` | `entry entries[]` where `entry` is runtime-sized | A data-sized array (`[]`, `[EOF]`) needs one fixed element size to step by | Give the element a fixed size, or count the elements with an earlier field |
| `sizeof-of-dynamic-type` | `sizeof(entry)` where `entry` is runtime-sized | `sizeof` folds at construction, so the type must be complete and fixed-size | Name a fixed type, or compute the size from earlier fields |
| `unsupported-expression-call` | `strlen(root)` | Only `sizeof(type)` and `offsetof(type, field)` are accepted calls; nothing ever invokes user code | Use an integer expression |
| `anonymous-flag-expression-member` | `flag { A = N, B };` | An anonymous flag's omitted member needs the previous values as literals to find the next bit | Give every member of an anonymous flag a literal value, or name the flag |
| `qualifier-not-in-closed-set` | `_Atomic uint8 value;` | Only `const`/`volatile`/`restrict` are recognized and discarded | Remove the unrecognized qualifier before construction |
| `trailing-qualifier-position` | `uint8 value const;` | Accepted qualifiers appear before the type or after a pointer star, not after the declarator name | Move the qualifier to an accepted position |
| `void-value-field` | `void value;` | `void` has no storage; only `void *` (an opaque address) is a field | Declare a pointer, or a real element type |
| `unrecognized-integer-spelling` | `intmax_t value;` | The accepted alias table is curated (see [alias spellings](primitive-types.md#alias-spellings)); `intmax_t` has no single portable width | Use a documented spelling, or add `typedef int64 intmax_t;` |
| `floating-point-field` | `long double value;` | No single portable width exists to standardize on (80-bit extended, 128-bit quad, or 64-bit, depending on compiler/target) | Use `float32`/`float64` (or the `float`/`double` aliases) when 64 bits of precision is enough |
| `zero-width-bitfield` | `uint8 reserved : 0;` | Native separator/allocation rules vary | Start an explicit new field/storage unit |
| `non-power-of-two-alignment` | `uint8 value @align(3);` | An explicit alignment override must be a positive power of two, matching every native ABI's own alignment rule | Use a power-of-two value, e.g. `@align(4)` |
| `non-power-of-two-composite-alignment` | `struct root @align(3) { uint8 value; };` | A composite's own explicit alignment override must also be a positive power of two | Use a power-of-two value, e.g. `@align(4)` |
| `non-power-of-two-pack-pragma` | `#pragma pack(3)` | A pack value is a composite alignment override and follows the same power-of-two rule | Use `#pragma pack(1)`, `(2)`, `(4)`, ... |
| `offset-assertion-mismatch` | `struct root { uint8 a; uint8 value @5; };` where `value` naturally lands at offset 1 | An offset assertion is checked against the field's actual computed offset | Correct the asserted value, or omit it if the field's placement is expected to vary |
| `offset-assertion-on-bitfield` | `uint8 flag : 1 @2;` | An offset assertion is not supported on a bitfield declarator | Assert the offset of a non-bitfield sibling, or omit the assertion |

Broader unsupported families include other floating types, textual macro expansion, and named compiler modes. One
fixture may represent several equivalent spellings. Forms that were rejected in earlier releases and are now
accepted: `#include` (recorded), `#pragma pack` (a composite alignment clamp), forward declarations (`struct node;`),
function pointers (an opaque address, like `void *`), `T values[]` on a non-character type (zero-terminated) and
`T values[EOF]` (read to the end),
`typedef T name[N];`, `typedef struct tag alias;`, typedef declarator lists (`typedef struct _X {...} X, *PX;`),
`typedef struct NAME {...};` with no alias, top-level `struct { ... } name;` and `struct X { ... } variable;`, and
the Windows/kernel/IDA/C99 alias spellings.

## No host ABI inference

ABI means application binary interface: the compiler/target rules for native widths, alignment, calling convention,
and related details. Portable uses explicit binary-format rules instead:

| Native concept | Portable behavior |
| --- | --- |
| C `long` / `unsigned long` width | `long` / `ulong` are 64-bit unless the layout is compiled with `CStructCompilationOptions.CLongWidth = 32`; Windows `LONG`/`ULONG` are always 32-bit |
| `size_t` / `intptr_t` width | As wide as the layout's configured pointer size |
| Plain `char` signedness | `char` is one unsigned raw code unit |
| `_Bool`/C++ `bool` width and representation | `bool`/`_Bool` is always 1 byte, canonical `0x00`/`0x01` write output |
| `wchar_t` width/locale | `wchar` is one 16-bit UTF-16 code unit |
| Pointer width | Constructor value 1, 2, 4, or 8 |
| Enum backing | Supported explicit integral type; omitted means unsigned byte |
| Struct/union padding | Constructor chooses packed or Portable aligned placement |
| Bitfield allocation | Low-bit-first Portable storage-unit rule |
| Native byte order | Constructor order plus optional field suffix |

The core does not inspect OS, CPU, process bitness, current culture, installed compiler, system headers, target
triple, or native data model. Compiler-comparison files show selected observations only; they do not add MSVC, SysV,
GCC, Clang, LLP64, or LP64 modes.

## The limited preprocessor

`#define NAME expression` binds an integer expression. It is not textual macro expansion: a name is never
substituted into later source text, there are no parameters, `defined`, header search, compiler built-ins, casts,
`sizeof`, or target macros. `#define NAME "text"`, `#define NAME b"bytes"`, a bare `#define NAME`, and a
function-like `#define NAME(args) ...` are accepted so a pasted header keeps its magic strings and guards; they are
published on `CStruct.Constants` and take no part in layout expressions. `#ifdef`/`#ifndef`/`#else`/`#endif`
select declarations by the names defined so far (plus `CStructCompilationOptions.Defined`), `#undef` removes a
constant for the rest of the source, `#include` is recorded on `CStruct.Includes`, `#pragma pack(...)` clamps the
alignment of the composites that follow it, and any other `#pragma` is ignored. A backslash before a line end joins
the lines, as in C.

One operation may supply an integer variable that overrides a matching definition. See
[Expressions, defines, and runtime variables](expressions-defines-and-variables.md).

## Other invalid layouts

Portable also rejects duplicate names, unknown types, circular aliases/definitions, recursive by-value composites,
invalid enum backing/ranges, bit widths outside storage, negative/overflowing array counts, and layout dependencies
that cannot produce finite storage.

Recursive structures are possible through pointers to real named declarations and remain subject to traversal
limits. Use the [grammar](grammar.md), [primitive table](primitive-types.md), and
[feature table](operation-matrix.md) for accepted forms.
