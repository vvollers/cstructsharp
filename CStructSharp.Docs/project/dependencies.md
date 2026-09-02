---
title: Dependencies
description: Understand which packages ship with CStructSharp and which are used only to build, test, or document it.
---

# Dependencies

A runtime dependency can reach an application that installs CStructSharp. A private build, analyzer, test, or
documentation dependency is used only while developing the repository. Keep that distinction in mind when reviewing
an update: a runtime change has a different compatibility and package-size impact from a test-tool change.

Versions on this page come from project manifests, the local .NET tool manifest, and npm lockfiles.

## Core and managed development packages

| Scope | Dependency | Version | Why it is used |
| --- | --- | ---: | --- |
| Core runtime | [Pidgin](https://www.nuget.org/packages/Pidgin/3.5.1) | 3.5.1 | Recognizes layout source with parser combinators |
| CI build, private | Microsoft.SourceLink.GitHub | 8.0.0 | Connects symbols to repository source |
| Build, private | Roslynator.Analyzers | 4.15.0 | Finds C# correctness and maintainability issues |
| Build, private | StyleCop.Analyzers | 1.2.0-beta.556 | Checks source style |
| Tests | Microsoft.NET.Test.Sdk | 18.8.1 | Hosts managed tests |
| Tests | MSTest | 4.3.2 | Defines and runs test cases |
| Tests, private | coverlet.collector / coverlet.msbuild | 10.0.1 | Measures line and branch coverage |
| Benchmarks | BenchmarkDotNet | 0.15.8 | Measures timing and allocation |
| Local tool | dotnet-stryker | 4.16.0 | Runs mutation tests |
| Local tool | sourcelink | 3.1.1 | Checks symbol/source links |
| Local tool | PublicApiGenerator.Tool | 11.5.4 | Produces the managed API signature snapshot |
| Local tool | DocFX | 2.78.5 | Builds conceptual and generated API documentation |

Pidgin is the only package needed by the core at runtime. It recognizes tokens and grammar. CStructSharp remains
responsible for name resolution, layout calculation, value conversion, safety limits, and every public operation.
Changing parser implementation must not silently change the documented language.

`PrivateAssets` prevents analyzer and build packages from becoming dependencies of an application that installs the
library. Source Link is enabled in CI/release-style builds where repository metadata is available.

## Project relationships

| Project | Additional role |
| --- | --- |
| `CStructSharp.Fuzz` | Uses no extra package; references core and owns the replay corpus |
| `CStructSharpTests` | Uses the test SDK, MSTest, coverage tools, analyzers, core, and fuzz support |
| `CStructSharp.Benchmarks` | Uses BenchmarkDotNet and core |
| `CStructSharp.PackageConsumer` | Installs the produced package without a project reference |
| `CStructSharpWeb.Wasm` | Adds no managed package; references core |
| `CStructSharp.Docs` | Uses pinned DocFX and Node quality tools and reads a prebuilt core assembly |

## Documentation tools

| Tool | Version | Check |
| --- | ---: | --- |
| markdownlint-cli2 | 0.23.1 | Markdown structure and style |
| cspell | 10.0.1 | Prose and identifier spelling |
| `@playwright/test` | 1.61.1 | Navigation, search, theme, viewport, keyboard, and code-copy behavior |
| `@axe-core/playwright` | 4.12.1 | Serious and critical automated accessibility findings |

The documentation manifest pins exact versions and commits `package-lock.json`. Its `js-yaml` override is fixed at
5.2.2 so the lint dependency tree does not retain the earlier vulnerable release. Use Node 24 or 26. These tools do
not enter either the NuGet package or the generated static site.

Install exactly the locked documentation tree with:

```powershell
npm --prefix .\CStructSharp.Docs ci --ignore-scripts
```

Run this from the repository root. `npm ci` removes and recreates the local `node_modules` directory from the lockfile
instead of rewriting dependency versions. Success reports the installed package count and an audit summary.

## Optional frontend packages

The Web workbench is outside routine core and documentation builds:

| Scope | Package | Manifest range |
| --- | --- | ---: |
| Runtime | [vue](https://www.npmjs.com/package/vue) | `^3.5.40` |
| Build | `vite` / `@vitejs/plugin-vue` | `^8.1.5` / `^6.0.8` |
| Language | `typescript` / `vue-tsc` / `@types/node` | `^6.0.3` / `^3.3.8` / `^26.1.1` |
| Unit/component test | `vitest` / `@vue/test-utils` / `happy-dom` | `^4.1.10` / `2.2.7` / `^20.11.1` |
| Browser test | `@playwright/test` | `^1.61.1` |
| Lint | `eslint` / `@eslint/js` / `eslint-plugin-vue` | `^10.7.0` / `^10.0.1` / `^10.10.0` |
| Lint integration | `typescript-eslint` / `globals` | `^8.65.0` / `^17.7.0` |
| Format/orchestration | `prettier` / `concurrently` | `^3.9.6` / `^9.2.4` |

That manifest requires Node 22.12 or newer and npm 10 or newer; `packageManager` records npm 11.6.2. The lockfile,
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
