---
title: Learning path
description: Follow one header from installation through parsing, error recovery and updates, then choose generated-code or memory-analysis topics.
---

# Learning path

Start with one small header and keep using it until you can explain every byte. You need basic programming,
not experience with binary formats. A declaration describes where values live; it does not prove that an
entire file is valid. CStructSharp uses a C-like layout language, not a complete C compiler or your machine's
native C memory-layout rules.

## One complete first journey

Follow these steps in the same console project. The pages include complete programs; replace `Program.cs`
when instructed rather than combining several top-level programs in one file. JavaScript readers can follow
the equivalent [browser and Node starter](browser/index.md), which uses the same header and operations.

| Step | Do this | Check your understanding before continuing |
| --- | --- | --- |
| 1. Install and read | Follow [install and first parse](install-and-first-parse.md). Run the six-byte program. | Explain why `kind` is 2 and `length` is 6, and why the second field starts at byte offset 2. |
| 2. Predict and recover | Change the first byte to `03`, then try the short input in [the header continuation](header-next-steps.md#read-into-a-class-and-handle-missing-bytes). Restore complete input and read again. | Distinguish a valid changed value from missing bytes. A failed read does not supply a usable result. |
| 3. Create bytes | Run `Next.cs` in [the same continuation](header-next-steps.md#create-bytes). Compare `Created` with the original six bytes. | Explain that serialization creates bytes from values; a C# class's own memory layout does not decide the wire layout. |
| 4. Change one field | Follow [change existing bytes](header-next-steps.md#change-existing-bytes). Compare the original and updated bytes. | Identify the two-byte slot that can change and the four bytes that must stay unchanged. An update cannot insert space. |
| 5. Choose realistic limits | Read [variables, options and limits](variables-options-and-limits.md), then [errors and recovery](errors-and-recovery.md). | Explain why an element limit differs from a byte limit, and why successful header parsing does not validate a whole format. |

An **offset** counts bytes from an origin, starting at zero. The first example's origin is the beginning of its
input array. Passing a slice gives that slice its own zero; file offsets and mapped-memory addresses need the
explicit origins explained in [debug ranges and addresses](debug-data-and-addresses.md). Do not add a file offset
twice when turning a returned range into a UI selection.

Array limits count elements: ten `uint32` values are ten elements but forty bytes. String limits count encoded
bytes, not displayed characters. A total-read budget counts parser reads, including repeated pointer visits,
not the distance of a seek or the physical file's length. Raising a budget does not make a large result cheap.

After step 4, choose an optional branch:

- **Fixed layout in your source:** [generate the same header](generated/first-generated-layout.md). Compare its
  typed result with the runtime result. Generation moves layout work to build time; it is not required to parse.
- **Memory images with addresses:** start with [memory and stored data](memory-and-stored-data.md), then
  [analyze mapped memory](memory-analysis.md). First explain how a virtual address maps to bytes in a file.
  The executable example works with a synthetic image; it does not attach to or change a live process.
- **Inspect a real file:** use the [desktop inspector](browser/inspector.md). Its catalog describes supported
  header structures, not full decoders. Changing bytes, schema or settings invalidates the previous result;
  run again before trusting field ranges. Edits are temporary, with no export, and need a browser width of at
  least 1200 CSS pixels.

## Continue by topic

These pages extend the journey. Skip questions you can already answer.

1. **What it is.** [Binary layout basics](binary-layout-basics.md): bytes, offsets, widths, byte order, padding,
   and what a `CStruct` adds to them.
2. **Install.** [Install and make a first parse](install-and-first-parse.md) - a console project and six bytes
   (JavaScript readers: the [Node.js and browser quick start](browser/index.md) instead).
3. **First parse.** [Continue with the header](header-next-steps.md): the same header created, changed, and read
   into a class, and what a short input does.
4. **Read.** [Read values and paths](reading-values.md), then [typed values](typed-values.md) when application
   code wants a C# class or a checked scalar; [choose an API](choosing-an-api.md) when the input is a stream,
   a span, or memory; [trimming and Native AOT](trimming-and-native-aot.md) before a trimmed or AOT publish.
   - **Step 4b, generate.** When the layout is part of your source, the [generated code series](generated/index.md)
     turns it into typed classes at build time: start with
     [your first generated layout](generated/first-generated-layout.md) and read
     [runtime or generated?](generated/choosing-runtime-or-generated.md) for the decision.
5. **Modify and write.** [Write and serialize values](writing-and-serialization.md) and
   [update existing data](updating-existing-data.md).
6. **Layout features.** [Strings](strings-and-encodings.md), [enums](enums.md), [unions](unions.md),
   [pointers](pointers.md), [conditional fields](conditional-fields.md), and
   [binary metadata types](binary-metadata-types.md); the
   [language tutorial](../language/tutorial/index.md) teaches the syntax behind them.
7. **Errors, files, and streams.** [Errors and recovery](errors-and-recovery.md),
   [variables, options, and limits](variables-options-and-limits.md),
   [async reads, cancellation, and pipelines](async-and-pipelines.md) for streams that arrive while the program
   runs, and the [binary file walkthrough](binary-file-walkthrough.md), which combines reading, validating, and
   patching a file.
8. **JavaScript.** [The JavaScript API](browser/api.md), [large files and streams](browser/large-data.md), and
   [deployment](browser/deployment.md).
9. **Reference and advanced.** The [layout-language manual](../language/index.md), the
   [C# API reference](../api/index.md), [performance](performance.md), [spans and buffer writers](spans-and-memory.md),
   [debug ranges and addresses](debug-data-and-addresses.md), and the memory-image series starting at
   [analyze mapped memory](memory-analysis.md).

## Four examples to read first

Choose from the [tested recipe catalog](recipes/index.md) and the
[browser lessons](https://vvollers.github.io/cstructsharp/explorer/#lesson=header). Read these four examples first;
together they cover reading, writing, updating, typed results, and a data-dependent shape:

| Example | What it shows |
| --- | --- |
| [`starter/Program.cs`](install-and-first-parse.md) (the README program) | Parse six bytes and read two typed members. |
| [`starter/Next.cs`](header-next-steps.md) | Serialize from a class, update one field in place, read into a class, and handle a truncated input with `TryReadValue`. |
| [`starter/Generated.cs`](generated/first-generated-layout.md) | The same header as a `[CStructLayout]` class: typed `Parse`, `Serialize`, a view, and a typed setter, generated at build time. |
| [Supply a runtime array count](../examples/recipes/runtime-payload.md) | A count-prefixed payload: a caller-supplied variable sizes the array, and `GetArrayLength` reports the count without decoding the payload. |

For JavaScript the equivalent is the standalone starter's [`app.js`](browser/index.md#complete-page-and-javascript),
which reads, writes, updates, and rereads the same header with `parse`, `serialize`, and `update`.

Background reading for anyone new to native data: [how C structs occupy memory](native-c-memory.md) and
[memory addresses and stored data](memory-and-stored-data.md). The [glossary](glossary.md) defines the terms the
guides use.
