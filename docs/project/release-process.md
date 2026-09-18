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

`verify` checks out the workflow's exact main-branch source SHA, updates version files, builds the managed and
WASM artifacts, and tests the installed npm tarball in Node, TypeScript, Vite development/production, SSR, and
static hosting. Six additional jobs execute that tarball on Windows, Linux, and macOS with Node 22.14 and 24.0.
No installation scripts or .NET SDK are required by those consumers. npm preflight failures stop verification;
only an explicit 404 is treated as a missing registry version.

The publish job runs after those gates pass, or resumes a verified original run. It verifies downloaded artifact
hashes and the original workflow, source SHA, and completed test jobs. The version commit may only change the
six version files on top of the verified source; unexpected advances on main stop publication. The job publishes
the exact npm tarball using OIDC and attaches it to the GitHub Release. Existing npm versions are skipped only
after integrity comparison. NuGet pushes handle duplicates; Pages can be deployed again, and GitHub Release
attachments can be completed on recovery. Publishing is not atomic across services.

Artifacts and the source/hash manifest are retained for 90 days. Recover before they expire; after expiration,
the workflow cannot promise publication of identical tested artifacts. Release actions remain pinned to
immutable commits. Start a release only after CI has passed on main.

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

## First npm publication: maintainer steps

Account login and 2FA are one-time prerequisites. Confirm your email is verified and that `cstructsharp` is
available to your publishing account. Then:

1. Merge the implementation and wait for CI to pass on `main`.
2. In GitHub Actions select **Release**, **Run workflow**, branch `main`, mode `prepare`, and the desired bump.
   Wait for verification and all six Node jobs to succeed. The publish job is intentionally skipped.
3. Record the run ID from the Actions URL (`/actions/runs/NUMBER`). Download its **npm-package** artifact and
   extract the downloaded ZIP. It contains `cstructsharp-VERSION.tgz` and `package-info.json`. Keep the `.tgz` intact.
4. Run `npm login --registry=https://registry.npmjs.org/`, complete authentication, and confirm
   your account with `npm whoami --registry=https://registry.npmjs.org/`.
5. Publish the downloaded file with
   `npm publish "FULL-PATH/cstructsharp-VERSION.tgz" --access public --registry=https://registry.npmjs.org/`.
   Substitute the actual path and version. Complete npm's authentication prompt. Do not publish the repository
   or private explorer directory. A first package must exist before trust can be configured.
6. On the [npm website](https://www.npmjs.com/) open the package's **Settings**, then **Trusted Publisher**, and select **GitHub Actions**.
   Enter user `vvollers`, repository `cstructsharp`, workflow `release.yml`, and environment `github-pages`.
   Enable direct `npm publish` and save. A staged-only publisher requires manual approval of every npm release.
7. Run **Release** again with mode `recover` and `recovery_run_id` set to the prepared run's number. The bump
   input is ignored. Recovery compares the manually published npm integrity, skips uploading it again,
   and completes the version commit/tag, NuGet, Pages, and GitHub Release.
8. Verify installation of the explicit registry version in a clean Node and browser consumer. Subsequent
   normal releases use mode `release` and require no interactive npm login or npm token secret.

The first interactive publication has no CI-generated provenance. Subsequent direct OIDC publications from the
public repository generate provenance. The publishing job pins Node 26.5.0 and npm 12.0.2, satisfying npm's OIDC
minimums. See [npm trusted publishing](https://docs.npmjs.com/trusted-publishers/) and
[npm trust prerequisites](https://docs.npmjs.com/cli/v11/commands/npm-trust/).

## Recover a partial release

Choose mode `recover` and the **original verified run ID**, not the failed recovery run. Recovery stops if an npm
version has different integrity, any artifact changed, required tests did not pass, or the tag has unexpected
changes. It accepts an existing matching tag or a matching version commit pushed before a previous tag push
failed. A bad immutable package needs a new version; it must not be overwritten.

npm can accept a publication before the version becomes readable through its registry API. The current workflow
checks immediately, so a processing delay can fail **Confirm npm publication integrity** after the version commit
and tag already exist. Check `npm view cstructsharp@VERSION version`, replacing VERSION with the accepted version.
Once it is available, recover from the original verified run:

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
See [onboarding review](onboarding-review.md) and [browser development](web-development.md).

## Release URLs

- [Project landing page](https://vvollers.github.io/cstructsharp/)
- [Documentation](https://vvollers.github.io/cstructsharp/docs/)
- [Interactive WASM explorer](https://vvollers.github.io/cstructsharp/explorer/)
- [GitHub releases](https://github.com/vvollers/cstructsharp/releases)
- [npm package](https://www.npmjs.com/package/cstructsharp)
