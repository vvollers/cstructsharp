---
title: Use CStructSharp in a browser
description: Run the complete JavaScript starter included in the WebAssembly bundle.
---

# Use CStructSharp in a browser

CStructSharp runs locally in your browser through WebAssembly, often called WASM. Your JavaScript supplies a layout
and bytes; the library returns values. The person using your page does not need .NET installed.

For a first experiment with no setup, [open the header lesson](https://vvollers.github.io/cstructsharp/explorer/#lesson=header).
Read the six bytes, change the first byte to `03`, and read again. The kind changes from `2` to `3`.

## Run the included starter

You need a browser and Node.js to run the included local server. Download `cstructsharp-wasm-v<VERSION>.zip` from
[GitHub Releases](https://github.com/vvollers/cstructsharp/releases). Use a release containing the `starter` directory;
older bundles may contain only the library. Keep the complete extracted archive together:

```text
cstructsharp-wasm/
  cstructsharp-wasm.js
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

[!code-html[Starter HTML](../../../CStructSharpWeb/wasm/starter/index.html)]

[!code-javascript[Starter JavaScript](../../../CStructSharpWeb/wasm/starter/app.js)]

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
