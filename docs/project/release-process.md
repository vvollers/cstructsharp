---
title: Release process
description: Build, verify, publish, and recover NuGet, npm, WebAssembly, and website releases.
---

# Release process

The manually triggered `.github/workflows/release.yml` releases NuGet, the `cstructsharp` npm package,
the standalone WASM ZIP, and the documentation/explorer/inspector Pages site at one version.

| Mode | Behavior |
| --- | --- |
| `release` | Calculate a bump, build and test all artifacts, then publish |
| `prepare` | Calculate a bump and verify artifacts; do not commit, tag, or publish |
| `recover` | Reuse artifacts from `recovery_run_id`; do not bump or rebuild |

Normal and prepare runs first call the same managed (`ci.yml`), web (`web.yml`) and documentation (`docs.yml`)
workflows used by ordinary CI, whatever their path filters would select. These jobs use the release workflow's exact
source SHA. Managed verification includes whole-library coverage, generator/parity tests, API and quality contracts,
formatting, package consumers, Native AOT, and the Windows/macOS managed tests. Web verification lints and unit-tests
the apps, builds the WASM publication once, and runs the package, explorer, inspector and onboarding browser tests.
It also installs the packed npm tarball, without installation scripts or the .NET SDK, at the edges of the supported
Node range: the package's minimum Node (22.14) on Windows, Linux and macOS, and the newest Node (26.5) on Linux.
Missing, failed or skipped shared jobs prevent the artifact-verification job from running.

`verify` does not repeat those tests. It checks out that exact main-branch source SHA, updates the version files,
and builds the versioned NuGet package, npm tarball, WASM archive and Pages site. It then checks only what differs
from the tested source: the NuGet package's metadata, symbols and framework assets; that the npm version is not yet
published; the onboarding pages from the WASM archive; a Firefox and WebKit smoke of the starter page and the
inspector (ordinary CI uses Chromium only); and the landing-page example against the release tarball. npm preflight
failures stop verification; only an explicit 404 is treated as a missing registry version.

The publish job runs after those gates pass, or resumes a verified original run. It verifies downloaded artifact
hashes and the original workflow, source SHA, and completed jobs: every shared source gate and `verify` itself.
The version commit may only change the
six version files on top of the verified source; unexpected advances on main stop publication. The job publishes
the exact npm tarball using OIDC and attaches it to the GitHub Release. Existing npm versions are skipped only
after integrity comparison. NuGet pushes handle duplicates; Pages can be deployed again, and GitHub Release
attachments can be completed on recovery. Publishing is not atomic across services.

Artifacts and the source/hash manifest are retained for 90 days. Recover before they expire; after expiration,
the workflow cannot promise publication of identical tested artifacts. Release actions remain pinned to
immutable commits. Start a release only after CI has passed on main.

Ordinary workflow and check names remain unchanged. Inside a release, GitHub prefixes the reused jobs with
`Managed verification /`, `Web verification /`, or `Documentation verification /`. The publisher does not keep its
own list of these names. `tools/lib/release-verification.mjs` reads `release.yml` at the release's source commit,
takes every job that the `publish` job waits for, replaces each reused workflow by its jobs, and expands each
matrix into one job per combination. Each of those jobs must have passed exactly once on the source SHA. Renaming a
job or changing a matrix therefore changes the expected list automatically;
`node --test tools/quality/release-verification.test.mjs` checks the derivation against the real workflows.
Recovery verifies the original run and reuses its artifacts; it does not rerun the shared jobs or rebuild packages.
Repository protection settings are not changed by this workflow design.

Each check runs once per release: the shared workflows test the source, and `verify` tests only the versioned
artifacts. When comparing release cost, compare elapsed run time and the sum of job durations; parallel jobs reduce
waiting but not runner usage, and GitHub's billed minutes can differ from that sum because of platform multipliers.

## Normal release and local synchronization

From a clean, synchronized `main`, dispatch the workflow with the intended version bump:

```sh
gh workflow run release.yml --ref main -f mode=release -f bump=patch
```

Follow the returned Actions run through publication, not just the build job. The workflow calculates the version
from `VersionPrefix`, commits the tested version files, and creates the release tag. It also publishes the npm
and NuGet packages, the standalone archive, and the Pages site. Do not manually bump again while it is running.

After successful publication, synchronize your local checkout:

```sh
git pull --ff-only origin main
git fetch origin --tags --prune
git status -sb
```

Check that the release tag points to the release commit, package versions agree, and the GitHub Release has its
NuGet package, symbols, npm tarball, WASM ZIP, and release manifest. A successful build alone does not mean all
services have been published.

## npm trusted publishing

The npm package is published by the release workflow through GitHub Actions trusted publishing (OIDC): the
package's **Trusted Publisher** setting names user `vvollers`, repository `cstructsharp`, workflow `release.yml`,
and environment `github-pages`, with direct `npm publish` enabled, so a normal release needs no interactive npm
login and no npm token secret and every publication carries CI-generated provenance. The publishing job pins
Node 26.5.0 and npm 12.0.2, satisfying npm's OIDC minimums. If the trusted publisher ever has to be re-created
(a renamed workflow or environment), publish one release manually with `npm login` / `npm publish` of the
verified `.tgz` from a `prepare` run, then restore the setting; see
[npm trusted publishing](https://docs.npmjs.com/trusted-publishers/) and
[npm trust prerequisites](https://docs.npmjs.com/cli/v11/commands/npm-trust/).

## Recover a partial release

Choose mode `recover` and the **original verified run ID**, not the failed recovery run. Recovery stops if an npm
version has different integrity, any artifact changed, required tests did not pass, or the tag has unexpected
changes. It accepts an existing matching tag or a matching version commit pushed before a previous tag push
failed. A bad immutable package needs a new version; it must not be overwritten.

npm can accept a publication before the version becomes readable through its registry API ("your package is
being processed"). **Confirm npm publication integrity** therefore polls the registry every 10 seconds for up to
five minutes; a version that is still invisible after that fails the step after the version commit and tag already
exist. Check `npm view cstructsharp@VERSION version`, replacing VERSION with the accepted version. Once it is
available, recover from the original verified run:

```sh
gh workflow run release.yml --ref main -f mode=recover -f recovery_run_id=ORIGINAL_RUN_ID
```

Replace ORIGINAL_RUN_ID with the numeric run ID. Recovery checks the package's integrity before skipping its upload.
Do not start another normal patch release to complete a partly published version.

## Build and test npm locally

From the repository root (the explorer's dependencies supply Vite, Playwright, and tsc for the consumer checks):

```sh
npm --prefix apps/explorer ci
npm run build:wasm
npm run pack:npm
npm run test:npm
npm run test:npm-release
```

Install Chromium with `npx playwright install chromium` if needed. Outputs at the repository root are
`artifacts/npm/cstructsharp-VERSION.tgz` and `artifacts/npm/package-info.json`. The pack step requires npm metadata
to match the managed version and rejects stale WASM. It includes the selected .NET runtime pack's notices.
Package size is recorded; the existing 6 MiB runtime budget remains enforced.
`npm run test:npm:node` runs the host-only consumer checks.

## Validate the other consumer artifacts

From the repository root:

```sh
node tools/packaging/test-onboarding-package.mjs --package-directory artifacts/package
node tools/packaging/test-onboarding-browser.mjs --archive-path artifacts/cstructsharp-wasm-vVERSION.zip
```

Replace VERSION with the archive version. These checks use isolated package consumers and an extracted browser
bundle. The release workflow is the authoritative publishing procedure.
See [browser development](web-development.md).

## Release URLs

- [Project landing page](https://vvollers.github.io/cstructsharp/)
- [Documentation](https://vvollers.github.io/cstructsharp/docs/)
- [Interactive WASM explorer](https://vvollers.github.io/cstructsharp/explorer/)
- [GitHub releases](https://github.com/vvollers/cstructsharp/releases)
- [npm package](https://www.npmjs.com/package/cstructsharp)
