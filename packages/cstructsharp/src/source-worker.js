// A session owns its runtime; requests are serialized by the client. Termination cancels CPU-bound parsing.
import { stringifyInteropJson } from "./cstructsharp-shared.js";

const isNode = typeof process !== "undefined" && !!process.versions?.node;
// .NET detects an independent worker when onmessage is unset at import time. Our request handler
// already exists, so identify this as a sidecar rather than an Emscripten pthread worker explicitly.
if (!isNode) globalThis.dotnetSidecar = true;
const port = isNode ? (await import("node:worker_threads")).parentPort : null;

let managedPromise;
/** Starts the .NET runtime once and returns the managed CStructExports; later calls share the same promise. */
function loadManaged() {
  return managedPromise ??= (async () => {
    const { dotnet } = await import("./_framework/dotnet.js");
    const runtime = await dotnet.create();
    runtime.setModuleImports("cstructsharp-source", {
      read: (source, offset, count) => source.read(offset, count),
    });
    const exports = await runtime.getAssemblyExports("CStructSharpWeb.Wasm");
    return exports.CStructSharpWeb.Wasm.CStructExports;
  })();
}

/**
 * Handles one request from the session client: compiles a layout or reads a source descriptor with the managed exports.
 * A file descriptor is opened for the request and closed before returning; failures are reported, not thrown.
 * @param {object} request The command, the source descriptor, the definition, options, `debug` and `path`.
 * @returns {Promise<{result: unknown} | {envelope: Uint8Array} | {error: string}>} The reply posted back to the client:
 *   a parse's UTF-8 envelope bytes, another command's parsed envelope, or the message of an unexpected failure.
 */
async function run({ command, descriptor, definition, options, debug, path }) {
  /** Releases the request's file handle; replaced when a file is opened. */
  let close = () => {};
  try {
    const managed = await loadManaged();
    const optionsJson = stringifyInteropJson(options);
    if (command === "compile") {
      return { result: JSON.parse(managed.InitializeCompiledLayout(definition, optionsJson)) };
    }
    let read;
    if (descriptor.kind === "bytes") {
      read = (offset, count) =>
        descriptor.bytes.subarray(offset, offset + count);
    } else if (descriptor.kind === "file" && isNode) {
      const fs = await import("node:fs");
      const fd = fs.openSync(descriptor.path, "r");
      close = () => fs.closeSync(fd);
      read = (offset, count) => {
        const bytes = new Uint8Array(count);
        let readCount = 0;
        while (readCount < count) {
          const n = fs.readSync(
            fd,
            bytes,
            readCount,
            count - readCount,
            offset + readCount,
          );
          if (!n) break;
          readCount += n;
        }
        return bytes.subarray(0, readCount);
      };
    } else if (descriptor.kind === "blob" && !isNode) {
      const reader = new FileReaderSync();
      read = (offset, count) =>
        new Uint8Array(
          reader.readAsArrayBuffer(
            descriptor.blob.slice(offset, offset + count),
          ),
        );
    } else {
      throw new TypeError("Unsupported worker source descriptor.");
    }
    const source = { size: descriptor.size, read };
    switch (command) {
      case "parseCompiled":
        return { envelope: ownedBytes(managed.ParseCompiledSource(source, optionsJson, debug)) };
      case "resolveAddress":
        return { result: JSON.parse(managed.ResolveAddress(definition, source, path, optionsJson)) };
      case "resolveAddressCompiled":
        return { result: JSON.parse(managed.ResolveAddressCompiled(source, path, optionsJson)) };
      default:
        return { envelope: ownedBytes(managed.ParseSource(definition, source, optionsJson, debug)) };
    }
  } catch (error) {
    return { error: error instanceof Error ? error.message : String(error) };
  } finally {
    close();
  }
}

/**
 * Returns a parse export's UTF-8 envelope as a Uint8Array that owns its whole buffer, so the reply can transfer the
 * buffer instead of copying it. The runtime already returns such a copy; anything else (a view into another buffer,
 * which transferring would detach) is copied once.
 * @param {unknown} bytes The bytes the export returned.
 * @returns {Uint8Array} The envelope bytes; the client decodes and validates them.
 * @throws {TypeError} When the export did not return bytes.
 */
function ownedBytes(bytes) {
  if (!(bytes instanceof Uint8Array)) throw new TypeError("CStructSharp returned an invalid parse response envelope.");
  const owned = bytes.byteOffset === 0 && bytes.byteLength === bytes.buffer.byteLength && bytes.buffer instanceof ArrayBuffer;
  return owned ? bytes : bytes.slice();
}

/**
 * Posts one reply. A parse reply carries the envelope as UTF-8 bytes whose buffer is transferred: the client decodes
 * and parses it once, instead of this worker parsing the JSON and the message channel cloning the object graph.
 * @param {(message: object, transfer: ArrayBuffer[]) => void} post The worker port's postMessage.
 * @param {object} message The value `run` returned.
 */
function postReply(post, message) {
  post(message, message.envelope ? [message.envelope.buffer] : []);
}

if (isNode)
  port.on("message", async (data) => postReply((message, transfer) => port.postMessage(message, transfer), await run(data)));
else self.onmessage = async (event) => postReply((message, transfer) => self.postMessage(message, transfer), await run(event.data));
