# CStructSharp

CStructSharp is a .NET library for reading and writing binary data. You describe the data with a small language that
looks like a C struct, then use that description to work with bytes, streams, or memory.

For example, this layout describes a six-byte header:

```c
struct header {
    uint16 kind;
    uint32 length;
};
```

CStructSharp understands this layout and can read values from binary data or write values back to it. It uses its
own portable layout rules. It does not parse complete C header files, run a C preprocessor, or copy the ABI rules of
a particular C compiler.

The current version is `0.2.7`.

## Install

For a .NET application, add the published NuGet package from your project directory:

```powershell
dotnet add package CStructSharp
```

This adds the latest published `CStructSharp` package and restores it for the project. The package targets .NET 8 and
.NET 10. See [Install and make a first parse](https://vvollers.github.io/cstructsharp/docs/guides/install-and-first-parse.html)
for a complete example.

For a browser application, download the `cstructsharp-wasm-v<VERSION>.zip` asset from the
[GitHub Releases](https://github.com/vvollers/cstructsharp/releases) page and extract the complete archive into your
static assets. Do not separate `cstructsharp-wasm.js` from `main.js`, `bootstrap.js`, or `_framework/`.

```js
import { parseWithDebug, serialize } from "./cstructsharp-wasm/cstructsharp-wasm.js";

const definition = "struct root { byte value; };";
const parsed = await parseWithDebug(definition, new Uint8Array([42]), { rootTypeName: "root" });
if (parsed.Success) console.log(parsed.Data);

const serialized = await serialize(definition, { value: 165 }, { rootTypeName: "root" });
if (serialized.Success) console.log(serialized.Data); // Base64: "pQ=="
```

The browser bundle runs CStructSharp locally through WebAssembly. It must be served over HTTP(S), not opened with
`file://`. The archive's `README.md` documents `parseWithDebug`, `serialize`, `update`, and direct runtime loading.

## What it supports

- Fixed-size integers and characters, with a byte order you can choose.
- Structs, unions, enums, typedefs, arrays, strings, bitfields, expressions, and pointers.
- Reading into dynamic objects or regular C# classes.
- Writing complete values or updating one value at a path such as `packet.header.length`.
- Streams, spans, memory, and `IBufferWriter<byte>`.
- Limits for input size, nesting, arrays, strings, pointers, reads, and writes.
- Reusing one compiled layout for many operations and from more than one thread.

Unknown enum values and raw union bytes can be kept without losing information. See `EnumValueResult` and
`UnionValue` in the [API reference](https://vvollers.github.io/cstructsharp/docs/api/CStructSharp.html).

## Where to learn more

- [Project home](https://vvollers.github.io/cstructsharp/)
- [Start with the library](https://vvollers.github.io/cstructsharp/docs/guides/index.html)
- [Read the layout-language manual](https://vvollers.github.io/cstructsharp/docs/language/index.html)
- [Run the examples](https://vvollers.github.io/cstructsharp/docs/examples/index.html)
- [Browse the API reference](https://vvollers.github.io/cstructsharp/docs/api/CStructSharp.html)
- [Open the interactive WASM explorer](https://vvollers.github.io/cstructsharp/explorer/)
- [Learn how the repository is maintained](https://vvollers.github.io/cstructsharp/docs/project/index.html)
- [Read the release notes](CHANGELOG.md)

The source for the website is in `CStructSharp.Docs/`. The machine-readable description of the Portable v1 layout
rules is in
[portable-v1.json](CStructSharp.Docs/contracts/language/portable-v1.json).

## What you need

For normal library work, install:

- Git;
- PowerShell 7 if you want to use the scripts in `tools/`;
- a stable .NET 10 SDK; and
- the .NET 8 runtime if you want to run the `net8.0` tests or fuzz harness.

The repository's `global.json` asks for SDK `10.0.100` or a newer .NET 10 feature band. It does not allow a preview
SDK. Check your installation with:

```powershell
dotnet --version
dotnet --list-runtimes
```

You do not need Node.js for the library, tests, fuzz harness, benchmarks, or NuGet package. You do need it for these
two parts of the repository:

- Documentation checks require Node 24 or 26.
- The web workbench requires Node 22.12 or newer and npm 10 or newer. Its preferred npm version is 11.6.2.

Node 24 with npm 11.6.2 works for both.

## Build and test the project

Run these commands from the repository root:

```powershell
dotnet restore .\CStructSharp.NonWeb.sln
dotnet build .\CStructSharp.NonWeb.sln -c Release --no-restore
dotnet test .\CStructSharpTests\CStructSharpTests.csproj -c Release --no-build
```

Here is what each command does:

1. `restore` downloads the required NuGet packages.
2. `build` compiles the library, tests, fuzz harness, and benchmarks.
3. `test` runs the test suite on both .NET 8 and .NET 10.

The build step compiles the test, fuzz, and benchmark programs, but it does not run them. A successful test run
shows one result for `net8.0` and one for `net10.0`.

To build only the library on .NET 10:

```powershell
dotnet restore .\CStructSharp\CStructSharp.csproj
dotnet build .\CStructSharp\CStructSharp.csproj -c Release -f net10.0 --no-restore
```

## Read your first value

```csharp
using CStructSharp;

var definition = """
    struct header {
        uint16 kind;
        uint32 length;
    };
    """;

var layout = new CStruct(definition);
using var input = new MemoryStream(
    new byte[] { 0x02, 0x00, 0x06, 0x00, 0x00, 0x00 });

dynamic header = layout.ParseStream(input, "header");

Console.WriteLine(header.kind);   // 2
Console.WriteLine(header.length); // 6
```

By default, CStructSharp uses packed fields, little-endian values, and eight-byte pointers. Set the pointer size,
alignment, and byte order yourself when they are part of a stored file format or network protocol. The
[Portable v1 guide](CStructSharp.Docs/language/portable-v1-reference.md) explains the layout rules and the places
where they differ from C.

## How the projects fit together

Most projects use the main `CStructSharp` library:

| Path | What it is | What it uses |
| --- | --- | --- |
| `CStructSharp/` | The library | Pidgin for parsing; no other project in this repository |
| `CStructSharp.Fuzz/` | A program that tries many unusual inputs | The library |
| `CStructSharpTests/` | The MSTest test suite | The library, fuzz support, and test data from `CStructSharp.Docs/` |
| `CStructSharp.Benchmarks/` | Performance tests | The library and BenchmarkDotNet |
| `CStructSharpWeb/wasm/` | The .NET bridge used in a browser | The library and the WebAssembly workload |
| `CStructSharpWeb/` | The Vue web workbench | The published WebAssembly bridge |
| `CStructSharp.Docs/` | The documentation website and examples | A .NET 10 build of the library, DocFX, and Node tools |
| `CStructSharp.PackageConsumer/` | A test app for the NuGet package | A built `.nupkg`, not the library project |

The main library does not depend on the tests, fuzz harness, benchmarks, website, or documentation.

There are two solution files:

- `CStructSharp.NonWeb.sln` contains the library, tests, fuzz harness, and benchmarks. Use this for normal work.
- `CStructSharp.sln` adds the .NET WebAssembly bridge. Use it when you are working on browser integration.

Neither solution builds the Vue app, the documentation website, the documentation examples, or the package
consumer. The next sections show how to work with them.

## Run one part of the repository

### Tests

Run every test on both supported .NET versions:

```powershell
dotnet test .\CStructSharpTests\CStructSharpTests.csproj -c Release
```

Add `-f net8.0` or `-f net10.0` when you only need one target framework.

### Fuzz harness

The fuzz harness changes valid and invalid inputs in small ways. It helps find crashes and unexpected exceptions
that an ordinary test may miss. This runs the standard set on .NET 10:

```powershell
dotnet run --project .\CStructSharp.Fuzz\CStructSharp.Fuzz.csproj -c Release -f net10.0 -- --target all
```

Run it again with `-f net8.0` when you need to check both supported .NET versions.

### Benchmarks

This runs the default short BenchmarkDotNet job:

```powershell
dotnet run --project .\CStructSharp.Benchmarks\CStructSharp.Benchmarks.csproj -c Release -- --filter '*'
```

Benchmark results are written below the ignored `artifacts/` directory.

### Web workbench

The web workbench has two parts: a .NET WebAssembly bridge and a Vue app. Build them together with:

```powershell
dotnet workload restore .\CStructSharpWeb\wasm\CStructSharpWeb.Wasm.csproj
npm --prefix .\CStructSharpWeb ci
npm --prefix .\CStructSharpWeb run build
```

The build places the browser-ready bridge in `CStructSharpWeb/public/wasm/` and the finished website in
`CStructSharpWeb/dist/`.

After the first build, you can use:

```powershell
npm --prefix .\CStructSharpWeb run dev
```

This starts the Vite development server. It does not rebuild the C# code when that code changes. Run
`npm --prefix .\CStructSharpWeb run build:wasm` after a C# change.

The full managed solution can also be built directly:

```powershell
dotnet workload restore .\CStructSharpWeb\wasm\CStructSharpWeb.Wasm.csproj
dotnet restore .\CStructSharp.sln
dotnet build .\CStructSharp.sln -c Release --no-restore
```

This builds the .NET bridge, but not the Vue app.

### Documentation website

Run the complete documentation check with:

```powershell
.\tools\Validate-Documentation.ps1
```

It builds the library, examples, API pages, and website, then checks the writing, links, and browser behavior.

After a successful build, use this faster command while editing pages:

```powershell
.\tools\Build-Documentation.ps1 -NoBuild -Serve
```

It starts a local website at `http://localhost:8080`. See
[CStructSharp.Docs/README.md](CStructSharp.Docs/README.md) if setup or browser checks fail.

### NuGet package

Create a local package and test it as a real dependency:

```powershell
dotnet pack .\CStructSharp\CStructSharp.csproj -c Release -o .\artifacts\package
$package = Get-ChildItem .\artifacts\package -Filter '*.nupkg' | Where-Object Extension -eq '.nupkg'
$symbols = Get-ChildItem .\artifacts\package -Filter '*.snupkg'
.\tools\Validate-Package.ps1 -PackagePath $package.FullName -SymbolPackagePath $symbols.FullName
.\tools\Test-PackageConsumer.ps1 -PackageDirectory .\artifacts\package
```

Start with an empty `artifacts/package/` directory so the scripts find one package and one symbol package. These
commands create and test files on your machine. They do not publish anything.

## Compatibility files

Files below `CStructSharp.Docs/contracts/` record the public API, layout language, browser interface, and performance
limits. Automated checks compare the code with these files. If a check finds a difference, first decide whether the
code changed by mistake or whether the public behavior really needs to change. Do not update a contract file only
to make the check pass.

The .NET API and browser interface have separate compatibility files because they can change at different times.

## Contributing

Read [CONTRIBUTING.md](CONTRIBUTING.md) before changing behavior. A bug fix should normally start with a test that
shows the problem. Keep the fix focused, run the checks for the part you changed, and update the related docs when
users will notice the change.

## License

CStructSharp uses the [MIT License](LICENSE.txt).

You can report questions and bugs in the
[CStructSharp issue tracker](https://github.com/vvollers/CStructSharp/issues).
