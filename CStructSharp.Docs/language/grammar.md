---
title: Complete Portable grammar
description: Look up every accepted source, expression, lexical, and public path production.
---

# Complete Portable grammar

This page is a precise syntax reference. You do not need to understand EBNF to follow the tutorial or use ordinary
layouts.

EBNF (Extended Backus–Naur Form) is a compact way to describe syntax:

- quoted text is written literally;
- `{ x }` means zero or more repetitions;
- `[ x ]` means optional; and
- `|` separates alternatives.

A layout must match through `end-of-input`; unsupported trailing text is not ignored. The
[source-text rules](lexical-rules.md) explain names, comments, and numbers. Other language pages explain combinations
that are syntactically recognizable but invalid, such as an array bitfield or unsupported enum backing type.

## Source and expression EBNF

```ebnf
definition       = trivia, { declaration }, end-of-input ;
declaration      = struct-declaration
                 | union-declaration
                 | typedef-struct-declaration
                 | typedef-declaration
                 | enum-declaration
                 | define-declaration ;

struct-declaration
                 = "struct", identifier, "{", { struct-field }, "}", [ ";" ] ;
union-declaration
                 = "union", identifier, "{", { union-field }, "}", [ ";" ] ;
typedef-struct-declaration
                 = "typedef", struct-declaration, identifier, ";" ;
typedef-declaration
                 = "typedef", identifier, pointer-stars, identifier, ";" ;
enum-declaration = "enum", identifier, [ enum-storage ],
                   "{", [ enum-values ], "}", ";" ;
enum-storage     = ":", identifier ;
enum-values      = enum-value, { ",", enum-value } ;
enum-value       = identifier, [ "=", expression ] ;
define-declaration
                 = "#define", identifier, expression ;

struct-field     = field | inline-struct-field ;
inline-struct-field
                 = "struct", "{", { struct-field }, "}", identifier, ";" ;
union-field      = field ;
field            = type-name, pointer-stars, identifier, [ array ], [ bit-width ], ";" ;
pointer-stars    = { "*" } ;
array            = "[", [ expression ], "]" ;
bit-width        = ":", expression ;

expression       = bitwise-or ;
bitwise-or       = bitwise-and, { "|", bitwise-and } ;
bitwise-and      = shift, { "&", shift } ;
shift            = additive, { ( "<<" | ">>" ), additive } ;
additive         = multiplicative, { ( "+" | "-" ), multiplicative } ;
multiplicative   = unary, { ( "*" | "/" ), unary } ;
unary            = { "-" | "~" }, primary ;
primary          = literal | identifier | "(", expression, ")" ;
literal          = sign, ( decimal | hexadecimal | binary | octal ) ;
sign             = [ "+" | "-" ] ;
decimal          = decimal-digits ;
hexadecimal      = ( "0x" | "0X" ), hex-digits ;
binary           = ( "0b" | "0B" ), binary-digits ;
octal            = ( "0o" | "0O" ), octal-digits ;
decimal-digits   = decimal-part, { decimal-part } ;
hex-digits       = hex-part, { hex-part } ;
binary-digits    = binary-part, { binary-part } ;
octal-digits     = octal-part, { octal-part } ;
decimal-part     = decimal-digit | "_" ;
hex-part         = hex-digit | "_" ;
binary-part      = binary-digit | "_" ;
octal-part       = octal-digit | "_" ;

type-name        = identifier | endian-primitive ;
endian-primitive = identifier, ( "<" | ">" ) ;
identifier       = identifier-start, { identifier-continue } ;
identifier-start = unicode-letter | "_" ;
identifier-continue
                 = unicode-letter | decimal-digit | "_" ;
trivia           = { whitespace | line-comment | block-comment } ;
whitespace       = whitespace-character, { whitespace-character } ;
line-comment     = "//", { non-line-end-character }, [ line-end ] ;
block-comment    = "/*", { block-comment-character }, "*/" ;
line-end         = "\r\n" | "\r" | "\n" ;
end-of-input     = ? no remaining character ? ;
```

Each numeric digit sequence must contain at least one real digit; underscores alone are invalid. `decimal-digit` is
`0`–`9`, `binary-digit` is `0` or `1`, `octal-digit` is `0`–`7`, and `hex-digit` is `0`–`9`, `a`–`f`, or `A`–`F`.
`unicode-letter` and identifier continuation use .NET Unicode letter/letter-or-digit classification. Block comments
do not nest. The parser accepts pointer stars adjacent to either token (`uint8* p`, `uint8 *p`, and `uint8 * p`) and
normalizes the total star count.

Only one array declarator is accepted. Empty `[]` has meaning only for a supported character type and is then a
terminated string. A `#define` is one object-like integer expression; there are no parameters or textual expansion.
Function-call spelling is recognized only so construction can reject it explicitly. It is not a supported
`primary`, and it never executes user code.

## Public path EBNF

```ebnf
path             = segment, { ".", segment } ;
segment          = identifier, [ indexer ] ;
indexer          = "[", canonical-decimal-index, "]" ;
canonical-decimal-index
                 = "0" | nonzero-decimal-digit, { decimal-digit } ;
pointer-accessor = ".address" | ".value" ;
```

`pointer-accessor` describes the special meaning of an ordinary path segment after a pointer: `.address` selects
pointer storage and `.value` consumes one pointer level. It is not a separate lexical token. A non-pointer field may
therefore still be named `address` or `value`. Indices have no sign, whitespace, leading zero, base prefix, or
underscore, and must fit a non-negative 32-bit integer. See [paths and selection](paths-and-selection.md).

## Production index

The table explains each production and links to the page that defines its additional meaning/range rules.

| Production | Meaning and detailed rules |
| --- | --- |
| `definition` | Complete standalone input; [Portable rules](portable-v1-reference.md) |
| `declaration` | One exported declaration kind |
| `struct-declaration` | Named sequential composite; [declarations](structs-unions-enums-typedefs.md#named-structs) |
| `union-declaration` | Named overlapping composite; [declarations](structs-unions-enums-typedefs.md#unions) |
| `typedef-struct-declaration` | Supported named struct alias form; [typedefs](structs-unions-enums-typedefs.md#typedefs) |
| `typedef-declaration` | Alias of one name plus optional pointer depth |
| `enum-declaration` | Named integral enum |
| `enum-storage` | Optional explicit integral backing |
| `enum-values` | Comma-separated member sequence |
| `enum-value` | Member name plus optional bounded expression |
| `define-declaration` | Object-like integer expression binding |
| `struct-field` | Ordinary or named inline-struct member |
| `inline-struct-field` | Lexically scoped sequential composite |
| `union-field` | Ordinary field; inline structs/unions are not accepted here |
| `field` | One type, one declarator, optional array, optional bit width |
| `pointer-stars` | Zero or more data-pointer levels |
| `array` | One fixed/runtime count or character-string marker |
| `bit-width` | One named nonzero portable bit slice |
| `expression` | Complete checked integer expression |
| `bitwise-or` | Lowest-precedence bitwise OR |
| `bitwise-and` | Bitwise AND |
| `shift` | Checked left/right shift |
| `additive` | Checked addition/subtraction |
| `multiplicative` | Checked multiplication/division |
| `unary` | Negation and bitwise complement |
| `primary` | Literal, variable/name, or parenthesized expression |
| `literal` | Optional sign plus one radix-specific integer |
| `sign` | Literal-leading plus/minus |
| `decimal` | Base-10 digit sequence |
| `hexadecimal` | `0x`/`0X` base-16 digit sequence |
| `binary` | `0b`/`0B` base-2 digit sequence |
| `octal` | `0o`/`0O` base-8 digit sequence |
| `decimal-digits` | Decimal digits with optional visual underscores |
| `hex-digits` | Hexadecimal digits with optional visual underscores |
| `binary-digits` | Binary digits with optional visual underscores |
| `octal-digits` | Octal digits with optional visual underscores |
| `decimal-part` | One decimal digit or underscore |
| `hex-part` | One hexadecimal digit or underscore |
| `binary-part` | One binary digit or underscore |
| `octal-part` | One octal digit or underscore |
| `type-name` | Declared/aliased name or primitive with byte-order suffix |
| `endian-primitive` | Primitive name followed by `<` or `>` |
| `identifier` | Case-sensitive Unicode identifier |
| `identifier-start` | Unicode letter or underscore |
| `identifier-continue` | Unicode letter, digit, or underscore |
| `trivia` | Ignorable whitespace and comments between tokens |
| `whitespace` | One or more .NET whitespace characters |
| `line-comment` | `//` through a line ending or input end |
| `block-comment` | Non-nesting `/* ... */` comment |
| `line-end` | CRLF, CR, or LF |
| `end-of-input` | Requires the parser to consume the complete input |
| `path` | Dot-separated public selector |
| `segment` | Named path component with at most one index |
| `indexer` | Normalized decimal array index |
| `canonical-decimal-index` | Formal production name for `0` or an unpadded positive decimal integer |
| `pointer-accessor` | `.address`/`.value` selection after a pointer |

Invalid combinations—unknown types, duplicate names, recursive by-value storage, bad enum backing,
oversized expressions, unsupported bitfield storage, and unsized non-character arrays—fail layout construction with
`CStructErrorCode.InvalidLayout`. Syntax recognized only for a focused error does not expand the supported grammar.
