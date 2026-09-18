# CStructSharp

<p align="center">
  <a href="LICENSE.txt"><img alt="License" src="https://img.shields.io/github/license/vvollers/cstructsharp"></a>
  <a href="https://www.npmjs.com/package/cstructsharp"><img alt="npm version" src="https://img.shields.io/npm/v/cstructsharp"></a>
  <a href="https://www.npmjs.com/package/cstructsharp"><img alt="npm unpacked size, including WASM" src="https://img.shields.io/npm/unpacked-size/cstructsharp?label=npm%20unpacked"></a>
  <a href="https://www.nuget.org/packages/CStructSharp"><img alt="NuGet version" src="https://img.shields.io/nuget/v/CStructSharp"></a>
  <a href="https://github.com/vvollers/cstructsharp/releases/latest"><img alt="NuGet package download size" src="https://img.shields.io/endpoint?url=https%3A%2F%2Fvvollers.github.io%2Fcstructsharp%2Fbadges%2Fnuget-size.json"></a>
</p>
<p align="center">
  <a href="https://github.com/vvollers/cstructsharp/actions/workflows/ci.yml"><img alt="Managed CI" src="https://github.com/vvollers/cstructsharp/actions/workflows/ci.yml/badge.svg?branch=main&amp;event=push"></a>
  <a href="https://vvollers.github.io/cstructsharp/badges/"><img alt="C# line coverage on .NET 10" src="https://img.shields.io/endpoint?url=https%3A%2F%2Fvvollers.github.io%2Fcstructsharp%2Fbadges%2Fline-coverage.json"></a>
  <a href="https://vvollers.github.io/cstructsharp/badges/"><img alt="C# branch coverage on .NET 10" src="https://img.shields.io/endpoint?url=https%3A%2F%2Fvvollers.github.io%2Fcstructsharp%2Fbadges%2Fbranch-coverage.json"></a>
  <a href="https://vvollers.github.io/cstructsharp/badges/"><img alt="C# test results on .NET 10" src="https://img.shields.io/endpoint?url=https%3A%2F%2Fvvollers.github.io%2Fcstructsharp%2Fbadges%2Ftests.json"></a>
</p>

CStructSharp reads and writes binary data using a description that looks like a C struct. Give it a layout and
some bytes, and it gives you named values. Give it values, and it can create bytes or change a field in existing
data. Use it from C#, Node.js, or JavaScript in a browser.

**Zero runtime package dependencies.** The core .NET library uses only the .NET runtime, keeping integration
simple and your application's dependency tree small.

## Choose your starting point

- [Open the binary inspector](https://vvollers.github.io/cstructsharp/inspector/): apply a layout to one of your own files in the browser, no installation.
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
using CStructSharp.Values;

var layout = new CStruct("struct header { uint16 kind; uint32 length; };");
byte[] bytes = { 0x02, 0x00, 0x06, 0x00, 0x00, 0x00 };
StructValue header = layout.Parse(bytes, "header");

Console.WriteLine($"kind = {header.Get<ushort>("kind")}");
Console.WriteLine($"length = {header.Get<uint>("length")}");
```

Output:

```text
kind = 2
length = 6
```

The layout names the fields. The byte array supplies the data. The result is a `StructValue`: read a member typed
with `header.Get<ushort>("kind")`, or declare it `dynamic` and write `header.kind`. The values:

| Field | Byte offsets | Input bytes | Value |
| --- | --- | --- | --- |
| `kind` | 0–1 | `02 00` | 2 |
| `length` | 2–5 | `06 00 00 00` | 6 |

By default, fields are packed together, numbers use little-endian byte order, and pointers occupy eight bytes.
The [binary layout basics](https://vvollers.github.io/cstructsharp/docs/guides/binary-layout-basics.html) explain these choices.
For the background, read [how C structs occupy memory](https://vvollers.github.io/cstructsharp/docs/guides/native-c-memory.html) and
[memory addresses and stored data](https://vvollers.github.io/cstructsharp/docs/guides/memory-and-stored-data.html).
See [reading values](https://vvollers.github.io/cstructsharp/docs/guides/reading-values.html) for managed result types and the
[JavaScript API](https://vvollers.github.io/cstructsharp/docs/guides/browser/api.html) for browser results.
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
  and caller-supplied variables to size one-dimensional arrays. Select conditional fields with `if`/`else` or `switch`. Describe count-prefixed payloads, fixed multidimensional tables, and arrays of structured records directly in the definition.
- **Control the bytes precisely.** Mix little- and big-endian primitives in one record with `<` and `>` suffixes.
  Choose packed or aligned layout, refine alignment with `@align(N)`, reserve bits with unnamed bitfields, and
  assert expected field offsets with `@N`. Type widths follow portable rules, and pointer width is configured
  explicitly, so the format's interpretation stays independent of the host process.
- **Navigate beyond sequential records.** Describe stored pointers, pointer arrays, and multiple levels of
  indirection. Read targets using absolute or relative addressing, or inspect stored addresses without following
  them. Select nested values with paths such as `packet.samples[2].value` or `root.ptr.value`.
- **Analyze memory images.** `CStructSharp.Memory` adds unsigned address spaces, mapped regions, BTF/ISF type
  import, bounded traversal, and offline patches, with the same zero-dependency runtime; see the
  [memory-analysis guide](https://vvollers.github.io/cstructsharp/docs/guides/memory-analysis.html) and the
  [runnable synthetic consumer](https://vvollers.github.io/cstructsharp/docs/examples/memory-analysis/index.html).

Prepare a layout once and reuse it to read dynamic objects or C# classes, write new records, and update selected
fields in existing data. The definition keeps the format's structure and byte-level rules together as your tools
grow from a single header parser into a complete format explorer. The library is trim-safe and Native AOT
compatible; see [typed values](https://vvollers.github.io/cstructsharp/docs/guides/typed-values.html#trimming-and-native-aot) for the one rule about
nested classes.

Start with the [language tutorial](https://vvollers.github.io/cstructsharp/docs/language/tutorial/index.html),
explore the [language reference](https://vvollers.github.io/cstructsharp/docs/language/index.html), or consult
[differences from C](https://vvollers.github.io/cstructsharp/docs/language/differences-from-c.html) when adapting
an existing header.

## Why CStructSharp instead of …

| If you would otherwise use | CStructSharp instead |
| --- | --- |
| Manual offsets with `BinaryReader` / `BinaryPrimitives` | The layout text names every field, offset, width, and byte order once; reads, writes, updates, address lookups, and the debug byte map all come from that one description, and a change to the format is a change to the text. |
| `[StructLayout]` structs with `MemoryMarshal` | Portable widths never depend on the host process; layouts load at run time, so a tool can accept formats it did not compile against, and variable-length arrays, conditional fields, pointers, and strings are part of the description rather than hand code. |
| A source generator or a serializer | No build step and no generated types to keep in sync: the same text drives C#, Node.js, and the browser, and the parsed result is a typed `StructValue` (or a POCO through `ReadValue<T>`) either way. |
| Kaitai Struct or another schema language | The schema is C: an existing header or a `dissect.cstruct` definition is the input, with `#define`, `#ifdef`, and `#pragma pack` honored, so format knowledge that already exists as C stays C. |
| dissect.cstruct (Python) | The same definition language and habits on .NET and in JavaScript, with a compiled layout cache, bounded read budgets, trim-safe Native AOT support, and a [migration guide](https://vvollers.github.io/cstructsharp/docs/guides/migrating-from-dissect.html) for the few places the two libraries read bytes differently. |

## Use JavaScript in Node.js or a browser

Read large files, buffers, and streamed binary input with automatic paging and worker execution. The
[large-data guide](https://vvollers.github.io/cstructsharp/docs/guides/browser/large-data.html) shows how to pass
`File`, `Blob`, byte views, fetch responses, and Node streams directly to `parse` or `parseWithDebug`.

The npm package includes the prebuilt WebAssembly runtime and TypeScript declarations:

```sh
npm install cstructsharp
```

Save this as `example.mjs` and run `node example.mjs` with Node.js 22.14 or later:

```js
import { parse } from "cstructsharp";

const result = await parse(
  "struct header { uint16 kind; uint32 length; };",
  new Uint8Array([2, 0, 6, 0, 0, 0]),
  { root: "header" },
);
if (!result.success) throw new Error(result.error.message);
console.log(result.data.kind); // 2
```

`parse` returns the values; `parseWithDebug` additionally lists each field's byte range for a hex viewer. Node
loads the installed runtime from disk; no .NET SDK or server is needed. Browser applications use the same API
with the `cstructsharp/vite` plugin or an explicit static-asset directory. See the
[npm package README](https://www.npmjs.com/package/cstructsharp) for complete setup, write/update examples, and
supported hosts.

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

## Versioning and support

CStructSharp follows semantic versioning and is at major version 0: a minor release (0.5 → 0.6) may change the
public API, the layout language, or the JavaScript contract, and the [changelog](https://github.com/vvollers/cstructsharp/blob/main/CHANGELOG.md)
marks every such change **Breaking** with the migration; a patch release never does. Pin `0.5.*` in a project that
must not absorb breaking changes. The managed API baseline (`contracts/api/managed-rc1`) and the browser contract
(`contracts/api/browser-rc1`, `contractVersion` 8) are reviewed together with each change; a breaking JavaScript
change increments the contract version.

The NuGet package targets .NET 8 (LTS) and .NET 10 (LTS); a target is dropped in the first minor release after
Microsoft ends its support. The npm package supports the Node.js releases that are active or in maintenance
(currently 22.14 and later) and evergreen Chromium, Firefox, and WebKit browsers. Release assets describe published
versions; the repository's `src/CStructSharp/CStructSharp.csproj` records the development version.

## Work on the project

Package consumers do not need to clone or build this repository. Contributors should start with the
[repository setup guide](https://vvollers.github.io/cstructsharp/docs/project/getting-started.html), then follow
[build instructions](https://vvollers.github.io/cstructsharp/docs/project/building.html),
[testing](https://vvollers.github.io/cstructsharp/docs/project/testing.html), and
[contribution guidance](https://github.com/vvollers/CStructSharp/blob/main/CONTRIBUTING.md).
The [repository map](https://vvollers.github.io/cstructsharp/docs/project/repository-map.html) explains the projects.

CStructSharp uses the [MIT License](https://github.com/vvollers/CStructSharp/blob/main/LICENSE.txt).
Report questions and bugs in the [issue tracker](https://github.com/vvollers/CStructSharp/issues).
