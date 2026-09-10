---
title: Tested recipes
description: Choose a complete executable recipe by task, difficulty, and platform.
---

# Tested recipes

Start with the [first C# program](../install-and-first-parse.md) or the [Node.js and browser quick start](../browser/index.md).
These 22 recipes include complete programs, exact byte/value checks, exercises, and answers. Browser links identify
related lessons; C# streams, spans, typed classes, and runtime-variable dictionaries have no direct browser equivalent.

Run one recipe from the repository root with the .NET 10 SDK:

```sh
dotnet run --project CStructSharp.Docs/examples/CStructSharp.Docs.Examples.csproj -c Release -- decode-header
```

Use `--list` instead of `decode-header` to list names. Omit arguments to run all 22 scenarios. Success ends with
`PASS all 22 scenarios`. Complete programs can also be copied into a console project with a matching package.

## Beginner

| Task | Result checked | Browser lesson |
| --- | --- | --- |
| [Read a fixed header](../../examples/recipes/decode-header.md) | kind 2, length 6; typed read succeeds and truncated read fails | [Open](https://vvollers.github.io/cstructsharp/explorer/#lesson=header) |
| [Write and update a header](../../examples/recipes/header-round-trip.md) | 03 00 06 00 00 00 after updating kind | [Open](https://vvollers.github.io/cstructsharp/explorer/#lesson=header-update) |
| [Diagnose the wrong byte order](../../examples/recipes/byte-order.md) | little-endian kind 2; big-endian kind 512 and length 100663296 | [Open](https://vvollers.github.io/cstructsharp/explorer/#lesson=byte-order) |
| [Read into a C# class](../../examples/recipes/map-poco.md) | Point with X -2 and Y 5 | See browser API limits |
| [Repair an invalid path](../../examples/recipes/invalid-path.md) | Header.kind fails; header.kind reads 2 | [Open](https://vvollers.github.io/cstructsharp/explorer/#lesson=invalid-path) |

## Intermediate

| Task | Result checked | Browser lesson |
| --- | --- | --- |
| [Read an item in a nested array](../../examples/recipes/nested-array.md) | packet.items[1].id is 2; exact four-byte round trip | [Open](https://vvollers.github.io/cstructsharp/explorer/#lesson=arrays) |
| [Explain padding in an aligned header](../../examples/recipes/aligned-header.md) | length 6 at offset 4; eight-byte round trip | [Open](https://vvollers.github.io/cstructsharp/explorer/#lesson=alignment) |
| [Read flags stored in one byte](../../examples/recipes/bit-flags.md) | 0B stores enabled 1, mode 5, reserved 0 | [Open](https://vvollers.github.io/cstructsharp/explorer/#lesson=bitfields) |
| [Read and write fixed text](../../examples/recipes/fixed-text.md) | ABC followed by a zero character; XY writes 58 59 00 00 | [Open](https://vvollers.github.io/cstructsharp/explorer/#lesson=text) |
| [Read zero-terminated text](../../examples/recipes/terminated-text.md) | 41 42 00 reads AB and writes back unchanged | [Open](https://vvollers.github.io/cstructsharp/explorer/#lesson=terminated-text) |
| [Combine text, an enum, and a union](../../examples/recipes/composite-record.md) | Text, AB with a zero character, and exact six-byte round trip | See browser API limits |
| [Connect a field to its bytes](../../examples/recipes/inspect-ranges.md) | uint16 occupies offsets 1 and 2; ResolveAddress returns 1 | [Open](https://vvollers.github.io/cstructsharp/explorer/#lesson=header) |
| [Patch a nested field in a stream](../../examples/recipes/patch-field.md) | EE EE 34 12 A5; invalid replacement preserves bytes and position | [Open](https://vvollers.github.io/cstructsharp/explorer/#lesson=nested-update) |

## Advanced

| Task | Result checked | Browser lesson |
| --- | --- | --- |
| [Preserve or select union storage](../../examples/recipes/preserve-union.md) | raw 34 12 round trip; selected small writes A5 00 | [Open](https://vvollers.github.io/cstructsharp/explorer/#lesson=union) |
| [Preserve an unknown enum number](../../examples/recipes/preserve-enum.md) | 4294967295 with no known name, unsigned 32-bit backing | [Open](https://vvollers.github.io/cstructsharp/explorer/#lesson=enum) |
| [Supply a runtime array count](../../examples/recipes/runtime-payload.md) | three payload values; second is 32; length lookup preserves position | See browser API limits |
| [Follow an absolute stored pointer](../../examples/recipes/follow-pointer.md) | stored address 1 points to value 42 | [Open](https://vvollers.github.io/cstructsharp/explorer/#lesson=pointer) |
| [Follow a pointer relative to an origin](../../examples/recipes/relative-pointer.md) | stored address 1 plus origin 1 reaches offset 2 and value 42 | See browser API limits |
| [Bound the work of a read](../../examples/recipes/bounded-read.md) | three-byte budget fails; six-byte budget reads length 6 | [Open](https://vvollers.github.io/cstructsharp/explorer/#lesson=limits) |
| [Read inside a larger stream](../../examples/recipes/positioned-stream.md) | length address 4; inspection preserves Position 2; length reads 6 | See browser API limits |
| [Reuse a layout with different output storage](../../examples/recipes/round-trip.md) | 34 12 A5 from array, span, and buffer writer; unused capacity preserved | See browser API limits |
| [Inspect and edit a complete binary file](../../examples/recipes/edit-file.md) | 435301020100100200A5; truncated and excessive-count fixtures rejected | See browser API limits |

## Larger walkthroughs

- [Inspect and edit a binary file](../binary-file-walkthrough.md) validates a signature, version, and count before patching a record.
- [Build a browser inspector](../browser/inspector.md) adds local file input, a field map, and a verified download.
