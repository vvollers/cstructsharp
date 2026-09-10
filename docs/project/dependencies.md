---
title: Dependencies
description: Understand which packages ship with CStructSharp and which are used only to build, test, or document it.
---

# Dependencies

A runtime dependency can reach an application that installs CStructSharp. A private build, analyzer, test, or
documentation dependency is used only while developing the repository. Keep that distinction in mind when reviewing
an update: a runtime change has a different compatibility and package-size impact from a test-tool change.

Use project manifests, the local .NET tool manifest, and npm lockfiles for current versions. This page explains their roles.

## Core and managed development packages

| Scope | Dependency | Why it is used |
| --- | --- | --- |
| Core runtime | [Pidgin](https://www.nuget.org/packages/Pidgin/3.5.1) | Recognizes layout source with parser combinators |
| CI build, private | Microsoft.SourceLink.GitHub | Connects symbols to repository source |
| Build, private | Roslynator.Analyzers | Finds C# correctness and maintainability issues |
| Build, private | StyleCop.Analyzers | Checks source style |
| Tests | Microsoft.NET.Test.Sdk | Hosts managed tests |
| Tests | MSTest | Defines and runs test cases |
| Tests, private | coverlet.collector / coverlet.msbuild | Measures line and branch coverage |
| Benchmarks | BenchmarkDotNet | Measures timing and allocation |
| Local tool | dotnet-stryker | Runs mutation tests |
| Local tool | PublicApiGenerator.Tool | Produces the managed API signature snapshot |
| Local tool | DocFX | Builds conceptual and generated API documentation |

Pidgin is the only package needed by the core at runtime. It recognizes tokens and grammar. CStructSharp remains
responsible for name resolution, layout calculation, value conversion, safety limits, and every public operation.
Changing parser implementation must not silently change the documented language.

`PrivateAssets` prevents analyzer and build packages from becoming dependencies of an application that installs the
library. Source Link is enabled in CI/release-style builds where repository metadata is available.

## Project relationships

| Project | Additional role |
| --- | --- |
| `tests/CStructSharp.Fuzz` | Uses no extra package; references core and owns the replay corpus |
| `tests/CStructSharpTests` | Uses the test SDK, MSTest, coverage tools, analyzers, core, and fuzz support |
| `benchmarks/CStructSharp.Benchmarks` | Uses BenchmarkDotNet and core |
| `tests/CStructSharp.PackageConsumer` | Installs the produced package without a project reference |
| `CStructSharpWeb.Wasm` | Adds no managed package; references core |
| `docs` | Uses pinned DocFX and Node quality tools and reads a prebuilt core assembly |

## Documentation tools

| Tool | Check |
| --- | --- |
| markdownlint-cli2 | Markdown structure and style |
| cspell | Prose and identifier spelling |
| `@playwright/test` | Navigation, search, theme, viewport, keyboard, and code-copy behavior |
| `@axe-core/playwright` | Serious and critical automated accessibility findings |

The documentation manifest pins exact versions and commits `package-lock.json`. Its `js-yaml` override is fixed at
5.2.2 so the lint dependency tree does not retain the earlier vulnerable release. Use Node 24 or 26. These tools do
not enter either the NuGet package or the generated static site.

Install exactly the locked documentation tree with:

```powershell
npm --prefix .\docs ci --ignore-scripts
```

Run this from the repository root. `npm ci` removes and recreates the local `node_modules` directory from the lockfile
instead of rewriting dependency versions. Success reports the installed package count and an audit summary.

## Optional frontend packages

The Web workbench is outside routine core and documentation builds:

| Scope | Package |
| --- | --- |
| Runtime | [vue](https://www.npmjs.com/package/vue) |
| Build | `vite` / `@vitejs/plugin-vue` |
| Language | `typescript` / `vue-tsc` / `@types/node` |
| Unit/component test | `vitest` / `@vue/test-utils` / `happy-dom` |
| Browser test | `@playwright/test` |
| Lint | `eslint` / `@eslint/js` / `eslint-plugin-vue` |
| Lint integration | `typescript-eslint` / `globals` |
| Format/orchestration | `prettier` |

That manifest requires Node 22.12 or newer and npm 10 or newer; `packageManager` records the preferred npm version. The lockfile,
not a floating manifest range, records the exact installed graph.

## Review an update

For any dependency change:

1. Read the upstream release notes and confirm why the update is needed.
2. Inspect the manifest and lock/restore changes rather than accepting unrelated updates.
3. Review vulnerabilities, licenses, and whether the package is reachable at runtime.
4. Run the owning project's tests and validators.
5. Compare package or site size when the dependency can affect an artifact.

Do not change a version only to silence an audit. Confirm whether the vulnerable code is present and reachable, then
record how the chosen update or accepted limitation addresses it.
