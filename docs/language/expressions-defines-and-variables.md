---
title: Expressions, defines, and runtime variables
description: Calculate array counts, bit widths, and enum values with checked integer expressions.
---

# Expressions, defines, and runtime variables

Portable expressions calculate integer values used by array counts, bitfield widths, enum members, and `#define`
declarations. They are not general C expressions and cannot call application code.

## A simple count

```c
#define WORD_COUNT 4

struct root {
    uint16 values[WORD_COUNT];
};
```

`WORD_COUNT` supplies a default count of four. A caller variable can override that name for an operation,
so it is not an immutable array-size declaration. Use `[4]` for a literal fixed count. Multidimensional arrays
currently require identifier-free count expressions in every dimension; even a named `#define` is rejected there.

When a value is not known until one operation, use a caller variable:

```c
struct packet {
    uint8 payload[COUNT];
};
```

```csharp
var variables = new Dictionary<string, int>
{
    ["COUNT"] = 3,
};
```

The operation copies the dictionary and uses the caller value in preference to a matching `#define`. Undefined names
and circular dependencies fail explicitly.

## Operators and precedence

Expressions support decimal, hexadecimal (`0x`), binary (`0b`), and octal (`0o`) integers, parentheses, unary
`!`/`-`/`~`, and these binary operators:

| Precedence, high to low | Operators |
| --- | --- |
| Unary | `-`, `~`, `!` |
| Multiply/divide/remainder | `*`, `/`, `%` |
| Add/subtract | `+`, `-` |
| Shift | `<<`, `>>` |
| Relational | `<`, `<=`, `>`, `>=` |
| Equality | `==`, `!=` |
| Bitwise AND | `&` |
| Bitwise XOR | `^` |
| Bitwise OR | `\|` |
| Logical AND | `&&` |
| Logical OR | `\|\|` |
| Conditional | `c ? a : b` (right-associative; only the selected arm is evaluated) |

Operators on the same row are evaluated left to right; `%` truncates like `/` and fails on a zero divisor. The two
calls `sizeof(type)` and `offsetof(type, field)` are accepted in array dimensions and fold to literals when the
layout is constructed: the type may be a primitive, a typedef, an enum, a pointer (`sizeof(uint8*)` is the pointer
width), or a complete fixed-size struct or union declared anywhere in the layout, and the field must be statically
placed. No other call is accepted, and nothing ever invokes user code. A qualified `enum.Member` names one member of
a named enum or flag as a constant.

Pointer stars follow the complete type name: `sizeof(unsigned int**)` is valid, but `sizeof(h*o)` is not.
The latter is a multiplication expression, not a type name. Use `sizeof(type)` with the intended storage type;
arithmetic can appear outside the call, such as `2 * sizeof(uint16)`.

## A nested field's value

A scalar read earlier in the same struct, or in any struct read before, is a variable under its bare name: after
`h hdr;` with `struct h { uint8 n; }`, `uint8 v[n]` counts with the `n` just read. When two nested fields have the
same member name, or a header is clearer when spelled as a path, name the field through the struct field that
holds it:

```c
struct h { uint8 n; uint8 pad; };
struct root {
    h a;
    h b;
    uint8 first[a.n];
    uint8 second[b.n];
};
```

`a.n` and `b.n` are the values of `n` inside `a` and `b`; a path may reach through several levels (`a.b.n`). The
head of a path is a scalar struct or union field of the layout (not an array element and not a pointer target),
and the value is published while that field is read, written, or measured, so every operation counts with the same
number. A path that names no such field is an undefined identifier when it is evaluated, like any other unknown
name. The `nested-references` fixture checks `v[hdr.n]` through parsing, addressing, and serialization.

## Which fields an expression can use

An expression computes an integer, so it can only read fields whose value is an integer. These are integer types,
characters (their character code), `bool` (1 or 0), enums (the member's number), and pointers (the stored address,
not the value it points to). A custom codec's field counts when the value it decodes is an integer.

Other fields have no single integer value: text (`char name[4]`, `utf8 name[8]`, `cstring`), arrays, structs, unions,
floating-point and fixed-point numbers, and UUIDs. Naming one of them in an expression is a layout error, reported
when the layout is built:

```c
struct root { char tag[4]; uint8 body[tag]; };
```

This fails with `Field 'tag' is text, but a layout expression uses it; layout expressions can only use integer
fields (integers, characters, bool, enums and pointers).` To branch on a four-character tag, read it as a number and
name the values with an enum:

```c
enum chunk : uint32 { IHDR = 0x52444849 };  // "IHDR" read little-endian
struct root { chunk tag; switch (tag) { case chunk.IHDR: { uint32 width; } } };
```

A name can belong to more than one field, for example a numeric `n` in one struct and a text `n` in a nested one. The
layout is then valid, and the value in effect is the last field read under that name. While that is the text field, an
expression that uses `n` fails with `'n' is text, but layout expressions can only use integer fields ...`; it never
falls back to an older value. The same holds when a `#define` shares the name with a non-integer field.

## Expressions use exact 128-bit integers

Every ordinary layout expression is evaluated in one number range: signed 128-bit integers, from -2^127 through
2^127 - 1. That range is wide enough to hold every value a field up to 64 bits can store, signed or unsigned. An
expression therefore sees the exact number a field holds. A kernel address such as `0xFFFF800000001000` is a large
positive number, not a negative one, and a `uint64` size is never mistaken for a small or negative count.

The arithmetic is *checked*, which means a result outside the range fails instead of wrapping around:

- addition, subtraction, multiplication, negation, and left shift fail when the result leaves the range;
- division and `%` truncate toward zero, and fail on a zero divisor or on -2^127 divided by -1;
- a shift count must be between 0 and 127 and is never silently masked;
- right shift is arithmetic, so a negative value stays negative;
- `~`, `&`, `|`, and `^` operate on the two's-complement bits of the 128-bit value; and
- comparisons and logical operators produce 0 or 1.

A literal is its exact value, in every base. `0xFFFFFFFF` is 4294967295, as it is in C, and `-0x80000000` is
-2147483648. A literal outside the range, such as a 32-digit hexadecimal `uint128` mask, is kept exactly (an enum or
a constant can still use it), but an expression that evaluates it fails:
`The literal 340282366920938463463374607431768211455 is outside the 128-bit range that layout expressions support.`

### Example: follow a pointer only when it is set

A linked list in a memory dump often stores the next node's address as a 64-bit number, with zero meaning "no next
node". This layout reads a payload only after a nonzero `next`:

```c
struct node {
    uint64 next;
    if (next != 0) {
        uint32 payload;
    }
};
```

With little-endian input `00 10 00 00 00 80 FF FF EF BE AD DE`, `next` sits at offset 0 and holds
18446603336221200384 (`0xFFFF800000001000`). The condition is true, so `payload` is read at offset 4 + 4 = 8 and
holds 3735928559 (`0xDEADBEEF`). The node is 12 bytes. With `next` equal to zero (`00 00 00 00 00 00 00 00`), the
condition is false, `payload` is absent, and the node is 8 bytes. Writing and updating evaluate the same condition
on the value being written, so both produce these same bytes.

### Each use checks its own range

The expression range is wider than most places a result can go. Each place checks the final value where it uses it,
and the failure names that value:

| Use | Accepted values | Failure for a larger value |
| --- | --- | --- |
| Array length or `@count` read from data | 0 through `MaxArrayElements` | `Array length 2147483648 exceeds MaxArrayElements (1000000).` |
| Fixed array length, typedef shape, bit width, `@align`, `@N` | a signed 32-bit integer (then each rule's own limits) | `The array length for data is 4294967296, which does not fit in a signed 32-bit integer.` |
| `if` condition | any value; nonzero is true | none |
| `switch` selector and `case` labels | any value in the range | none |

For example, in `struct root { uint32 count; uint8 items[count]; };` the bytes `00 00 00 80` give `count` the
valid `uint32` value 2147483648. The count is exact, so the read fails at `items` with the element limit and that
number, not with a negative length. A `switch (tag)` on a `uint64` tag can match `case 0xFFFFFFFFFFFFFFFF:`.

### Values outside the range

A `uint128` field at or above 2^127 is still read normally. It only fails when an expression selects it, and then
the failure names the field and its value:
`'n' is 170141183460469231731687303715884105728, which is outside the 128-bit range that layout expressions support.`
The same diagnostic applies when `?:`, `&&`, or `||` selects that field. An unselected operand is not evaluated:
`0 ? n : 0` is zero whatever `n` holds.

## Enum expressions use the full backing range

An enum may use signed or unsigned 8-, 16-, 32-, or 64-bit backing storage. Its member expressions use exact
`BigInteger` arithmetic and are checked against that declared range. Every member value fits the 128-bit expression
range, so an ordinary expression can use any member, such as `case big.High:` for a `uint64` member above 2^63.

```c
enum state : uint8 {
    None = 0,
    Ready = 1,
    Busy = Ready << 1
};
```

An omitted first value starts at zero; each later omitted value is the previous exact value plus one. Every result is
range-checked. `enum state : uint8 { Maximum = 255, Next }` fails because `Next` would be 256.

Bitwise enum operations use signed two's-complement `BigInteger` behavior, and shift counts must be less than the
backing width. Arithmetic never wraps.

## Non-integer defines and conditionals

A header's other `#define` forms are accepted so it can be pasted unchanged, but they are constants, not expression
inputs: `#define MAGIC "CD001"` (text), `#define RAW b"\x00\x01"` (bytes), `#define HAS_TAIL` (a bare name),
`#define SZ(x) ((x) + 1)` (a function-like macro kept as text, never expanded), and any line whose value is not an
integer expression at all (kept as text, as dissect keeps it). `CStruct.Constants` publishes every define by name
as a `LayoutConstant` whose `Kind` says which form it was; an integer define that could be evaluated without caller
variables is published as `Integer` - with its exact value, even one beyond the 128-bit expression range such as
`(1 << 127)` - and one that depends on a variable as `Expression`. Using a non-integer constant in a count, or a value
outside the 128-bit range in any expression, is a layout error; a define that names an unknown identifier and is
never used is not (a compiler ignores an unused macro too). `#ifdef`/`#ifndef` test whether a name has been defined
by any form so far, or listed in `CStructCompilationOptions.Defined`.

## Evaluation limits and reuse

The constructor prepares expression trees once, checks dependency cycles, and caches values that do not depend on
runtime variables. A caller override recalculates only definitions that depend on that name.

`CStructCompilationOptions` limits expression/dependency depth and total work so hostile source cannot create
unbounded preparation. Public callers supply only `IReadOnlyDictionary<string, int>` values; parser and expression
tree types remain internal.

Expression failures encountered while preparing a layout become `CStructLayoutException`. The same expression
evaluated against decoded or supplied values during an operation fails as that operation: `CStructReadException`
for reads, address lookups, and lengths; `CStructWriteException` for serialization and updates. If a runtime array
unexpectedly becomes huge or negative, check the supplied variable, byte order of any upstream count, and the
expression before increasing a safety limit.

## Comparisons and short-circuit predicates

For worked conditional examples, see [Choose fields with if and switch](../guides/conditional-fields.md).

Comparisons `==`, `!=`, `<`, `<=`, `>`, `>=` and logical `!`, `&&`, `||`
produce integer 0 or 1. Nonzero operands are true. `&&` skips its right operand
when the left is zero; `||` skips it when the left is nonzero. Inactive operands
may contain unavailable identifiers without causing an evaluation error.
Active expressions still enforce checked arithmetic, cycle, depth and work limits.

Precedence from highest to lowest: unary `! - ~`, multiplication/division,
addition/subtraction, shifts, relational comparisons, equality, bitwise `&`,
bitwise `|`, logical `&&`, logical `||`. Parentheses override precedence.
For example, `count != 0 && size / count > 2` does not divide when count is zero.
