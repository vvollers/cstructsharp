---
title: Install and make a first parse
description: Set up CStructSharp and decode a six-byte binary header step by step.
---

# Install and make a first parse

This walkthrough reads the header introduced in [Binary layout basics](binary-layout-basics.md). You will create one
layout, decode six bytes as a dynamic C# object, map the same bytes to a class, and handle truncated input without
throwing.

## Prerequisites

You need:

- a project targeting .NET 8 or .NET 10;
- the .NET SDK for that target; and
- a terminal opened in the directory containing your `.csproj` file.

Add the published package from your project directory:

```powershell
dotnet add package CStructSharp
```

This adds a `PackageReference` to your project and restores the latest published package. A successful command reports
that the reference is compatible with your target framework. CStructSharp publishes support for .NET 8 and .NET 10.

## Use the browser WebAssembly release

For a browser project, download the `cstructsharp-wasm-v<VERSION>.zip` asset from the
[CStructSharp GitHub Releases](https://github.com/vvollers/cstructsharp/releases) page. Extract the complete archive
into a directory served by your application's static file server. Keep `cstructsharp-wasm.js`, `main.js`, `bootstrap.js`,
the runtime configuration file, and `_framework/` together; the JavaScript entry point loads the runtime and assemblies
from those relative paths.

Import the JavaScript library from your application:

```js
import { parseWithDebug, serialize, update } from "./cstructsharp-wasm/cstructsharp-wasm.js";

const definition = "struct root { byte value; };";
const parsed = await parseWithDebug(definition, new Uint8Array([42]), {
  rootTypeName: "root",
});

if (parsed.Success) {
  console.log(parsed.Data); // JSON string containing the parsed value
}

const serialized = await serialize(definition, { value: 165 }, {
  rootTypeName: "root",
});

if (serialized.Success) {
  console.log(serialized.Data); // Base64: "pQ=="
}

const changed = await update(
  definition,
  new Uint8Array([0]),
  "root.value",
  42,
  { rootTypeName: "root" },
);

if (changed.Success) {
  console.log(changed.Data); // Base64: "Kg=="
}
```

The functions return CStructSharp's versioned result envelope. `parseWithDebug` accepts a `Uint8Array` and returns
parsed JSON plus `DebugData`; `serialize` and `update` return binary output as Base64 in `Data`. The bundle runs the
managed library locally in the browser and does not require .NET on the user's machine. Serve it over HTTP(S), not
`file://`, and configure the server to serve `.wasm` files with the WebAssembly media type.

## Define the binary layout

The input contains a two-byte kind followed by a four-byte length:

```c
struct header {
    uint16 kind;
    uint32 length;
};
```

The complete example below is compiled and run as part of this documentation:

[!code-csharp[Compile and decode a fixed header](../examples/Program.cs#api-reference-cstruct)]

Work through it in this order:

1. `new CStruct(...)` checks and prepares the layout. Keep this object and reuse it for other headers with the same
   format.
2. `ReadOnlySpan<byte>` supplies the six input bytes. A span is a temporary view over memory; CStructSharp does not
   keep it after the call returns.
3. `layout.Parse(bytes, "header")` reads the named root and returns a dynamic object. The field names from the layout
   become `header.kind` and `header.length`.
4. `TryReadValue<Header>` reads the same data and maps it to a C# class. This is useful when application code benefits
   from compile-time property names.
5. The final call passes only one byte. That is too short for the header, so `TryReadValue` returns `false` and sets
   the output to its default value.

The expected values are:

```text
header.kind   = 2
header.length = 6
typed.Kind    = 2
typed.Length  = 6
truncated read succeeds = false
```

## Why the root name matters

The string `"header"` selects the declaration to read. Layouts can contain helper structures and more than one
top-level declaration, so passing the root explicitly makes the call clear and avoids depending on declaration
order. Names are case-sensitive.

The constructor defaults are an eight-byte pointer, packed placement, and little-endian byte order. Pointer width
does not affect this layout because it contains no pointer. For a persisted format, pass all three choices explicitly
so the code records the format rather than relying on defaults.

## Verify your result

Run your application from its project directory:

```powershell
dotnet run
```

The exact console output depends on how you print the values, but it should contain kind `2`, length `6`, and a
failed result for the truncated input. If construction fails before any bytes are read, inspect the layout spelling.
If values are unexpectedly large or reversed, check byte order. If reading fails, confirm that the input contains all
six bytes and that `"header"` matches the declaration's case.

## Next steps

- Read [Choose an API](choosing-an-api.md) before deciding between dynamic, typed, stream, and memory calls.
- Use [Read values and paths](reading-values.md) when you need one nested field instead of the whole root.
- Continue the [layout-language tutorial](../language/tutorial/index.md) to add arrays and nested data.
