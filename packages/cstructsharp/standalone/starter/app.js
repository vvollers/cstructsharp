const status = document.querySelector("#status");
const output = document.querySelector("#output");
const button = document.querySelector("#run");
const definition = "struct header { uint16 kind; uint32 length; };";
const options = { root: "header", littleEndian: true, aligned: false, pointerSize: 8 };

// data holds the parsed value after a read, and a Uint8Array after a write or update.
function requireData(result) {
  if (!result.success) {
    const error = result.error;
    output.textContent = `Operation failed: ${error.code}\n${error.message}\nPath: ${error.path ?? "unknown"}\nOffset: ${error.offset ?? "unknown"}`;
    return null;
  }
  return result.data;
}

function hex(bytes) {
  return Array.from(bytes, (byte) => byte.toString(16).padStart(2, "0")).join(" ");
}

try {
  // A dynamic import lets us display a useful error even if the bundle is missing.
  const { loadCStructSharpWasm, parseWithDebug, serialize, update } =
    await import("../cstructsharp-wasm.js");
  await loadCStructSharpWasm();
  status.textContent = "Ready";
  button.disabled = false;

  button.addEventListener("click", async () => {
    button.disabled = true;
    status.textContent = "Running…";
    output.textContent = "";
    try {
      const input = new Uint8Array([2, 0, 6, 0, 0, 0]);
      const parsed = requireData(await parseWithDebug(definition, input, options));
      if (parsed === null) return;
      const values = parsed;
      const written = requireData(await serialize(definition, { kind: 3, length: 6 }, options));
      if (written === null) return;
      const bytes = written;
      const changed = requireData(await update(definition, bytes, "header.kind", 4, options));
      if (changed === null) return;
      const updatedBytes = changed;
      const reread = requireData(await parseWithDebug(definition, updatedBytes, options));
      if (reread === null) return;
      output.textContent = [
        `Read: ${JSON.stringify(values)}`,
        `Created: ${hex(bytes)}`,
        `Updated: ${hex(updatedBytes)}`,
        `Read again: ${JSON.stringify(reread)}`,
      ].join("\n");
    } catch (error) {
      output.textContent = `JavaScript or runtime error: ${error.message}`;
    } finally {
      status.textContent = "Ready";
      button.disabled = false;
    }
  });
} catch (error) {
  status.textContent = "Could not load WebAssembly";
  output.textContent = `Serve the complete extracted bundle over HTTP. Check the browser Network tab for missing files.\n${error.message}`;
}
