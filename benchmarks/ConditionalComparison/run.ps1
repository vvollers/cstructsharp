param(
    [Parameter(Mandatory=$true)][string]$MainRoot,
    [int]$Rounds = 2
)
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path "$PSScriptRoot/../..").Path
Set-Location $repoRoot
$outputRoot = Join-Path $repoRoot 'artifacts/conditional-comparison'
$resultRoot = Join-Path $repoRoot 'agentdocs/benchmark-results'
New-Item -ItemType Directory -Force $outputRoot, $resultRoot | Out-Null
foreach ($label in @('main','branch')) {
    $sourceRoot = if ($label -eq 'main') { $MainRoot } else { $repoRoot }
    dotnet build "$PSScriptRoot/Comparison.csproj" -c Release "-p:LibraryRoot=$sourceRoot" -o "$outputRoot/$label-native"
    if ($LASTEXITCODE) { throw "Native build failed: $label" }
    dotnet publish "$sourceRoot/src/CStructSharp.Wasm/CStructSharpWeb.Wasm.csproj" -c Release "-p:CustomAfterMicrosoftCommonTargets=$PSScriptRoot/Benchmark.targets" -p:RunAnalyzers=false
    if ($LASTEXITCODE) { throw "WASM build failed: $label" }
}
for ($round = 1; $round -le $Rounds; $round++) {
    $labels = if ($round % 2) { @('main','branch') } else { @('branch','main') }
    foreach ($label in $labels) {
        $sourceRoot = if ($label -eq 'main') { $MainRoot } else { $repoRoot }
        dotnet "$outputRoot/$label-native/Comparison.dll" "$PSScriptRoot/cases.json" $label "$resultRoot/native-$label-$round.json"
        if ($LASTEXITCODE) { throw "Native benchmark failed: $label" }
        node "$PSScriptRoot/run-js.mjs" $sourceRoot $label "$resultRoot/js-$label-$round.json"
        if ($LASTEXITCODE) { throw "JavaScript benchmark failed: $label" }
    }
}
python "$PSScriptRoot/summarize.py"
