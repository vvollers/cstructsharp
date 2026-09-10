---
title: Tutorial 2 — composites and overlapping storage
description: Combine an enum, fixed text, and a union while predicting every byte.
---

# Tutorial 2 — composites and overlapping storage

A struct can combine named types instead of containing only primitive integers. This lesson adds three shapes:

- an enum stores an integer and optionally gives that number a name;
- a fixed character buffer owns an exact number of code units; and
- a union gives several interpretations to the same storage.

The complete tested layout is:

```c
enum kind : uint8 {
    Text = 1,
    Numbers = 2
};

union payload_word {
    uint8 small;
    uint16 large;
};

struct record {
    kind type;
    char label[3];
    payload_word payload;
};
```

## Predict the packed layout

In packed mode:

1. `type` is a one-byte enum at offset 0.
2. `label` owns three one-byte `char` code units at offsets 1 through 3.
3. `payload` starts at offset 4. Its largest member is two bytes, so the union occupies offsets 4 and 5.

For bytes `01 41 42 00 34 12`:

| Byte range | Interpretation |
| --- | --- |
| `01` | enum member `Text`, numeric value 1 |
| `41 42 00` | fixed text `"AB\0"` |
| `34 12` | union raw storage; `small=52`, `large=4660` |

```text
offset   0        1        2        3        4        5
byte    01       41       42       00       34       12
field   type     label[0] label[1] label[2] payload────────
union                                            small=34
union                                            large=1234 (little-endian)
```

The union does not contain a tag that says which member is active. The separate `type` field may help the application
choose an interpretation, but CStructSharp keeps every bounded union view and the complete raw bytes.

## Run the composite example

[!code-csharp[Read and round-trip the composite record](../../examples/Program.cs#language-tutorial-composite-record)]

The scenario checks the enum name, fixed string, both union views, and a byte-for-byte round trip of the parsed
record. Keeping `UnionValue` is what makes the unchanged union storage safe to write back.

To create a new union, the application must select one member. CStructSharp clears the complete union storage and
writes that member so leftover bytes from a previous larger member cannot survive accidentally.

## Check aligned placement

In this particular record, aligned placement does not move `payload`: offset 4 is already divisible by the union's
two-byte alignment. The final size also remains six. This is a useful reminder that enabling alignment does not
always add padding.

To see padding, place a `uint8 marker` before a `uint32 value`. Packed placement starts `value` immediately; aligned
placement moves it to the next offset divisible by four.

Common mistakes are treating a fixed `char[3]` as a terminated scan, assuming the first union member is active, or
expecting enum storage to default to a C compiler's `int`.

Continue with [Tutorial 3 — runtime data and safe traversal](03-runtime-data.md). The
[declaration reference](../structs-unions-enums-typedefs.md) contains the complete rules.
