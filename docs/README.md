# Work on the CStructSharp documentation

`docs` is the DocFX project behind the documentation site. It contains the written pages, navigation,
compiled examples, API-page additions, styling, search checks, and browser tests.

The documentation tools use only the core `src/CStructSharp` project. They must not restore, build, publish, or test
`apps/explorer` or `CStructSharpWeb.Wasm`. Avoid the full `CStructSharp.sln` during routine documentation work
because that solution includes the WebAssembly bridge.

## Before you start

Install these prerequisites:

- the .NET 10 SDK selected by the repository's `global.json`;
- Node.js 24 or 26 for the documentation scripts, prose, search, browser, and accessibility checks.

Run commands from the repository root, which is the directory containing `docs` and `tools`. You do not
need a global DocFX installation. `dotnet tool restore` installs the repository-pinned version locally.

Check your SDK and Node.js versions when setup fails:

```sh
dotnet --version
node --version
```

The first command should report the .NET 10 SDK selected by `global.json`. The second should report a version from
24 through 26.

## Build and preview the site

For a first build, run:

```sh
node tools/documentation/build-documentation.mjs --serve
```

The wrapper restores the pinned .NET tools, restores and builds the core library for `Release/net10.0`, generates
API metadata, builds the site with DocFX warnings treated as errors, and starts a local server. Open
`http://localhost:8080` in a browser. A successful build reports zero DocFX warnings and errors before the server
starts.

Press Ctrl+C in the terminal to stop the server. DocFX serves the files it already built; after changing a page,
stop and rerun the command to see the change.

Once the core assembly is current, use the faster authoring command:

```sh
node tools/documentation/build-documentation.mjs --no-build --serve
```

`--no-build` skips the core restore and compilation. The wrapper refuses to continue if the assembly is missing or
older than a C# or project file, which prevents an API page from being generated from stale code.

If port 8080 is already in use, choose another port:

```sh
node tools/documentation/build-documentation.mjs --no-build --serve --port 8088
```

Open `http://localhost:8088` for that example.

## Run the complete documentation check

Before handing off a documentation change, run:

```sh
node tools/documentation/validate-documentation.mjs
```

This is the full local gate. It restores pinned .NET and Node dependencies, builds only the core library, regenerates
the site, and then checks:

- generated managed API coverage;
- Portable language grammar, fixtures, and feature tables;
- Markdown, spelling, links, navigation, and search data;
- all compiled C# examples;
- browser behavior, keyboard navigation, and accessibility; and
- the shape and size of the GitHub Pages output.

The command prints each check as it runs and exits nonzero at the first failure. Fix the first reported error, rerun
the focused check if one is named, and finish with the full command again.

## Watch the existing artifact margin

The documentation artifact has a 50 MiB (52,428,800-byte) uncompressed limit in
[`contracts/documentation/pages-v1.json`](../contracts/documentation/pages-v1.json). Its compressed archive has
a separate 16 MiB limit. These limits measure the complete generated documentation, including theme assets and
debugging source maps, not the bytes downloaded for one page. Read the total printed by
`validate-documentation.mjs` for your current build and subtract it from the limit to find the remaining margin.
The combined app/WASM deployment has separate existing gates.

Revisit payload growth when a change adds
large assets, many API pages or duplicated examples, and whenever the existing gate fails. Inspect the existing
artifact report to locate the growth before deciding on a focused follow-up. Do not silently raise the limit or
remove validation to fit. No extra size-reporting tool or automatic optimization is required by this guidance.

## Run a focused check

The wrapper is the normal entry point, but focused commands make an editing loop faster.

Install the exact Node.js dependencies from `package-lock.json`:

```sh
npm --prefix docs ci --ignore-scripts
```

Then check Markdown formatting and spelling:

```sh
npm --prefix docs run lint:markdown
npm --prefix docs run lint:spelling
```

Both commands should finish with zero findings. Run them from the repository root; the `--prefix` argument tells npm
to use the package inside `docs`.

To run the browser checks for the first time, install the pinned Chromium build and start the tests:

```sh
npm --prefix docs run install:browser
npm --prefix docs run test:browser
```

The first command downloads Playwright's browser runtime. The second serves the existing `_site` output and checks
navigation, search, keyboard use, and accessibility. Build the site first if `_site` is missing or stale. A
successful run reports all configured browser tests passed. Browser installation needs network access and can take longer than the
prose checks.

To isolate a DocFX problem after the core project has been built, run:

```sh
dotnet tool restore
dotnet tool run docfx docs/docfx.json --warningsAsErrors
dotnet tool run docfx serve docs/_site --hostname localhost --port 8080
```

These commands restore local tools, rebuild `_site`, and serve the result. Prefer
`build-documentation.mjs` for ordinary work because it also checks for stale core output and enforces the site time
and size limits.

## Check external links and the Pages archive

External sites can fail temporarily, so their check runs separately from the ordinary pull-request gate:

```sh
node tools/documentation/test-documentation-external-links.mjs
```

A successful run reports no unexpected broken link. Review a failure before changing the allowlist; a typo and a
temporary third-party outage need different fixes.

After a successful site build, create and validate the archive expected by GitHub Pages:

```sh
node tools/documentation/new-documentation-pages-artifact.mjs
```

The command writes the ignored file `artifacts/documentation/cstructsharp-pages.tar.gz`. It prepares a local
artifact only; it does not publish or deploy the site. See the site's **Project > Documentation deployment** page
for the separately authorized deployment procedure.

Before there is a commit to check out, you can test the files Git would actually keep:

```sh
node tools/documentation/test-documentation-source-snapshot.mjs
```

The script creates a temporary copy from the current committed base, overlays only Git-visible prospective source,
runs the complete documentation gate, creates the Pages archive, and removes a successful temporary copy. This is
slower than the normal check, but it catches accidental dependencies on ignored local files.

## Files produced by a build

Do not commit these generated files:

- `_site/`;
- generated `api/*.yml`;
- `.tmp/`;
- logs, binary logs, browser reports, and test results; and
- project `bin/` and `obj/` directories.

Markdown, table-of-contents files, configuration, assets, templates, examples, and dependency lockfiles are source
inputs and should remain trackable.

## Sources and outputs

`examples/` owns executable runners and starter programs. Recipe pages and standalone C# files are exported from those runners and generator metadata; edit their sources, then run `node tools/documentation/export-documentation-examples.mjs` from the root. `generated-files.json` lists the ignored exports. `guides/`, `language/`, and `project/` contain authored pages. `api/` mixes authored landing pages with ignored DocFX metadata. `assets/`, `templates/`, and `landing/` provide site presentation. `browser-tests/` and quality configuration validate the resulting site. Reviewed cross-project data lives in `../contracts/` and is published at the same contract URLs. `_site/`, logs, and browser reports are ignored output.
