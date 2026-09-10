---
title: Binary data glossary
description: Plain-language definitions for the terms used in CStructSharp examples.
---

# Binary data glossary

| Term | Meaning in these guides |
| --- | --- |
| Byte | Eight bits; shown as two hexadecimal digits, such as `2A` |
| Offset | Position measured from the start of the input; the first byte has offset 0 |
| Width | Number of bytes a field occupies |
| Endianness or byte order | Order of bytes within a number; little-endian stores the least significant byte first |
| Padding | Unused bytes inserted between fields or at the end to satisfy alignment rules |
| Packed | Fields follow each other without alignment gaps |
| Layout | Text describing field names, types, and placement |
| Root | Named declaration selected as the starting point for an operation |
| Path | A selection such as `packet.header.kind` or `packet.items[1]` |
| Parse | Read bytes according to a layout and produce values |
| Serialize | Turn values into bytes according to a layout |
| Update | Replace a field in existing bytes without moving later fields |
| ABI | Application binary interface: a compiler and platform's rules for native data and calls |
| Span | A temporary view over a region of memory |
| POCO | Plain old CLR object; usually an ordinary C# class with properties |
| Base64 | A text representation of bytes, used by the browser result object |
| Envelope | The outer result object containing success, data, and error information |
| Round trip | Read data and write it back, checking which bytes and values are preserved |

Start with [binary layout basics](binary-layout-basics.md) to see these ideas applied to six bytes.
