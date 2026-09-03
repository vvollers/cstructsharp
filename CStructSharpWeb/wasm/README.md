# CStructSharp WebAssembly bundle

This directory is a standalone browser distribution of CStructSharp. It runs the managed CStructSharp library locally through .NET WebAssembly; it does not require a server or a .NET runtime on the user's machine.

Keep the complete extracted directory together. It contains `cstructsharp-wasm.js`, the .NET WebAssembly runtime under `_framework/`, `main.js`, `bootstrap.js`, and the runtime configuration file.

## Use from a browser project

```js
import {
  getVersion,
  parseWithDebug,
  serialize,
  update,
} from "./cstructsharp-wasm.js";

const definition = "struct root { byte value; };";
const parsed = await parseWithDebug(definition, new Uint8Array([42]), {
  rootTypeName: "root",
});
console.log(parsed.Data); // JSON string: {"value":42}

const serialized = await serialize(definition, { value: 165 }, {
  rootTypeName: "root",
});
console.log(serialized.Data); // Base64: "pQ=="

const changed = await update(
  definition,
  new Uint8Array([0]),
  "root.value",
  42,
  { rootTypeName: "root" },
);
console.log(changed.Data); // Base64: "Kg=="
```

The functions return the versioned CStructSharp result envelope. Check `Success` before using `Data`; failures contain an `Error` object with `Code`, `Message`, `Offset`, and `Path`.

`serialize` and `update` return their binary result in `Data` as Base64. `parseWithDebug` returns parsed JSON in `Data` and includes field-to-byte mappings in `DebugData`.

## Direct loading

To only load the managed API, use:

```js
import { loadCStructSharpWasm } from "./cstructsharp-wasm.js";

const api = await loadCStructSharpWasm();
console.log(api.getVersion());
```

The bundle must be served over HTTP(S), not opened with `file://`, because browsers restrict module and WebAssembly loading from local files. The server must preserve the `.wasm` content type. The bundle can be copied into any static web application's assets and imported using a relative URL.

The archive is the browser/WASM distribution. For the .NET library and NuGet package, use the regular CStructSharp package.
