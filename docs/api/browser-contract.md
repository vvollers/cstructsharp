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
5. It records four managed entry points:

- `GetVersion`
- `ParseWithDebug`
- `Serialize`
- `UpdateStream`

Binary data crosses the boundary as a native `byte[]`/`Uint8Array`, never Base64 text. `ParseWithDebug` still
returns the same outer object, called an *envelope* - its fields are `ContractVersion`,
`Operation`, `Success`, `Data`, `DebugData`, and `Error` - since its `Data` is genuinely JSON text either way.
`Serialize`/`UpdateStream` return the encoded bytes directly on success; there is no envelope object left to carry
an `Error` field alongside a native byte-array success payload, so they report failure by throwing instead. The
thrown JS `Error`'s message is the same JSON-serialized `Code`/`Message`/`Offset`/`Path` shape the envelope's
`Error` field already uses, so callers reconstruct an identical error object either way.

The complete list of accepted options and error categories is in the
[machine-readable browser description](../../contracts/api/browser-rc1/contract.json). Use that JSON file when changing
or testing the adapter; this page is an orientation guide, not a substitute for the exact field list.

## Compatibility and testing

Managed and browser compatibility are reviewed independently. Changing a managed method does not automatically
approve a change to the browser JSON. A browser-facing change must increase the interface version and update the
saved `browser-rc1` description as part of the same reviewed change.

Routine documentation validation checks this page against tracked sources and saved data. It deliberately does not
restore, build, or test `apps/workshop` or `CStructSharpWeb.Wasm`, because those projects are expensive and
optional. Their implementation and browser tests run together during the repository's final Web integration phase.
