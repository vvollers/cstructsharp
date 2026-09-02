---
title: Tutorial 3 — runtime data and safe traversal
description: Supply an array count, select one value, follow a stored pointer, and limit untrusted work.
---

# Tutorial 3 — runtime data and safe traversal

Not every field count can be known when the layout is constructed. A packet may carry a count in an earlier header,
or another protocol layer may supply it. Portable lets an array expression use an integer variable provided by the
application:

```c
struct packet {
    uint8 kind;
    uint8 payload[COUNT];
};
```

## Step 1: supply the count

The application passes `COUNT` in a read-only dictionary:

[!code-csharp[Read a runtime-sized payload and select one element](../../examples/Program.cs#language-tutorial-runtime-payload)]

With `COUNT = 3`, bytes `7F 10 20 30` mean:

```text
offset              0    1    2    3
bytes              7F   10   20   30
field              kind payload[0..2]
packet.payload[1]             20
```

The operation copies the variable entries before reading. Pass the same count to later address, length, serialize,
write, or update calls so they calculate the same positions.

`GetStructSizeInBytes("packet")` cannot return one fixed number because `COUNT` can change. The actual operation can
still measure or traverse the value after it receives the variable.

## Step 2: select only what you need

`ReadValue(bytes, "packet.payload[1]", variables)` returns the second payload byte, `0x20`. Paths start with the
case-sensitive root, use dots for fields, and use zero-based brackets for array elements.

A selected read avoids decoding unrelated later fields, but it still performs the work required to reach the target.
Counts, earlier terminated strings, alignment, and pointers can all affect its position.

## Step 3: follow a stored pointer

A pointer in binary data is a stored coordinate, not a process memory address:

[!code-csharp[Follow a one-byte stored pointer](../../examples/Program.cs#api-reference-pointer-read-options)]

```text
offset                  0        1
bytes                  01       2A
root.target.address     1
root.target.value  ───────────► 0x2A
```

The example uses one-byte pointer storage because the format says so. `.address` accesses the stored coordinate.
`.value` follows one declared pointer level.

Disable `DereferencePointers` when you need to inspect coordinates without reading targets. Use relative addressing
and an `Origin` only when the format defines offsets from a known base.

## Step 4: bound untrusted work

A count or pointer from an untrusted file can request excessive reading. `ReadOptions` limits:

- array elements;
- encoded string bytes;
- total physical bytes read;
- nested structs/unions;
- pointer depth; and
- fixed pointer-target size.

A malformed or truncated value reports `ReadFailed`. Reaching a configured ceiling reports
`ReadLimitExceeded`. Keep those cases separate: one describes invalid data, while the other says the operation was
stopped by policy.

Common mistakes are omitting `COUNT` on a later operation, using a pointer width from the current process instead of
the file format, treating address zero as a target, or raising limits before checking byte order and positions.

You can now use the [Portable cookbook](../cookbook/index.md). Keep
[Paths and selection](../paths-and-selection.md), [Pointers and addressing](../pointers-and-addressing.md), and
[Limits and diagnostics](../limits-and-diagnostics.md) as references.
