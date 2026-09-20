# Repository tooling

`documentation/` builds DocFX, exports executable recipes, validates content, and packages the site. `packaging/` publishes and verifies WASM, assembles npm/ZIP/NuGet consumer artifacts, and checks onboarding. `release/` handles version state, release manifests, and npm publication identity. `quality/` checks API, fuzz, compiler fixtures, coverage, mutation, solutions, and performance, and generates the generator's parity layouts (`quality/generate-parity-layouts.mjs`, `--check` in CI); `documentation/validate-generator-diagnostics.mjs` keeps the diagnostics lesson in step with the analyzer's release file. `lib/` holds the helpers the scripts share (assertions, a logged `dotnet` runner, argument parsing, and small XML, ZIP, and NuGet readers); `compiler-fixtures/` and `fixtures/` contain authored test inputs. Every tool is a Node script (`node tools/<area>/<name>.mjs --option value`; `--self-test` where a tool has fail-first fixtures). Run scripts from the root; output belongs in ignored `artifacts/` or documented app build folders. Start with `node tools/documentation/validate-documentation.mjs`, `node tools/quality/managed-api-baseline.mjs compare`, and the package commands in [release guidance](../docs/project/release-process.md). Publication scripts are invoked only by explicitly requested release workflows.

## Manual measurements

README quality badges come from `quality/readme-badges.mjs`, using CI's .NET 10 TRX and
library-only Cobertura reports. Website and release builds run `packaging/prepare-site-badges.mjs`
to retrieve the latest successful main CI statistics and measure the NuGet download. Releases use
the newly built package; website-only deployments use the latest published release asset.
`packaging/assemble-site.mjs` includes these files under `/badges/` alongside the complete site.
Badge links lead to the measured CI run. Statistics refresh with site deployments; no generated
Git branch or separate publishing workflow is needed.

Use `quality/artifact-baseline.mjs --package-directory artifacts/package --output-path artifacts/package-sizes.json` to capture raw and gzip-equivalent package sizes. Pass that report to `quality/non-web-release-budgets.mjs --package-artifact-path artifacts/package-sizes.json`. The same meter accepts `--wasm-directory` and `--frontend-directory`. Benchmark JSON conversion is documented in [benchmarks](../benchmarks/README.md). Reports are generated output; historical raw measurements are not required inputs.

Run `node tools/packaging/measure-web-artifacts.mjs` after building to record current web sizes without historical report dependencies. Add `--check` to enforce all historical frontend/gzip budgets; these are manual checks and may expose existing budget drift. The 6 MiB WASM publication limit and browser startup limit remain automatic gates.
