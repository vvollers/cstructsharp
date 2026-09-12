# CStructSharp WebAssembly bundle

This bundle reads and writes binary data in a browser using a C-like layout. The browser runs the managed library
locally; its user does not need .NET installed.

For Node.js or a browser app with a build tool, install the npm package with `npm install cstructsharp`.
Follow the [JavaScript quick start](https://vvollers.github.io/cstructsharp/docs/guides/browser/index.html)
for Node.js and Vite examples. The instructions below are for this optional standalone browser ZIP.

## Run your first example

Keep the complete extracted archive together. With Node.js installed, open a terminal in this directory and run:

```sh
node serve.mjs
```

Open `http://127.0.0.1:8080/starter/`, wait for Ready, then select Read, write, and update.
The six-byte header starts with kind 2 and length 6. The page creates kind 3 and updates it to 4.
Press Ctrl+C to stop the local server. Open `http://127.0.0.1:8080/starter/inspector.html` for the larger file inspector.

`starter/index.html` and `starter/app.js` are the complete beginner application. Copy and adapt them for your own
page. They check operation success, decode read JSON, use output byte arrays, and report runtime loading errors separately.

## Large binary sources

Import `parse` or `parseWithDebug` from `./cstructsharp-wasm.js` and pass a `File`, `Blob`, buffer/view,
`Response`, or stream/iterable of binary chunks. Browser files are read in pages by an automatically managed
worker; one-pass sources are staged to origin-private storage before parsing. `parse` skips debug byte copies.
Use `signal` to cancel and `maxSpoolBytes` to control staging (default 1 GiB). Keep `large-source.js` and
`source-worker.js` beside the runtime and allow same-origin workers in your CSP. File length is independent of
the old synchronous byte-array transport ceiling; returned values and read budgets are still bounded.

```js
import { parse } from "./cstructsharp-wasm.js";
const result = await parse("struct header { uint32 signature; };", file, { rootTypeName: "header" });
if (!result.Success) throw new Error(result.Error.Message);
console.log(JSON.parse(result.Data).header.signature);
```

See [large files, buffers, and streams](https://vvollers.github.io/cstructsharp/docs/guides/browser/large-data.html)
for source types, Node streams, decompression, cancellation, and memory/storage behavior. Streamed writes are
not part of these read APIs.

## Use from your JavaScript

```js
import { parseWithDebug } from "./cstructsharp-wasm.js";

try {
  const result = await parseWithDebug(
    "struct header { uint16 kind; uint32 length; };",
    new Uint8Array([2, 0, 6, 0, 0, 0]),
    { rootTypeName: "header" },
  );
  if (result.Success) {
    const values = JSON.parse(result.Data);
    console.log(values.header.kind); // 2; debug parses include the root wrapper
  } else {
    console.error(result.Error.Code, result.Error.Path, result.Error.Offset);
  }
} catch (error) {
  console.error("Loading or JavaScript error:", error.message);
}
```

Also exported: `serialize(definition, value, options)`, `update(definition, bytes, path, value, options)`,
`getVersion()`, and `loadCStructSharpWasm()`. All return promises. Serialize and update return a `Uint8Array` in `Data`.
Use it directly; older examples that call `atob(result.Data)` should remove that conversion.
Pass the selected struct's fields when serializing, without the debug root wrapper.

Keep `cstructsharp-wasm.js`, `cstructsharp-api.js`, `main.js`, `bootstrap.js`, the runtime configuration, and `_framework/` together.
Serve over HTTP(S), not `file://`, with `.wasm` served as `application/wasm`. Relative imports work under a deployment
subdirectory when the complete bundle is kept together. The included server is for local development.

Read the [browser API guide](https://vvollers.github.io/cstructsharp/docs/guides/browser/api.html) for options,
large integers, union values, and the differences from C#. Read the
[deployment guide](https://vvollers.github.io/cstructsharp/docs/guides/browser/deployment.html) if loading fails.

## TypeScript

Keep `cstructsharp-wasm.d.ts` beside the public JavaScript entry point. Editors discover the options and
success/failure result types from the same import. After checking `Success`, parse `Data` is JSON text and
serialize/update `Data` is a `Uint8Array`. No explorer source or separate type package is required.

## Reusing a compiled layout

```js
import { compile } from "cstructsharp"; // ZIP: ./cstructsharp-wasm.js

const layout = await compile("struct root { uint32 value; };", { littleEndian: true });
try {
  const result = await layout.parse(new Uint8Array([42, 0, 0, 0]));
  const debug = await layout.parseWithDebug(new Blob([new Uint8Array([42, 0, 0, 0])]));
  if (result.Success) console.log(JSON.parse(result.Data));
} finally {
  await layout.dispose();
}
```

`compile(definition, layoutOptions)` validates and retains an immutable layout in a dedicated worker/runtime. It rejects on invalid definitions; the error's `details` property contains the bridge diagnostic. `parse` and `parseWithDebug` accept the same binary sources and read limits as the existing functions and return the same version-5 envelopes (including JSON text in `Data`). Compiler settings are fixed; read options may override `rootTypeName`, addressing and resource limits. Passing a compiler setting to a retained read rejects.

Calls on one handle are queued, with independent read state. Byte views are snapshotted when their queued read starts; keep inputs unchanged until the read completes. Separate handles have separate runtimes. Cancellation rejects with `AbortError`; an active parse is stopped by terminating its worker. The next read recreates the runtime and recompiles the saved definition. Cancelling a queued read does not cancel the active read. Always `await dispose()` to cancel outstanding calls, finish source cleanup, and release the worker and layout. Disposal is idempotent; subsequent reads reject. Keep handles only for layouts you need: each owns a WASM runtime, not merely a small native descriptor. Node idle workers do not keep the process alive.

Ordinary source-based `parse()` calls now share a serialized worker, released after 30 seconds idle. This avoids repeated runtime startup without retaining an unbounded definition cache. Small signal-less `parseWithDebug()` calls keep their existing main-runtime path. Existing signatures, envelopes, bounded parsing and temporary-source staging remain supported. Cancellation/disposal can cause a later call to pay runtime startup again.
