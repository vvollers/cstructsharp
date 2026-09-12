param(
    [string]$MainRoot = "$PSScriptRoot/../../../cstructsharp-benchmark-main",
    [string]$FeatureRoot = "$PSScriptRoot/../../../cstructsharp-benchmark-diagnostic",
    [string]$OutputRoot = "$PSScriptRoot/../../artifacts/perf-implementation/refined",
    [string]$ResultRoot = "$PSScriptRoot/../../agentdocs/performance-improvements/compilation-controls",
    [string]$BuildEnvironmentPath = "$PSScriptRoot/../../agentdocs/performance-improvements/refinement/environment.json"
)
$ErrorActionPreference = 'Stop'
$resultRoot = [IO.Path]::GetFullPath($ResultRoot)
$outputRoot = [IO.Path]::GetFullPath($OutputRoot)
$roots = @{main=[IO.Path]::GetFullPath($MainRoot); feature=[IO.Path]::GetFullPath($FeatureRoot); optimized=[IO.Path]::GetFullPath("$PSScriptRoot/../..")}
$fixed = @{main='2c4ad4c';feature='d30ee60'}
foreach ($label in $fixed.Keys) {
    if ((git -C $roots[$label] rev-parse --short=7 HEAD) -ne $fixed[$label]) { throw "Wrong fixed control: $label" }
    if (git -C $roots[$label] status --porcelain --untracked-files=no) { throw "Dirty fixed control: $label" }
}
$buildEnvironment = Get-Content $BuildEnvironmentPath -Raw | ConvertFrom-Json
foreach ($version in $buildEnvironment.versions) {
    $label = $version.label
    if ((Get-FileHash "$outputRoot/$label-native/CStructSharp.dll").Hash -ne $version.nativeHash) { throw "Native build hash changed: $label" }
    foreach ($file in $version.wasmFiles) {
        $path = "$($roots[$label])/src/CStructSharp.Wasm/bin/Release/net10.0/browser-wasm/AppBundle/_framework/$($file.name)"
        if ((Get-FileHash $path).Hash -ne $file.sha256) { throw "WASM build hash changed: $label/$($file.name)" }
    }
}
New-Item -ItemType Directory -Force $resultRoot | Out-Null
$names = @('header','fields128','wideplain128','wideif128')
@(Get-Content "$PSScriptRoot/implementation-cases.json" -Raw | ConvertFrom-Json | Where-Object { $_.name -in $names }) | ConvertTo-Json -Depth 10 | Set-Content "$resultRoot/cases.json"
Copy-Item -LiteralPath $BuildEnvironmentPath -Destination "$resultRoot/environment.json"
$env:BENCH_CASES = "$resultRoot/cases.json"
$env:BENCH_OPERATIONS = 'compile'
for ($round=1; $round -le 2; $round++) {
    $order = if ($round -eq 1) { @('main','optimized','feature') } else { @('feature','optimized','main') }
    foreach ($label in $order) {
        Write-Output "START $label $round $([DateTime]::UtcNow.ToString('o'))"
        dotnet "$outputRoot/$label-native/Comparison.dll" $env:BENCH_CASES $label "$resultRoot/native-$label-$round.json"
        if ($LASTEXITCODE) { throw "Native control failed: $label" }
        node "$PSScriptRoot/run-js.mjs" $roots[$label] $label "$resultRoot/js-$label-$round.json"
        if ($LASTEXITCODE) { throw "JS control failed: $label" }
    }
}
