---
title: Choose fields with if and switch
description: Learn when conditions run, how each array item gets its own decision, and which variables an expression can use.
---

# Choose fields with if and switch

A binary record sometimes contains different fields depending on an earlier value.
For example, a message's `tag` might say which payload follows it. A **conditional
group** is an `if`/`else` or `switch` that chooses those fields. You only need to
know basic structs, arrays, and integer expressions to follow this guide.

Unlike an ordinary C function, the blocks contain field declarations, not statements
that assign values or call functions. The parser reads bytes for the selected fields.
Inactive fields consume no bytes and are absent from the result; they are not zero-filled.

## Every array item makes its own decision

Start with the [per-item decision lesson](https://vvollers.github.io/cstructsharp/explorer/#lesson=conditional-decisions).
It uses this layout with alignment disabled and little-endian byte order:

```c
struct entry {
    uint8 tag;
    int8 some_parameter;
    if (some_parameter * 20 > 10) {
        uint8 high;
    } else {
        uint8 low;
    }
    switch (tag) {
        case 1: { uint8 first; }
        case 2: { uint8 second; }
        default: { uint8 other; }
    }
};
struct root { entry items[3]; };
```

The lesson starts with these hexadecimal bytes:

```text
01 00 0a 0b   02 01 14 15   03 ff 1e 1f
```

Each item happens to occupy four bytes because every alternative here is one byte.
`ff` represents -1 in the signed `int8` field.

| Item | tag | some_parameter | Calculation | Selected fields and decimal values |
| --- | --- | --- | --- | --- |
| `items[0]` | 1 | 0 | `0 * 20 > 10` is false | `low = 10`, `first = 11` |
| `items[1]` | 2 | 1 | `1 * 20 > 10` is true | `high = 20`, `second = 21` |
| `items[2]` | 3 | -1 | `-1 * 20 > 10` is false | `low = 30`, `other = 31` |

For each item, the parser reads `tag` and `some_parameter`, evaluates the `if`,
reads its selected field, evaluates the `switch`, and reads its selected field.
The next item starts this sequence again with its own values.

Try changing only the first byte from `01` to `02`. Predict the result before
running: the first item's `first` member becomes `second`, still containing 11.
The other items stay the same. Reset, then change the first item's parameter byte
from `00` to `01`: its `low` member becomes `high`, still containing 10.

In other layouts, alternatives can have different sizes. Do not assume a fixed
array stride or change a tag without also supplying the bytes its new branch needs.
See [alignment and padding](../language/layout-alignment-and-padding.md).

## What "evaluate once" means

The decision is made **once per group, per struct instance, per operation**.
Compiling a layout prepares its expressions and case lookup table. It does not
choose one runtime branch for every future parse or every item in an array.

```c
uint8 tag;
if (tag == 1) {
    uint8 a;
    uint8 b;
    uint8 c;
}
```

The parser evaluates `tag == 1` once for this group, then uses that decision for
`a`, `b`, and `c`. It does not repeat the comparison before each field. The decision
stays fixed until that group finishes, even if reading nested data changes a value
in the expression environment. A later or nested group makes its own decision
when reached, using the values available at that point.

A new array item or a new parse starts with fresh decisions. This also holds when
reusing one C# `CStruct` or a JavaScript handle returned by `compile()`.
Decisions are operation state, not mutable state shared by the compiled layout.

## Which value does a name refer to?

Here, "parameter" just means a name used in an expression. It can come from an
earlier decoded integer field, a `#define`, or a caller-supplied integer variable.
Caller variables are copied for the operation and override matching definitions.
For example, `if (MODE == 1)` can use `variables: new Dictionary<string, int>
{ ["MODE"] = 1 }` passed to a C# parse operation, provided the struct has no local
field named `MODE`.

In a struct containing conditional fields, its local declarations take precedence
over outer or caller values **from the start of that struct**. A local name becomes
usable only after its field has actually been read. Declaring a field later does
not make its future bytes available earlier.

```c
struct entry {
    uint8 tag;
    if (tag) { uint8 count; }
    if (count > 0) { uint8 payload[count]; }
};
struct root { entry items[2]; };
```

Try the [unavailable-local lesson](https://vvollers.github.io/cstructsharp/explorer/#lesson=conditional-scope).
With bytes `01 01 2a 00`, the first item has `count = 1` and payload 42.
The second item has `tag = 0`, so it never reads `count`. Its second condition
raises a layout error. It cannot borrow the first item's count, even if the caller
also supplied a variable named `count`.
The browser lesson also defines `count` as 99 before the struct; that definition
cannot replace the unavailable local either. The C# example checks a caller override too.

Change the second condition to `if (tag != 0 && count > 0)` and run again.
For the second item, the left side is false, so `count` is never evaluated.
The parse succeeds and the second item contains only `tag`.

Named nested structs cannot replace the enclosing conditional struct's own local
fields for later conditions. If a parent has `tag = 1` and `child.tag = 0`, a later
parent condition on `tag` still sees 1. Anonymous structs promote their member names
into the enclosing result; those promoted locals follow the same unavailable-value
rules. Conditions use the layout's simple variable names, not general C member-access
expressions such as `child.tag`.

Branch braces do not introduce separate field namespaces. Two alternatives cannot
both declare `value` directly. Use distinct names, or named structs such as
`short_record` and `long_record`, each with its own `value` member.

## Calculations and skipped conditions

`if (some_parameter * 20 > 10)` evaluates the whole calculation when the group is
reached. Multiplication happens before comparison. Comparisons return 0 or 1;
an `if` treats any nonzero integer as true. Parentheses can make your intention clear.

Layout expressions use checked signed 32-bit integer arithmetic. They are not
floating-point calculations: division truncates toward zero, and overflow or division
by zero produces an error. Missing names and expression depth/work limits also
produce errors when the expression is evaluated.

`&&` and `||` evaluate only as much as needed. For example,
`count != 0 && size / count > 2` never divides when count is zero.
An entire nested condition is also skipped when its outer branch is inactive:

```c
struct root {
    uint8 tag;
    if (tag) {
        if (missing > 0) { uint8 value; }
    }
    uint8 tail;
};
```

In the [nested-condition lesson](https://vvollers.github.io/cstructsharp/explorer/#lesson=conditional-nesting),
bytes `00 09` produce `tag = 0, tail = 9`. The unavailable name `missing` causes
no error because its condition is never reached. Change `00` to `01` to see the
missing-variable error. This does not make invalid field types or duplicate field
names legal: the layout still validates declarations in inactive alternatives.

## Switch and write rules to remember

`switch` evaluates its selector once on entry and selects one matching case.
Every case needs a braced field block. There is no fall-through and no `break`.
Without a matching case, `default` is selected; without a default, the group
contributes no fields.

Case labels are distinct compile-time integer constants. They can use `#define`
values and constant calculations, but not runtime fields. Labels `1` and `1 + 0`
are duplicates. Caller overrides do not change already compiled case labels.

Serialization makes decisions from the supplied values and rejects supplied inactive
members. An in-place update cannot change a selected branch, even if both branches
have the same byte width. Serialize a new buffer to change that layout.

Continue with the [tagged-record write lesson](https://vvollers.github.io/cstructsharp/explorer/#lesson=conditional-records-serialize),
the [executable C# examples](../examples/recipes/conditional-records.md), and the
[expression reference](../language/expressions-defines-and-variables.md).
