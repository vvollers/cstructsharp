$ErrorActionPreference = 'Stop'
$env:DOTNET_ROLL_FORWARD = 'LatestPatch'
function Check { if ($LASTEXITCODE) { throw "Command failed with exit code $LASTEXITCODE" } }
dotnet build CStructSharp.NonWeb.sln -c Release -t:Rebuild *> artifacts-perf-verified-build.log
Check
dotnet test tests/CStructSharpTests/CStructSharpTests.csproj -c Release --no-build *> artifacts-perf-verified-native-tests.log
Check
dotnet test tests/CStructSharpTests/CStructSharpTests.csproj -c Release -f net10.0 --no-build /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura /p:CoverletOutput=C:/projects/github/cstructsharp/artifacts/perf-implementation/coverage/ '/p:Include=[CStructSharp]*' *> artifacts-perf-verified-coverage.log
Check
./tools/quality/Measure-CoverageRisk.ps1 -CoveragePath artifacts/perf-implementation/coverage/coverage.net10.0.cobertura.xml -OutputDirectory artifacts/perf-implementation/coverage -MinimumLinePercent 78 -MinimumBranchPercent 80 -MaximumCriticalRiskFiles 0 -MaximumHighRiskFiles 0 *>> artifacts-perf-verified-coverage.log
try {
    $env:DOTNET_ROLL_FORWARD = 'LatestMajor'
    ./tools/quality/Compare-ManagedApiBaseline.ps1 *> artifacts-perf-verified-api.log
} finally {
    $env:DOTNET_ROLL_FORWARD = 'LatestPatch'
}
dotnet pack src/CStructSharp/CStructSharp.csproj -c Release -o artifacts/perf-implementation/nuget *> artifacts-perf-verified-nuget.log
Check
./tools/packaging/Validate-Package.ps1 -PackagePath artifacts/perf-implementation/nuget/CStructSharp.0.3.3.nupkg -SymbolPackagePath artifacts/perf-implementation/nuget/CStructSharp.0.3.3.snupkg *>> artifacts-perf-verified-nuget.log
./tools/packaging/Test-PackageConsumer.ps1 -PackageDirectory artifacts/perf-implementation/nuget *>> artifacts-perf-verified-nuget.log
node tools/packaging/publish-wasm.mjs *> artifacts-perf-verified-publication.log
Check
node tools/packaging/create-wasm-package.mjs *>> artifacts-perf-verified-publication.log
Check
node tools/packaging/test-public-types.mjs *> artifacts-perf-verified-types.log
Check
node tools/packaging/test-compiled-layout.mjs *> artifacts-perf-verified-lifecycle.log
Check
npm run pack:npm --prefix apps/workshop *> artifacts-perf-verified-npm.log
Check
npm run test:npm --prefix apps/workshop *>> artifacts-perf-verified-npm.log
Check
npm run build --prefix apps/inspector *> artifacts-perf-verified-inspector-build.log
Check
Push-Location apps/inspector
try {
    npm exec -- playwright test tests/e2e/compiled-layout.spec.ts tests/e2e/large-source.spec.ts tests/e2e/binary-types.spec.ts --workers=1 *> ../../artifacts-perf-verified-browser.log
    Check
} finally { Pop-Location }
Write-Output 'Final native, coverage, API, package, lifecycle and browser checks passed.'
