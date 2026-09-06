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
