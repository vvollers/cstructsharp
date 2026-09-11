// Public asynchronous source adapter. Source bytes never become one managed byte[].
const isNode = typeof process !== "undefined" && !!process.versions?.node;
const defaultSpoolLimit = 1024 * 1024 * 1024;

function abortError() {
  return new DOMException("Binary parsing was cancelled.", "AbortError");
}

function checkAbort(signal) {
  if (signal?.aborted) throw abortError();
}

async function abortable(promise, signal) {
  checkAbort(signal);
  if (!signal) return promise;
  let onAbort;
  try {
    return await Promise.race([
      promise,
      new Promise((_, reject) => {
        onAbort = () => reject(abortError());
        signal.addEventListener("abort", onAbort, { once: true });
      }),
    ]);
  } finally {
    signal.removeEventListener("abort", onAbort);
  }
}

function byteView(value) {
  if (
    value instanceof ArrayBuffer ||
    (typeof SharedArrayBuffer !== "undefined" &&
      value instanceof SharedArrayBuffer)
  ) {
    return new Uint8Array(value);
  }
  if (ArrayBuffer.isView(value)) {
    return new Uint8Array(value.buffer, value.byteOffset, value.byteLength);
  }
  throw new TypeError(
    "Binary chunks must be ArrayBuffers or typed-array/DataView byte views, not strings or numbers.",
  );
}

async function* chunks(input, signal) {
  if (input instanceof Blob) input = input.stream();
  const reader =
    typeof input?.getReader === "function" ? input.getReader() : null;
  const iterator = reader
    ? null
    : (input?.[Symbol.asyncIterator]?.() ?? input?.[Symbol.iterator]?.());
  if (!reader && !iterator)
    throw new TypeError(
      "Unsupported binary source. Use a Blob/File, buffer, byte view, Response, or iterable/stream of binary chunks.",
    );
  let done = false;
  try {
    while (true) {
      const item = await abortable(
        Promise.resolve(reader ? reader.read() : iterator.next()),
        signal,
      );
      if (item.done) {
        done = true;
        break;
      }
      yield byteView(item.value);
    }
  } finally {
    if (!done) {
      // Do not let a stalled custom iterator prevent cancellation or temporary-file cleanup.
      const cleanup = reader ? reader.cancel() : iterator.return?.();
      Promise.resolve(cleanup).catch(() => {});
    }
    reader?.releaseLock();
  }
}

/** Internal, exported for source-contract tests. Every staged source owns its cleanup. */
export async function prepareSource(
  input,
  { signal, maxSpoolBytes = defaultSpoolLimit } = {},
) {
  checkAbort(signal);
  if (!Number.isSafeInteger(maxSpoolBytes) || maxSpoolBytes <= 0) {
    throw new RangeError("maxSpoolBytes must be a positive safe integer.");
  }
  if (typeof Response !== "undefined" && input instanceof Response) {
    if (!input.ok)
      throw new Error(`Binary response failed: HTTP ${input.status}.`);
    if (!input.body || input.bodyUsed)
      throw new TypeError("The binary response has no unread body.");
    input = input.body;
  }
  if (typeof input?.getFile === "function")
    input = await abortable(input.getFile(), signal);
  if (
    input instanceof ArrayBuffer ||
    ArrayBuffer.isView(input) ||
    (typeof SharedArrayBuffer !== "undefined" &&
      input instanceof SharedArrayBuffer)
  ) {
    const bytes = byteView(input);
    // Snapshot exactly the selected range. Never detach or transfer the caller's buffer.
    if (isNode) {
      return {
        descriptor: {
          kind: "bytes",
          bytes: new Uint8Array(bytes),
          size: bytes.byteLength,
        },
        dispose: async () => {},
      };
    }
    input = new Blob([new Uint8Array(bytes)]);
  }
  if (!isNode && input instanceof Blob) {
    return {
      descriptor: { kind: "blob", blob: input, size: input.size },
      dispose: async () => {},
    };
  }

  // Forward-only sources must be made seekable: parsing follows pointers and rereads debug ranges.
  let size = 0;
  let write;
  let close;
  let dispose;
  let descriptor;
  if (isNode) {
    const fs = await import("node:fs/promises");
    const os = await import("node:os");
    const path = await import("node:path");
    const directory = await fs.mkdtemp(
      path.join(os.tmpdir(), "cstructsharp-source-"),
    );
    const file = path.join(directory, "source.bin");
    let handle;
    dispose = async () => {
      await handle?.close();
      handle = null;
      await fs.rm(directory, { recursive: true, force: true });
    };
    try {
      handle = await fs.open(file, "wx");
    } catch (error) {
      await dispose();
      throw error;
    }
    write = async (bytes) => {
      let offset = 0;
      while (offset < bytes.length) {
        const { bytesWritten } = await handle.write(
          bytes,
          offset,
          bytes.length - offset,
        );
        if (!bytesWritten)
          throw new Error("Temporary binary storage stopped accepting bytes.");
        offset += bytesWritten;
      }
    };
    close = async () => {
      await handle.close();
      handle = null;
    };
    descriptor = { kind: "file", path: file };
  } else {
    if (!globalThis.navigator?.storage?.getDirectory) {
      throw new Error(
        "Stream parsing requires origin-private file storage (HTTPS or localhost). Pass a Blob/File or byte buffer instead on this host.",
      );
    }
    const directory = await navigator.storage.getDirectory();
    const name = `cstructsharp-source-${crypto.randomUUID()}`;
    let writable;
    let handle;
    dispose = async () => {
      if (writable) {
        await writable.abort().catch(() => {});
        writable = null;
      }
      await directory.removeEntry(name).catch((error) => {
        if (error.name !== "NotFoundError") throw error;
      });
    };
    try {
      handle = await directory.getFileHandle(name, { create: true });
      writable = await handle.createWritable();
    } catch (error) {
      await dispose();
      throw error;
    }
    write = (bytes) => writable.write(bytes);
    close = async () => {
      await writable.close();
      writable = null;
      descriptor.blob = await handle.getFile();
    };
    descriptor = { kind: "blob" };
  }
  try {
    for await (const bytes of chunks(input, signal)) {
      checkAbort(signal);
      if (size + bytes.byteLength > maxSpoolBytes) {
        throw new RangeError(
          `Streaming input exceeds maxSpoolBytes (${maxSpoolBytes} bytes). Increase this staging limit or supply a seekable Blob/File.`,
        );
      }
      await write(bytes);
      size += bytes.byteLength;
    }
    await close();
    checkAbort(signal);
    return { descriptor: { ...descriptor, size }, dispose };
  } catch (error) {
    await dispose();
    throw error;
  }
}

async function runWorker(descriptor, definition, options, debug, signal) {
  checkAbort(signal);
  const url = new URL("./source-worker.js", import.meta.url);
  const worker = isNode
    ? new (await import("node:worker_threads")).Worker(url, { execArgv: [] })
    : new Worker(url, { type: "module" });
  let onAbort;
  try {
    return await new Promise((resolve, reject) => {
      onAbort = () => reject(abortError());
      signal?.addEventListener("abort", onAbort, { once: true });
      const receive = (data) =>
        data.error ? reject(new Error(data.error)) : resolve(data.result);
      if (isNode) {
        worker.once("message", receive);
        worker.once("error", reject);
        worker.once("exit", (code) =>
          reject(
            new Error(
              `Binary worker exited before returning a result (${code}).`,
            ),
          ),
        );
      } else {
        worker.onmessage = (event) => receive(event.data);
        worker.onerror = (event) =>
          reject(
            new Error(
              event.message ||
                "Binary worker could not start. Check runtime assets and worker-src policy.",
            ),
          );
        worker.onmessageerror = () =>
          reject(new Error("Binary worker returned an unreadable response."));
      }
      if (signal?.aborted) {
        reject(abortError());
        return;
      }
      worker.postMessage({ descriptor, definition, options, debug });
    });
  } finally {
    signal?.removeEventListener("abort", onAbort);
    await worker.terminate();
  }
}

export async function parseLargeSource(
  definition,
  input,
  options = {},
  debug = true,
) {
  const { signal, maxSpoolBytes, ...parserOptions } = options ?? {};
  const source = await prepareSource(input, { signal, maxSpoolBytes });
  try {
    return await runWorker(
      source.descriptor,
      definition,
      parserOptions,
      debug,
      signal,
    );
  } finally {
    await source.dispose();
  }
}
