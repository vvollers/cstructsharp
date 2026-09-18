---
title: Library guides
description: Learn CStructSharp through practical reading, writing, inspection, and update tasks.
---

# Library guides

These guides start with the job you need to do and then explain which part of CStructSharp fits that job. You don't
need to understand compiler construction or native memory layout before you begin.

If this is your first binary-format library, follow the [learning path](learning-path.md): nine steps, one page
each, from what a layout is to the reference material, plus the three examples worth reading first. Its first
steps are:

1. [Binary layout basics](binary-layout-basics.md) explains bytes, offsets, byte order, padding, and the role of a
   `CStruct`.
2. [Install and make a first parse](install-and-first-parse.md) turns a six-byte header into C# values.
   Continue with [writing, updating, and a complete C# class](header-next-steps.md).
3. [Read values and paths](reading-values.md) shows how to read either a whole object or one nested field;
   [typed values](typed-values.md) maps a layout to a C# class; [choose an API](choosing-an-api.md) compares
   stream, span, memory, and output overloads.
4. [Write and serialize values](writing-and-serialization.md) creates new binary data, and
   [update existing data](updating-existing-data.md) changes one field without rebuilding the surrounding object.

The data-shape guides cover [strings](strings-and-encodings.md), [enums](enums.md), [unions](unions.md),
[pointers](pointers.md), [conditional fields](conditional-fields.md) (with per-item decisions, variable scope, and
browser exercises), and the [binary metadata types](binary-metadata-types.md). The operational guides cover
[errors](errors-and-recovery.md) (including the common-mistakes checklist),
[runtime variables and limits](variables-options-and-limits.md), [performance](performance.md) (including layout
reuse and ownership), [spans and buffer writers](spans-and-memory.md), and
[byte ranges and addresses](debug-data-and-addresses.md). The [binary file walkthrough](binary-file-walkthrough.md)
combines the concepts in a larger task.

For JavaScript, take the separate [Node.js and browser quick start](browser/index.md); the
[JavaScript API](browser/api.md) covers browser results, debug ranges, and compiled-layout reuse. Use the
[glossary](glossary.md) when a term is new.

For a deeper foundation, read [how C structs occupy memory](native-c-memory.md) and
[memory addresses and stored data](memory-and-stored-data.md). These explain alignment calculations, native C
arrays and pointers, platform ABIs, byte order, and text encodings, with exercises and worked answers.
They assume introductory programming knowledge, not operating-systems or compiler courses.

For captured or mapped memory, follow the [memory-analysis guide series](memory-analysis.md). It covers sources,
metadata layouts, pointers, bounded traversal, offline editing, and reliability through executable byte-level examples.

Use the [tested recipes](recipes/index.md) when you already know the result you want. Use the
[layout-language manual](../language/index.md) when you need to choose syntax or predict exact byte positions.
