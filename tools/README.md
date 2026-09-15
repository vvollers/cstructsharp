# Repository tooling

`documentation/` builds DocFX, exports executable recipes, validates content, and packages the site. `packaging/` publishes and verifies WASM, assembles npm/ZIP/NuGet consumer artifacts, and checks onboarding. `release/` handles version state, release manifests, and npm publication identity. `quality/` checks API, fuzz, compiler fixtures, coverage, mutation, solutions, and performance (`convert-benchmark-baseline.mjs`, `compare-benchmark-baseline.mjs`, and `record-benchmark-baseline.mjs` are Node equivalents of the benchmark converter plus the soft drift report and Phase 0 baseline recorder, usable without PowerShell). `CStructSharp.Tooling.psm1` supplies shared PowerShell helpers; `compiler-fixtures/` and `fixtures/` contain authored test inputs. Run scripts from the root; output belongs in ignored `artifacts/` or documented app build folders. Start with `pwsh -File tools/documentation/Validate-Documentation.ps1`, `pwsh -File tools/quality/Compare-ManagedApiBaseline.ps1`, and the package commands in [release guidance](../docs/project/release-process.md). Publication scripts are invoked only by explicitly requested release workflows.

## Manual measurements

README quality badges come from `quality/Write-ReadmeBadges.ps1`, using CI's .NET 10 TRX and
library-only Cobertura reports. Website and release builds run `packaging/prepare-site-badges.mjs`
to retrieve the latest successful main CI statistics and measure the NuGet download. Releases use
the newly built package; website-only deployments use the latest published release asset.
`packaging/assemble-site.mjs` includes these files under `/badges/` alongside the complete site.
Badge links lead to the measured CI run. Statistics refresh with site deployments; no generated
Git branch or separate publishing workflow is needed.

Use `quality/Measure-ArtifactBaseline.ps1 -PackageDirectory artifacts/package -OutputPath artifacts/package-sizes.json` to capture raw and gzip-equivalent package sizes. Pass that report to `quality/Validate-NonWebReleaseBudgets.ps1 -PackageArtifactPath artifacts/package-sizes.json`. The same meter accepts `-WasmDirectory` and `-FrontendDirectory`. Benchmark JSON conversion is documented in [benchmarks](../benchmarks/README.md). Reports are generated output; historical raw measurements are not required inputs.

Run `node tools/packaging/measure-web-artifacts.mjs` after building to record current web sizes without historical report dependencies. Add `--check` to enforce all historical frontend/gzip budgets; these are manual checks and may expose existing budget drift. The 6 MiB WASM publication limit and browser startup limit remain automatic gates.
