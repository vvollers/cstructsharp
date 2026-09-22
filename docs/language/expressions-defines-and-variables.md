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

## Counts and bit widths use signed 32-bit values

Ordinary layout expressions use checked `Int32` arithmetic:

- addition, subtraction, multiplication, negation, and left shift fail on overflow;
- division truncates toward zero and fails on zero or `int.MinValue / -1`;
- shift counts must be between 0 and 31 and are not silently masked;
- right shift is arithmetic;
- `~`, `&`, and `|` operate on two's-complement bits; and
- decimal literals must fit signed 32-bit range.

Base-prefixed literals may use any 32-bit bit pattern. `0xFFFFFFFF` therefore represents `-1`; a wider pattern fails.
A written sign is applied with checked arithmetic, so `-0xFFFFFFFF` is `1`, while `-0x80000000` overflows.

Array counts must resolve to a non-negative `Int32`. Bit widths have the additional requirement that they fit the
chosen storage unit.

A decoded field wider than that domain (`uint32` above `2147483647`, any `uint64` or `int64` beyond the range, a
128-bit integer) is still read normally. It only fails when an expression selects it, and then the failure names the
field and its value: `'n' is 4294967295, which is outside the 32-bit range that layout expressions support.`
The same diagnostic applies when `?:`, `&&`, or `||` selects that field. An unselected operand is not evaluated.

For example, consider this little-endian layout:

```c
struct root { uint32 count; uint8 items[1 ? count : 0]; };
```

The bytes `00 00 00 80` place `count` at offset 0 with value `2147483648`. That is a valid `uint32`, but it exceeds
the signed 32-bit limit for array lengths. Reading fails before `items`, with the field name and value in the
diagnostic. Using `0 ? count : 0` instead selects zero: the same four bytes produce an empty `items` array.

## Enum expressions use the full backing range

An enum may use signed or unsigned 8-, 16-, 32-, or 64-bit backing storage. Its member expressions therefore use
exact `BigInteger` arithmetic and are checked against that declared range rather than the ordinary `Int32` range.

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
variables is published as `Integer` - with its exact value even beyond the 32-bit expression domain, so a header's
`(1 << 63)` masks are published - and one that depends on a variable as `Expression`. Using a non-integer constant,
or a value outside the 32-bit domain, in a count is a layout error; a define that names an unknown identifier and is
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
