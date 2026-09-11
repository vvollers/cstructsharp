---
title: Large files, buffers, and streams
description: Parse JavaScript binary sources with paged reads, worker execution, cancellation, and temporary storage.
---

# Large files, buffers, and streams

Pass a binary source directly to `parse` or `parseWithDebug`. You do not need to split a file into parser calls,
raise the old 4 MiB transport limit, or call `file.arrayBuffer()` first. Both functions return the usual result
envelope with root-wrapped JSON text in `Data`. `parse` returns empty `DebugData` and avoids debug byte copies;
prefer it when you only need values.

```js
import { parse, parseWithDebug } from "cstructsharp";

const definition = "struct header { uint16 kind; uint32 length; };";
const options = { rootTypeName: "header" };
const file = document.querySelector('input[type="file"]').files[0];
const result = await parse(definition, file, options);
if (!result.Success) throw new Error(result.Error.Message);
console.log(JSON.parse(result.Data).header.kind);

// Inspect field byte ranges when building a binary viewer.
const inspected = await parseWithDebug(definition, file, options);
if (!inspected.Success) throw new Error(inspected.Error.Message);
console.log(inspected.DebugData);
```

Standalone ZIP consumers import the same functions from `cstructsharp-wasm.js`. Browser npm consumers configure
the [runtime assets](deployment.md) as usual. Worker setup is automatic.

## Choose a source

| Source | Typical use | How it is read |
| --- | --- | --- |
| `File`, `Blob` | Uploads, drag-and-drop, generated content | Browser worker reads slices on demand; the full source stays outside WASM memory |
| `FileSystemFileHandle` | Browser file picker | Calls `getFile()` and reads the resulting snapshot |
| `ArrayBuffer`, `SharedArrayBuffer` | Existing binary storage | Takes a byte snapshot and supplies small pages to WASM |
| Typed arrays, `DataView`, Node `Buffer` | Whole buffers or selected ranges | Uses exactly the view's `byteOffset` and `byteLength` |
| `Response` | Fetch result | Checks HTTP success and stages the unread body |
| `ReadableStream` | Fetch bodies, decompression | Stages binary chunks to temporary storage before parsing |
| `Iterable` / `AsyncIterable` of binary chunks | Chunk arrays, generators, Node readable streams | Stages chunks in order without concatenating one large array |

Views supply **raw bytes**, not converted numeric elements. A `Uint32Array` supplies its underlying representation;
choose the layout's byte order accordingly. Result offsets are relative to the passed view. Caller buffers are
not detached or modified. Coordinate access to shared memory with its producer: a copy of concurrently changing
memory is not an atomic snapshot. Do not mutate an input while the operation is acquiring or consuming it.

```js
const view = new DataView(downloadBuffer, headerOffset, headerByteLength);
const result = await parse(definition, view, options);
const combined = await parse(definition, [firstChunk, secondChunk], options);
```

Chunks must be buffers or views, never numbers or strings. For header-only access to a local file, pass `file`
rather than `file.stream()` so the library can read just the needed ranges.

## Fetch and decompression

```js
const controller = new AbortController();
const response = await fetch("/captures/session.bin", { signal: controller.signal });
const result = await parse(definition, response, {
  ...options,
  signal: controller.signal,
  maxSpoolBytes: 2 * 1024 ** 3,
});
if (!result.Success) throw new Error(result.Error.Message);
```

Decompress a transport before interpreting its binary layout:

```js
const response = await fetch("/captures/session.bin.gz");
if (!response.ok || !response.body) throw new Error(`Download failed: ${response.status}`);
const decompressed = response.body.pipeThrough(new DecompressionStream("gzip"));
const result = await parse(definition, decompressed, {
  ...options,
  maxSpoolBytes: 2 * 1024 ** 3, // Counts decompressed bytes.
});
if (!result.Success) throw new Error(result.Error.Message);
```

One-pass sources are fully consumed before parsing begins. The parser seeks for pointers, union views, and debug
ranges. Staging preserves those operations; this API does not implement HTTP range requests or emit incremental
parsed records.

## Node.js streams

```js
import { createReadStream } from "node:fs";
import { parse } from "cstructsharp";

const result = await parse(
  "struct header { uint16 kind; uint32 length; };",
  createReadStream("capture.bin"),
  { rootTypeName: "header", maxSpoolBytes: 2 * 1024 ** 3 },
);
if (!result.Success) throw new Error(result.Error.Message);
console.log(JSON.parse(result.Data).header);
```

Use binary mode: a stream configured with a text encoding yields strings and is rejected. Pass `Buffer` directly
when bytes are already resident in memory. Node also stages `Blob`/`File` inputs because it does not expose the
browser worker's synchronous Blob reader.

## Memory, storage, and cancellation

Source parsing runs in a dedicated worker with a 64 KiB managed page cache and seek positions up to JavaScript's
safe integer range. Full-file length is independent of WASM linear-memory capacity. Resident byte buffers still
require snapshot/worker copies; this is not zero-copy. Each source parse starts and disposes a worker/runtime,
so short operations have startup overhead. Small `Uint8Array` debug calls retain the direct runtime path;
supplying `signal` selects the cancellable worker path.

Streams use origin-private file storage in browsers and a private temporary directory in Node. Browser staging
requires HTTPS or localhost and storage quota. `maxSpoolBytes` defaults to 1 GiB and can be increased explicitly;
it does not limit browser `File`/`Blob` length. Temporary files are removed on success, failure, and cancellation
during normal execution. An abruptly terminated process or browser can leave temporary storage behind. Storage
failures reject rather than falling back to full buffering.

```js
const controller = new AbortController();
const pending = parseWithDebug(definition, file, { ...options, signal: controller.signal });
cancelButton.onclick = () => controller.abort();
try {
  const result = await pending;
  if (!result.Success) console.error(result.Error.Code, result.Error.Message, result.Error.Offset);
} catch (error) {
  if (error.name !== "AbortError") throw error;
}
```

Cancellation rejects with `AbortError`, stops source consumption, and terminates worker parsing. Loading, storage,
HTTP, and invalid-source errors also reject; layout/read failures use `Success: false`. Use both `try`/`catch` and
the `Success` check.

Read, array, string, and nesting limits still apply to parser work. A header in a multi-gigabyte file can be cheap;
decoding an enormous array still constructs an enormous result. Debug parsing additionally retains field values
and byte copies. Results are materialized JSON, not lazy objects. `serialize` and `update` remain the existing
in-memory APIs; this change does not add streamed writes.

The [binary inspector](inspector.md) parses against the full source and loads a separate hex window on demand.
Use **Go to byte** with a decimal or `0x` hexadecimal offset to inspect a distant location. Scrolling, searching,
and session edits do not require a full-file `Uint8Array`.
