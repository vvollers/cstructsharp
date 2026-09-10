---
title: Develop the browser explorer
description: Build the managed WebAssembly bridge and Vue explorer, then run their tests and package checks.
---

# Develop the browser explorer

This page is for contributors changing the explorer or its managed bridge. Browser consumers should use the
[release bundle starter](../guides/browser/index.md). They do not need a source checkout or .NET workload.

## Build both parts

Install the stable .NET 10 SDK selected by `global.json`, Node.js and npm compatible with
`CStructSharpWeb/package.json`, and PowerShell 7 for repository scripts. The manifest's `packageManager` field
records the preferred npm version; the lockfile fixes the dependency graph.

From the repository root:

```powershell
dotnet workload restore ./CStructSharpWeb/wasm/CStructSharpWeb.Wasm.csproj
npm --prefix ./CStructSharpWeb ci
npm --prefix ./CStructSharpWeb run build
```

The production build publishes the C# bridge into `CStructSharpWeb/public/wasm` and builds Vue into
`CStructSharpWeb/dist`. It verifies the copied runtime publication. The managed solution alone does not build Vue.

After the first build:

```powershell
npm --prefix ./CStructSharpWeb run dev
```

Open the address printed by Vite. Changes to Vue update during development; changes to C# require
`npm --prefix ./CStructSharpWeb run build:wasm`. Run the complete production build before browser tests.

## Managed bridge trimming

`CStructSharpWeb/wasm/CStructSharpWeb.Wasm.csproj` builds with `PublishTrimmed` and `TrimMode=partial`, roots the
`CStructSharp` and `Microsoft.CSharp` assemblies, and suppresses trim-analysis warnings. This was investigated to
see whether removing the `CStructSharp` root would shrink the published bundle:

- Removing `<TrimmerRootAssembly Include="CStructSharp" />` produces a byte-for-byte identical `CStructSharp.wasm`
  (verified by hash) and an identical total `_framework` size. Under `TrimMode=partial`, an application assembly
  that does not opt in to trimming (`IsTrimmable`) is never member-trimmed regardless of whether it is explicitly
  rooted, so this specific entry has no measurable effect on bundle size either way. A real reduction would require
  `CStructSharp` to opt in to trimming and carry full `DynamicallyAccessedMembers` annotations across its
  reflection-based paths (see below) - a larger, separate change to the core library, not something to attempt from
  this project alone.
- Removing the root does surface real `IL2026`/`IL2075`/`IL2067`/`IL2072`/`IL2111` trim-analysis warnings when
  `SuppressTrimAnalysisWarnings` is temporarily set to `false`. They fall into two groups, both genuinely reachable
  from the four `[JSExport]` methods in `CStructExports.cs`, not dead code: (1) every `dynamic`/`ExpandoObject`
  result path (`ParseWithDebugInternal` and the core reader/writer methods it calls) uses the C# runtime binder,
  which is why `Microsoft.CSharp` must stay rooted; (2) `CStruct.TryGetMemberValue` (the POCO-property fallback
  used by `Serialize`/`UpdateStream` when a caller's value is not already a dictionary/`ExpandoObject`) and
  `TypedValueConverter`'s array/list/object conversion helpers use unannotated `Type.GetProperty`/`GetField`
  reflection. In practice this second group is never exercised from the browser: `ParseJsonValue` in
  `CStructJsonConversion.cs` always converts incoming JSON into the dynamic/dictionary shape, so the reflection
  branch is statically reachable but not actually hit at runtime through the JS API. One consequence: the
  `bindingMode` interop option (`WriteOptions.BindingMode`) has no observable effect through the JS API, because it
  only changes behavior inside that same unreached reflection branch.

Given the measured result, `SuppressTrimAnalysisWarnings` stays `true` and both assemblies stay rooted; there is no
available bundle-size win from adjusting this, and unrooting would only add warning noise without changing output.

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
npm --prefix ./CStructSharpWeb run lint
npm --prefix ./CStructSharpWeb run test:unit
npm --prefix ./CStructSharpWeb run test:demos
npm --prefix ./CStructSharpWeb run test:bootstrap
npm --prefix ./CStructSharpWeb run build
npm --prefix ./CStructSharpWeb run test:e2e
```

Install Playwright Chromium from the web directory with `npx playwright install chromium` if it is missing.
For the exact released bundle, create and test an archive:

```powershell
node ./CStructSharpWeb/scripts/create-wasm-package.mjs
Compress-Archive -Path ./CStructSharpWeb/artifacts/wasm-package/* -DestinationPath ./artifacts/onboarding-browser.zip -Force
./tools/Test-OnboardingBrowser.ps1 -ArchivePath ./artifacts/onboarding-browser.zip
```

The last check extracts the ZIP into a nested static path and tests the starter and inspector using the public
JavaScript entry point. It does not substitute the explorer's internal adapter.

## Preview matching documentation and explorer sources

For the public npm package, its Node.js loader, and the Vite integration, use the
[npm build and consumer checks](release-process.md#build-and-test-npm-locally). Application users should start with
the [JavaScript quick start](../guides/browser/index.md); they do not need to build this repository.

For source review, run the documentation server with local explorer links in one PowerShell terminal:

```powershell
./tools/Build-Documentation.ps1 -Serve -Port 8080 -ExplorerUrl http://127.0.0.1:5173/cstructsharp/explorer/
```

In another terminal, set the explorer's documentation base before starting Vite:

```powershell
$env:VITE_DOCS_BASE_URL = 'http://localhost:8080/'
npm --prefix ./CStructSharpWeb run dev
```

Open `http://127.0.0.1:5173/cstructsharp/explorer/`. The header links now open the local docs, and lesson links
in the local docs return to this explorer. Build the WASM runtime first if needed, following the instructions above.
For production builds, omit these overrides: the explorer links to the sibling `../docs/` directory and documentation
links use the published explorer. `VITE_DOCS_BASE_URL` is a build-time setting when building the frontend.
Remove it from the terminal with `Remove-Item Env:VITE_DOCS_BASE_URL` before making a publication build.
The documentation override changes generated HTML only; rebuild without it before publishing.
