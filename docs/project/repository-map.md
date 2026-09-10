---
title: Repository map
description: Find the project, tests, tools, and generated outputs that belong to a proposed change.
---

# Repository map

Most changes should begin in the smallest project that owns the behavior. The table below shows the main directories
and which direction their dependencies point.

| Path | What belongs here | Direct project/package relationship |
| --- | --- | --- |
| `src/CStructSharp/` | Public library, layout parser/preparation, codecs, reads, writes, and updates | Uses the Pidgin runtime package |
| `tests/CStructSharpTests/` | Unit, integration, regression, property, limit, concurrency, and compatibility tests | References core and fuzz support |
| `tests/CStructSharp.Fuzz/` | Bounded fuzz targets and replay corpus | References core |
| `benchmarks/CStructSharp.Benchmarks/` | BenchmarkDotNet timing and allocation scenarios | References core |
| `tests/CStructSharp.PackageConsumer/` | A small external-style app that installs a built package | Uses the packed NuGet file, not the core project |
| `docs/` | DocFX pages, examples, site assets, browser checks, and machine-readable reference data | Reads a prebuilt core net10 assembly |
| `src/CStructSharp.Wasm/` | Managed WebAssembly adapter and standalone package source | References core |
| `packages/cstructsharp/` | Public npm package loaders, Vite plugin, and package README | Packages the prebuilt WASM adapter for Node.js and browsers |
| `apps/workshop/` | Independent test/lesson workshop | Loads the published WASM adapter output |
| `apps/inspector/` | Independent binary inspector UI, format examples, and browser checks | Stages the repository WASM publication |
| `contracts/` | Reviewed compatibility, fixture, quality, and documentation inputs | Read by managed tests, validators, and DocFX |
| `tools/` | Validation, measurement, package, and documentation scripts | Takes explicit files/projects as inputs |
| `.github/workflows/` | Continuous integration, scheduled mutation, docs, and release-candidate automation | Runs pinned actions and repository scripts |

`CStructSharp.NonWeb.sln` contains core, tests, fuzz, and benchmarks. Use it for routine development.
`CStructSharp.sln` adds the WASM project and belongs to final integration. The package-consumer project stays outside
both solutions because its package does not exist during the solution's initial restore.

## Dependency direction

The core library does not reference tests, tools, documentation, or frontends. This keeps the published package
small and prevents development-only dependencies from reaching users.

Place shared public behavior in the core and test it through managed tests. Place JSON or browser representation
rules in the adapter. A frontend workaround should not become a second implementation of core parsing or writing.

When you are unsure where a change belongs, ask which project can own it without depending on a higher-level UI,
test, or packaging concern. Then use [Architecture](architecture.md) to find the relevant execution stage.
