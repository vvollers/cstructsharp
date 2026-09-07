---
title: Release process
description: Build and review the NuGet, documentation, and WebAssembly explorer artifacts.
---

# Release process

The release workflow (`.github/workflows/release.yml`) is manually triggered by a maintainer, who picks a major,
minor, or patch bump. It runs as two jobs:

1. **`verify`** — computes the next version, runs the managed test suite (`CStructSharpTests`), builds the
   multi-target NuGet package and symbol package, the generated DocFX documentation site, and the production
   WebAssembly test explorer and standalone WASM bundle, then checks the exact NuGet and WASM artifacts using the
   onboarding programs and runs the explorer's unit and Playwright end-to-end tests. This job has no write access
   to the repository, NuGet, or GitHub Pages.
2. **`publish`** — runs only if `verify` succeeded (`needs: verify`). It commits and tags the version bump on
   `main`, publishes the NuGet package and symbols, deploys the combined GitHub Pages site (a landing page at the
   root, documentation at `/docs/`, and the interactive explorer at `/explorer/`), and creates a GitHub Release
   containing the NuGet package and the standalone `cstructsharp-wasm-v<VERSION>.zip` download. The WASM archive
   contains the browser JavaScript entry point, the required .NET WebAssembly runtime and assemblies, and a README
   with a copy-and-import example. It is intended for embedding CStructSharp in another static browser project; it
   is separate from the full explorer website.

A release should still be started only after CI has passed for the current `main` revision — `verify` re-running
the managed test suite is a safety net against a stale or racing `main`, not a substitute for a green CI run. See
[onboarding review](onboarding-review.md) for the human sessions and artifact checks, and
[browser development](web-development.md) for local explorer commands.

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
