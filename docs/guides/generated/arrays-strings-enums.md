---
title: Arrays, strings, and enums
description: How fixed and count-sized arrays, text encodings and TrimFixedText, enums and flags become C# types in generated code, and what happens with an unnamed enum value.
---

# Arrays, strings, and enums

The [layout language](../../language/index.md) has arrays, several kinds of text, enums, and flags. This lesson shows
the C# type each one becomes and what the generated reader does with it.

## The layout and its bytes

[!code-csharp[A layout with arrays, text, and enums](../../examples/GeneratedExamples.cs#generated-arrays-strings-enums-class)]

[!code-csharp[Reading it](../../examples/GeneratedExamples.cs#generated-arrays-strings-enums)]

## Arrays

`uint16 values[count]` becomes `ushort[] Values`. The count is an expression over an earlier member, so the
generated reader evaluates it after reading `count` - the same expression rules as the runtime, through the
`CStructSharp.Generated.Expressions` helpers - checks it against `ReadOptions.MaxArrayElements`, then reads all the
elements with one bulk decode. A fixed array (`uint16 values[4]`) is the same type with a constant count; a
`uint8 data[n]` is a `byte[]`; an array of structs is an array of the generated class; a two-dimensional array
`uint8 grid[2][3]` is a jagged array `byte[][]`.

## Text

| Layout | C# | What the reader does |
| --- | --- | --- |
| `char name[8]` | `string` | Reads 8 one-byte characters. Trailing NULs stay unless `ReadOptions.TrimFixedText` is set - the bytes are the data. |
| `wchar name[8]` | `string` | Reads 8 UTF-16 code units in the layout's byte order and validates them. |
| `utf8 label[6]` | `string` | A buffer of 6 *bytes* decoded as UTF-8 (`utf16le[N]`, `latin1[N]`, `cp437[N]` likewise); the byte count must not exceed `MaxStringBytes`. |
| `cstring note` | `string` | Reads to the terminator, which is consumed but not part of the value; `string` and `wchar *` are the UTF-8 and UTF-16 forms. |

Every rule the runtime applies - the byte limits, an unterminated string, an invalid byte sequence - applies in the
generated reader with the runtime's message.

## Enums and flags

`enum color : uint8 { ... }` becomes `public enum Color : byte { Red = 1, Green = 2, Blue = 3 }` and a `flag` a
`[Flags]` enum. The property type is the C# enum, so `sample.Colour == Samples.Color.Green` compiles and shows up in
IntelliSense.

A stored value the enum does not name is not an error: `Samples.Parse(bytes).Colour` is `(Samples.Color)9`, a plain
cast of the number, exactly as C# treats any enum. The runtime API represents the same case as an `EnumValueResult`
with a `null` name; the generated class does not need that wrapper because the C# enum already carries the number.

## Check yourself

1. Why does `"png"` come back as `"png\0\0\0\0\0"`?
2. What limits how large `values[count]` may be?
3. What is the value of `sample.Colour` when the byte is `9`?

<details>
<summary>Answers</summary>

1. `char name[8]` is eight bytes of data; the reader returns them unless `TrimFixedText` asks it to drop trailing NULs.
2. `ReadOptions.MaxArrayElements` (one million by default) and the remaining bytes.
3. `(Samples.Color)9`: an enum value with no name, printed as `9`.

</details>

## Exercise

Change `char name[8]` to `wchar name[4]` and adjust the bytes so `Name` is still `"png"` followed by one NUL
(in UTF-16 the layout's little-endian order).

<details>
<summary>Solution</summary>

`wchar name[4]` occupies 8 bytes: `70 00 6E 00 67 00 00 00`. The property is still a `string`; the reader decodes
four UTF-16 code units and, without `TrimFixedText`, returns `"png\0"`.

</details>
