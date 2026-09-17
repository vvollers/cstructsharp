---
title: Develop the browser explorer
description: Build the managed WebAssembly bridge and Vue explorer, then run their tests and package checks.
---

# Develop the browser explorer

This page is for contributors changing the explorer or its managed bridge. Browser consumers should use the
[release bundle starter](../guides/browser/index.md). They do not need a source checkout or .NET workload.

## Build both parts

Install the stable .NET 10 SDK selected by `global.json`, Node.js and npm compatible with
`apps/workshop/package.json`, and PowerShell 7 for repository scripts. The manifest's `packageManager` field
records the preferred npm version; the lockfile fixes the dependency graph.

From the repository root:

```powershell
dotnet workload restore ./src/CStructSharp.Wasm/CStructSharpWeb.Wasm.csproj
npm --prefix ./apps/workshop ci
npm --prefix ./apps/workshop run build
```

The production build publishes the C# bridge into `artifacts/wasm`, stages it into the workshop's `public/wasm`, and builds Vue into
`apps/workshop/dist`. It verifies the copied runtime publication. The managed solution alone does not build Vue.

After the first build:

```powershell
npm --prefix ./apps/workshop run dev
```

Open the address printed by Vite. Changes to Vue update during development; changes to C# require
`npm --prefix ./apps/workshop run build:wasm`. Run the complete production build before browser tests.

## Managed bridge trimming

`src/CStructSharp.Wasm/CStructSharpWeb.Wasm.csproj` builds with `PublishTrimmed` and `TrimMode=full`, declares the
`CStructSharp` and bridge assemblies trimmable for this publish (`TrimmableAssembly` items, so the NuGet library's
own metadata is unchanged), roots nothing, and suppresses trim-analysis warnings. The browser value-conversion rules make these settings possible:

- No C# runtime-binder call site executes in the browser build. Parsed values are `StructValue` objects, and the
  bridge and the benchmark exports handle every `dynamic`-typed library result as `object` (an explicit cast at
  each parse call site keeps it that way). `Microsoft.CSharp` is not part of the publication, and
  `System.Linq.Expressions` is trimmed to the `DynamicObject` surface the value types derive from. This avoids
  loading and initializing the runtime binder for a parse.
- The library's reflection paths (`CStruct.TryGetMemberValue`'s POCO-property fallback and `TypedValueConverter`'s
  object conversion) are statically reachable from `Serialize`/`UpdateStream` but never executed from JavaScript:
  `ParseJsonValue` in `CStructJsonConversion.cs` always produces dictionary/list shapes. The trimmer keeps the
  reflection calls themselves; it can only remove members nothing references, and no browser-reachable code
  depends on members that are reached only through reflection. One consequence stands: the `bindingMode` interop
  option (`WriteOptions.BindingMode`) has no observable effect through the JS API.
- The project switches the library's `CStructSharp.CompiledAccessors` feature off (a
  `RuntimeHostConfigurationOption` with `Trim="true"`). The compiled POCO accessors (`PocoCompiledAccessors`)
  are behind that switch, so the trimmer removes them - and `System.Linq.Expressions` with them - from the
  publication, where they would never run; the browser keeps reflection for the POCO fallback it never takes.
  (Declaring dynamic code unsupported would trim the same code but was measured to add ≈ 820 B of managed
  allocation to every export call through `System.Text.Json`, so the library-specific switch is used instead.)

Measured effect of the full trim (publication as shipped by `publish-wasm.mjs`): 33 → 27 files, 5.35 → 4.36 MB
raw, 2.05 → 1.66 MB gzip; Node cold start: first public parse 142 → 17 ms, process wall −25 %; runtime creation
and first layout compilation unchanged. The gates that must stay green when touching these settings: the JS
benchmark harness's fixture verification, `Validate-BrowserContract.ps1`, both apps' e2e suites against the
production build, `verify-wasm-publication.mjs`, and `measure-web-artifacts.mjs --check`. If a change reintroduces
a `dynamic` call site in the bridge, the trimmer fails the publish or the first parse throws a
`MissingMethodException` - the harness verification catches both.

## Change lessons and test examples

`src/lessons.ts` contains authored titles, exercises, and operation presets. The generator's test catalog remains
separate. Each lesson references a registered C# documentation scenario; the unit check detects missing references.
Real-browser tests compare every offered lesson operation against its expected values, bytes, or error code.
Preserve lesson IDs because documentation links use them.

The standalone browser examples are in `wasm/starter`. Documentation includes those source files directly, and
the package builder copies them into the release ZIP. Editing a browser example does not require duplicating it
in Markdown.

## Verify the change

```powershell
npm --prefix ./apps/workshop run lint
npm --prefix ./apps/workshop run test:unit
npm --prefix ./apps/workshop run test:demos
npm --prefix ./apps/workshop run test:bootstrap
npm --prefix ./apps/workshop run build
npm --prefix ./apps/workshop run test:e2e
```

Install Playwright Chromium from the web directory with `npx playwright install chromium` if it is missing.
For the exact released bundle, create and test an archive:

```powershell
node ./tools/packaging/create-wasm-package.mjs
Compress-Archive -Path ./artifacts/wasm-package/* -DestinationPath ./artifacts/onboarding-browser.zip -Force
./tools/packaging/Test-OnboardingBrowser.ps1 -ArchivePath ./artifacts/onboarding-browser.zip
```

The last check extracts the ZIP into a nested static path and tests the starter and inspector using the public
JavaScript entry point. It does not substitute the explorer's internal adapter.

## Preview matching documentation and explorer sources

For the public npm package, its Node.js loader, and the Vite integration, use the
[npm build and consumer checks](release-process.md#build-and-test-npm-locally). Application users should start with
the [JavaScript quick start](../guides/browser/index.md); they do not need to build this repository.

For source review, run the documentation server with local explorer links in one PowerShell terminal:

```powershell
./tools/documentation/Build-Documentation.ps1 -Serve -Port 8080 -ExplorerUrl http://127.0.0.1:5173/cstructsharp/explorer/
```

In another terminal, set the explorer's documentation base before starting Vite:

```powershell
$env:VITE_DOCS_BASE_URL = 'http://localhost:8080/'
npm --prefix ./apps/workshop run dev
```

Open `http://127.0.0.1:5173/cstructsharp/explorer/`. The header links open the local docs, and lesson links
in the local docs return to this explorer. Build the WASM runtime first if needed, following the instructions above.
For production builds, omit these overrides: the explorer links to the sibling `../docs/` directory and documentation
links use the published explorer. `VITE_DOCS_BASE_URL` is a build-time setting when building the frontend.
Remove it from the terminal with `Remove-Item Env:VITE_DOCS_BASE_URL` before making a publication build.
The documentation override changes generated HTML only; rebuild without it before publishing.
