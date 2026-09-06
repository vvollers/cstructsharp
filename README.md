# CStructSharp

CStructSharp reads and writes binary data using a description that looks like a C struct. Give it a layout and
some bytes, and it gives you named values. Give it values, and it can create bytes or change a field in existing
data. Use it from C# or JavaScript in a browser.

## Choose your starting point

- [Try the browser lesson](https://vvollers.github.io/cstructsharp/explorer/#lesson=header): no installation.
- [Use C#](https://vvollers.github.io/cstructsharp/docs/guides/install-and-first-parse.html): create a console app.
- [Use JavaScript and WASM](https://vvollers.github.io/cstructsharp/docs/guides/browser/index.html): run a complete browser starter.

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

## When to use it

Use CStructSharp to read a documented device message, inspect a file header, or change a fixed field in a binary
record. A hand-written `BinaryReader` may be sufficient for a few fixed fields. A reusable layout description is
useful when several operations share a format or the format is supplied at runtime.

The language has its own portable rules. It does not compile C, import arbitrary C headers, discover an unknown
format, or automatically match a native compiler's struct layout. Floating-point and boolean fields are currently
unsupported. See [differences from C](https://vvollers.github.io/cstructsharp/docs/language/differences-from-c.html)
before translating a header.

Supported features include integers, structs, unions, enums, arrays, text, bitfields, expressions, and stored pointers.
You can read dynamic objects or C# classes, write new bytes, and update a path such as `packet.header.length`.
Advanced APIs support streams, spans, memory, buffer writers, explicit limits, and reuse of a compiled layout.

## Use the browser bundle

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
`CStructSharp/CStructSharp.csproj` records the development version. Historical compatibility snapshots have their
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
