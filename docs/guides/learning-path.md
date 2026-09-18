---
title: Learning path
description: Nine steps from what CStructSharp is to the reference material, one page each, with the three examples worth reading first.
---

# Learning path

Read these in order; each step is one page and builds on the previous one. Skip a step when its question is
already answered for you.

1. **What it is.** [Binary layout basics](binary-layout-basics.md): bytes, offsets, widths, byte order, padding,
   and what a `CStruct` adds to them.
2. **Install.** [Install and make a first parse](install-and-first-parse.md) - a console project and six bytes
   (JavaScript readers: the [Node.js and browser quick start](browser/index.md) instead).
3. **First parse.** [Continue with the header](header-next-steps.md): the same header created, changed, and read
   into a class, and what a short input does.
4. **Read.** [Read values and paths](reading-values.md), then [typed values](typed-values.md) when application
   code wants a C# class or a checked scalar; [choose an API](choosing-an-api.md) when the input is a stream,
   a span, or memory.
5. **Modify and write.** [Write and serialize values](writing-and-serialization.md) and
   [update existing data](updating-existing-data.md).
6. **Layout features.** [Strings](strings-and-encodings.md), [enums](enums.md), [unions](unions.md),
   [pointers](pointers.md), [conditional fields](conditional-fields.md), and
   [binary metadata types](binary-metadata-types.md); the
   [language tutorial](../language/tutorial/index.md) teaches the syntax behind them.
7. **Errors, files, and streams.** [Errors and recovery](errors-and-recovery.md),
   [variables, options, and limits](variables-options-and-limits.md), and the
   [binary file walkthrough](binary-file-walkthrough.md), which combines reading, validating, and patching a file.
8. **JavaScript.** [The JavaScript API](browser/api.md), [large files and streams](browser/large-data.md), and
   [deployment](browser/deployment.md).
9. **Reference and advanced.** The [layout-language manual](../language/index.md), the
   [C# API reference](../api/index.md), [performance](performance.md), [spans and buffer writers](spans-and-memory.md),
   [debug ranges and addresses](debug-data-and-addresses.md), and the memory-image series starting at
   [analyze mapped memory](memory-analysis.md).

## The three examples to read first

The repository ships thirty-three tested recipes, four browser lessons, and two starters. Read these three first;
together they cover reading, writing, updating, typed results, and a data-dependent shape:

| Example | What it shows |
| --- | --- |
| [`starter/Program.cs`](install-and-first-parse.md) (the README program) | Parse six bytes and read two typed members. |
| [`starter/Next.cs`](header-next-steps.md) | Serialize from a class, update one field in place, read into a class, and handle a truncated input with `TryReadValue`. |
| [Supply a runtime array count](../examples/recipes/runtime-payload.md) | A count-prefixed payload: a caller-supplied variable sizes the array, and `GetArrayLength` reports the count without decoding the payload. |

For JavaScript the equivalent is the standalone starter's [`app.js`](browser/index.md#complete-page-and-javascript),
which reads, writes, updates, and rereads the same header with `parse`, `serialize`, and `update`.

Background reading for anyone new to native data: [how C structs occupy memory](native-c-memory.md) and
[memory addresses and stored data](memory-and-stored-data.md). The [glossary](glossary.md) defines the terms the
guides use.
