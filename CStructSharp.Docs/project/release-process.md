---
title: Release process
description: Build and review the NuGet, documentation, and WebAssembly explorer artifacts.
---

# Release process

The release workflow builds three downloadable artifacts when a `v*` tag is pushed or a maintainer starts it
manually:

1. The NuGet package and symbol package.
2. The generated DocFX documentation site.
3. The production WebAssembly test explorer.

Tests run in the CI workflow, so the release workflow does not repeat them. It only restores the dependencies needed
for these deliverables, builds them, and uploads them to the workflow run.

## Validate the workflow boundary

From the repository root:

```powershell
.\tools\Validate-ReleasePolicy.ps1
```

The check confirms that CI is limited to restore, build, test, and test-result reporting, and that release builds
exactly the three requested artifacts. Release actions remain pinned to immutable commits.

## Publication is separate

The workflow does not publish to NuGet, create a GitHub release, deploy the documentation site, or change repository
settings. A maintainer reviews the uploaded artifacts and performs any publication separately. Documentation can
still be deployed through the explicitly authorized
[documentation workflow](documentation-deployment.md).
