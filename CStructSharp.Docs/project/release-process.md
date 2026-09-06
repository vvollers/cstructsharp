---
title: Release process
description: Build and review the NuGet, documentation, and WebAssembly explorer artifacts.
---

# Release process

The release workflow is manually triggered by a maintainer. It takes a major, minor, or patch bump, updates the
library and explorer versions, commits and tags that change, and builds the release from the tagged source:

1. The multi-target NuGet package and symbol package.
2. The generated DocFX documentation site.
3. The production WebAssembly test explorer and a standalone WASM bundle.

The workflow publishes a combined GitHub Pages site with a landing page at the root, documentation at `/docs/`, and
the interactive explorer at `/explorer/`. It also creates a GitHub Release containing the NuGet package and a
standalone `cstructsharp-wasm-v<VERSION>.zip` download. The WASM archive contains the browser JavaScript entry point,
the required .NET WebAssembly runtime and assemblies, and a README with a copy-and-import example. It is intended for
embedding CStructSharp in another static browser project; it is separate from the full explorer website.

Managed regression tests run in CI. The release workflow additionally checks the exact NuGet and WASM artifacts
using the onboarding programs before publishing their corresponding artifacts. A release should be started only
after CI has passed for the current `main` revision. See [onboarding review](onboarding-review.md) for the human
sessions and artifact checks, and [browser development](web-development.md) for local explorer commands.

## Validate the consumer experience

From the repository root:

```powershell
./tools/Test-OnboardingPackage.ps1 -PackageDirectory ./artifacts/package
./tools/Test-OnboardingBrowser.ps1 -ArchivePath ./artifacts/cstructsharp-wasm-vVERSION.zip
```

Replace VERSION with the actual archive version. These checks use isolated package consumers and an extracted
browser bundle. The old `contracts/release/rc1.json` and its validator describe the historical build-only release
candidate policy; they do not describe the current publishing workflow. Release actions remain pinned to immutable
commits. Do not update old compatibility snapshots merely to make them appear current.

## Release URLs

- [Project landing page](https://vvollers.github.io/cstructsharp/)
- [Documentation](https://vvollers.github.io/cstructsharp/docs/)
- [Interactive WASM explorer](https://vvollers.github.io/cstructsharp/explorer/)
- [GitHub releases](https://github.com/vvollers/cstructsharp/releases)
