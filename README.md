# CStructSharp

CStructSharp reads and writes binary data using a description that looks like a C struct. Give it a layout and
some bytes, and it gives you named values. Give it values, and it can create bytes or change a field in existing
data. Use it from C#, Node.js, or JavaScript in a browser.

## Choose your starting point

- [Try the browser lesson](https://vvollers.github.io/cstructsharp/explorer/#lesson=header): no installation.
- [Use C#](https://vvollers.github.io/cstructsharp/docs/guides/install-and-first-parse.html): create a console app.
- [Use JavaScript and WASM](https://vvollers.github.io/cstructsharp/docs/guides/browser/index.html): install the npm package for Node.js or browsers.

## Read your first value in C#

Install a stable .NET 10 SDK. These commands work in PowerShell or a Unix shell:

```sh
dotnet new console -n BinaryHeader -f net10.0
cd BinaryHeader
dotnet add package CStructSharp
```

Replace `Program.cs` with this complete program, then run `dotnet run`:

```csharp
using CStructSharp;

var layout = new CStruct("struct header { uint16 kind; uint32 length; };");
byte[] bytes = { 0x02, 0x00, 0x06, 0x00, 0x00, 0x00 };
dynamic header = layout.Parse(bytes.AsSpan(), "header");

Console.WriteLine($"kind = {header.kind}");
Console.WriteLine($"length = {header.length}");
```

Output:

```text
kind = 2
length = 6
```

The layout names the fields. The byte array supplies the data. The result contains the values:

| Field | Byte offsets | Input bytes | Value |
| --- | --- | --- | --- |
| `kind` | 0–1 | `02 00` | 2 |
| `length` | 2–5 | `06 00 00 00` | 6 |

By default, fields are packed together, numbers use little-endian byte order, and pointers occupy eight bytes.
The [binary layout basics](https://vvollers.github.io/cstructsharp/docs/guides/binary-layout-basics.html) explain these choices.
Try changing `0x02` to `0x03`: `kind` becomes `3`.

## A portable C struct definition language

Turn a binary format into an executable specification. CStructSharp combines familiar C struct syntax with
portable layout rules, giving you one definition for decoding records, generating bytes, inspecting offsets, and
updating individual fields. Load definitions at runtime and use the same format description from C#, Node.js, or
a browser to build protocol tools, file inspectors, and binary editors.

- **Model rich binary data.** Compose nested structs, overlapping union views, enums with explicit integer storage,
  and reusable `typedef` aliases. Represent values with fixed-width integers, IEEE-754 floats, booleans, bitfields,
  fixed character buffers, and terminated ASCII, UTF-8, or UTF-16 strings.
- **Let the data determine the shape.** Use arithmetic and bitwise expressions, `#define` constants, earlier fields,
  and caller-supplied variables to size arrays. Describe count-prefixed payloads, multidimensional tables with
  runtime-sized outer dimensions, and arrays of structured records directly in the definition.
- **Control the bytes precisely.** Mix little- and big-endian primitives in one record with `<` and `>` suffixes.
  Choose packed or aligned layout, refine alignment with `@align(N)`, reserve bits with unnamed bitfields, and
  assert expected field offsets with `@N`. Type widths follow portable rules, and pointer width is configured
  explicitly, so the format's interpretation stays independent of the host process.
- **Navigate beyond sequential records.** Describe stored pointers, pointer arrays, and multiple levels of
  indirection. Read targets using absolute or relative addressing, or inspect stored addresses without following
  them. Select nested values with paths such as `packet.samples[2].value` or `root.ptr.value`.

Prepare a layout once and reuse it to read dynamic objects or C# classes, write new records, and update selected
fields in existing data. The definition keeps the format's structure and byte-level rules together as your tools
grow from a single header parser into a complete format workbench.

Start with the [language tutorial](https://vvollers.github.io/cstructsharp/docs/language/tutorial/index.html),
explore the [language reference](https://vvollers.github.io/cstructsharp/docs/language/index.html), or consult
[differences from C](https://vvollers.github.io/cstructsharp/docs/language/differences-from-c.html) when adapting
an existing header.

## Use JavaScript in Node.js or a browser

The npm package includes the prebuilt WebAssembly runtime and TypeScript declarations:

```sh
npm install cstructsharp
```

Save this as `example.mjs` and run `node example.mjs` with Node.js 22.14 or later:

```js
import { parseWithDebug } from "cstructsharp";

const result = await parseWithDebug(
  "struct header { uint16 kind; uint32 length; };",
  new Uint8Array([2, 0, 6, 0, 0, 0]),
  { rootTypeName: "header" },
);
if (!result.Success) throw new Error(result.Error.Message);
console.log(JSON.parse(result.Data).header.kind); // 2
```

Node loads the installed runtime from disk; no .NET SDK or server is needed. Browser applications use the same
API with the `cstructsharp/vite` plugin or an explicit static-asset directory. See the
[npm package README](packages/cstructsharp/README.md) for complete setup, write/update examples, and supported hosts.
Until the first npm publication, contributors can install the tested `.tgz` produced by `npm run pack:npm`
in `apps/workshop`. The [release guide](docs/project/release-process.md) covers the first publication.

## Use the standalone browser bundle

Download `cstructsharp-wasm-v<VERSION>.zip` from [GitHub Releases](https://github.com/vvollers/cstructsharp/releases).
Extract the complete archive. With Node.js installed, run `node serve.mjs` in that directory and open
`http://127.0.0.1:8080/starter/`. The included page reads, writes, and updates the same header.

Browser users do not need .NET installed. Keep the runtime files together and serve them over HTTP(S).
The [browser guide](https://vvollers.github.io/cstructsharp/docs/guides/browser/index.html) explains the files,
JavaScript API, result conversion, and common loading errors.

## Continue learning

- [Learn step by step](https://vvollers.github.io/cstructsharp/docs/guides/index.html)
- [Find an executable recipe](https://vvollers.github.io/cstructsharp/docs/guides/recipes/index.html)
- [Learn the layout language](https://vvollers.github.io/cstructsharp/docs/language/tutorial/index.html)
- [Look up the C# API](https://vvollers.github.io/cstructsharp/docs/api/CStructSharp.html)
- [Read release notes](https://github.com/vvollers/CStructSharp/blob/main/CHANGELOG.md)

The package targets .NET 8 and .NET 10. Release assets describe published versions; the repository's
`src/CStructSharp/CStructSharp.csproj` records the development version. Historical compatibility snapshots have their
own labels and do not identify the latest release.

## Work on the project

Package consumers do not need to clone or build this repository. Contributors should start with the
[repository setup guide](https://vvollers.github.io/cstructsharp/docs/project/getting-started.html), then follow
[build instructions](https://vvollers.github.io/cstructsharp/docs/project/building.html),
[testing](https://vvollers.github.io/cstructsharp/docs/project/testing.html), and
[contribution guidance](https://github.com/vvollers/CStructSharp/blob/main/CONTRIBUTING.md).
The [repository map](https://vvollers.github.io/cstructsharp/docs/project/repository-map.html) explains the projects.

CStructSharp uses the [MIT License](https://github.com/vvollers/CStructSharp/blob/main/LICENSE.txt).
Report questions and bugs in the [issue tracker](https://github.com/vvollers/CStructSharp/issues).
