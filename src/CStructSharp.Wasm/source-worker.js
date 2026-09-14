// A session owns its runtime; requests are serialized by the client. Termination cancels CPU-bound parsing.
const isNode = typeof process !== "undefined" && !!process.versions?.node;
// .NET detects an independent worker when onmessage is unset at import time. Our request handler
// already exists, so identify this as a sidecar rather than an Emscripten pthread worker explicitly.
if (!isNode) globalThis.dotnetSidecar = true;
const port = isNode ? (await import("node:worker_threads")).parentPort : null;

let managedPromise;
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

async function run({ command, descriptor, definition, options, debug }) {
  let close = () => {};
  try {
    const managed = await loadManaged();
    const optionsJson = JSON.stringify(options, (_key, value) =>
      typeof value === "bigint" ? value.toString() : value);
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
    const json = command === "parseCompiled"
      ? managed.ParseCompiledSource(source, optionsJson, debug)
      : managed.ParseSource(definition, source, optionsJson, debug);
    return { result: JSON.parse(json) };
  } catch (error) {
    return { error: error instanceof Error ? error.message : String(error) };
  } finally {
    close();
  }
}

if (isNode)
  port.on("message", async (data) => port.postMessage(await run(data)));
else self.onmessage = async (event) => self.postMessage(await run(event.data));
