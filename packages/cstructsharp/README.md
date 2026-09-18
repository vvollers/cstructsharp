# cstructsharp

Read, serialize, and update binary data using C-style struct layouts in Node.js and browsers.
The package includes the prebuilt .NET WebAssembly runtime. Consumers do not need .NET, native compilation,
or installation scripts. All data processing runs locally.

## Node.js

Requires Node.js 22.14 or later. Install:

```sh
npm install cstructsharp
```

Save this as `example.mjs`, then run `node example.mjs`:

```js
import { parse, parseWithDebug, serialize, update } from "cstructsharp";

const definition = "struct header { uint16 kind; uint32 length; };";
const options = { root: "header" };
const bytes = new Uint8Array([2, 0, 6, 0, 0, 0]);
const read = await parse(definition, bytes, options);
if (!read.success) throw new Error(read.error.message);
console.log(read.data.kind); // 2

// parseWithDebug also records every field's byte range, for a hex viewer.
const inspected = await parseWithDebug(definition, bytes, options);
console.log(inspected.debug[1]); // { start: 2, end: 6, path: "header.length", type: "uint32", value: "6" }

const written = await serialize(definition, { kind: 3, length: 6 }, options);
if (!written.success) throw new Error(written.error.message);
const updated = await update(
  definition,
  written.data,
  "header.kind",
  4,
  options,
);
if (!updated.success) throw new Error(updated.error.message);
console.log(updated.data); // Uint8Array [4, 0, 6, 0, 0, 0]
```

Node `Buffer` inputs are also supported. The installed runtime loads from disk independently of the current
working directory, with no HTTP server or runtime downloads. CommonJS applications can use
`const api = await import("cstructsharp")` inside an async function; synchronous `require()` is not part of the API.

## Large files, buffers, and streams

`parse` and `parseWithDebug` accept `File`/`Blob`, `ArrayBuffer`/`SharedArrayBuffer`, typed-array views,
`DataView`, Node `Buffer`, `Response`, `ReadableStream`, and iterables/async iterables of binary chunks.
Browser file handles with `getFile()` are also supported. The view's exact byte range is used, without numeric
conversion or detaching the caller's buffer.

```js
import { parse } from "cstructsharp";

// Browser: pass the selected File, without reading it all into an ArrayBuffer.
const controller = new AbortController();
const fileInput = document.querySelector('input[type="file"]');
const result = await parse(
  "struct header { uint16 kind; uint32 length; };",
  fileInput.files[0],
  { root: "header", signal: controller.signal },
);
if (!result.success) throw new Error(result.error.message);
console.log(result.data);
```

In Node, pass `createReadStream("capture.bin")` from `node:fs`, or a resident `Buffer`. For a network response,
pass `await fetch(url)`; a decompression pipeline can supply its readable output directly. One-pass sources are
fully staged to temporary storage before parsing, preserving pointer/seek behavior. The default `maxSpoolBytes`
is 1 GiB and can be increased. Browsers use origin-private storage (HTTPS/localhost required); Node uses a private
temporary directory and also stages Blob inputs. Normal completion, failures, and cancellation clean up staging.

Large-source parsing runs in a worker and passes 64 KiB pages to WASM. Browser files are read on demand regardless
of full-file size. `parse` avoids debug byte copies; `parseWithDebug` also returns field ranges. Small Uint8Array
debug calls retain the direct path unless `signal` is supplied. Decoded results still use memory, and read limits
still apply. `serialize` and `update` are in-memory operations: `update` reads any binary source completely and
hands at most 4 MiB to the runtime (larger inputs fail with `invalid-input`), and the raw adapter's byte-array
exports share that 4 MiB limit; `parse`, `parseWithDebug`, and `resolveAddress` page larger sources through the
worker. See the
[large-data guide](https://vvollers.github.io/cstructsharp/docs/guides/browser/large-data.html)
for complete examples, memory behavior, cancellation, and storage limits.

## Browser with Vite

Register the supplied plugin in `vite.config.js`:

```js
import { defineConfig } from "vite";
import { cstructsharp } from "cstructsharp/vite";

export default defineConfig({ plugins: [cstructsharp()] });
```

Use the same public API imports as the Node example. The plugin serves the runtime in development and emits
its assets in production. Set an absolute Vite `base`, such as `/my-app/`, for nested deployments. Relative
`base: '.'` is not supported by this integration. Vite 8 is tested; other bundlers use the static-asset route below.

## Other browser build tools and static hosting

Copy the runtime into a **new** directory in your app's public/static output:

```sh
npx --no-install cstructsharp-copy --out public/cstructsharp
```

Then initialize once, before calling operations:

```js
import { loadCStructSharpWasm, parseWithDebug } from "cstructsharp/browser";
await loadCStructSharpWasm({ runtimeUrl: "/cstructsharp/" });
```

The copy command refuses to overwrite an existing directory. On upgrades, copy to a new versioned directory
and update the URL, or remove only the old generated directory yourself. Keep all runtime assets from the same
package together. Serve over HTTP(S), with `.wasm` as `application/wasm` and `.js` as JavaScript. The runtime
requires WebAssembly compilation; restrictive CSPs must permit it (for example `script-src 'self' 'wasm-unsafe-eval'`
and `connect-src 'self'` for same-origin assets). No cross-origin isolation headers are required by this build.

## API and lifecycle

The package includes TypeScript declarations. All public functions return promises: `parse`, `parseWithDebug`,
`serialize`, `update`, `resolveAddress`, `compile`, `getVersion`, and `loadCStructSharpWasm`. Every operation
returns the same envelope: `contractVersion`, `operation`, `success`, `root`, `data`, `debug`, and `error`. Read
results carry the selected value in `data` (the root struct's members by name); writes return a `Uint8Array`;
`resolveAddress` returns a byte position. Check `success` before using `data`; operation errors carry `code`,
`message`, `path`, `offset`, `member`, `memberType`, `line`, and `column`, with the library's message verbatim
unless `redactDiagnostics` is set. Loading and argument failures reject the promise, so use `try/catch` at the
application boundary as well. BigInt input is preserved as decimal text; large integers in read results may be
strings and should not be coerced to Number. A float that is NaN or infinite arrives as the string `"NaN"`,
`"Infinity"`, or `"-Infinity"` (JSON has no such numbers), and `serialize`/`update` accept those strings for a
float field. A layout error carries the offending `line` and `column`; a read or write error carries the `path`,
`offset`, `member`, and `memberType` it stopped at.

Imports are lazy and safe during SSR. Node conditions select the Node loader; explicit `cstructsharp/node` and
`cstructsharp/browser` imports resolve ambiguous host configurations. Concurrent operations share initialization.
The runtime lives for the process/page lifetime and normal Node processes exit without a disposal call. A failed
runtime startup remains failed for that instance; restart the process/page after fixing missing assets. A browser
instance cannot be reconfigured to a different runtime URL after initialization starts.

The tarball includes the runtime and third-party license notices: the 0.5 package is about 1.9 MB compressed and
5.0 MB unpacked, of which the runtime is 26 files and 4.8 MB (about 1.7 MB over gzip). A browser downloads the
runtime on the first page load and keeps it in the HTTP cache afterwards, so deploy each release under its own
versioned URL (the copy command refuses to overwrite) and let the static host send cache headers for that path;
Node loads the runtime from disk. No Vue, Monaco, or other UI dependencies are installed. The library manages workers for large or cancellable reads;
loading the public entry point inside an application-created web worker is not a supported deployment target.
Other JS runtimes and server bundling of the Node entry point are not currently supported. Keep `cstructsharp` external in server bundles (the Vite plugin does this).

[API and layout guide](https://vvollers.github.io/cstructsharp/docs/guides/browser/api.html) ·
[Source and releases](https://github.com/vvollers/cstructsharp) · MIT license

## Reusing a compiled layout

```js
import { compile } from "cstructsharp"; // ZIP: ./cstructsharp-wasm.js

const layout = await compile("struct root { uint32 value; };", { littleEndian: true });
try {
  const result = await layout.parse(new Uint8Array([42, 0, 0, 0]));
  const debug = await layout.parseWithDebug(new Blob([new Uint8Array([42, 0, 0, 0])]));
  if (result.success) console.log(result.data);
} finally {
  await layout.dispose();
}
```

`compile(definition, layoutOptions)` validates and retains an immutable layout in a dedicated worker/runtime. It rejects on invalid definitions; the error's `details` property contains the bridge diagnostic. The handle reports the selected `root` and exposes `parse`, `parseWithDebug`, `serialize`, `update`, and `resolveAddress`; they accept the same binary sources and per-operation options as the standalone functions and return the same version-8 envelopes (with the selected value in `data`). Compiler settings are fixed; per-call options may override `root`, addressing and resource limits. Passing a compiler setting to a retained call rejects.

Calls on one handle are queued, with independent read state. Byte views are snapshotted when their queued read starts; keep inputs unchanged until the read completes. Separate handles have separate runtimes. Cancellation rejects with `AbortError`; an active parse is stopped by terminating its worker. The next read recreates the runtime and recompiles the saved definition. Cancelling a queued read does not cancel the active read. Always `await dispose()` to cancel outstanding calls, finish source cleanup, and release the worker and layout. Disposal is idempotent; subsequent reads reject. Keep handles only for layouts you need: each owns a WASM runtime, not merely a small native descriptor. Node idle workers do not keep the process alive.

Byte inputs of at most 64 KiB without a `signal` are parsed on the calling thread (no worker round trip); larger
or cancellable inputs use the worker, receiving a transferred snapshot rather than a second copy. Ordinary
source-based `parse()` calls share a serialized worker, released after 30 seconds idle. This avoids repeated runtime startup without retaining an unbounded definition cache. Small signal-less `parseWithDebug()` calls keep their existing main-runtime path. Existing signatures, envelopes, bounded parsing and temporary-source staging remain supported. Cancellation/disposal can cause a later call to pay runtime startup again.
