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

The reviewed `browser-rc1` description targets package candidate `0.2.0-preview` and uses browser interface version
8. It records seven managed entry points:

- `GetVersion`
- `ParseWithDebug` and `ParseBytes` (parses byte inputs on the calling thread; the latter without debug ranges)
- `Serialize`
- `UpdateStream`
- `ResolveAddress`
- `GetStaticPlan` (describes a fully fixed root's member offsets and codecs as JSON so the adapters can read such
  layouts in JavaScript; an adapter treats its absence as "feature not available")

The retained-layout exports (`InitializeCompiledLayout`, `ParseCompiledSource`, `SerializeCompiled`,
`UpdateCompiled`, `ResolveAddressCompiled`) and `ParseSource` back the worker-side `compile` operation and large
sources; they exchange the same envelope.

Binary data crosses the boundary as a native `byte[]`/`Uint8Array`, never Base64 text. Every JSON-returning export
returns the same outer object, called an *envelope*: `contractVersion` (8), `operation` (`parse`, `serialize`,
`update`, `resolveAddress`, or `compile`), `success`, `root` (the root or path the operation selected), `data`,
`debug`, and `error`. On success `data` is the selected value itself - the root struct's members by name, or the
union, array, or scalar a path selects - with no wrapper object; `debug` lists `{ start, end, path, type, value }`
byte ranges after `ParseWithDebug` and is empty otherwise. `Serialize`/`UpdateStream` return the encoded bytes
directly on success; there is no envelope object left to carry an error alongside a native byte-array payload, so
they report failure by throwing instead. The thrown JS `Error`'s message is the same JSON-serialized error shape
the envelope's `error` field uses - `{ code, message, path, offset, member, memberType, line, column }` - so the
JavaScript adapters rebuild an identical envelope either way. The `message` is the library's own diagnostic
verbatim; the `redactDiagnostics` option keeps only the category `code` and its curated text and clears `path`,
`member`, and `memberType`, for pages that must not echo layout text, values, or paths.

Values inside `data` keep their tagged shapes across versions: an enum is `{ kind: "enum", enum, name, value }`, and
a value read from a `flag` declaration adds `names` (the set members) and `remainder` (bits no member covers); a
union is `{ kind: "union", union, rawStorage, members, selectedMember }`; a pointer is
`{ kind: "pointer", address, depth, dereferenced, value }`. A promoted (anonymous) struct or union member's fields
sit directly on the parent object, and a `_` padding field never appears. Integers beyond JavaScript's exact range
and 64-bit enum values are decimal strings; a non-finite float is `"NaN"`, `"Infinity"`, or `"-Infinity"`.

Options are one camelCase object per call: the compiler settings (`aligned`, `littleEndian`, `pointerSize`,
`bitfieldPacking`, `bitfieldAllocation`, `cLongWidth`, the definition limits), `root`, and the read, write, or
update settings of the operation (`trimFixedText`, `unknownMembers`, the addressing and budget options). Version 8
replaced the PascalCase envelope of versions 1-7, dropped the root wrapper around `data`, renamed `rootTypeName` to
`root`, and added `resolveAddress`; its history entry in the description lists every change.

The complete list of accepted options and error categories is in the
[machine-readable browser description](../../contracts/api/browser-rc1/contract.json). Use that JSON file when changing
or testing the adapter; this page is an orientation guide, not a substitute for the exact field list.

## Compatibility and testing

Managed and browser compatibility are reviewed independently. Changing a managed method does not automatically
approve a change to the browser JSON. A browser-facing change must increase the interface version and update the
saved `browser-rc1` description as part of the same reviewed change; `node tools/quality/browser-contract.mjs`
checks the description against the canonical declarations (`packages/cstructsharp/index.d.ts`), the explorer's
contract module, the managed bridge, and the bootstrap.

Routine documentation validation checks this page against tracked sources and saved data. It deliberately does not
restore, build, or test `apps/explorer` or `CStructSharpWeb.Wasm`, because those projects are expensive and
optional. Their implementation and browser tests run together during the repository's final Web integration phase.
