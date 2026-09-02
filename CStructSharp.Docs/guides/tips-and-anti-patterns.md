---
title: Practical tips and common mistakes
description: Review the assumptions that most often cause incorrect layouts, unsafe traversal, or damaged output.
---

# Practical tips and common mistakes

Use this checklist when a new integration almost works but produces the wrong values or positions.

| Mistake | Why it causes trouble | Better approach |
| --- | --- | --- |
| Constructing `CStruct` for every record | Repeats layout parsing and preparation | Build it once per layout configuration and reuse it |
| Copying a C struct without checking its ABI | Native widths, padding, bitfields, and pointers may differ | Translate the documented file format to explicit Portable types |
| Relying on constructor defaults for a persisted format | The intended byte order, placement, or pointer width is hidden | Pass all three format choices explicitly |
| Reading the whole root for one early field | Decodes values the caller does not need | Use `ReadValue` with a path |
| Sharing one stream across concurrent calls | Seeks and reads interfere | Give each operation a separate stream or lock the complete call |
| Treating a stored pointer as process memory | File coordinates are not safe native addresses | Use `Pointer`, addressing options, and traversal limits |
| Discarding `EnumValueResult` or union raw storage | Unknown numbers or overlapping bytes may be lost | Keep the rich result until faithful round trip is no longer needed |
| Treating `char[N]` as terminated text | Fixed capacity and scanning have different extents | Choose fixed or terminated syntax from the format specification |
| Expecting span, writer, or stream output to roll back | A late failure can leave a prefix | Stage through an owned array when all-or-nothing output matters |
| Raising every safety limit after a failure | Can hide a wrong count, offset, or byte order | Verify the format and change only the justified limit |
| Feeding arbitrary headers to the core | Includes, macros, qualifiers, and compiler ABI rules are not accepted | Normalize externally or write the supported Portable layout |

Before shipping a reader or writer, keep at least one known byte fixture and verify offsets, decoded values, output
bytes, failure categories, and starting/ending stream positions. A successful round trip by itself can reproduce the
same wrong assumption in both directions.

Use the [tested recipes](recipes/index.md) for working examples and
[Differences from C](../language/differences-from-c.md) when translating a C header.
