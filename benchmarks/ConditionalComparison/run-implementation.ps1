param(
    [switch]$BuildOnly,
    [switch]$SkipBuild,
    [switch]$CompileOnly,
    [switch]$SpotCheck,
    [string]$MainRoot = "$PSScriptRoot/../../../cstructsharp-benchmark-main",
    [string]$FeatureRoot = "$PSScriptRoot/../../../cstructsharp-benchmark-diagnostic",
    [string]$OutputRoot = "$PSScriptRoot/../../artifacts/perf-implementation/final",
    [string]$ResultRoot = "$PSScriptRoot/../../agentdocs/performance-improvements/results"
)
$ErrorActionPreference = 'Stop'
if ($CompileOnly -and $SpotCheck) { throw 'Choose CompileOnly or SpotCheck.' }
$repo = [IO.Path]::GetFullPath("$PSScriptRoot/../..")
$roots = [ordered]@{ main = [IO.Path]::GetFullPath($MainRoot); feature = [IO.Path]::GetFullPath($FeatureRoot); optimized = $repo }
$fixed = @{main='2c4ad4c';feature='d30ee60'}
$out = [IO.Path]::GetFullPath($OutputRoot)
$results = [IO.Path]::GetFullPath($ResultRoot)
New-Item -ItemType Directory -Force $out,$results | Out-Null
foreach ($label in $fixed.Keys) {
    $actual = git -C $roots[$label] rev-parse --short=7 HEAD
    if ($actual -ne $fixed[$label]) { throw "Wrong $label baseline: $actual" }
    if (git -C $roots[$label] status --porcelain --untracked-files=no) { throw "Dirty $label baseline" }
}
if (!$SkipBuild) {
    foreach ($label in $roots.Keys) {
        dotnet build "$PSScriptRoot/Comparison.csproj" -t:Rebuild -c Release "-p:LibraryRoot=$($roots[$label])" -o "$out/$label-native" > "$out/build-$label.log" 2>&1
        if ($LASTEXITCODE) { throw "Native build failed: $label" }
        dotnet publish "$($roots[$label])/src/CStructSharp.Wasm/CStructSharpWeb.Wasm.csproj" -c Release "-p:CustomAfterMicrosoftCommonTargets=$PSScriptRoot/Benchmark.targets" -p:RunAnalyzers=false > "$out/wasm-$label.log" 2>&1
        if ($LASTEXITCODE) { throw "WASM build failed: $label" }
    }
    $environment = foreach ($label in $roots.Keys) {
        $native = "$out/$label-native/CStructSharp.dll"
        $framework = "$($roots[$label])/src/CStructSharp.Wasm/bin/Release/net10.0/browser-wasm/AppBundle/_framework"
        @{label=$label;commit=(git -C $roots[$label] rev-parse HEAD);nativeHash=(Get-FileHash $native).Hash;wasmFiles=@(Get-ChildItem $framework -File | Where-Object { $_.Name -match 'CStructSharp|dotnet.native.wasm' } | ForEach-Object { @{name=$_.Name;bytes=$_.Length;sha256=(Get-FileHash $_.FullName).Hash} })}
    }
    @{utc=[DateTime]::UtcNow.ToString('o');versions=$environment;sdk=(dotnet --version);node=(node --version);casesSha256=(Get-FileHash "$PSScriptRoot/implementation-cases.json").Hash} | ConvertTo-Json -Depth 8 | Set-Content "$results/environment.json"
}
if ($BuildOnly) { return }
$env:BENCH_CASES = "$PSScriptRoot/implementation-cases.json"
if ($SpotCheck) {
    $names = @('header','fields128','bytes1024','plain128','if128','switch128','wideplain128','wideif128','mixedplain128','mixedswitch128','nestedif128')
    @(Get-Content $env:BENCH_CASES -Raw | ConvertFrom-Json | Where-Object { $_.name -in $names }) | ConvertTo-Json -Depth 10 | Set-Content "$results/spot-cases.json"
    $env:BENCH_CASES = "$results/spot-cases.json"
}
if ($CompileOnly) { $env:BENCH_OPERATIONS = 'compile' } else { Remove-Item Env:BENCH_OPERATIONS -ErrorAction SilentlyContinue }
for ($round=1; $round -le 2; $round++) {
    $order = if ($round -eq 1) { @('main','feature','optimized') } else { @('optimized','feature','main') }
    foreach ($label in $order) {
        Write-Output "START native $label round $round"
        if ($SpotCheck) { $env:BENCH_OPERATIONS = 'parse,debug' }
        dotnet "$out/$label-native/Comparison.dll" $env:BENCH_CASES $label "$results/native-$label-$round.json"
        if ($LASTEXITCODE) { throw "Native benchmark failed: $label" }
        Write-Output "START JS $label round $round"
        if ($SpotCheck) {
            $env:BENCH_OPERATIONS = 'parseCore,publicParse,publicDebug'
            if ($label -eq 'optimized') { $env:BENCH_OPERATIONS += ',compiledParse,compiledDebug' }
        }
        node "$PSScriptRoot/run-js.mjs" $roots[$label] $label "$results/js-$label-$round.json"
        if ($LASTEXITCODE) { throw "JS benchmark failed: $label" }
    }
}
