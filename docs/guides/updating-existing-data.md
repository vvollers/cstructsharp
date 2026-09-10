---
title: Update existing data
description: Replace one value in an existing stream without moving or rebuilding surrounding data.
---

# Update existing data

`UpdateStream` is for binary data that already exists. You give it a path such as `root.value.flags`, and it locates
the corresponding byte range before writing the replacement.

Use an update when:

- the field's position and size are already defined by the stored layout;
- bytes before and after the field must remain where they are; and
- the destination is a readable, writable, seekable stream.

Do not use it to insert data, grow the stream, relocate following fields, or rebuild a variable-size object. Use
`Serialize` or `WriteStream` to create new output.

## Patch one nested field

The executable example uses:

```c
struct item {
    uint16 id;
    uint8 flags;
};

struct root {
    item value;
};
```

Two prefix bytes occur before the root. The caller sets the stream position to 2, so that position becomes the
starting point for `root`:

[!code-csharp[Patch one field and verify failed validation](../examples/Program.cs#api-reference-update-options)]

The initial stream is:

```text
absolute offset   0    1    2    3    4
bytes            EE   EE   34   12   01
                       └─ id ─┘  flags
root offset                       2
```

After updating `root.value.flags` to `0xA5`, the bytes are:

```text
EE EE 34 12 A5
```

The prefix and `id` are unchanged, the stream length is unchanged, and the caller-visible position returns to 2.

## How validation protects existing data

The method separates the work into two phases:

1. It follows the path and prepares the replacement in bounded temporary storage.
2. Only after path, type, range, shape, pointer, union, and configured-limit checks succeed does it copy the changed
   byte ranges to the destination.

In the example, replacing an eight-bit `flags` field with `999` cannot succeed. The method throws
`CStructWriteException`, and the test confirms that every destination byte and the stream position stayed unchanged.

This protection covers failures CStructSharp can detect before the commit. It cannot make every possible `Stream`
transactional. A disk, network, or custom stream may accept part of the final commit and then throw. In that case,
the accepted prefix may remain changed. If the destination needs storage-level atomicity, use a transactional storage
system or write a complete replacement elsewhere and swap it through a mechanism provided by that system.

## Paths, strings, unions, and pointers

An indexed path such as `root.items[2]` updates one array element with the element's codec. A terminated string can
be replaced only within its existing storage plan; the update does not shift later fields to make room.

A path selecting one union member changes only that member's byte range. Replacing a whole union clears its storage
before writing the selected member by default, preventing bytes from an older larger member from surviving.

For pointers, `.address` changes the stored pointer number. `.value` follows one pointer level and updates the target.
Pointer traversal has separate read limits because locating the destination may itself read untrusted data.

## Troubleshooting

If an update fails, check these in order:

1. The stream supports reading, writing, and seeking.
2. Its position points to the start of the root object.
3. The path begins with the correct case-sensitive root name.
4. Runtime array variables match the values used when the data was written.
5. The replacement has the exact scalar, collection, struct, enum, union, or pointer shape required by the path.
6. The replacement fits the existing extent and the configured traversal and write limits.

`InvalidPath` points to selection, `ReadFailed` or `ReadLimitExceeded` points to locating the stored target, and
`WriteFailed` or `WriteLimitExceeded` points to the replacement.

Read [Paths and selection](../language/paths-and-selection.md) for the complete path syntax and
[Writing and updating](../language/writing-and-updating.md) for every union, pointer, and budget rule.
