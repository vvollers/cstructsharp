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

// One in-flight request per runtime. Abort terminates only that runtime; the next
// queued request starts a replacement and reinstalls its immutable layout.
class WorkerSession {
  worker = null;
  tail = Promise.resolve();
  lifetime = new AbortController();
  idleTimer = null;

  constructor(layout = null) {
    this.layout = layout;
  }

  enqueue(operation, signal) {
    const combined = signal
      ? AbortSignal.any([signal, this.lifetime.signal])
      : this.lifetime.signal;
    let started = false;
    const result = this.tail.then(async () => {
      started = true;
      checkAbort(combined);
      clearTimeout(this.idleTimer);
      try {
        return await operation(combined);
      } finally {
        this.worker?.unref?.();
        if (!this.layout && this.worker) {
          this.idleTimer = setTimeout(() => this.stop(), 30_000);
          this.idleTimer.unref?.();
        }
      }
    });
    this.tail = result.catch(() => {});
    // Queued cancellation settles promptly without disturbing the active request.
    return new Promise((resolve, reject) => {
      const onAbort = () => { if (!started) reject(abortError()); };
      combined.addEventListener("abort", onAbort, { once: true });
      result.then(resolve, reject).finally(() => combined.removeEventListener("abort", onAbort));
      if (combined.aborted) onAbort();
    });
  }

  async stop() {
    const worker = this.worker;
    this.worker = null;
    if (worker) await worker.terminate();
  }

  async dispose() {
    this.lifetime.abort();
    clearTimeout(this.idleTimer);
    await this.tail;
    await this.stop();
  }

  async ensureWorker(signal) {
    checkAbort(signal);
    if (this.worker) return;
    const url = new URL("./source-worker.js", import.meta.url);
    const worker = isNode
      ? new (await import("node:worker_threads")).Worker(url, { execArgv: [] })
      : new Worker(url, { type: "module" });
    this.worker = worker;
    // Keep an error listener during idle periods too (Node otherwise throws).
    if (isNode) {
      const forget = () => { if (this.worker === worker) this.worker = null; };
      worker.on("error", forget);
      worker.on("exit", forget);
    }
    try {
      checkAbort(signal);
      if (this.layout) {
        const result = await this.send({ command: "compile", ...this.layout }, signal);
        if (!result.Success) {
          const error = new Error(result.Error.Message);
          error.details = result.Error;
          throw error;
        }
      }
    } catch (error) {
      // Creation yields during Node's import. An abort in that window must not
      // leave an uninitialized worker that a later read mistakes for a ready one.
      await this.stop();
      throw error;
    }
  }

  async send(message, signal) {
    const worker = this.worker;
    worker.ref?.();
    let cleanup = () => {};
    try {
      checkAbort(signal);
      return await new Promise((resolve, reject) => {
        const onAbort = () => reject(abortError());
        const receive = (data) => data.error
          ? reject(new Error(data.error)) : resolve(data.result);
        const onError = (error) => reject(new Error(error.message || "Binary worker failed."));
        const onExit = (code) => reject(new Error(`Binary worker exited before returning a result (${code}).`));
        signal.addEventListener("abort", onAbort, { once: true });
        if (isNode) {
          worker.once("message", receive);
          worker.once("error", onError);
          worker.once("exit", onExit);
        } else {
          worker.onmessage = (event) => receive(event.data);
          worker.onerror = onError;
          worker.onmessageerror = onError;
        }
        cleanup = () => {
          signal.removeEventListener("abort", onAbort);
          if (isNode) {
            worker.off("message", receive);
            worker.off("error", onError);
            worker.off("exit", onExit);
          } else {
            worker.onmessage = worker.onerror = worker.onmessageerror = null;
          }
        };
        if (signal.aborted) onAbort();
        else worker.postMessage(message);
      });
    } catch (error) {
      // Await termination before deleting any spooled file used by the worker.
      await this.stop();
      throw error;
    } finally {
      cleanup();
    }
  }

  parse(definition, input, options, debug) {
    const { signal, maxSpoolBytes, ...parserOptions } = options ?? {};
    return this.enqueue(async (combined) => {
      const source = await prepareSource(input, { signal: combined, maxSpoolBytes });
      try {
        await this.ensureWorker(combined);
        return await this.send({
          command: this.layout ? "parseCompiled" : "parse",
          descriptor: source.descriptor, definition, options: parserOptions, debug,
        }, combined);
      } finally {
        await source.dispose();
      }
    }, signal);
  }
}

const sharedSession = new WorkerSession();

export async function parseLargeSource(definition, input, options = {}, debug = true) {
  const { signal, maxSpoolBytes, ...parserOptions } = options ?? {};
  // Independent ordinary API calls may stage concurrently. A stalled producer
  // must not prevent an unrelated ready source from reaching the shared worker.
  const source = await prepareSource(input, { signal, maxSpoolBytes });
  try {
    return await sharedSession.enqueue(async (combined) => {
      await sharedSession.ensureWorker(combined);
      return sharedSession.send({
        command: "parse", descriptor: source.descriptor,
        definition, options: parserOptions, debug,
      }, combined);
    }, signal);
  } finally {
    await source.dispose();
  }
}

const layoutKeys = new Set([
  "aligned", "pointerSize", "littleEndian", "maxDefinitionLength",
  "maxLayoutNestingDepth", "maxExpressionNestingDepth", "maxExpressionTokens",
]);

/** A dedicated runtime retains one immutable layout until explicit disposal. */
export async function compileLargeSource(definition, options = {}) {
  if (typeof definition !== "string") throw new TypeError("Layout definition must be a string.");
  const frozenOptions = Object.freeze({ ...options });
  for (const key of Object.keys(frozenOptions)) {
    if (!layoutKeys.has(key) && key !== "rootTypeName") {
      throw new TypeError(`Unsupported compile option: ${key}`);
    }
  }
  const session = new WorkerSession({ definition, options: frozenOptions });
  try {
    await session.enqueue((signal) => session.ensureWorker(signal));
  } catch (error) {
    await session.dispose();
    throw error;
  }
  let disposed = false;
  function parse(input, readOptions, debug) {
    if (disposed) return Promise.reject(new Error("Compiled layout has been disposed."));
    for (const key of Object.keys(readOptions ?? {})) {
      if (layoutKeys.has(key)) {
        return Promise.reject(new TypeError(`Layout option ${key} is fixed at compilation.`));
      }
    }
    return session.parse(definition, input, {
      ...frozenOptions, ...readOptions,
      rootTypeName: readOptions?.rootTypeName === undefined
        ? frozenOptions.rootTypeName : readOptions.rootTypeName,
    }, debug);
  }
  return Object.freeze({
    parse: (input, options = null) => parse(input, options, false),
    parseWithDebug: (input, options = null) => parse(input, options, true),
    async dispose() {
      disposed = true;
      await session.dispose();
    },
  });
}
