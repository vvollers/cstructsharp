---
title: Use CStructSharp from JavaScript
description: Install the npm package for Node.js or browsers, or run the standalone WebAssembly starter.
---

# Use CStructSharp from JavaScript

CStructSharp runs locally in Node.js and browsers through WebAssembly, often called WASM. Your JavaScript supplies
a layout and bytes; the library returns values. Consumers do not need .NET installed.

## Install from npm

```sh
npm install cstructsharp
```

On Node.js 22.14 or later, save this as `example.mjs` and run `node example.mjs`:

```js
import { parseWithDebug } from "cstructsharp";

try {
  const result = await parseWithDebug(
    "struct header { uint16 kind; uint32 length; };",
    new Uint8Array([2, 0, 6, 0, 0, 0]),
    { rootTypeName: "header" },
  );
  if (!result.Success) throw new Error(result.Error.Message);
  console.log(JSON.parse(result.Data).header.kind); // 2
} catch (error) {
  console.error(error);
}
```

Node loads the runtime directly from the installed package, with no HTTP server or runtime downloads.
`Buffer` is accepted as byte input. CommonJS callers can use `await import("cstructsharp")` inside an async
function. Synchronous `require()` and worker execution are not supported.

For a Vite browser app, add this to `vite.config.js`, then use the same API import in your app:

```js
import { defineConfig } from "vite";
import { cstructsharp } from "cstructsharp/vite";

export default defineConfig({ plugins: [cstructsharp()] });
```

The plugin handles runtime assets in development and production. For other tools and nested deployment paths,
see [deployment](deployment.md). TypeScript declarations ship with the package.

For a first experiment with no setup, [open the header lesson](https://vvollers.github.io/cstructsharp/explorer/#lesson=header).
Read the six bytes, change the first byte to `03`, and read again. The kind changes from `2` to `3`.

## Alternative: run the standalone browser starter

You need a browser and Node.js to run the included local server. Download `cstructsharp-wasm-v<VERSION>.zip` from
[GitHub Releases](https://github.com/vvollers/cstructsharp/releases). Use a release containing the `starter` directory;
older bundles may contain only the library. Keep the complete extracted archive together:

```text
cstructsharp-wasm/
  cstructsharp-wasm.js
  cstructsharp-api.js
  main.js
  bootstrap.js
  CStructSharpWeb.Wasm.runtimeconfig.json
  _framework/
  serve.mjs
  starter/
    index.html
    app.js
```

Open a terminal in `cstructsharp-wasm` and run:

```sh
node serve.mjs
```

Open `http://127.0.0.1:8080/starter/`. Wait for **Ready**, then select **Read, write, and update**.
Press Ctrl+C in the terminal to stop the server. It listens only on your own computer.

The page reads `02 00 06 00 00 00`, creates a header with kind `3`, changes the kind to `4`, and reads it again.
The created bytes are `03 00 06 00 00 00`; the updated bytes are `04 00 06 00 00 00`.
The parsed JSON contains a root object named `header` with `kind` and `length` fields.

## Complete page and JavaScript

These are the actual files included in the bundle. You can copy them into a `starter` directory beside the runtime.
The script import uses `../` to reach its parent directory.

[!code-html[Starter HTML](../../../src/CStructSharp.Wasm/starter/index.html)]

[!code-javascript[Starter JavaScript](../../../src/CStructSharp.Wasm/starter/app.js)]

`type="module"` allows JavaScript imports. `await` waits for the runtime and the operation to finish.
The result's `Success` field tells you whether the operation worked. Parse `Data` is JSON text; write and update
`Data` is a `Uint8Array` ready to use. The starter decodes the read JSON and keeps operation failures separate
from loading errors.

## Try a change

In `app.js`, change the update replacement from `4` to `5`, save, and reload the page. Predict the first output byte.
Answer: the updated bytes start with `05`; the final read reports kind `5`. The length stays `6`.

To see a failure, remove the final zero from the input array. The page reports `read-failed`. Restore it to fix the
input. If the page never reaches Ready, follow [Loading and deployment](deployment.md).

Continue with the [JavaScript API and value guide](api.md). The C# API has additional stream and memory operations;
the browser API offers parse, serialize, and update rather than every managed method.
