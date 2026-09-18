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
definition       = trivia, { declaration | preprocessor-line }, end-of-input ;
declaration      = struct-declaration
                 | union-declaration
                 | anonymous-composite-declaration
                 | forward-declaration
                 | typedef-struct-declaration
                 | typedef-union-declaration
                 | typedef-tag-alias
                 | typedef-enum-declaration
                 | typedef-declaration
                 | enum-declaration ;

struct-declaration
                 = "struct", identifier, [ alignment-override ], "{", { struct-field }, "}", [ identifier ], [ ";" ] ;
union-declaration
                 = "union", identifier, [ alignment-override ], "{", { union-field }, "}", [ identifier ], [ ";" ] ;
anonymous-composite-declaration
                 = ( "struct" | "union" ), [ alignment-override ], "{", { struct-field }, "}", identifier, ";" ;
forward-declaration
                 = ( "struct" | "union" ), identifier, ";" ;
typedef-struct-declaration
                 = "typedef", "struct", [ identifier ], [ alignment-override ], "{", { struct-field }, "}", typedef-aliases ;
typedef-union-declaration
                 = "typedef", "union", [ identifier ], [ alignment-override ], "{", { union-field }, "}", typedef-aliases ;
typedef-aliases  = ";" | typedef-alias, { ",", typedef-alias }, ";" ;
typedef-alias    = pointer-stars, identifier ;
typedef-tag-alias
                 = "typedef", ( "struct" | "union" | "enum" ), identifier, identifier, ";" ;
typedef-enum-declaration
                 = "typedef", ( "enum" | "flag" ), [ identifier ], [ enum-storage ], "{", [ enum-values ], [ "," ], "}",
                   typedef-aliases ;
typedef-declaration
                 = "typedef", type-name, typedef-declarator, { ",", typedef-declarator }, ";" ;
typedef-declarator
                 = pointer-stars, identifier, { "[", expression, "]" } ;
enum-declaration = ( "enum" | "flag" ), [ identifier ], [ enum-storage ],
                   "{", [ enum-values ], [ "," ], "}", ";" ;
enum-storage     = ":", identifier ;
enum-values      = enum-value, { [ "," ], enum-value } ;
enum-value       = enum-member-name, [ "=", expression ] ;
enum-member-name = identifier | decimal-digit, { identifier-continue } ;
preprocessor-line
                 = define-declaration | constant-definition | undef-line | include-line | pragma-line
                 | conditional-line ;
define-declaration
                 = "#define", identifier, expression ;
constant-definition
                 = "#define", identifier, [ quoted-literal | "b", quoted-literal | macro-parameters, rest-of-line
                                          | rest-of-line ] ;
macro-parameters = "(", { non-line-end-character }, ")" ;
undef-line       = "#undef", identifier ;
include-line     = "#include", ( "<", { non-line-end-character }, ">" | '"', { non-line-end-character }, '"' ) ;
pragma-line      = "#pragma", ( "pack", "(", [ "push", [ ",", expression ] | "pop" | expression ], ")" | rest-of-line ) ;
conditional-line = ( "#ifdef" | "#ifndef" ), identifier | "#else" | "#endif" ;
quoted-literal   = '"', { character | escape }, '"' | "'", { character | escape }, "'" ;

struct-field     = field | inline-struct-field | conditional-field | switch-field ;
field-block      = "{", { struct-field }, "}" ;
conditional-field = "if", "(", expression, ")", field-block, [ "else", field-block ] ;
switch-field     = "switch", "(", expression, ")", "{", { switch-case }, [ "default", ":", field-block ], "}" ;
switch-case      = "case", expression, ":", field-block ;
inline-struct-field
                 = "struct", [ identifier ], [ alignment-override ], "{", { struct-field }, "}", [ declarator-list ], ";"
                 | "union", [ identifier ], [ alignment-override ], "{", { union-field }, "}", [ declarator-list ], ";" ;
declarator-list  = declarator, { ",", declarator } ;
union-field      = field | inline-struct-field ;
field            = { type-qualifier }, [ tag-keyword ], type-name, declarator, { ",", declarator }, ";" ;
declarator       = named-declarator | anonymous-bitfield ;
named-declarator = { type-qualifier }, pointer-stars, { type-qualifier }, identifier, [ array ], [ bit-width ],
                   [ placement-suffix ]
                 | "(", "*", identifier, ")", "(", { non-line-end-character }, ")" ;
anonymous-bitfield
                 = bit-width, [ placement-suffix ] ;
type-qualifier   = "const" | "volatile" | "restrict" ;
tag-keyword      = "struct" | "union" | "enum" ;
pointer-stars    = { "*" } ;
array            = { "[", [ expression | "EOF" ], "]" } ;
bit-width        = ":", expression ;
placement-suffix = alignment-override | offset-assertion ;
alignment-override
                 = "@align", "(", expression, ")" ;
offset-assertion = "@", expression ;

expression       = logical-or, [ "?", expression, ":", expression ] ;
logical-or       = logical-and, { "||", logical-and } ;
logical-and      = bitwise-or, { "&&", bitwise-or } ;
bitwise-or       = bitwise-xor, { "|", bitwise-xor } ;
bitwise-xor      = bitwise-and, { "^", bitwise-and } ;
bitwise-and      = equality, { "&", equality } ;
equality         = relational, { ( "==" | "!=" ), relational } ;
relational       = shift, { ( "<" | "<=" | ">" | ">=" ), shift } ;
shift            = additive, { ( "<<" | ">>" ), additive } ;
additive         = multiplicative, { ( "+" | "-" ), multiplicative } ;
multiplicative   = unary, { ( "*" | "/" | "%" ), unary } ;
unary            = { "-" | "~" | "!" }, primary ;
primary          = literal | qualified-name | size-call | "(", expression, ")" ;
qualified-name   = identifier, { ".", identifier } ;
size-call        = "sizeof", "(", type-spelling, ")" | "offsetof", "(", type-spelling, ",", identifier, ")" ;
type-spelling    = identifier, { identifier }, pointer-stars ;
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
trivia           = { whitespace | line-comment | block-comment | line-continuation } ;
line-continuation
                 = "\\", line-end ;
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

An `inline-struct-field`'s trailing `identifier` is optional: when omitted, this is an *anonymous promoted
member* - its own fields splice directly into the containing struct's addressable path/POCO/JSON namespace
(`root.x`, not `root.<name>.x`) instead of nesting under a name of their own. The empty declarator name reuses the
same sentinel `anonymous-bitfield` already established for a nameless field. Promotion is transitive (an anonymous
member's own anonymous members promote further) and a name collision anywhere in the flattened, transitively-promoted
namespace is a construction-time error. Placement, size, and alignment are unaffected - this changes only which
path/POCO/JSON name resolves to a field. Promotion is supported for inline structs, not inline unions.
A field referencing a separately declared named union is supported. See
[Structs, unions, enums, and typedefs](structs-unions-enums-typedefs.md#anonymous-promoted-members).

A declarator accepts zero or more bracketed dimensions, written outermost first (`value[rows][columns]`).
Every dimension of a multidimensional declaration must currently be compile-time fixed, with no named
expression dependencies. Literal arithmetic such as `[2 + 1][4]` is allowed; a named `#define` count is not. A one-dimensional
array may use a runtime expression referencing an earlier field, caller variable, or definition. This is a
Portable restriction, not a claim about C variable-length arrays. Empty `[]` has meaning only for a supported character type and is then a terminated string;
it is accepted only as the sole dimension of a one-dimensional declarator (`char name[10][]` is rejected - an
unsized dimension can never be an inner dimension of a multidimensional array). A fixed table of fixed-width
strings (`char names[10][32]`) is ordinary within this rule: the innermost dimension behaves exactly like today's
`char[32]` fixed buffer, and every outer dimension nests around it. See
[Arrays and strings](arrays-and-strings.md#multidimensional-arrays). A field declaration may share one type across multiple comma-separated declarators
(`uint8 first, second;`); each declarator has its own independent pointer stars, array, and bit width, matching C's
declarator-list semantics - a leading star belongs only to the declarator it directly precedes, not to every name in
the list, so `uint8 *a, b;` declares `a` as a pointer and `b` as a plain `uint8`. `const`, `volatile`, and `restrict`
are recognized before the type or immediately after a pointer star (`const uint8 value;`, `uint8 * const p;`) and
discarded with no effect on the compiled field; this is a fixed, closed set - other tokens in that position, or a
qualifier in any other position, are still rejected. A field's type reference may optionally be written with a
leading `struct`, `union`, or `enum` keyword (`struct child value;`), matching how C itself refers to a tagged type;
the keyword is checked against the referenced declaration's actual kind at construction time and rejected on a
mismatch, but otherwise has no effect - `struct child value;` and `child value;` compile to the identical field. A
declarator with a bit width and no name at all (`anonymous-bitfield`) reserves storage as pure padding -
its bits are consumed from the shared storage unit but it never becomes an addressable path, POCO member, or JSON
field (`uint8 flag:1, :3, other:4;`). This applies to any declarator after the first without ambiguity, since its
type is already fixed by the field's shared `type-name`. The first declarator is a special case: when exactly one
word appears before it and a bit width follows, that one word is the whole `type-name` and the first declarator
itself is the anonymous one (`uint8 :3;`); a run of two or more words before a bit width still splits normally into
`type-name` plus a named first declarator, even though a bit width follows (`uint8 flag:1;`), since only a
single-word run has no name token to spare. This means a multi-word anonymous type is not supported directly - it
falls back to naming the field after its last word instead (`unsigned int :3;` declares a field named `int` of type
`unsigned`, not an anonymous `unsigned int`). See [bitfields](bitfields.md#unnamed-padding-fields). A declarator may
carry at most one trailing placement suffix - either `@align(N)`, overriding that one
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

Whitespace between a directive and its name includes .NET whitespace characters such as a non-breaking space.
Carriage return and line feed end the directive: a name cannot start on the next physical line.

## Public path EBNF

```ebnf
path             = segment, { ".", segment } ;
segment          = identifier, { indexer } ;
indexer          = "[", canonical-decimal-index, "]" ;
canonical-decimal-index
                 = "0" | nonzero-decimal-digit, { decimal-digit } ;
pointer-accessor = ".address" | ".value" ;
```

`pointer-accessor` describes the special meaning of an ordinary path segment after a pointer: `.address` selects
pointer storage and `.value` consumes one pointer level. It is not a separate lexical token. A non-pointer field may
therefore still be named `address` or `value`. Indices have no sign, whitespace, leading zero, base prefix, or
underscore, and must fit a non-negative 32-bit integer. A segment mirrors its field's own declaration syntax: an
N-dimensional array accepts up to N repeated `indexer`s in one segment (`root.matrix[2][3]`, not
comma-separated), one per dimension, outermost first. Supplying fewer than N selects the corresponding
lower-dimensional sub-array rather than one scalar/struct element; supplying more than N is rejected. See
[paths and selection](paths-and-selection.md#multidimensional-arrays).

## Production index

The table explains each production and links to the page that defines its additional meaning/range rules.

| Production | Meaning and detailed rules |
| --- | --- |
| `definition` | Complete standalone input; [Portable rules](portable-v1-reference.md) |
| `declaration` | One exported declaration kind |
| `struct-declaration` | Named sequential composite, optionally followed by an ignored object name; [declarations](structs-unions-enums-typedefs.md#named-structs) |
| `union-declaration` | Named overlapping composite, optionally followed by an ignored object name; [declarations](structs-unions-enums-typedefs.md#unions) |
| `anonymous-composite-declaration` | A body whose trailing name is the declared type (`struct { ... } timeval;`); [declarations](structs-unions-enums-typedefs.md#top-level-declaration-forms) |
| `forward-declaration` | `struct node;` - accepted and declares nothing; [declarations](structs-unions-enums-typedefs.md#top-level-declaration-forms) |
| `typedef-struct-declaration` | Named-tag or anonymous struct body with one or more aliases; the tag is declared too; [typedefs](structs-unions-enums-typedefs.md#typedefs) |
| `typedef-union-declaration` | Named-tag or anonymous union body with one or more aliases; [typedefs](structs-unions-enums-typedefs.md#typedefs) |
| `typedef-aliases` | `;` alone (tag only) or a comma-separated alias list |
| `typedef-alias` | One alias with its own pointer depth (`*PX`) |
| `typedef-tag-alias` | `typedef struct tag alias;` (or `union`/`enum`) - an alias of a declared tag, kind-checked |
| `typedef-enum-declaration` | Tagged or anonymous `enum`/`flag` body with aliases, mirroring the struct forms; [typedefs](structs-unions-enums-typedefs.md#typedefs) |
| `typedef-declaration` | One type spelling with one or more declarators |
| `typedef-declarator` | Alias name with optional pointer depth and fixed array dimensions (`typedef T name[N];`) |
| `enum-declaration` | Named integral enum or `flag` (bitmask enum); an unnamed one declares constants |
| `enum-storage` | Optional explicit integral backing (any accepted integer spelling or a typedef of one) |
| `enum-values` | Member sequence; the comma is optional because a value can never be followed by a name |
| `enum-value` | Member name plus optional bounded expression |
| `enum-member-name` | An identifier, or a name that starts with (or consists of) digits (`32BIT_MACHINE`, `0`) |
| `preprocessor-line` | One `#` line; see [source text](lexical-rules.md#preprocessor-lines) |
| `define-declaration` | Object-like integer expression binding |
| `constant-definition` | A text, byte, bare, function-like, or otherwise non-expression `#define` published as a constant |
| `macro-parameters` | The parameter list glued to a function-like macro name |
| `undef-line` | Removes a constant for the rest of the source |
| `include-line` | Recorded path, never resolved |
| `pragma-line` | `pack` maintains the alignment clamp stack; other pragmas are ignored |
| `conditional-line` | `#ifdef`/`#ifndef`/`#else`/`#endif` over defined names |
| `quoted-literal` | A `"`- or `'`-delimited literal with C escapes (`\n`, `\r`, `\t`, `\0`, `\xHH`, `\"`) |
| `struct-field` | Ordinary, named-inline-struct, or anonymous-promoted-struct member |
| `inline-struct-field` | Inline struct or union member; anonymous (promoted) without a declarator; a tag makes the body a global type as well; [declarations](structs-unions-enums-typedefs.md#inline-structs) |
| `declarator-list` | The member declarators of a tagged inline body (`} gen, *pgen;`) |
| `union-field` | Ordinary field or an inline composite; conditionals are not accepted in a union |
| `field` | One optionally qualified, optionally tagged type, one or more comma-separated declarators |
| `declarator` | A named declarator, a `_` padding field, or an anonymous nonzero-width bitfield; [padding fields](structs-unions-enums-typedefs.md#padding-fields) |
| `named-declarator` | One name with its own optional qualifiers, pointer stars, optional array, optional bit width, and optional placement suffix; or a function-pointer declarator, stored as an opaque pointer |
| `anonymous-bitfield` | A nameless bit-width-only declarator used as pure padding |
| `type-qualifier` | A recognized layout-neutral qualifier, discarded with no effect on the compiled field |
| `tag-keyword` | An optional struct/union/enum keyword, checked against the referenced declaration's actual kind |
| `pointer-stars` | Zero or more data-pointer levels |
| `array` | Zero or more fixed/runtime dimension counts or character-string markers, outermost first |
| `bit-width` | One named nonzero portable bit slice, or unnamed reserved padding for `anonymous-bitfield` |
| `placement-suffix` | At most one trailing alignment override or offset assertion per declarator |
| `alignment-override` | An explicit per-declarator alignment override, effective only when `aligned: true` |
| `offset-assertion` | An explicit per-declarator byte-offset assertion, checked when statically computable |
| `expression` | Complete checked integer expression, optionally a conditional `c ? a : b` |
| `bitwise-or` | Lowest-precedence bitwise OR |
| `bitwise-xor` | Bitwise XOR, between `&` and `\|` as in C |
| `qualified-name` | A variable, field, define, `Enum.Member` constant, or a nested field through its struct field (`hdr.n`, `a.b.n`) |
| `size-call` | `sizeof(type)` / `offsetof(type, field)`, folded to a literal at construction |
| `type-spelling` | A primitive/typedef/enum/composite spelling with optional pointer stars |
| `bitwise-and` | Bitwise AND |
| `shift` | Checked left/right shift |
| `additive` | Checked addition/subtraction |
| `multiplicative` | Checked multiplication, division, and remainder |
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
| `line-continuation` | A backslash immediately before a line end joins the lines |
| `trivia` | Ignorable whitespace and comments between tokens |
| `whitespace` | One or more .NET whitespace characters |
| `line-comment` | `//` through a line ending or input end |
| `block-comment` | Non-nesting `/* ... */` comment |
| `line-end` | CRLF, CR, or LF |
| `end-of-input` | Requires the parser to consume the complete input |
| `path` | Dot-separated public selector |
| `field-block` | Braced sequence of fields, including nested conditional groups |
| `conditional-field` | Runtime if/else group with lazy inactive branches |
| `switch-field` | Discriminator-selected cases and optional fallback |
| `switch-case` | Compile-time constant label and a braced field block |
| `logical-or` | Short-circuit logical OR |
| `logical-and` | Short-circuit logical AND |
| `equality` | Integer equality and inequality comparisons |
| `relational` | Ordered integer comparisons |
| `segment` | Named path component with zero or more indices, one per dimension actually indexed |
| `indexer` | Normalized decimal array index |
| `canonical-decimal-index` | Formal production name for `0` or an unpadded positive decimal integer |
| `pointer-accessor` | `.address`/`.value` selection after a pointer |

Invalid combinations—unknown types, duplicate names, recursive by-value storage, bad enum backing,
oversized expressions, unsupported bitfield storage, and unsized non-character arrays—fail layout construction with
`CStructErrorCode.InvalidLayout`. Syntax recognized only for a focused error does not expand the supported grammar.

## Conditional field groups

For a step-by-step explanation with array items, calculations, scope exercises,
and browser lessons, start with [Choose fields with if and switch](../guides/conditional-fields.md).

A struct body accepts `if (expression) { fields }` with an optional
`else { fields }`, and `switch (expression) { case expression: { fields }
... default: { fields } }`. Each case requires braces and has no fall-through.
The default is optional; a switch without a matching case/default contributes
no fields. Cases and branches retain the enclosing field namespace: give
alternatives distinct names, preferably named inline structs. Duplicate field
names remain errors even in mutually exclusive branches.

Case labels must evaluate to distinct checked integer constants when the layout
is compiled. Labels may use `#define` constants; later caller overrides do not
change their values. Equivalent labels such as `1` and `1 + 0` are duplicates,
including on empty arms. Runtime fields cannot supply case labels.

```c
struct packet {
    uint8 kind;
    switch (kind) {
        case 1: { struct { uint16 value; } short_record; }
        case 2: { struct { uint32 value; } long_record; }
        default: { uint8 unknown_tag_marker; }
    }
    uint8 trailer;
};
```

Only active fields consume storage, appear in results/debug data, or resolve as
paths. Predicates use earlier decoded fields, definitions and caller variables.
In a composite containing conditional fields, local declarations shadow caller
values from the start of that composite. A forward or inactive local is unavailable;
it cannot reuse a value from an earlier array element. This includes fields exposed
through anonymous struct promotion, including transitive promotion. Unavailable active expressions
raise `CStructLayoutException` during reads, writes, and address resolution.
Each group uses the variable environment captured when that group is first
reached. Nested groups see fields read before their own entry. Named nested
members cannot overwrite the enclosing conditional composite's local fields
for later predicates; each struct-array element starts a fresh scope.
Nested inactive predicates are not evaluated. Put conditional bitfields inside
a named inline struct so each group owns its storage units. Ordinary unions
retain their existing overlapping semantics.

Serialization chooses branches from supplied values and rejects supplied inactive
members, including promoted members of inactive anonymous structs. For roots with
reachable conditional types (including aliases and pointer targets), in-place
updates validate the original and staged root, rejecting changes
to active branch decisions or field byte ranges. This requires the complete root
to be readable within the configured budgets. Serialize a new buffer when a
change requires a different layout. Unrelated conditional type declarations do not
force full-root validation for a selected update whose root has no conditional types.
