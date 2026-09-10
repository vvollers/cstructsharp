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
| `await parseWithDebug(definition, bytes, options)` | Layout string, `Uint8Array`, options | Result with JSON text in `Data` and field ranges in `DebugData` |
| `await serialize(definition, value, options)` | Layout, JavaScript value, options | Result with a `Uint8Array` in `Data` |
| `await update(definition, bytes, path, value, options)` | Layout, original bytes, field path, replacement, options | Result with the complete updated `Uint8Array` in `Data` |

The result object, also called an envelope, contains `ContractVersion`, `Operation`, `Success`, `Data`, `DebugData`,
and `Error`. Check `Success` before using `Data`. A failure has an error `Code`, `Message`, and optional `Path` and
`Offset`. Loading problems and invalid JavaScript arguments can instead throw; keep a `try`/`catch` around calls.

## Choose layout options

| Option | Default | Meaning |
| --- | --- | --- |
| `rootTypeName` | First struct selected by the bridge | Pass a name explicitly, such as `header` |
| `littleEndian` | `true` | Least significant byte first |
| `aligned` | `false` | Packed fields; `true` inserts Portable padding |
| `pointerSize` | `8` | Pointer storage width, in bytes: 1, 2, 4, or 8 |
| `addressingMode` | `"Absolute"` | Use `"Relative"` when addresses are measured from an origin |
| `origin` | `0` | Address origin; use a decimal string for a large exact integer |

Reading and updating both accept `dereferencePointers`; reading also accepts `maxArrayElements`, `maxStringBytes`,
`maxTotalBytesRead`, and `maxNestingDepth`. Writing has `maxTotalBytesWritten`; updating adds traversal limits.
The bridge enforces upper bounds, so arbitrary increases are not accepted. The
[versioned contract](../../../contracts/api/browser-rc1/contract.json) lists exact option bounds and error categories.

The browser API does not expose the C# runtime-variable dictionary, streams, spans, or typed class mapping.
Use fixed array counts or layout constants in browser examples. Do not assume a runtime-sized C# recipe can be
copied unchanged into JavaScript.

## Convert values deliberately

- Parse `Data` is a string: call `JSON.parse(result.Data)` after checking success. A debug parse retains the root
  wrapper: the header example is read as `values.header.kind`. C# `Parse` returns the selected struct directly.
- For serialize, pass the selected struct's fields, such as `{ kind: 3, length: 6 }`, without the debug root wrapper.
- Write/update `Data` is already a `Uint8Array`. Use it directly for reading, saving, or sending bytes.
- Large integers can arrive as decimal strings. Keep them as strings or convert them to `BigInt`; converting to
  JavaScript `Number` can lose precision. The public wrapper converts BigInt values to decimal strings when writing.
- Enums include their enum name, optional member name, and numeric value. An unknown member name can be `null`.
- Unions include `$kind: "union"`, `Union`, `RawStorage`, `Members`, and `SelectedMember`. Preserve raw storage for
  an unchanged round trip, or explicitly select a member when creating a different value.
- Fixed text can contain a zero character, displayed as `\u0000` in JSON. Capacity and termination are format rules,
  not a reason to trim every string automatically.

Try the [large integer lesson](https://vvollers.github.io/cstructsharp/explorer/#lesson=large-integer) and
[fixed text lesson](https://vvollers.github.io/cstructsharp/explorer/#lesson=text).

If adapting an older wrapper example, remove the `atob(result.Data)` conversion after serialize or update.
Binary data now crosses the boundary as native bytes end to end, so no Base64 decoding step remains. Parse
JSON and union `RawStorage` representations are unchanged.

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

The result type narrows on `Success`. Read `Data` remains JSON text, while create/update `Data` is a byte array.
Declarations include option help and the raw adapter's distinct text transport types. They do not change runtime
validation: loading and invalid JavaScript arguments can still throw, and parsed large integers can still be strings.
