---
title: Library guides
description: Learn CStructSharp through practical reading, writing, inspection, and update tasks.
---

# Library guides

These guides start with the job you need to do and then explain which part of CStructSharp fits that job. You don't
need to understand compiler construction or native memory layout before you begin.

If this is your first binary-format library, read these pages in order:

1. [Binary layout basics](binary-layout-basics.md) explains bytes, offsets, byte order, padding, and the role of a
   `CStruct`.
2. [Install and make a first parse](install-and-first-parse.md) turns a six-byte header into C# values.
3. [Choose an API](choosing-an-api.md) compares dynamic, typed, stream, memory, and output APIs.
4. [Read values and paths](reading-values.md) shows how to read either a whole object or one nested field.
5. [Write and serialize values](writing-and-serialization.md) creates new binary data.
6. [Update existing data](updating-existing-data.md) changes one field without rebuilding the surrounding object.

The remaining guides cover data shapes and operational concerns:

- [C# type mapping](typed-values.md), [strings](strings-and-encodings.md), [enums](enums.md),
  [unions](unions.md), and [pointers](pointers.md);
- [spans and buffer writers](spans-and-memory.md), [byte ranges and addresses](debug-data-and-addresses.md), and
  [runtime variables and limits](variables-options-and-limits.md);
- [errors](errors-and-recovery.md), [ownership and concurrency](concurrency-and-ownership.md), and
  [performance](performance.md).

Use the [tested recipes](recipes/index.md) when you already know the result you want. Use the
[layout-language manual](../language/index.md) when you need to choose syntax or predict exact byte positions.
