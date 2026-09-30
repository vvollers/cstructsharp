# Repository tooling

Every tool is a Node script. Run it from the repository root as `node tools/<area>/<name>.mjs --option value`; a
tool with fail-first fixtures also accepts `--self-test`. Each script starts with a header comment that states its
purpose and usage. Output belongs in the ignored `artifacts/` folder or in a documented app build folder.

## Folders

| Folder | What it holds |
| --- | --- |
| `documentation/` | The DocFX build, the export of executable recipes, the content validators, and the Pages artifact. `validate-generator-diagnostics.mjs` keeps the diagnostics lesson in step with the analyzer's release file. |
| `packaging/` | WASM publication and verification, the npm, ZIP, and NuGet consumer artifacts, and the onboarding checks. |
| `release/` | Version state, release manifests, and npm publication identity. Only explicitly requested release workflows run the publication scripts. |
| `quality/` | Checks of the public API, fuzz corpus, compiler fixtures, coverage, mutation testing, engine golden outcomes, solutions, and performance. |
| `lib/` | Helpers the scripts share: assertions, a logged `dotnet` runner, argument parsing, small XML, ZIP, and NuGet readers, and the workflow YAML reader that release verification and `documentation/validate-documentation-workflow.mjs` use. |
| `compiler-fixtures/`, `fixtures/` | Authored test inputs. |

## Where to start

- `node tools/documentation/validate-documentation.mjs` runs the complete documentation gate
  ([docs/README.md](../docs/README.md)).
- `node tools/quality/managed-api-baseline.mjs compare` compares the public .NET API with its reviewed baseline.
- `node tools/quality/engine-golden.mjs record|check` records or checks the golden outcomes of the engine tests
  ([CONTRIBUTING.md](../CONTRIBUTING.md#engine-golden-outcomes)).
- `node tools/quality/quick-perf-check.mjs --baseline <checkout> --job Quick --categories Impact` compares benchmark
  timings and allocations of this checkout with a baseline checkout while you change a hot path; both checkouts
  must already be built in Release.
- `node tools/quality/comparison-benchmarks.mjs` runs the serializer comparison behind the README tables.
- `node tools/quality/generate-parity-layouts.mjs` generates the generator's parity layouts (`--check` in CI).
- The package commands are in the [release guidance](../docs/project/release-process.md).

## Manual measurements

README quality badges come from `quality/readme-badges.mjs`, using CI's .NET 10 TRX and
library-only Cobertura reports. Website and release builds run `packaging/prepare-site-badges.mjs`
to retrieve the latest successful main CI statistics and measure the NuGet download. Releases use
the newly built package; website-only deployments use the latest published release asset.
`packaging/assemble-site.mjs` includes these files under `/badges/` alongside the complete site.
Badge links lead to the measured CI run. Statistics refresh with site deployments; no generated
Git branch or separate publishing workflow is needed.

Benchmark JSON conversion is documented in [benchmarks](../benchmarks/README.md). Reports are generated output; historical raw measurements are not required inputs.

Run `node tools/packaging/measure-web-artifacts.mjs` after building to record current web sizes without historical report dependencies. Add `--check` to fail when a size exceeds `contracts/performance/web-size-budget.json`; that is a manual check. The 6 MiB WASM publication limit and the browser startup limit are automatic gates.
