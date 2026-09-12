param([string]$DiagnosticRoot = 'C:/projects/github/cstructsharp-benchmark-diagnostic')
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path "$PSScriptRoot/../..").Path
Set-Location $repoRoot
$resultRoot = "$repoRoot/agentdocs/benchmark-results"
foreach ($mode in @('control-start','registry','bookkeeping','fixedpoint','control-end')) {
    python "$PSScriptRoot/ablate.py" $DiagnosticRoot $mode
    if ($LASTEXITCODE) { throw 'Ablation failed' }
    git -C $DiagnosticRoot diff --unified=0 --output="$resultRoot/ablation-$mode.patch"
    dotnet build "$PSScriptRoot/Comparison.csproj" -c Release "-p:LibraryRoot=$DiagnosticRoot" -o "$repoRoot/artifacts/conditional-comparison/$mode-native" *> "$repoRoot/artifacts-$mode-build.log"
    if ($LASTEXITCODE) { throw "Native build failed: $mode" }
    dotnet publish "$DiagnosticRoot/src/CStructSharp.Wasm/CStructSharpWeb.Wasm.csproj" -c Release "-p:CustomAfterMicrosoftCommonTargets=$PSScriptRoot/Benchmark.targets" -p:RunAnalyzers=false *> "$repoRoot/artifacts-$mode-wasm-build.log"
    if ($LASTEXITCODE) { throw "WASM build failed: $mode" }
    $env:BENCH_OPERATIONS = 'compile,parse'
    dotnet "$repoRoot/artifacts/conditional-comparison/$mode-native/Comparison.dll" "$PSScriptRoot/diagnostic-cases.json" branch "$resultRoot/native-$mode-1.json" *> "$repoRoot/artifacts-native-$mode.log"
    if ($LASTEXITCODE) { throw "Native diagnostic failed: $mode" }
    $env:BENCH_OPERATIONS = 'compile,parseCore'
    $env:BENCH_CASES = "$PSScriptRoot/diagnostic-cases.json"
    node "$PSScriptRoot/run-js.mjs" $DiagnosticRoot branch "$resultRoot/js-$mode-1.json" *> "$repoRoot/artifacts-js-$mode.log"
    if ($LASTEXITCODE) { throw "JS diagnostic failed: $mode" }
}
Remove-Item Env:BENCH_OPERATIONS, Env:BENCH_CASES
