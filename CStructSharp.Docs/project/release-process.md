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
the interactive explorer at `/explorer/`. It also creates a GitHub Release containing the NuGet and WASM downloads.

Tests run in the CI workflow, so the release workflow does not repeat them. A release should be started only after CI
has passed for the current `main` revision.

## Validate the workflow boundary

From the repository root:

```powershell
.\tools\Validate-ReleasePolicy.ps1
```

The check confirms that CI is limited to restore, build, test, and test-result reporting, and that release builds
exactly the three requested artifacts. Release actions remain pinned to immutable commits.

## Release URLs

- [Project landing page](https://vvollers.github.io/cstructsharp/)
- [Documentation](https://vvollers.github.io/cstructsharp/docs/)
- [Interactive WASM explorer](https://vvollers.github.io/cstructsharp/explorer/)
- [GitHub releases](https://github.com/vvollers/cstructsharp/releases)
