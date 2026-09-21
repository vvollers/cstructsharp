---
title: Writing and updating with generated code
description: Serialize, Write, the typed Update setters, path updates through the runtime, and the validation errors a write can raise.
---

# Writing and updating with generated code

Reading turns bytes into a class; writing is the reverse, with the same layout deciding where every byte goes.
The generated writer validates exactly what the runtime writer validates and says the same things when it refuses.

[!code-csharp[Serialize, Write, and Update](../../examples/GeneratedExamples.cs#generated-writing-updating)]

## Three writers

- `Wire.Serialize(header)` returns a new `byte[]` of exactly the value's size.
- `Wire.Serialize(header, span)` writes into memory you own and returns the number of bytes; a destination that is
  too small fails with the runtime's capacity message before anything is written past it.
- `Wire.Write(stream, header)` writes the bytes to a stream at its current position; `Wire.WriteAsync(stream, header)`
  does the same with one awaitable write, and `Wire.ParseAsync(stream)` is the awaitable reader (the
  [sequences and TryParse](sequences-and-try-parse.md) lesson covers both).

Every struct also has `Serialize<Name>`/`Write<Name>` overloads that accept a `variables` dictionary for the
layout's free identifiers.

## What a write validates

The class's types already rule out most mistakes a dictionary write could make - there is no way to put a string
into `Length`. What remains are the constraints the types cannot express, and each has the runtime's text:

| Situation | Message |
| --- | --- |
| A fixed array with the wrong number of elements | `Array length mismatch for values: expected 4, got 3.` |
| A string longer than its fixed buffer | `String is too long for name: 9 > 8.` |
| A terminated string containing its terminator | `String value contains its encoded terminator.` |
| An `int24`/`uint48` value outside its range | `Value is outside the int24 range.` |
| A bitfield value that does not fit its width | `Bitfield value for 'version' exceeds the unsigned 4-bit range.` |
| A member of an inactive conditional arm supplied | `Inactive conditional field supplied: code` |
| A union with neither `SelectedMember` nor `RawStorage` | `A whole union write requires SelectedMember or RawStorage: payload` |

Padding bytes are written as zeros, a struct's aligned tail included.

## Typed setters

`Wire.Update.Kind(bytes, 8)` stores one value at the offset the generator computed at build time and touches
nothing else. A setter exists for every scalar whose position is fixed - nested struct members appear as
`Update.Header.Length(...)`, a fixed array takes an element index, a bitfield merges its bits into the storage
unit. A member placed after a runtime-sized array has no setter because its offset depends on the data.

`Wire.UpdatePath(bytes, "header.length", 2048u)` covers everything else: it runs the runtime's path update on
`Wire.Layout`, with the runtime's path grammar and options.

## Check yourself

1. Why does `Serialize(header, span)` return an `int`?
2. Which members get typed setters?
3. What does a write do with the padding between fields of an aligned struct?

<details>
<summary>Answers</summary>

1. It writes into memory you own, which may be larger than the value; the return value says how many bytes it used.
2. Scalars (and fixed arrays of them) whose offset the compiler fixed at build time; anything after a runtime-sized member goes through `UpdatePath`.
3. It writes zeros.

</details>

## Exercise

Give `Wire` a `char tag[4]` member after `length`, serialize a header with `Tag = "abcde"`, and observe the
failure. Then write `"ab"` and inspect the bytes.

<details>
<summary>Solution</summary>

`"abcde"` fails with `String is too long for tag: 5 > 4 (field 'tag' (char), in 'header', offset 6).`. `"ab"`
writes `61 62 00 00`: the buffer is filled to its declared size with NULs.

</details>
