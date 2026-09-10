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

`WORD_COUNT` is known when `CStruct` is constructed, so this array has a fixed count of four.

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
`-`/`~`, and these binary operators:

| Precedence, high to low | Operators |
| --- | --- |
| Unary | `-`, `~` |
| Multiply/divide | `*`, `/` |
| Add/subtract | `+`, `-` |
| Shift | `<<`, `>>` |
| Bitwise AND | `&` |
| Bitwise OR | `\|` |

Operators on the same row are evaluated left to right. Function-call-looking syntax is recognized only so the
constructor can report that it is unsupported; it never invokes user code.

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

## Evaluation limits and reuse

The constructor prepares expression trees once, checks dependency cycles, and caches values that do not depend on
runtime variables. A caller override recalculates only definitions that depend on that name.

`CStructCompilationOptions` limits expression/dependency depth and total work so hostile source cannot create
unbounded preparation. Public callers supply only `IReadOnlyDictionary<string, int>` values; parser and expression
tree types remain internal.

Expression failures encountered while preparing or applying a layout become `CStructLayoutException`. If a runtime
array unexpectedly becomes huge or negative, check the supplied variable, byte order of any upstream count, and the
expression before increasing a safety limit.
