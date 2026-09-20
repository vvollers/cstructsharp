---
title: Repository map
description: Find the project, tests, tools, and generated outputs that belong to a proposed change.
---

# Repository map

Most changes should begin in the smallest project that owns the behavior. The table below shows the main directories
and which direction their dependencies point.

| Path | What belongs here | Direct project/package relationship |
| --- | --- | --- |
| `src/CStructSharp.Core/` | Shared sources, not a project: the parser, expressions, compiled model, codec descriptors, diagnostics texts, introspection, and options, compiled into both the library and the generator (`src/CStructSharp.Core/README.md` lists the rules) | Included by the two projects below |
| `src/CStructSharp/` | Public library; one folder per pipeline stage with a matching namespace (`src/CStructSharp/README.md` maps folders to roles); `Generated/` is the runtime support generated code calls | No runtime package dependencies; packs the generator as an analyzer |
| `src/CStructSharp.Generators/` | The `[CStructLayout]`/`[CStructMapped]` source generator and the `CSG` analyzer (`netstandard2.0`, Roslyn 4.8), shipped inside the package under `analyzers/dotnet/cs` | Compiles the Core sources; references only Roslyn |
| `tests/CStructSharpTests/` | Unit, integration, regression, property, limit, concurrency, and compatibility tests; `Reference/` holds the frozen Pidgin grammar used only by the parser differential tests | References core and fuzz support, plus Pidgin (test-only) |
| `tests/CStructSharp.Generators.Tests/` | Generator snapshot, parity, analyzer, and option tests (`Snapshots/*.g.cs` are the golden files; `UPDATE_SNAPSHOTS=1` rewrites them) | References the generator and core |
| `tests/CStructSharp.Generated.Parity/` | Every layout fixture generated into one project (`Layouts.g.cs` from `tools/quality/generate-parity-layouts.mjs`) and compared with the runtime on the same bytes | References core, uses the generator as an analyzer |
| `tests/CStructSharp.Fuzz/` | Bounded fuzz targets and replay corpus, including the generated-differential target | References core, uses the generator as an analyzer |
| `benchmarks/CStructSharp.Benchmarks/` | BenchmarkDotNet timing and allocation scenarios | References core |
| `benchmarks/CStructSharp.FixtureTool/` and `benchmarks/fixtures/` | The seeded fixture corpus shared by the .NET, Node, and browser harnesses, and the tool that records its managed expectations | References core |
| `benchmarks/js/` | Node and headless-Chromium harness for the WASM bridge | Loads its own staged bundle |
| `tests/CStructSharp.PackageConsumer/` and `tests/CStructSharp.Memory.PackageConsumer/` | Small external-style apps that install a built package (the second uses only the memory namespace) | Use the packed NuGet file, not the core project |
| `tests/CStructSharp.AotConsumer/` | A Native AOT publication of the starter, typed reads, writes, generated layouts, mapped classes, and diagnostics | References core and the generator, published with `PublishAot` in CI |
| `docs/` | DocFX pages, examples, site assets, browser checks, and machine-readable reference data | Reads a prebuilt core net10 assembly |
| `src/CStructSharp.Wasm/` | Managed WebAssembly bridge (exports, DTOs, JSON projection) | References core |
| `packages/cstructsharp/` | Public npm package: loaders, Vite plugin, README, declarations, the JavaScript adapter sources (`src/`), and the standalone bundle pieces (`standalone/`) | Packages the prebuilt WASM bridge for Node.js and browsers |
| `apps/explorer/` | Independent test/lesson explorer | Loads the published WASM adapter output |
| `apps/inspector/` | Independent binary inspector UI, format examples, and browser checks | Stages the repository WASM publication |
| `contracts/` | Reviewed compatibility, fixture, quality, and documentation inputs | Read by managed tests, validators, and DocFX |
| `tools/` | Node validation, measurement, packaging, release, and documentation scripts; `lib/` holds their shared helpers | Takes explicit files/projects as inputs |
| `.github/workflows/` | Continuous integration, scheduled mutation, docs, and release-candidate automation | Runs pinned actions and repository scripts |

`CStructSharp.NonWeb.sln` contains core, the generator, tests, fuzz, and benchmarks. Use it for routine development.
`CStructSharp.sln` adds the WASM project and belongs to final integration. The package-consumer project stays outside
both solutions because its package does not exist during the solution's initial restore.

## Dependency direction

The core library does not reference tests, tools, documentation, or frontends. This keeps the published package
small and prevents development-only dependencies from reaching users.

Place shared public behavior in the core and test it through managed tests. Place JSON or browser representation
rules in the adapter. A frontend workaround should not become a second implementation of core parsing or writing.

When you are unsure where a change belongs, ask which project can own it without depending on a higher-level UI,
test, or packaging concern. Then use [Architecture](architecture.md) to find the relevant execution stage.
