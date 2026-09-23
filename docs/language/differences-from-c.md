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
| `negative-unused-typedef-array` | `typedef uint8 invalid[-1];` | Array counts must be nonnegative, even when an alias is unused | Use zero for an empty array or a positive count for stored elements |
| `dynamic-union-member` | `union choice { uint8 count; uint8 values[count]; };` | Every union member must have fixed storage, so the union's extent is known before any member is read | Read the count outside the union, or wrap the runtime-sized member in a struct that is parsed on its own |
| `runtime-sized-multidimensional-array` | `uint8 values[count][3];` | Every dimension of a multidimensional array must be a compile-time-fixed count; a fixed `value[2][3]` is supported (see [Arrays and strings](arrays-and-strings.md#multidimensional-arrays)) | Make every dimension a compile-time-fixed count, or flatten the runtime-sized dimension into a single-dimension array |
| `data-sized-array-of-dynamic-element` | `entry entries[]` where `entry` is runtime-sized | A data-sized array (`[]`, `[EOF]`) needs one fixed element size to step by | Give the element a fixed size, or count the elements with an earlier field |
| `sizeof-of-dynamic-type` | `sizeof(entry)` where `entry` is runtime-sized | `sizeof` folds at construction, so the type must be complete and fixed-size | Name a fixed type, or compute the size from earlier fields |
| `sizeof-of-multiplication` | `sizeof(h*o)` | The argument must be a type name, not a value expression | Name the intended storage type with `sizeof(type)`; place arithmetic outside the call |
| `unsupported-expression-call` | `strlen(root)` | Only `sizeof(type)` and `offsetof(type, field)` are accepted calls; nothing ever invokes user code | Use an integer expression |
| `anonymous-flag-expression-member` | `flag { A = N, B };` | An anonymous flag's omitted member needs the previous values as literals to find the next bit | Give every member of an anonymous flag a literal value, or name the flag |
| `qualifier-not-in-closed-set` | `_Atomic uint8 value;` | Only `const`/`volatile`/`restrict` are recognized and discarded | Remove the unrecognized qualifier before construction |
| `trailing-qualifier-position` | `uint8 value const;` | Accepted qualifiers appear before the type or after a pointer star, not after the declarator name | Move the qualifier to an accepted position |
| `void-value-field` | `void value;` | `void` has no storage; only `void *` (an opaque address) is a field | Declare a pointer, or a real element type |
| `unrecognized-integer-spelling` | `intmax_t value;` | The accepted alias table is curated (see [alias spellings](primitive-types.md#alias-spellings)); `intmax_t` has no single portable width | Use a documented spelling, or add `typedef int64 intmax_t;` |
| `floating-point-field` | `long double value;` | No single portable width exists to standardize on (80-bit extended, 128-bit quad, or 64-bit, depending on compiler/target) | Use `float32`/`float64` (or the `float`/`double` aliases) when 64 bits of precision is enough |
| `enum-value-outside-storage` | `enum kind : uint8 { BIG = 256 };` | A member must fit the declared (or defaulted) backing type; a compiler would widen the enum or reject it, dissect keeps the value and fails on write | Declare a wider backing type, or omit it for the 32-bit compiler default |
| `indexed-nested-reference` | `uint8 v[items[0].n];` | The head of a dotted reference is a scalar struct field; an array element's field is reached through its bare name after the element is read | Count with `n` after `items`, or copy the value into a field of the enclosing struct |
| `zero-width-bitfield` | `uint8 reserved : 0;` | A zero width has no storage, so it cannot carry a name; C rejects it too | Write the separator unnamed: `uint8 : 0;` |
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
| Enum backing | Supported explicit integral type; omitted means the configured 32-bit default (or an explicit DefaultEnumStorage option) |
| Struct/union padding | Constructor chooses packed or Portable aligned placement |
| Bitfield placement | `BitfieldPacking.SysV` (GCC/Clang rule, the default) or `Msvc`; low-bit-first numbering by default, high-bit-first by option |
| Native byte order | Constructor order plus optional field suffix |

The core does not inspect OS, CPU, process bitness, current culture, installed compiler, system headers, target
triple, or native data model. Compiler-comparison files show selected observations only; they do not add LLP64 or
LP64 modes or infer a compiler from the host.

### Portable versus real compilers

The table below is generated (`node tools/quality/compiler-fixture.mjs table`) from the observations recorded by
`tools/compiler-fixtures/portable-host-facts.c`. Each row is one C declaration compiled with the values in
`contracts/quality/compiler-fixtures/shapes.json`; each compiler column shows the object it produced; the *Portable*
column names the `BitfieldPacking` mode(s) in which the library, given the equivalent Portable declaration
(natural placement, or packed placement for the `#pragma pack(1)` rows), produces the same bytes. `long` rows use
`CLongWidth` matching the compiler's `long`. Signed bitfields match in bytes; the Portable value is the unsigned
slice.

<!-- compiler-fixture-table:start -->
| Shape | C declaration | Portable | GCC 15.2.0 (Linux x64, SysV ABI) |
| --- | --- | --- | --- |
| `bits-u8-u16` | `struct { uint8_t a:4; uint16_t b:4; }` | `SysV`, `Msvc` (modelled, no msvc baseline yet) | size 2, align 2: `AF00` |
| `bits-u8-u8-u16` | `struct { uint8_t a:3; uint8_t b:5; uint16_t c; }` | `SysV`, `Msvc` (modelled, no msvc baseline yet) | size 4, align 2: `FF00CDAB` |
| `bits-u32-3-29-1` | `struct { uint32_t a:3; uint32_t b:29; uint32_t c:1; }` | `SysV`, `Msvc` (modelled, no msvc baseline yet) | size 8, align 4: `FFFFFFFF01000000` |
| `bits-u16-15-u8-2` | `struct { uint16_t a:15; uint8_t b:2; }` | `SysV`, `Msvc` (modelled, no msvc baseline yet) | size 4, align 2: `FF7F0300` |
| `bits-zero-width` | `struct { uint8_t a:3; uint8_t :0; uint8_t b:3; }` | `SysV`, `Msvc` (modelled, no msvc baseline yet) | size 2, align 1: `0707` |
| `bits-zero-width-u32` | `struct { uint8_t a:3; uint32_t :0; uint8_t b:3; }` | `SysV`, `Msvc` (modelled, no msvc baseline yet) | size 5, align 1: `0700000007` |
| `bits-signed` | `struct { int8_t a:3; uint8_t b:5; }  /* a = -1 */` | `SysV`, `Msvc` (modelled, no msvc baseline yet) | size 1, align 1: `FF` |
| `bits-u8-6-6` | `struct { uint8_t a:6; uint8_t b:6; }` | `SysV`, `Msvc` (modelled, no msvc baseline yet) | size 2, align 1: `3F3F` |
| `bits-u64-u8` | `struct { uint64_t a:4; uint8_t b:4; }` | `SysV`, `Msvc` (modelled, no msvc baseline yet) | size 8, align 8: `FF00000000000000` |
| `bits-after-byte` | `struct { uint8_t x; uint32_t a:4; uint8_t b:4; }` | `SysV`, `Msvc` (modelled, no msvc baseline yet) | size 4, align 4: `AAFF0000` |
| `u64-after-u8` | `struct { uint8_t a; uint64_t b; }` | `SysV`, `Msvc` (modelled, no msvc baseline yet) | size 16, align 8: `11000000000000001122334455667788` |
| `double-after-u8` | `struct { uint8_t a; double b; }` | `SysV`, `Msvc` (modelled, no msvc baseline yet) | size 16, align 8: `1100000000000000000000000000F83F` |
| `long` | `struct { uint8_t a; long b; }` | `SysV`, `Msvc` (modelled, no msvc baseline yet) | size 16, align 8: `11000000000000007856341200000000` |
| `enum-large` | `struct { uint8_t a; enum { BIG = 0x7FFFFFFF } b; }` | `SysV`, `Msvc` (modelled, no msvc baseline yet) | size 8, align 4: `11000000FFFFFF7F` |
| `bool` | `struct { uint8_t a; _Bool b; uint16_t c; }` | `SysV`, `Msvc` (modelled, no msvc baseline yet) | size 4, align 2: `11013322` |
| `pack2-array` | `#pragma pack(2) struct { uint8_t a; uint32_t b[2]; uint8_t c; }` | `SysV`, `Msvc` (modelled, no msvc baseline yet) | size 12, align 2: `11005544332299887766AA00` |
| `nested-align` | `struct inner { uint8_t a; uint32_t b; }; struct { uint8_t x; struct inner in; uint8_t y; }` | `SysV`, `Msvc` (modelled, no msvc baseline yet) | size 16, align 4: `11000000220000006655443377000000` |
| `union-size` | `union { uint8_t a; uint32_t b; uint16_t c[3]; }  /* c set */` | `SysV`, `Msvc` (modelled, no msvc baseline yet) | size 8, align 4: `2211443366550000` |
| `packed-bits-u8-u16` | `#pragma pack(1) struct { uint8_t a:4; uint16_t b:4; }` | `SysV`, `Msvc` (modelled, no msvc baseline yet) | size 1, align 1: `AF` |
| `packed-bits-u8-6-6` | `#pragma pack(1) struct { uint8_t a:6; uint8_t b:6; }` | `SysV`, `Msvc` (modelled, no msvc baseline yet) | size 2, align 1: `FF0F` |
| `packed-bits-u16-15-u8-2` | `#pragma pack(1) struct { uint16_t a:15; uint8_t b:2; }` | `SysV`, `Msvc` (modelled, no msvc baseline yet) | size 3, align 1: `FFFF01` |
| `packed-bits-after-byte` | `#pragma pack(1) struct { uint8_t x; uint32_t a:4; uint8_t b:4; }` | `SysV`, `Msvc` (modelled, no msvc baseline yet) | size 2, align 1: `AAFF` |

Baselines: GCC 15.2.0 on Linux x64 (x86_64-linux-gnu). The *Portable* column names the `BitfieldPacking` mode(s) in which the library reproduces the compiler of the same ABI family byte for byte; `CompilerDifferentialFixtureTests` verifies every claim against every baseline. Compilers not recorded here (MSVC, clang-cl, macOS clang, 32-bit targets) are recorded by the `compiler-fixtures` workflow when it runs.
<!-- compiler-fixture-table:end -->

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
