const find = (id) => document.getElementById(id);
const definition =
  "struct record { uint16 id; uint8 flags; }; struct root { uint16 signature; uint8 version; uint8 count; record records[2]; };";
const options = {
  rootTypeName: "root",
  aligned: false,
  littleEndian: true,
  pointerSize: 8,
  maxArrayElements: 2,
  maxTotalBytesRead: 64,
  maxNestingDepth: 8,
};
let current = null;
let downloadUrl = null;
const hex = (bytes) => Array.from(bytes, (b) => b.toString(16).padStart(2, "0")).join(" ");

function clearDownload() {
  if (downloadUrl) URL.revokeObjectURL(downloadUrl);
  downloadUrl = null;
  find("download").hidden = true;
  find("download").removeAttribute("href");
}

function fail(message) {
  find("result").textContent = message;
  find("patch").disabled = true;
  clearDownload();
}

function dataOrThrow(result) {
  if (!result.Success)
    throw new Error(
      `${result.Error.Code}: ${result.Error.Message} (path ${result.Error.Path ?? "unknown"}, offset ${result.Error.Offset ?? "unknown"})`,
    );
  return result.Data;
}

try {
  const { loadCStructSharpWasm, parseWithDebug, update } = await import("../cstructsharp-wasm.js");
  await loadCStructSharpWasm();
  find("status").textContent = "Ready";
  find("fixture").disabled = false;
  find("file").disabled = false;

  async function inspect(bytes) {
    current = null;
    clearDownload();
    find("fields").replaceChildren();
    find("selection").textContent = "";
    find("bytes").textContent = hex(bytes);
    if (
      bytes.length !== 10 ||
      bytes[0] !== 0x43 ||
      bytes[1] !== 0x53 ||
      bytes[2] !== 1 ||
      bytes[3] !== 2
    ) {
      throw new Error(
        "Expected exactly ten bytes: signature CS, version 1, count 2, and two complete records.",
      );
    }
    const parsed = await parseWithDebug(definition, bytes, options);
    find("result").textContent = JSON.stringify(JSON.parse(dataOrThrow(parsed)), null, 2);
    for (const field of parsed.DebugData) {
      const button = document.createElement("button");
      button.type = "button";
      button.textContent = `${field.DebugStackString} · ${field.Type} · ${field.Value}`;
      button.addEventListener("click", () => {
        find("selection").textContent =
          `Offset ${field.CurPos}; width ${field.EndPos - field.CurPos} bytes; ${hex(bytes.slice(field.CurPos, field.EndPos))}`;
      });
      find("fields").append(button);
    }
    current = bytes;
    find("patch").disabled = false;
  }

  find("fixture").addEventListener("click", () => {
    inspect(new Uint8Array([0x43, 0x53, 1, 2, 1, 0, 0x10, 2, 0, 0x20])).catch((error) =>
      fail(error.message),
    );
  });
  find("file").addEventListener("change", async (event) => {
    try {
      const file = event.target.files[0];
      if (!file) return;
      if (file.size !== 10)
        throw new Error("This fixed-count example accepts a ten-byte file only.");
      await inspect(new Uint8Array(await file.arrayBuffer()));
    } catch (error) {
      fail(error.message);
    }
  });
  find("patch").addEventListener("click", async () => {
    try {
      const flags = Number(find("flags").value);
      if (!current || !Number.isInteger(flags) || flags < 0 || flags > 255)
        throw new Error("Enter an integer from 0 through 255, then reload the fixture if needed.");
      const before = current;
      const result = await update(definition, before, "root.records[1].flags", flags, {
        ...options,
        maxTotalBytesWritten: 10,
      });
      const bytes = dataOrThrow(result);
      if (
        bytes.length !== before.length ||
        bytes.slice(0, 9).some((value, index) => value !== before[index])
      )
        throw new Error("An unrelated byte changed.");
      await inspect(bytes);
      downloadUrl = URL.createObjectURL(new Blob([bytes], { type: "application/octet-stream" }));
      find("download").href = downloadUrl;
      find("download").hidden = false;
    } catch (error) {
      fail(error.message);
    }
  });
} catch (error) {
  find("status").textContent = "Could not load WebAssembly";
  fail(`Serve the complete bundle over HTTP and check missing runtime files. ${error.message}`);
}
window.addEventListener("pagehide", clearDownload);
