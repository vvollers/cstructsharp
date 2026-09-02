---
title: Read and write text fields
description: Choose fixed character buffers or terminated ASCII, UTF-8, and UTF-16 strings.
---

# Read and write text fields

Binary formats commonly store text in one of two ways:

- a fixed-capacity field reserves an exact number of character code units; or
- a terminated field continues until a special NUL or newline value.

Choose the layout form that matches the format. A fixed field and a terminated field may contain the same visible
text but occupy different byte ranges and have different update rules.

## Fixed character buffers

`char[N]` reserves exactly `N` one-byte code units. `wchar[N]` reserves exactly `N` UTF-16 code units, with two bytes
per code unit.

The executable example uses:

```c
struct label {
    char text[4];
};
```

[!code-csharp[Read and write a fixed four-byte text field](../examples/Program.cs#language-tutorial-fixed-text)]

Input `41 42 43 00` becomes the four-character C# string `"ABC\0"`. The trailing zero remains part of the fixed
buffer; CStructSharp does not stop scanning early.

Writing `"XY"` produces:

```text
58 59 00 00
 X  Y padding
```

The writer fills unused capacity with zero. A value longer than four code units fails instead of extending the field
or overwriting what follows it.

## Terminated strings

Use `cstring`, `ascii_string_zero`, `utf8_string_zero`, `string`, or another named terminated type when the format
ends text with NUL. Newline variants stop at LF instead. Empty character brackets such as `char name[]` and
`wchar name[]` are also terminated strings in this language; they are not general “use the rest of the file” arrays.

For:

```c
struct record {
    utf8_string_zero name;
    uint8 flags;
};
```

bytes `41 00 7E` contain name `"A"` followed by flags `126`. The terminator belongs to the encoded field but is not
part of the returned text. The `flags` field begins only after the terminator has been found.

Always set a sensible `MaxStringBytes` limit for untrusted data. A missing terminator would otherwise make the reader
scan farther than the format should allow.

## Encodings and byte order

- `char` is one raw byte-sized code unit. ASCII terminated handlers reject bytes outside valid ASCII.
- UTF-8 handlers decode strict UTF-8. Malformed sequences fail instead of inserting a replacement character.
- `wchar` and `string` use UTF-16. A neutral type follows the layout's byte order; `<` forces little-endian and `>`
  forces big-endian.
- A Unicode character outside the Basic Multilingual Plane uses two UTF-16 code units. A fixed `wchar[N]` count is a
  code-unit count, not necessarily the number of user-perceived characters.

CStructSharp does not detect a byte-order mark and does not use the machine's current locale. The layout must state
the encoding used by the format.

## Writing and updating safely

A string containing its own terminator is invalid for a terminated field because a later read could not distinguish
embedded data from the end marker.

A selected update to a terminated field cannot move the fields that follow it. The replacement must fit the existing
storage plan. If text needs to grow and the format allows following data to move, serialize a new containing object
instead of patching the old stream.

When text looks truncated or garbled, check:

1. fixed capacity versus terminated storage;
2. ASCII, UTF-8, or UTF-16;
3. little-endian versus big-endian UTF-16;
4. whether the length is measured in bytes or code units; and
5. whether the configured string limit includes the encoded terminator.

The full spelling table and exact failure rules are in
[Arrays, character buffers, and strings](../language/arrays-and-strings.md).
