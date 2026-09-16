---
title: JavaScript API and value conversion
description: Read, create, and update bytes in Node.js and browsers and preserve exact values across JSON.
---

# JavaScript API and value conversion

Start with the [JavaScript quick start](index.md). Import public functions from `cstructsharp` when using npm,
or from `cstructsharp-wasm.js` when using the standalone ZIP.
These functions return promises and load the runtime when first used. The explorer's TypeScript adapter and the raw
managed exports are implementation details; their signatures differ from this public entry point.

The npm browser loader additionally accepts `loadCStructSharpWasm({ runtimeUrl: "/cstructsharp/" })` for
custom static hosting. Configure it before operations; the URL must end in `/` and cannot change after startup.
Vite users register `cstructsharp/vite` instead. Node requires no runtime URL. Imports do not start WASM, and
concurrent calls share initialization. Failed startup remains failed until the process/page restarts. The runtime
lives for the process/page lifetime; no explicit disposal is needed for normal Node process exit.

| Function | Input | Successful result |
| --- | --- | --- |
| `await loadCStructSharpWasm()` | None | Loaded raw API, for advanced integration |
| `await getVersion()` | None | Version string from the loaded managed library |
| `await compile(definition, options)` | Layout string and compilation options | Reusable handle with `parse`, `parseWithDebug`, and `dispose` |
| `await parse(definition, source, options)` | Layout string, binary source, options | Result with the parsed value (root-wrapped object) in `Data`, without debug capture |
| `await parseWithDebug(definition, source, options)` | Layout string, binary source, options | Result with the parsed value in `Data` and field ranges in `DebugData` |
| `await serialize(definition, value, options)` | Layout, JavaScript value, options | Result with a `Uint8Array` in `Data` |
| `await update(definition, bytes, path, value, options)` | Layout, original bytes, field path, replacement, options | Result with the complete updated `Uint8Array` in `Data` |

The result object, also called an envelope, contains `ContractVersion`, `Operation`, `Success`, `Data`, `DebugData`,
and `Error`. Check `Success` before using `Data`. A failure has an error `Code`, `Message`, and optional `Path` and
`Offset`. Loading problems and invalid JavaScript arguments can instead throw; keep a `try`/`catch` around calls.
Each `DebugData` item names a field path (`DebugStackString`), its `Type`, its `Value` as text, and the byte range
`CurPos`–`EndPos` (end exclusive) in the input you supplied; slice your own bytes to inspect them. The current
`ContractVersion` is 7.

`parse` reads a layout whose members are all statically placed (fixed-width numbers, enums, `char[N]` buffers,
fixed arrays, nested such structs) directly in JavaScript: the bundle describes the layout's member offsets and
codecs once, and each parse is then a `DataView` walk with no WebAssembly call, producing exactly the values the
managed projection produces. That path is taken for byte inputs up to 64 KiB when the options carry only layout
settings (`aligned`, `littleEndian`, `pointerSize`, `rootTypeName`, compile limits); read limits, pointer settings,
`signal`, larger or streamed inputs, `parseWithDebug`, and every other layout use the managed parse. Results are
identical either way, so the choice is not observable except in timing.

See [large files, buffers, and streams](large-data.md) for `File`/`Blob`, views, responses, streams, and iterable
inputs, plus `signal` cancellation and the `maxSpoolBytes` staging limit. Larger, streamed, or cancellable reads page data
through a worker; the legacy synchronous raw byte adapter and `update` retain their 4 MiB input ceiling.

## Reuse a compiled layout

`compile` fixes the definition and compilation settings, then returns a handle for repeated reads:

```js
import { compile } from "cstructsharp";

const layout = await compile("struct header { uint16 kind; uint32 length; };", {
  rootTypeName: "header",
});
try {
  const result = await layout.parse(new Uint8Array([2, 0, 6, 0, 0, 0]));
  if (!result.Success) throw new Error(result.Error.Message);
  console.log(result.Data.header.kind);
} finally {
  await layout.dispose();
}
```

The handle exposes `parse`, `parseWithDebug`, and `dispose`, not write/update methods. Compilation rejects invalid
layouts; an error can carry a bridge diagnostic in `details`. Read calls may choose a root, addressing options,
limits, and cancellation, but cannot change compilation settings such as alignment or pointer width.

Worker reads on one handle are queued. A handle retains its own worker/runtime, while small byte reads without
`signal` can use the shared calling-thread runtime. Cancelling an active worker read terminates that worker;
a later read recreates it. `dispose` cancels outstanding worker work and releases resources; later reads reject.
Keep inputs unchanged until their reads finish. See [large inputs](large-data.md) and
[performance](../performance.md#javascript-compiled-reuse) for ownership and cost details.

## Choose layout options

| Option | Default | Meaning |
| --- | --- | --- |
| `rootTypeName` | First struct or union in source order | Pass a name explicitly, such as `header` |
| `littleEndian` | `true` | Least significant byte first |
| `aligned` | `false` | Packed fields; `true` inserts Portable padding |
| `pointerSize` | `8` | Pointer storage width, in bytes: 1, 2, 4, or 8 |
| `addressingMode` | `"Absolute"` | Use `"Relative"` when addresses are measured from an origin |
| `origin` | `0` | Address origin; use a decimal string for a large exact integer |

Reading and updating both accept `dereferencePointers`; reading also accepts `maxArrayElements`, `maxStringBytes`,
`maxTotalBytesRead`, and `maxNestingDepth`. Writing has `maxTotalBytesWritten`; updating adds traversal limits.
The default limits are configurable: arrays and string-byte budgets can be raised to `2_147_483_647`, and total
read/write budgets to `Number.MAX_SAFE_INTEGER`. These limits count decoded work, not file size or pointer distance.
Compilation and traversal-depth caps still apply. See [scattered pointers and budgets](large-data.md#scattered-pointers-and-read-budgets).

The browser API does not expose the C# runtime-variable dictionary, CLR streams/spans, or typed class mapping.
Use earlier count fields, fixed counts or layout constants in browser examples. Caller-supplied C# variables
need an equivalent source field or constant when adapting a recipe to JavaScript.

## Convert values deliberately

- Parse `Data` is the parsed object. It keeps the root wrapper: the header example is read as `result.Data.header.kind`. C# `Parse`
  returns the selected struct directly.
- For serialize, pass the selected struct's fields, such as `{ kind: 3, length: 6 }`, without the debug root wrapper.
- Write/update `Data` is already a `Uint8Array`. Use it directly for reading, saving, or sending bytes.
- Large integers can arrive as decimal strings. Keep them as strings or convert them to `BigInt`; converting to
  JavaScript `Number` can lose precision. The public wrapper converts BigInt values to decimal strings when writing.
- Enums include their enum name, optional member name, and numeric value. An unknown member name can be `null`.
  A `flag` adds `Names` (the members whose bits are set) and `Remainder` (the bits no member covers); serialize
  accepts `"READ|HIDDEN"`, one name, or a number for it.
- A promoted (anonymous) struct or union member's fields sit directly on the parent object, and a `_` padding field
  never appears; serialize does not need a value for either.
- Unions include `$kind: "union"`, `Union`, `RawStorage`, `Members`, and `SelectedMember`. Preserve raw storage for
  an unchanged round trip, or explicitly select a member when creating a different value.
- Fixed text can contain a zero character, displayed as `\u0000` in JSON. Capacity and termination are format rules,
  not a reason to trim every string automatically.

Try the [large integer lesson](https://vvollers.github.io/cstructsharp/explorer/#lesson=large-integer) and
[fixed text lesson](https://vvollers.github.io/cstructsharp/explorer/#lesson=text).

Union `RawStorage` is an array of byte numbers. Keep it when an unchanged union must preserve its exact bytes.

Deploy the JavaScript wrapper, TypeScript declarations, and runtime assets from the same npm package version or
release ZIP. Mixing assets can produce incompatible results.

## Diagnose a failure

For `read-failed`, compare the input byte count with the layout widths. For `invalid-path`, check case and spelling.
For a limit error, check the format's required size before raising the limit. A plausible but wrong number often
means the byte order or field placement is wrong; such a read may succeed because the bytes are still valid.

Keep error codes for program decisions and messages for people. See the [managed error guide](../errors-and-recovery.md)
for the distinction between validation failures and physical write failures, and the
[browser contract orientation](../../api/browser-contract.md) for compatibility maintenance.

## TypeScript and editor help

The npm package includes TypeScript declarations, resolved from the same import. No separate type package or
reference to the explorer source is needed. For the standalone ZIP, keep `cstructsharp-wasm.d.ts` beside
`cstructsharp-wasm.js` and use that local entry point instead.

```typescript
import { serialize } from 'cstructsharp';

const result = await serialize('struct header { uint16 kind; };', { kind: 3 }, { rootTypeName: 'header' });
if (result.Success) {
  const bytes: Uint8Array = result.Data;
  console.log(bytes);
} else {
  console.error(result.Error.Code, result.Error.Message);
}
```

The result type narrows on `Success`. Read `Data` is the parsed value (`ParsedData`), while create/update `Data` is a byte array.
Declarations include option help and the raw adapter's distinct text transport types. They do not change runtime
validation: loading and invalid JavaScript arguments can still throw, and parsed large integers can still be strings.
