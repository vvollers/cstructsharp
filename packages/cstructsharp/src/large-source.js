// Public asynchronous source adapter. Source bytes never become one managed byte[].
import { COMPILE_OPTION_KEYS, decodeEnvelopeText, isSmallByteInput, parseEnvelope } from "./cstructsharp-shared.js";

const isNode = typeof process !== "undefined" && !!process.versions?.node;
const defaultSpoolLimit = 1024 * 1024 * 1024;
/** Browser byte inputs up to this size are snapshotted and transferred to the worker rather than staged as a Blob. */
const transferableByteLimit = 64 * 1024 * 1024;

/** Creates the AbortError DOMException that every cancelled source operation rejects with. */
function abortError() {
  return new DOMException("Binary parsing was cancelled.", "AbortError");
}

/** Throws an AbortError when the optional signal has already been aborted. */
function checkAbort(signal) {
  if (signal?.aborted) throw abortError();
}

/**
 * Awaits a promise but rejects as soon as the signal aborts; the abort listener is removed afterwards.
 * @param {Promise<T>} promise The operation to await; it keeps running if the signal aborts first.
 * @param {AbortSignal | undefined} signal Optional cancellation signal.
 * @returns {Promise<T>} The promise's result.
 * @template T
 */
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

/**
 * Views a buffer, typed array or DataView as a Uint8Array over the same bytes, without copying.
 * @throws {TypeError} When the value is not binary data (for example a string or number chunk).
 */
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

/**
 * Yields the chunks of a Blob, readable stream or (async) iterable as Uint8Array views.
 * An unfinished read cancels the stream or returns the iterator, so an aborted or failed read releases its producer.
 * @param {unknown} input The binary source.
 * @param {AbortSignal | undefined} signal Aborts the wait for the next chunk.
 * @throws {TypeError} When the input is not a supported source or yields a non-binary chunk.
 */
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

/** Internal: a browser may release an aborted writer's file lock asynchronously. */
export async function removeTemporaryEntry(directory, name) {
  for (let attempt = 0; ; attempt++) {
    try {
      await directory.removeEntry(name);
      return;
    } catch (error) {
      if (error.name === "NotFoundError") return;
      // Retry only the transient lock failure, with a bounded total delay of 310 ms.
      // Permission and persistent storage failures must still reach the caller.
      if (error.name !== "NoModificationAllowedError" || attempt === 5)
        throw error;
      await new Promise((resolve) => setTimeout(resolve, 10 * 2 ** attempt));
    }
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
    // Snapshot exactly the selected range. Never detach or transfer the caller's buffer: the snapshot is what
    // the worker receives, and only the snapshot's own buffer is transferred. Browsers use the same
    // descriptor up to a bounded size instead of wrapping the bytes in a Blob the worker re-reads page by page;
    // beyond it a Blob keeps the memory footprint to one copy.
    if (isNode || bytes.byteLength <= transferableByteLimit) {
      return {
        descriptor: {
          kind: "bytes",
          bytes: new Uint8Array(bytes),
          size: bytes.byteLength,
          transfer: true,
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
      await removeTemporaryEntry(directory, name);
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
/**
 * Serializes requests to one source worker and owns that worker's lifetime.
 * A session with a layout keeps its worker (and the compiled layout inside it) until disposal; the shared session
 * without a layout stops its worker after 30 seconds of idleness.
 */
class WorkerSession {
  worker = null;
  tail = Promise.resolve();
  lifetime = new AbortController();
  idleTimer = null;

  /**
   * Creates a session without starting its worker.
   * @param {{definition: string, options: object} | null} layout The layout a dedicated session compiles into each
   *   worker it starts, or null for the shared session.
   */
  constructor(layout = null) {
    this.layout = layout;
  }

  /**
   * Runs an operation after every earlier request of this session has settled.
   * @param {(signal: AbortSignal) => Promise<T>} operation Receives a signal combining the caller's signal with the
   *   session lifetime.
   * @param {AbortSignal | undefined} signal Cancels this request; a queued request rejects at once, a running one
   *   through the operation.
   * @returns {Promise<T>} The operation's result.
   * @template T
   */
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
      /** Rejects a request that is still queued; a started request settles through its operation. */
      const onAbort = () => { if (!started) reject(abortError()); };
      combined.addEventListener("abort", onAbort, { once: true });
      result.then(resolve, reject).finally(() => combined.removeEventListener("abort", onAbort));
      if (combined.aborted) onAbort();
    });
  }

  /** Terminates the current worker, if any; the next request starts a replacement. */
  async stop() {
    const worker = this.worker;
    this.worker = null;
    if (worker) await worker.terminate();
  }

  /** Cancels queued and running requests, waits for them to settle and terminates the worker. */
  async dispose() {
    this.lifetime.abort();
    clearTimeout(this.idleTimer);
    await this.tail;
    await this.stop();
  }

  /**
   * Starts a worker when none is running and, for a dedicated session, compiles the session layout in it.
   * A failed or cancelled start terminates the new worker, so no half-initialized worker is reused.
   * @param {AbortSignal} signal Cancels the start.
   * @throws {Error} When the layout does not compile; `details` carries the structured error.
   */
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
      /** Drops the reference to a worker that failed or exited, so the next request starts a new one. */
      const forget = () => { if (this.worker === worker) this.worker = null; };
      worker.on("error", forget);
      worker.on("exit", forget);
    }
    try {
      checkAbort(signal);
      if (this.layout) {
        const result = await this.send({ command: "compile", ...this.layout }, signal);
        if (!result.success) {
          const error = new Error(result.error.message);
          error.details = result.error;
          throw error;
        }
        this.root = result.root;
      }
    } catch (error) {
      // Creation yields during Node's import. An abort in that window must not
      // leave an uninitialized worker that a later read mistakes for a ready one.
      await this.stop();
      throw error;
    }
  }

  /**
   * Posts one message to the running worker and waits for its reply.
   * An abort, worker error or early exit terminates the worker (before any staged file is deleted) and rejects.
   * @param {object} message The worker request; a `bytes` descriptor's buffer is transferred, not copied.
   * @param {AbortSignal} signal Cancels the request.
   * @returns {Promise<unknown>} The envelope the worker replied with: its `result`, or its parsed `envelope` bytes.
   */
  async send(message, signal) {
    const worker = this.worker;
    worker.ref?.();
    /** Removes the listeners of the pending request; replaced once they are registered. */
    let cleanup = () => {};
    try {
      checkAbort(signal);
      return await new Promise((resolve, reject) => {
        /** Rejects the pending request when the signal aborts. */
        const onAbort = () => reject(abortError());
        /**
         * Settles the pending request from a worker reply: `error` rejects, `result` resolves, and a parse's
         * `envelope` bytes resolve once decoded and parsed (an invalid envelope rejects with a TypeError).
         */
        const receive = (data) => {
          if (data.error) {
            reject(new Error(data.error));
          } else if (data.envelope) {
            try {
              resolve(parseEnvelope(decodeEnvelopeText(data.envelope, "parse"), "parse"));
            } catch (error) {
              reject(error);
            }
          } else {
            resolve(data.result);
          }
        };
        /** Rejects the pending request when the worker reports an error. */
        const onError = (error) => reject(new Error(error.message || "Binary worker failed."));
        /** Rejects the pending request when the worker thread exits before replying. */
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
        else {
          // A "bytes" descriptor carries our own snapshot; transferring its buffer avoids a second copy.
          const transfer = message.descriptor?.transfer ? [message.descriptor.bytes.buffer] : [];
          worker.postMessage(message, transfer);
        }
      });
    } catch (error) {
      // Await termination before deleting any spooled file used by the worker.
      await this.stop();
      throw error;
    } finally {
      cleanup();
    }
  }

  /**
   * Parses a source through this session's worker, using the compiled layout when the session has one.
   * @param {string} definition Portable layout source.
   * @param {unknown} input Binary source; it is staged for the worker and removed afterwards.
   * @param {object | null} options Parse options, including `signal` and `maxSpoolBytes`.
   * @param {boolean} debug Whether the result records every value's byte range.
   * @returns {Promise<object>} The parse envelope.
   */
  parse(definition, input, options, debug) {
    return this.request(this.layout ? "parseCompiled" : "parse", definition, input, options, { debug });
  }

  /**
   * Resolves a path's absolute byte position through this session's worker.
   * @param {string} definition Portable layout source.
   * @param {unknown} input Binary source; it is staged for the worker and removed afterwards.
   * @param {string} path Case-sensitive field path.
   * @param {object | null} options Read options, including `signal` and `maxSpoolBytes`.
   * @returns {Promise<object>} The resolveAddress envelope.
   */
  resolveAddress(definition, input, path, options) {
    return this.request(this.layout ? "resolveAddressCompiled" : "resolveAddress", definition, input, options, { path });
  }

  /**
   * Queues one worker command: stages the source, ensures a worker, sends the request and disposes the staged source.
   * @param {string} command The worker command name.
   * @param {string} definition Portable layout source.
   * @param {unknown} input Binary source.
   * @param {object | null} options Operation options; `signal` and `maxSpoolBytes` stay on this side.
   * @param {object} extra Additional message fields, such as `debug` or `path`.
   * @returns {Promise<object>} The worker's result envelope.
   */
  request(command, definition, input, options, extra) {
    const { signal, maxSpoolBytes, ...operationOptions } = options ?? {};
    return this.enqueue(async (combined) => {
      const source = await prepareSource(input, { signal: combined, maxSpoolBytes });
      try {
        await this.ensureWorker(combined);
        return await this.send({ command, descriptor: source.descriptor, definition, options: operationOptions, ...extra }, combined);
      } finally {
        await source.dispose();
      }
    }, signal);
  }
}

const sharedSession = new WorkerSession();

/**
 * Parses any supported binary source through the shared worker; the source is staged and removed afterwards.
 * @param {string} definition Portable layout source.
 * @param {unknown} input Binary source.
 * @param {object | null} options Parse options, including `signal` and `maxSpoolBytes`; null for none.
 * @param {boolean} debug Whether the result records every value's byte range; every caller states it.
 * @returns {Promise<object>} The parse envelope.
 */
export async function parseLargeSource(definition, input, options, debug) {
  return sharedRequest("parse", definition, input, options, { debug });
}

/** Resolves a path's absolute position through the shared worker; the source is staged exactly like a parse. */
export async function resolveAddressLargeSource(definition, input, path, options = {}) {
  return sharedRequest("resolveAddress", definition, input, options, { path });
}

/**
 * Stages a source outside the shared queue, then sends one command to the shared worker and removes the staged source.
 * @param {string} command The worker command name.
 * @param {string} definition Portable layout source.
 * @param {unknown} input Binary source.
 * @param {object | null} options Operation options; `signal` and `maxSpoolBytes` stay on this side.
 * @param {object} extra Additional message fields, such as `debug` or `path`.
 * @returns {Promise<object>} The worker's result envelope.
 */
async function sharedRequest(command, definition, input, options, extra) {
  const { signal, maxSpoolBytes, ...operationOptions } = options ?? {};
  // Independent ordinary API calls may stage concurrently. A stalled producer
  // must not prevent an unrelated ready source from reaching the shared worker.
  const source = await prepareSource(input, { signal, maxSpoolBytes });
  try {
    return await sharedSession.enqueue(async (combined) => {
      await sharedSession.ensureWorker(combined);
      return sharedSession.send({ command, descriptor: source.descriptor, definition, options: operationOptions, ...extra }, combined);
    }, signal);
  } finally {
    await source.dispose();
  }
}

/**
 * Reads a whole binary source into one Uint8Array for the operations that need every byte in memory (update). A
 * buffer or view is used as is (no copy); anything else is drained through the same chunk reader the staging
 * path uses, bounded by maxSpoolBytes. The options may be null, as the public update passes them when omitted.
 */
export async function collectBytes(input, options) {
  const { signal, maxSpoolBytes = defaultSpoolLimit } = options ?? {};
  checkAbort(signal);
  if (
    input instanceof ArrayBuffer ||
    ArrayBuffer.isView(input) ||
    (typeof SharedArrayBuffer !== "undefined" && input instanceof SharedArrayBuffer)
  ) {
    return byteView(input);
  }
  if (typeof Response !== "undefined" && input instanceof Response) {
    if (!input.ok) throw new Error(`Binary response failed: HTTP ${input.status}.`);
    if (!input.body || input.bodyUsed) throw new TypeError("The binary response has no unread body.");
    input = input.body;
  }
  if (typeof input?.getFile === "function") input = await abortable(input.getFile(), signal);
  const parts = [];
  let size = 0;
  for await (const bytes of chunks(input, signal)) {
    if (size + bytes.byteLength > maxSpoolBytes) {
      throw new RangeError(`Input exceeds maxSpoolBytes (${maxSpoolBytes} bytes).`);
    }
    parts.push(new Uint8Array(bytes));
    size += bytes.byteLength;
  }
  const result = new Uint8Array(size);
  let offset = 0;
  for (const part of parts) {
    result.set(part, offset);
    offset += part.byteLength;
  }
  return result;
}

/**
 * A dedicated runtime retains one immutable layout until explicit disposal. When the host adapter supplies
 * `parseBytes`, small byte inputs are parsed on the calling thread through the shared compiled-layout cache
 * instead of the retained worker; the worker still owns every large, streamed, or cancellable read.
 */
export async function compileLargeSource(definition, options = {}, { parseBytes, serialize, update } = {}) {
  if (typeof definition !== "string") throw new TypeError("Layout definition must be a string.");
  const frozenOptions = Object.freeze({ ...options });
  for (const key of Object.keys(frozenOptions)) {
    if (!COMPILE_OPTION_KEYS.has(key) && key !== "root") {
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
  /**
   * Combines the fixed compile options with one operation's options.
   * @param {object | null | undefined} operationOptions Per-call options; `root` overrides the compiled root.
   * @returns {object} The merged options.
   * @throws {Error} When the layout has been disposed.
   * @throws {TypeError} When the call sets an option fixed at compilation.
   */
  function merge(operationOptions) {
    if (disposed) throw new Error("Compiled layout has been disposed.");
    for (const key of Object.keys(operationOptions ?? {})) {
      if (COMPILE_OPTION_KEYS.has(key)) {
        throw new TypeError(`Compile option ${key} is fixed at compilation.`);
      }
    }
    return {
      ...frozenOptions, ...operationOptions,
      root: operationOptions?.root === undefined ? frozenOptions.root : operationOptions.root,
    };
  }
  /**
   * Parses with the compiled layout: small byte inputs on the calling thread when `parseBytes` is available, the rest
   * in the retained worker. Errors reject the returned promise.
   * @param {unknown} input Binary source.
   * @param {object | null} readOptions Per-call options.
   * @param {boolean} debug Whether the result records every value's byte range.
   * @returns {Promise<object>} The parse envelope.
   */
  function parse(input, readOptions, debug) {
    let merged;
    try {
      merged = merge(readOptions);
    } catch (error) {
      return Promise.reject(error);
    }
    if (parseBytes && isSmallByteInput(input, readOptions)) {
      const parserOptions = { ...merged };
      delete parserOptions.signal;
      delete parserOptions.maxSpoolBytes;
      try {
        return Promise.resolve(JSON.parse(parseBytes(definition, byteView(input), parserOptions, debug)));
      } catch (error) {
        return Promise.reject(error);
      }
    }
    return session.parse(definition, input, merged, debug);
  }
  return Object.freeze({
    root: session.root,
    parse: (input, options = null) => parse(input, options, false),
    parseWithDebug: (input, options = null) => parse(input, options, true),
    resolveAddress(input, path, options = null) {
      try {
        return session.resolveAddress(definition, input, path, merge(options));
      } catch (error) {
        return Promise.reject(error);
      }
    },
    // Writes run on the calling thread through the shared compiled-layout cache: the layout is immutable, and a
    // write never needs the worker's staged source.
    serialize(value, options = null) {
      try {
        return serialize(definition, value, merge(options));
      } catch (error) {
        return Promise.reject(error);
      }
    },
    update(input, path, value, options = null) {
      try {
        return update(definition, input, path, value, merge(options));
      } catch (error) {
        return Promise.reject(error);
      }
    },
    async dispose() {
      disposed = true;
      await session.dispose();
    },
  });
}
