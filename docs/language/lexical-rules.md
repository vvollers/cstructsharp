---
title: Source text, names, comments, and numbers
description: Write complete Portable source with valid identifiers, comments, integer literals, and punctuation.
---

# Source text, names, comments, and numbers

A CStructSharp layout is one .NET `string`. The core library does not open header files, follow includes, read
compiler flags, guess a text encoding, or apply the current culture. Your application supplies the complete layout
text to the `CStruct` constructor.

The parser must reach the end of that string. Unsupported trailing text is an error rather than something the
library silently ignores.

## Identifiers and case

An identifier starts with `_` or a Unicode letter. Later characters may also contain Unicode decimal digits. The one
exception is an enum member, which may start with, or consist of, digits (`32BIT_MACHINE` as Windows headers spell
it, `0` in an enum of character codes):

```c
struct header_2 {
    uint16 value;
};
```

Names are case-sensitive. `Header`, `header`, and `HEADER` are three different names. Lowercase words such as
`struct`, `union`, `enum`, `flag`, and `typedef` are language keywords, and only as whole tokens: `structX` is an
identifier, as in C. A field named `_` is unnamed padding (see
[padding fields](structs-unions-enums-typedefs.md#padding-fields)).

Portable does not have C's separate namespace for `struct` tags. After:

```c
struct child {
    uint8 value;
};
```

another field refers to the type as `child item;`; `struct child item;` is also accepted and checked against the
declaration's kind.

Use ASCII identifiers when a layout is shared with tools that apply narrower naming rules, even though CStructSharp
itself accepts Unicode letters.

## Whitespace and comments

Spaces, tabs, and .NET-recognized line endings can separate tokens. Both familiar comment forms are accepted:

```c
// A one-line comment
struct root {
    uint8 tag; /* A block comment */
};
```

Block comments do not nest. An unclosed block comment makes the source invalid. Comments cannot split one keyword or
identifier.

Fields, enum declarations, and typedef declarations require semicolons. A top-level struct or union may omit its
final semicolon, but writing it consistently makes copied layouts easier to read.

A backslash immediately before a line end joins the two lines, anywhere in the source, exactly as C's preprocessor
does; a copied multi-line `#define` therefore keeps working.

## Preprocessor lines

A line starting with `#` is one of a closed set of directives; any other directive is a syntax error. `#define` binds
an integer expression (the next declaration may follow it on the same line); any other value - a quoted text
literal, a `b"..."` byte literal, a function-like macro, or a line that is not an expression - is a constant that
is published on `CStruct.Constants` and takes no part in expressions. `#undef` removes a constant
for the rest of the source. `#include <path>` and `#include "path"` are recorded on `CStruct.Includes` and never
read. `#pragma pack(push[, N])`, `pack(pop)`, `pack(N)`, and `pack()` maintain the alignment clamp applied to the
composites that follow, exactly like a composite `@align(N)`; every other `#pragma` is ignored.
An explicit composite `@align(N)` takes precedence over the active pragma for that declaration only. It does not
change the pragma applied to following declarations.
A `#define` with no value before the line ends defines an empty constant. It does not take a value from the next
line; use the backslash line continuation described above when joining lines is intended.
`#ifdef NAME`/`#ifndef NAME`/`#else`/`#endif` select declarations by the names defined so far in the source plus
`CStructCompilationOptions.Defined`; the text of a false branch is skipped without being parsed, and conditionals
nest.

```c
#include <stdint.h>
#define MAGIC "CD001"
#define VERSION 2
#pragma pack(push, 1)
#ifdef WIDE
struct header { uint32 length; };
#else
struct header { uint16 length; };
#endif
#pragma pack(pop)
```

## Integer literals

Expressions accept four bases:

```c
#define DECIMAL_COUNT 1024
#define HEX_MASK 0xCA_FE
#define BINARY_MASK 0b1111_0000
#define OCTAL_VALUE 0o755
```

Prefix letters may be upper- or lowercase. Underscores are visual separators and may appear within the digit
sequence, but the sequence must contain at least one real digit.

Documented C integer suffixes such as `U`, `L`, and `LL` are accepted and discarded. They do not widen the
expression's checked `Int32` arithmetic or make it unsigned. See the [grammar](grammar.md) for accepted forms.

Portable integer expressions do not include:

- floating-point, string, or character literals;
- casts or `sizeof`; or
- C's implicit integer-promotion rules.

A `+` or `-` directly before a literal is part of that literal. Unary `-` and `~` can also apply to a parenthesized or
named expression. [Expressions, defines, and variables](expressions-defines-and-variables.md) explains range and
overflow behavior.

## Punctuation

`{ } ( ) [ ] ; , : * < > #` have only the meanings shown in the [complete grammar](grammar.md).

Pointer stars may touch either name (`uint8* p`, `uint8 *p`, and `uint8 * p` are equivalent). `<` and `>` select
little- or big-endian order after supported primitive names. In expressions, `<` and `>` can instead be comparison
operators. A declaration may contain comma-separated declarators, and arrays may have multiple fixed dimensions.
See [arrays](arrays-and-strings.md) and [conditional expressions](expressions-defines-and-variables.md).

## Diagnose common source errors

| Input | Why it fails | Correction |
| --- | --- | --- |
| `uint8 2value;` | A name cannot start with a digit | Rename it to `value2` |
| `uint16>> value;` | There is no double byte-order suffix | Use `uint16>` |
| `uint8 values[0x_];` | The hexadecimal literal contains no digit | Use `0x0` or another count |
| trailing `garbage` | Source must be consumed completely | Remove or translate the unsupported text |

Construction reports these as `CStructLayoutException` with code `InvalidLayout`. Message text gives human detail
and may improve over time; branch on the code, not an exact sentence.
