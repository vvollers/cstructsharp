# CStructSharp WebAssembly bundle

This bundle reads and writes binary data in a browser using a C-like layout. The browser runs the managed library
locally; its user does not need .NET installed.

## Run your first example

Keep the complete extracted archive together. With Node.js installed, open a terminal in this directory and run:

```sh
node serve.mjs
```

Open `http://127.0.0.1:8080/starter/`, wait for Ready, then select Read, write, and update.
The six-byte header starts with kind 2 and length 6. The page creates kind 3 and updates it to 4.
Press Ctrl+C to stop the local server. Open `http://127.0.0.1:8080/starter/inspector.html` for the larger file inspector.

`starter/index.html` and `starter/app.js` are the complete beginner application. Copy and adapt them for your own
page. They check operation success, decode read JSON, use output byte arrays, and report runtime loading errors separately.

## Use from your JavaScript

```js
import { parseWithDebug } from "./cstructsharp-wasm.js";

try {
  const result = await parseWithDebug(
    "struct header { uint16 kind; uint32 length; };",
    new Uint8Array([2, 0, 6, 0, 0, 0]),
    { rootTypeName: "header" },
  );
  if (result.Success) {
    const values = JSON.parse(result.Data);
    console.log(values.header.kind); // 2; debug parses include the root wrapper
  } else {
    console.error(result.Error.Code, result.Error.Path, result.Error.Offset);
  }
} catch (error) {
  console.error("Loading or JavaScript error:", error.message);
}
```

Also exported: `serialize(definition, value, options)`, `update(definition, bytes, path, value, options)`,
`getVersion()`, and `loadCStructSharpWasm()`. All return promises. Serialize and update return a `Uint8Array` in `Data`.
Use it directly; older examples that call `atob(result.Data)` should remove that conversion.
Pass the selected struct's fields when serializing, without the debug root wrapper.

Keep `cstructsharp-wasm.js`, `main.js`, `bootstrap.js`, the runtime configuration, and `_framework/` together.
Serve over HTTP(S), not `file://`, with `.wasm` served as `application/wasm`. Relative imports work under a deployment
subdirectory when the complete bundle is kept together. The included server is for local development.

Read the [browser API guide](https://vvollers.github.io/cstructsharp/docs/guides/browser/api.html) for options,
large integers, union values, and the differences from C#. Read the
[deployment guide](https://vvollers.github.io/cstructsharp/docs/guides/browser/deployment.html) if loading fails.

## TypeScript

Keep `cstructsharp-wasm.d.ts` beside the public JavaScript entry point. Editors discover the options and
success/failure result types from the same import. After checking `Success`, parse `Data` is JSON text and
serialize/update `Data` is a `Uint8Array`. No explorer source or separate type package is required.
