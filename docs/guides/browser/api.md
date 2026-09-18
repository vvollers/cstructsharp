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
| `await compile(definition, options)` | Layout string and compilation options | Reusable handle with `root`, `parse`, `parseWithDebug`, `serialize`, `update`, `resolveAddress`, and `dispose` |
| `await parse(definition, source, options)` | Layout string, binary source, options | Result with the selected value in `data`, without debug capture |
| `await parseWithDebug(definition, source, options)` | Layout string, binary source, options | Result with the selected value in `data` and field ranges in `debug` |
| `await serialize(definition, value, options)` | Layout, JavaScript value, options | Result with a `Uint8Array` in `data` |
| `await update(definition, source, path, value, options)` | Layout, original binary source, field path, replacement, options | Result with the complete updated `Uint8Array` in `data` |
| `await resolveAddress(definition, source, path, options)` | Layout, binary source, field path, options | Result with the absolute byte position in `data` (a decimal string beyond 2^53) |

The result object, also called an envelope, contains `contractVersion`, `operation`, `success`, `root`, `data`,
`debug`, and `error`. Check `success` before using `data`; `root` names the root or path the operation selected. A
failure has an `error` with a category `code`, the library's `message`, and, when known, the `path`, `offset`,
`member`, and `memberType` of the value that failed, plus `line` and `column` for a layout error. Loading problems
and invalid JavaScript arguments can instead throw; keep a `try`/`catch` around calls. Each `debug` item names a
field `path`, its `type`, its `value` as text, and the byte range `start`–`end` (end exclusive) in the input you
supplied; slice your own bytes to inspect them. The current `contractVersion` is 8.

Diagnostics are complete by default: the message is the same text the C# exception carries, including layout
text, values, and paths. Pass `redactDiagnostics: true` when a page must not echo those; the error then keeps only
its `code`, a curated category message, and the `offset`.

`parse` reads a layout whose members are all statically placed (fixed-width numbers, enums, `char[N]` buffers,
fixed arrays, nested such structs) directly in JavaScript: the bundle describes the layout's member offsets and
codecs once, and each parse is then a `DataView` walk with no WebAssembly call, producing exactly the values the
managed projection produces. That path is taken for byte inputs up to 64 KiB when the options carry only layout
settings (`aligned`, `littleEndian`, `pointerSize`, `root`, bitfield rules, compile limits); read limits, pointer settings,
`signal`, larger or streamed inputs, `parseWithDebug`, and every other layout use the managed parse. Results are
identical either way, so the choice is not observable except in timing.

See [large files, buffers, and streams](large-data.md) for `File`/`Blob`, views, responses, streams, and iterable
inputs, plus `signal` cancellation and the `maxSpoolBytes` staging limit. Larger, streamed, or cancellable reads page data
through a worker; the raw API from `loadCStructSharpWasm()` and `update` accept byte inputs up to 4 MiB.

## Reuse a compiled layout

`compile` fixes the definition and compilation settings, then returns a handle for repeated reads:

```js
import { compile } from "cstructsharp";

const layout = await compile("struct header { uint16 kind; uint32 length; };", {
  root: "header",
});
try {
  const result = await layout.parse(new Uint8Array([2, 0, 6, 0, 0, 0]));
  if (!result.success) throw new Error(result.error.message);
  console.log(layout.root, result.data.kind); // header 2
  const written = await layout.serialize({ kind: 3, length: 6 });
  const position = await layout.resolveAddress(written.data, "header.length"); // data: 2
} finally {
  await layout.dispose();
}
```

The handle exposes `root` (the name the layout selected), `parse`, `parseWithDebug`, `serialize`, `update`,
`resolveAddress`, and `dispose`. Compilation rejects invalid layouts; an error can carry a bridge diagnostic in
`details`. Per-call options may choose a root, addressing options, limits, and cancellation, but cannot change
compilation settings such as alignment or pointer width.

Worker reads on one handle are queued. A handle retains its own worker/runtime, while small byte reads without
`signal` can use the shared calling-thread runtime. Cancelling an active worker read terminates that worker;
a later read recreates it. `dispose` cancels outstanding worker work and releases resources; later reads reject.
Keep inputs unchanged until their reads finish. See [large inputs](large-data.md) and
[performance](../performance.md#javascript-compiled-reuse) for ownership and cost details.

## Choose layout options

| Option | Default | Meaning |
| --- | --- | --- |
| `root` | First struct or union in source order | A type name such as `header`, or a path such as `header.flags` |
| `littleEndian` | `true` | Least significant byte first |
| `aligned` | `false` | Packed fields; `true` inserts Portable padding |
| `pointerSize` | `8` | Pointer storage width, in bytes: 1, 2, 4, or 8 |
| `bitfieldPacking` | `"SysV"` | Bitfield unit rule: `"SysV"` (GCC/Clang) or `"Msvc"` (one unit per declared size) |
| `bitfieldAllocation` | `"LowBitFirst"` | Which end of a storage unit the first bitfield takes: `"LowBitFirst"` or `"HighBitFirst"` |
| `cLongWidth` | `64` | Width of C `long` in bits: `64` or `32` |
| `addressingMode` | `"Absolute"` | Use `"Relative"` when addresses are measured from an origin |
| `origin` | `0` | Address origin; use a decimal string for a large exact integer |

Reading and updating both accept `dereferencePointers`; reading also accepts `trimFixedText` (strip trailing
zero padding from fixed text), `maxArrayElements`, `maxStringBytes`, `maxTotalBytesRead`, and `maxNestingDepth`.
Writing has `unknownMembers` (`"Ignore"` or `"Reject"` for value properties no field declares) and
`maxTotalBytesWritten`; updating adds traversal limits.
The default limits are configurable: arrays and string-byte budgets can be raised to `2_147_483_647`, and total
read/write budgets to `Number.MAX_SAFE_INTEGER`. These limits count decoded work, not file size or pointer distance.
Compilation and traversal-depth caps still apply. See [scattered pointers and budgets](large-data.md#scattered-pointers-and-read-budgets).

The browser API does not expose the C# runtime-variable dictionary, CLR streams/spans, or typed class mapping.
Use earlier count fields, fixed counts or layout constants in browser examples. Caller-supplied C# variables
need an equivalent source field or constant when adapting a recipe to JavaScript.

## Convert values deliberately

- Parse `data` is the selected value: the root struct's members by name (`result.data.kind`), exactly as C# `Parse`
  returns the selected struct. A path that selects a union, array, or scalar returns that value.
- For serialize, pass the selected struct's fields, such as `{ kind: 3, length: 6 }`; a parse result's `data` works
  as is.
- Write/update `data` is already a `Uint8Array`. Use it directly for reading, saving, or sending bytes.
- Large integers can arrive as decimal strings. Keep them as strings or convert them to `BigInt`; converting to
  JavaScript `Number` can lose precision. The public wrapper converts BigInt values to decimal strings when writing.
- A float that is not a number or is infinite arrives as the string `"NaN"`, `"Infinity"`, or `"-Infinity"`,
  because JSON has no such numbers. `serialize` and `update` accept the same strings for a float field.
- Enums are `{ kind: "enum", enum, name, value }`: the enum name, the member name (`null` when no member
  matches), and the numeric value. A `flag` adds `names` (the members whose bits are set) and `remainder` (the bits
  no member covers); serialize accepts `"READ|HIDDEN"`, one name, or a number for it.
- Pointers are `{ kind: "pointer", address, depth, dereferenced, value }`; `value` is the target when it was
  followed.
- A promoted (anonymous) struct or union member's fields sit directly on the parent object, and a `_` padding field
  never appears; serialize does not need a value for either.
- Unions are `{ kind: "union", union, rawStorage, members, selectedMember }`. Preserve raw storage for an
  unchanged round trip, or explicitly select a member when creating a different value.
- Fixed text can contain a zero character, displayed as `\u0000` in JSON. Capacity and termination are format rules,
  not a reason to trim every string automatically.

Try the [large integer lesson](https://vvollers.github.io/cstructsharp/explorer/#lesson=large-integer) and
[fixed text lesson](https://vvollers.github.io/cstructsharp/explorer/#lesson=text).

Union `rawStorage` is the storage bytes as Base64 text. Keep it when an unchanged union must preserve its exact bytes.

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

const result = await serialize('struct header { uint16 kind; };', { kind: 3 }, { root: 'header' });
if (result.success) {
  const bytes: Uint8Array = result.data;
  console.log(bytes);
} else {
  console.error(result.error.code, result.error.message);
}
```

The result type narrows on `success`. Read `data` is the parsed value (`ParsedValue`; `ParsedStruct` names a struct's
members), while create/update `data` is a byte array.
Declarations include option help and the raw adapter's distinct text transport types. They do not change runtime
validation: loading and invalid JavaScript arguments can still throw, and parsed large integers can still be strings.
