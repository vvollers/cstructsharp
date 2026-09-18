---
title: Large files, buffers, and streams
description: Parse JavaScript binary sources with paged reads, worker execution, cancellation, and temporary storage.
---

# Large files, buffers, and streams

Pass a binary source directly to `parse` or `parseWithDebug`. You do not need to split a file into parser calls,
work around the 4 MiB limit of the raw API, or call `file.arrayBuffer()` first. Both functions return the usual result
envelope with the selected value in `data`. `parse` returns an empty `debug` list and avoids debug byte copies;
prefer it when you only need values.

```js
import { parse, parseWithDebug } from "cstructsharp";

const definition = "struct header { uint16 kind; uint32 length; };";
const options = { root: "header" };
const file = document.querySelector('input[type="file"]').files[0];
const result = await parse(definition, file, options);
if (!result.success) throw new Error(result.error.message);
console.log(result.data.kind);

// Inspect field byte ranges when building a binary viewer.
const inspected = await parseWithDebug(definition, file, options);
if (!inspected.success) throw new Error(inspected.error.message);
console.log(inspected.debug);
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
if (!result.success) throw new Error(result.error.message);
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
if (!result.success) throw new Error(result.error.message);
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
  { root: "header", maxSpoolBytes: 2 * 1024 ** 3 },
);
if (!result.success) throw new Error(result.error.message);
console.log(result.data);
```

Use binary mode: a stream configured with a text encoding yields strings and is rejected. Pass `Buffer` directly
when bytes are already resident in memory. Node also stages `Blob`/`File` inputs because it does not expose the
browser worker's synchronous Blob reader.

## Memory, storage, and cancellation

Source parsing runs in a shared worker with a 64 KiB managed page cache and seek positions up to JavaScript's
safe integer range. Full-file length is independent of WASM linear-memory capacity. Resident byte buffers are
snapshotted once and the snapshot is transferred to the worker (never the caller's buffer); this is not zero-copy.
Byte inputs (`ArrayBuffer`, typed arrays, `DataView`, `Buffer`) of at most 64 KiB without a `signal` are parsed
on the calling thread instead, avoiding a worker round trip. Eligible fixed-layout `parse()` calls execute a
JavaScript plan; other direct reads use the managed runtime. Public `parseWithDebug()` keeps a wider direct path
for `Uint8Array` inputs up to 4 MiB. Supplying `signal` selects the cancellable worker path. Handles from `compile()`
use direct managed reads for byte inputs up to 64 KiB without `signal`.

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
  if (!result.success) console.error(result.error.code, result.error.message, result.error.offset);
} catch (error) {
  if (error.name !== "AbortError") throw error;
}
```

Cancellation rejects with `AbortError`, stops source consumption, and terminates worker parsing. Loading, storage,
HTTP, and invalid-source errors also reject; layout/read failures use `success: false`. Use both `try`/`catch` and
the `success` check.

Read, array, string, and nesting limits still apply to parser work. A header in a multi-gigabyte file can be cheap;
decoding an enormous array still constructs an enormous result. Debug parsing additionally retains field values
and byte copies. Results are materialized JSON, not lazy objects. `serialize` and `update` remain the existing
in-memory APIs; this change does not add streamed writes.

The [binary inspector](inspector.md) parses against the full source and loads a separate hex window on demand.
Use **Go to byte** with a decimal or `0x` hexadecimal offset to inspect a distant location. Scrolling, searching,
and session edits do not require a full-file `Uint8Array`.

## Many records in one call

Each JavaScript call pays a fixed cost before any byte is decoded: a fully fixed layout runs in JavaScript for a
few microseconds, and anything else crosses into WebAssembly, where the options JSON, the layout lookup, and the
result envelope cost tens of microseconds. When a source is a run of records, let the layout express the repetition
and parse them in one call:

```c
struct record { uint8 tag; uint16 id; uint32 value; };
struct file { record records[EOF]; };
```

```js
const result = await parse(definition, bytes, { root: "file" });
for (const record of result.data.records) consume(record);
```

`records[EOF]` reads whole records to the end of the input; `records[1000]` (a fixed count) keeps the whole layout
fixed, so the JavaScript fast path reads it without a WebAssembly call. Measured in Node on the repository
benchmark machine for 1,000 seven-byte records: 0.06 µs per record through `records[1000]` and 2.6 µs per record
through `records[EOF]`, against 2.4 µs per record calling `parse` once per fixed record and 37 µs per record
calling it once per record through WebAssembly (any read option, a pointer, or a variable-length member takes that
path). A count field or an earlier length works the same way (`record records[count]`). There is no separate
batch API: the layout is the batch, and `compile` covers the case where the same layout is parsed repeatedly.

## Scattered pointers and read budgets

A distant pointer does not consume a budget equal to its address. The budget counts bytes read at the target;
seeking over unused space costs no decoded bytes. The source reader fetches small pages, and the parser restores
the parent position after each target. Multiple pointers can move forwards and backwards through a large file.

For a file whose first 24 bytes contain three little-endian, eight-byte file offsets:

```c
struct record {
    uint32 kind;
    uint32 length;
    uint8 payload[length];
};

struct root {
    record *records[3];
};
```

```js
const result = await parse(definition, file, {
  root: "root",
  pointerSize: 8,
  littleEndian: true,
});
if (!result.success) throw new Error(result.error.message);
console.log(result.data.records[0].value);
```

No offset calculation, file splitting, preprocessing or special large-address option is required. Absolute pointer
address zero is null. The source must contain the targets, and pointer width and byte order must match the file.
Browser source positions must be exact JavaScript safe integers (up to `Number.MAX_SAFE_INTEGER`). Managed
seekable streams use signed 64-bit positions. Neither guarantee implies that the browser, OS or filesystem can
create files of every representable length.

The same definition works with a managed `FileStream`, which avoids reading the whole file into an array:

```csharp
var layout = new CStructSharp.CStruct(definition, pointerSize: 8, isLittleEndian: true);
using var stream = File.OpenRead("large.bin");
StructValue root = layout.Parse(stream, "root");
Console.WriteLine(root.Get<uint>("records[0].value.kind"));
```

The default budgets remain one million elements per array, 16 MiB per string and 64 MiB of total reads. They
limit decoded work, not source length or pointer distance. Debug rereads and repeated target visits also count.
If you intentionally decode larger payloads, set larger budgets:

```js
const result = await parse(definition, file, {
  root: "root",
  pointerSize: 8,
  maxArrayElements: 8_000_000,
  maxStringBytes: 32 * 1024 ** 2,
  maxTotalBytesRead: 256 * 1024 ** 2,
});
```

Browser array/string limits can be raised to `2_147_483_647`; total read/write and pointer-target byte budgets can
be raised to `Number.MAX_SAFE_INTEGER`. The corresponding update traversal budgets use the same byte bounds.
Compilation, pointer-depth and nesting limits retain their existing bounds. The inspector exposes array, string
and total-read budgets in its settings dialog. Available memory is still the practical limit: parsing materializes
values and debug ranges. Prefer small typed pointer targets when you need metadata scattered through a large file.
Managed callers can also raise `ReadOptions` budgets; `MaxTotalBytesRead` accepts values through `long.MaxValue`.
