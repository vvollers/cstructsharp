// Each operation owns its runtime and source. Termination cancels CPU-bound parses without shared memory.
const isNode = typeof process !== "undefined" && !!process.versions?.node;
// .NET detects an independent worker when onmessage is unset at import time. Our request handler
// already exists, so identify this as a sidecar rather than an Emscripten pthread worker explicitly.
if (!isNode) globalThis.dotnetSidecar = true;
const port = isNode ? (await import("node:worker_threads")).parentPort : null;

async function run({ descriptor, definition, options, debug }) {
  let close = () => {};
  try {
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
    const { dotnet } = await import("./_framework/dotnet.js");
    const runtime = await dotnet.create();
    runtime.setModuleImports("cstructsharp-source", {
      read: (source, offset, count) => source.read(offset, count),
    });
    const exports = await runtime.getAssemblyExports("CStructSharpWeb.Wasm");
    const managed = exports.CStructSharpWeb.Wasm.CStructExports;
    const json = managed.ParseSource(
      definition,
      { size: descriptor.size, read },
      JSON.stringify(options, (_key, value) =>
        typeof value === "bigint" ? value.toString() : value,
      ),
      debug,
    );
    return { result: JSON.parse(json) };
  } catch (error) {
    return { error: error instanceof Error ? error.message : String(error) };
  } finally {
    close();
  }
}

if (isNode)
  port.once("message", async (data) => port.postMessage(await run(data)));
else self.onmessage = async (event) => self.postMessage(await run(event.data));
