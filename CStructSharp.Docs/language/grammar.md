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
                 | typedef-union-declaration
                 | typedef-declaration
                 | enum-declaration
                 | define-declaration ;

struct-declaration
                 = "struct", identifier, [ alignment-override ], "{", { struct-field }, "}", [ ";" ] ;
union-declaration
                 = "union", identifier, [ alignment-override ], "{", { union-field }, "}", [ ";" ] ;
typedef-struct-declaration
                 = "typedef", "struct", [ identifier ], [ alignment-override ], "{", { struct-field }, "}", identifier, ";" ;
typedef-union-declaration
                 = "typedef", "union", [ identifier ], [ alignment-override ], "{", { union-field }, "}", identifier, ";" ;
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
                 = "struct", [ alignment-override ], "{", { struct-field }, "}", [ identifier ], ";" ;
union-field      = field ;
field            = { type-qualifier }, [ tag-keyword ], type-name, declarator, { ",", declarator }, ";" ;
declarator       = named-declarator | anonymous-bitfield ;
named-declarator = { type-qualifier }, pointer-stars, { type-qualifier }, identifier, [ array ], [ bit-width ],
                   [ placement-suffix ] ;
anonymous-bitfield
                 = bit-width, [ placement-suffix ] ;
type-qualifier   = "const" | "volatile" | "restrict" ;
tag-keyword      = "struct" | "union" | "enum" ;
pointer-stars    = { "*" } ;
array            = "[", [ expression ], "]" ;
bit-width        = ":", expression ;
placement-suffix = alignment-override | offset-assertion ;
alignment-override
                 = "@align", "(", expression, ")" ;
offset-assertion = "@", expression ;

expression       = bitwise-or ;
bitwise-or       = bitwise-and, { "|", bitwise-and } ;
bitwise-and      = shift, { "&", shift } ;
shift            = additive, { ( "<<" | ">>" ), additive } ;
additive         = multiplicative, { ( "+" | "-" ), multiplicative } ;
multiplicative   = unary, { ( "*" | "/" ), unary } ;
unary            = { "-" | "~" }, primary ;
primary          = literal | identifier | "(", expression, ")" ;
literal          = sign, ( decimal | hexadecimal | binary | octal ), [ integer-suffix ] ;
integer-suffix   = { "u" | "U" | "l" | "L" } ;
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

An `inline-struct-field`'s trailing `identifier` is optional (LANG-14): when omitted, this is an *anonymous promoted
member* - its own fields splice directly into the containing struct's addressable path/POCO/JSON namespace
(`root.x`, not `root.<name>.x`) instead of nesting under a name of their own. The empty declarator name reuses the
same sentinel `anonymous-bitfield` already established for a nameless field. Promotion is transitive (an anonymous
member's own anonymous members promote further) and a name collision anywhere in the flattened, transitively-promoted
namespace is a construction-time error. Placement, size, and alignment are unaffected - this changes only which
path/POCO/JSON name resolves to a field. Scoped to structs only: a struct cannot nest a `union-field` at all today,
named or anonymous, so an anonymous inline union is not yet expressible. See
[Structs, unions, enums, and typedefs](structs-unions-enums-typedefs.md#anonymous-promoted-members).

Only one array declarator per name is accepted. Empty `[]` has meaning only for a supported character type and is
then a terminated string. A field declaration may share one type across multiple comma-separated declarators
(`uint8 first, second;`); each declarator has its own independent pointer stars, array, and bit width, matching C's
declarator-list semantics - a leading star belongs only to the declarator it directly precedes, not to every name in
the list, so `uint8 *a, b;` declares `a` as a pointer and `b` as a plain `uint8`. `const`, `volatile`, and `restrict`
are recognized before the type or immediately after a pointer star (`const uint8 value;`, `uint8 * const p;`) and
discarded with no effect on the compiled field; this is a fixed, closed set - other tokens in that position, or a
qualifier in any other position, are still rejected. A field's type reference may optionally be written with a
leading `struct`, `union`, or `enum` keyword (`struct child value;`), matching how C itself refers to a tagged type;
the keyword is checked against the referenced declaration's actual kind at construction time and rejected on a
mismatch, but otherwise has no effect - `struct child value;` and `child value;` compile to the identical field. A
declarator with a bit width and no name at all (`anonymous-bitfield`, LANG-17) reserves storage as pure padding -
its bits are consumed from the shared storage unit but it never becomes an addressable path, POCO member, or JSON
field (`uint8 flag:1, :3, other:4;`). This applies to any declarator after the first without ambiguity, since its
type is already fixed by the field's shared `type-name`. The first declarator is a special case: when exactly one
word appears before it and a bit width follows, that one word is the whole `type-name` and the first declarator
itself is the anonymous one (`uint8 :3;`); a run of two or more words before a bit width still splits normally into
`type-name` plus a named first declarator, even though a bit width follows (`uint8 flag:1;`), since only a
single-word run has no name token to spare. This means a multi-word anonymous type is not supported directly - it
falls back to naming the field after its last word instead (`unsigned int :3;` declares a field named `int` of type
`unsigned`, not an anonymous `unsigned int`). See [bitfields](bitfields.md#unnamed-padding-fields). A declarator may
carry at most one trailing placement suffix (LANG-15) - either `@align(N)`, overriding that one
declarator's own natural alignment, or bare `@N`, asserting the declarator's expected byte offset without ever
changing it. Both accept a full expression, evaluated the same way `bit-width` is, so a `#define`d constant works
for either. `@align(N)`'s `N` must be a positive power of two; it only has an observable effect when the enclosing
layout is constructed with `aligned: true` - in packed mode it is accepted but has no effect, the same as every
field's own natural alignment already having none there. `@N`'s value must be non-negative and is checked against
the declarator's actual computed offset only when that offset is statically known at construction time; if not
statically known, it is instead checked the first time any operation actually reaches the field. It is not
supported on a bitfield declarator. See
[Layout, alignment, and padding](layout-alignment-and-padding.md#explicit-field-alignment-override). A
`struct`/`union` declaration may itself carry `[ alignment-override ]` immediately before its opening brace,
clamping every one of that composite's own fields' alignment to at most `N` (matching `#pragma pack(N)` semantics)
unless a field carries its own explicit `@align(N)`, which always wins outright instead of being further clamped;
`N=1` therefore has the effect of "packed" for that one composite. Like the field-level form, it only has an
observable effect when the enclosing layout is constructed with `aligned: true`. See
[Layout, alignment, and padding](layout-alignment-and-padding.md#explicit-composite-alignment-override).
A `#define` is one object-like integer expression; there are no parameters or textual expansion. Function-call
spelling is recognized only so construction can reject it explicitly. It is not a supported `primary`, and it never
executes user code.

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
| `typedef-struct-declaration` | Named-tag or anonymous inline struct alias form; [typedefs](structs-unions-enums-typedefs.md#typedefs) |
| `typedef-union-declaration` | Named-tag or anonymous inline union alias form; [typedefs](structs-unions-enums-typedefs.md#typedefs) |
| `typedef-declaration` | Alias of one name plus optional pointer depth |
| `enum-declaration` | Named integral enum |
| `enum-storage` | Optional explicit integral backing |
| `enum-values` | Comma-separated member sequence |
| `enum-value` | Member name plus optional bounded expression |
| `define-declaration` | Object-like integer expression binding |
| `struct-field` | Ordinary, named-inline-struct, or anonymous-promoted-struct member |
| `inline-struct-field` | Lexically scoped sequential composite; anonymous when the trailing `identifier` is omitted (LANG-14) |
| `union-field` | Ordinary field; inline structs/unions are not accepted here |
| `field` | One optionally qualified, optionally tagged type, one or more comma-separated declarators |
| `declarator` | A named declarator or an anonymous nonzero-width bitfield |
| `named-declarator` | One name with its own optional qualifiers, pointer stars, optional array, optional bit width, and optional placement suffix |
| `anonymous-bitfield` | A nameless bit-width-only declarator used as pure padding (LANG-17) |
| `type-qualifier` | A recognized layout-neutral qualifier, discarded with no effect on the compiled field |
| `tag-keyword` | An optional struct/union/enum keyword, checked against the referenced declaration's actual kind |
| `pointer-stars` | Zero or more data-pointer levels |
| `array` | One fixed/runtime count or character-string marker |
| `bit-width` | One named nonzero portable bit slice, or unnamed reserved padding for `anonymous-bitfield` |
| `placement-suffix` | At most one trailing alignment override or offset assertion per declarator |
| `alignment-override` | An explicit per-declarator alignment override, effective only when `aligned: true` |
| `offset-assertion` | An explicit per-declarator byte-offset assertion, checked when statically computable |
| `expression` | Complete checked integer expression |
| `bitwise-or` | Lowest-precedence bitwise OR |
| `bitwise-and` | Bitwise AND |
| `shift` | Checked left/right shift |
| `additive` | Checked addition/subtraction |
| `multiplicative` | Checked multiplication/division |
| `unary` | Negation and bitwise complement |
| `primary` | Literal, variable/name, or parenthesized expression |
| `literal` | Optional sign plus one radix-specific integer, plus an optional discarded C-style suffix |
| `integer-suffix` | Zero or more `u`/`U`/`l`/`L` characters, recognized and discarded with no effect on the value |
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
