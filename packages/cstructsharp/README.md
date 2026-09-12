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
import { parseWithDebug, serialize, update } from "cstructsharp";

const definition = "struct header { uint16 kind; uint32 length; };";
const options = { rootTypeName: "header" };
const bytes = new Uint8Array([2, 0, 6, 0, 0, 0]);
const read = await parseWithDebug(definition, bytes, options);
if (!read.Success) throw new Error(read.Error.Message);
console.log(JSON.parse(read.Data).header.kind); // 2

const written = await serialize(definition, { kind: 3, length: 6 }, options);
if (!written.Success) throw new Error(written.Error.Message);
const updated = await update(
  definition,
  written.Data,
  "header.kind",
  4,
  options,
);
if (!updated.Success) throw new Error(updated.Error.Message);
console.log(updated.Data); // Uint8Array [4, 0, 6, 0, 0, 0]
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
  { rootTypeName: "header", signal: controller.signal },
);
if (!result.Success) throw new Error(result.Error.Message);
console.log(JSON.parse(result.Data).header);
```

In Node, pass `createReadStream("capture.bin")` from `node:fs`, or a resident `Buffer`. For a network response,
pass `await fetch(url)`; a decompression pipeline can supply its readable output directly. One-pass sources are
fully staged to temporary storage before parsing, preserving pointer/seek behavior. The default `maxSpoolBytes`
is 1 GiB and can be increased. Browsers use origin-private storage (HTTPS/localhost required); Node uses a private
temporary directory and also stages Blob inputs. Normal completion, failures, and cancellation clean up staging.

Large-source parsing runs in a worker and passes 64 KiB pages to WASM. Browser files are read on demand regardless
of full-file size. `parse` avoids debug byte copies; `parseWithDebug` also returns field ranges. Small Uint8Array
debug calls retain the direct path unless `signal` is supplied. Decoded results still use memory, and read limits
still apply. `serialize`/`update` remain in-memory APIs. See the
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

The package includes TypeScript declarations. All five public functions return promises:
`parseWithDebug`, `serialize`, `update`, `getVersion`, and `loadCStructSharpWasm`.
Read results contain JSON text with a root wrapper; writes return `Uint8Array`. Check `Success` before using
`Data`; operation errors carry `Code`, `Message`, `Path`, and `Offset`. Loading and argument failures reject the
promise, so use `try/catch` at the application boundary as well. BigInt input is preserved as decimal text;
large integers in read JSON may be strings and should not be coerced to Number.

Imports are lazy and safe during SSR. Node conditions select the Node loader; explicit `cstructsharp/node` and
`cstructsharp/browser` imports resolve ambiguous host configurations. Concurrent operations share initialization.
The runtime lives for the process/page lifetime and normal Node processes exit without a disposal call. A failed
runtime startup remains failed for that instance; restart the process/page after fixing missing assets. A browser
instance cannot be reconfigured to a different runtime URL after initialization starts.

The unpacked runtime is approximately 5.2 MB; the tarball also includes third-party license notices. No Vue,
Monaco, or other UI dependencies are installed. Web workers, other JS runtimes, and server bundling of the Node
entry point are not currently supported. Keep `cstructsharp` external in server bundles (the Vite plugin does this).

[API and layout guide](https://vvollers.github.io/cstructsharp/docs/guides/browser/api.html) ·
[Source and releases](https://github.com/vvollers/cstructsharp) · MIT license

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
