---
title: Browser adapter interface
description: Understand the separately versioned optional WebAssembly adapter.
---

# Browser adapter interface

This page is for maintainers of the optional browser integration. If you call CStructSharp directly from .NET, use
the [managed API reference](index.md) instead.

The WebAssembly bridge is a small adapter over the managed library. It lets browser code request a parse,
serialization, or update without exposing every .NET type to JavaScript. It is versioned separately from the NuGet
API because JSON passed between browser code and WebAssembly has different compatibility concerns from a C# method
call.

The reviewed browser description uses browser interface version 9. It records eleven managed entry points, which
fall into three groups:

- On the calling thread: `ParseBytes` (parses byte inputs, with debug ranges when its `debug` argument is true),
  `Serialize`, `UpdateStream`, `TakeOutput`, `GetVersion`, and `GetStaticPlan` (describes a fully fixed root's
  member offsets and codecs so the adapters can read such layouts in JavaScript).
- In the source worker: `ParseSource` and `ResolveAddress` read a large source page by page, and the retained-layout
  exports `InitializeCompiledLayout`, `ParseCompiledSource`, and `ResolveAddressCompiled` back the `compile`
  operation. A compiled layout's `serialize` and `update` run on the calling thread through `Serialize` and
  `UpdateStream`.

Every entry point except `TakeOutput` returns the same outer object as JSON text, called an *envelope*, and reports
failures inside it rather than by throwing. The envelope has seven members, always in this order:
`contractVersion` (9), `operation`, `success`, `root` (the root or path the operation selected, or the `root`
option a write echoes), `data`, `debug`, and `error`. `debug` lists `{ start, end, path, type, value }` byte ranges
after a parse with debug ranges and is empty otherwise. On failure `data` is null and `error` is
`{ code, message, path, offset, member, memberType, line, column }`. The `message` is the library's own diagnostic
verbatim; the `redactDiagnostics` option keeps only the category `code` and its curated text and clears `path`,
`member`, and `memberType`, for pages that must not echo layout text, values, or paths.

What `data` holds on success depends on the operation:

| Operation | Export | `data` on success |
| --- | --- | --- |
| `parse` | `ParseBytes`, `ParseSource`, `ParseCompiledSource` | The selected value itself: the root struct's members by name, or the union, array, or scalar a path selects |
| `resolveAddress` | `ResolveAddress`, `ResolveAddressCompiled` | The byte position, a number or a decimal string beyond 2^53 - 1 |
| `compile` | `InitializeCompiledLayout` | An empty object; `root` names the layout's default root |
| `serialize`, `update` | `Serialize`, `UpdateStream` | `{ byteLength }`: the bytes wait for `TakeOutput` |
| `version` | `GetVersion` | `{ version }`, the managed library version text |
| `staticPlan` | `GetStaticPlan` | `{ root, plan }`, or null when the root has no static plan |

The `version` and `staticPlan` envelopes are read by the JavaScript adapter itself and never reach callers of the
public API; `getVersion()` returns the version text.

Binary data crosses the boundary as a native `byte[]`/`Uint8Array`, never Base64 text. A write's bytes cannot sit
inside the JSON envelope, so they wait in managed memory: directly after a successful `Serialize` or `UpdateStream`
envelope, the adapter calls `TakeOutput`, which returns those bytes once and clears them. Starting any other envelope
also clears them, and `TakeOutput` throws when nothing is pending. This hand-over is safe because the .NET WebAssembly
runtime runs managed code on one thread per runtime instance, and the adapter calls the two exports back to back. The
public `serialize` and `update` results therefore carry the bytes as `data`, in the same envelope shape as every
other operation.

Values inside `data` keep their tagged shapes across versions: an enum is `{ kind: "enum", enum, name, value }`, and
a value read from a `flag` declaration adds `names` (the set members) and `remainder` (bits no member covers); a
union is `{ kind: "union", union, rawStorage, members, selectedMember }`; a pointer is
`{ kind: "pointer", address, depth, dereferenced, value }`. A promoted (anonymous) struct or union member's fields
sit directly on the parent object, and a `_` padding field never appears. Integers beyond JavaScript's exact range
and 64-bit enum values are decimal strings; a non-finite float is `"NaN"`, `"Infinity"`, or `"-Infinity"`.

Options are one camelCase object per call: the compiler settings (`aligned`, `littleEndian`, `pointerSize`,
`bitfieldPacking`, `bitfieldAllocation`, `cLongWidth`, the definition limits), `root`, and the read, write, or
update settings of the operation (`trimFixedText`, `unknownMembers`, the addressing and budget options).
The `root` option selects a declaration; successful `data` contains its fields directly. Use `resolveAddress`
to locate a selected field without decoding its value. Release history and migrations belong in the
[changelog](https://github.com/vvollers/cstructsharp/blob/main/CHANGELOG.md).

The complete list of accepted options and error categories is in the
[machine-readable browser description](../../contracts/api/browser/contract.json). Use that JSON file when changing
or testing the adapter; this page is an orientation guide, not a substitute for the exact field list.

## Compatibility and testing

Managed and browser compatibility are reviewed independently. Changing a managed method does not automatically
approve a change to the browser JSON. A browser-facing change must increase the interface version and update the
saved browser description as part of the same reviewed change; `node tools/quality/browser-contract.mjs`
checks the description against the canonical declarations (`packages/cstructsharp/index.d.ts`), the apps' shared
contract module, the managed bridge (its complete export list, envelope writer, options, and error codes), and the
JavaScript modules that bind the exports. The JavaScript package states no contract version of its own: every
envelope it returns carries the version the managed bridge wrote.

Routine documentation validation checks this page against tracked sources and saved data. It deliberately does not
restore, build, or test `apps/explorer` or `CStructSharpWeb.Wasm`, because those projects are expensive and
optional. Their implementation and browser tests run together during the repository's final Web integration phase.
