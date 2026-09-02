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

An identifier starts with `_` or a Unicode letter. Later characters may also contain Unicode decimal digits:

```c
struct header_2 {
    uint16 value;
};
```

Names are case-sensitive. `Header`, `header`, and `HEADER` are three different names. Lowercase words such as
`struct`, `union`, `enum`, and `typedef` are language keywords.

Portable does not have C's separate namespace for `struct` tags. After:

```c
struct child {
    uint8 value;
};
```

another field refers to the type as `child item;`, not `struct child item;`.

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

Portable integer expressions do not include:

- C suffixes such as `U`, `L`, or `LL`;
- floating-point, string, or character literals;
- casts or `sizeof`; or
- C's implicit integer-promotion rules.

A `+` or `-` directly before a literal is part of that literal. Unary `-` and `~` can also apply to a parenthesized or
named expression. [Expressions, defines, and variables](expressions-defines-and-variables.md) explains range and
overflow behavior.

## Punctuation

`{ } ( ) [ ] ; , : * < > #` have only the meanings shown in the [complete grammar](grammar.md).

Pointer stars may touch either name (`uint8* p`, `uint8 *p`, and `uint8 * p` are equivalent). `<` and `>` select
little- or big-endian order only on supported primitive names; they are not comparison operators. A field can have
one name and at most one array dimension.

## Diagnose common source errors

| Input | Why it fails | Correction |
| --- | --- | --- |
| `uint8 2value;` | A name cannot start with a digit | Rename it to `value2` |
| `uint8 a, b;` | One field declaration has one name | Write two declarations |
| `uint16>> value;` | There is no double byte-order suffix | Use `uint16>` |
| `#define N 4U` | C integer suffixes are unsupported | Use `4` |
| `uint8 values[0x_];` | The hexadecimal literal contains no digit | Use `0x0` or another count |
| trailing `garbage` | Source must be consumed completely | Remove or translate the unsupported text |

Construction reports these as `CStructLayoutException` with code `InvalidLayout`. Message text gives human detail
and may improve over time; branch on the code, not an exact sentence.
