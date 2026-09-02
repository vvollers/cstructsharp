[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$InputPath,

    [Parameter(Mandatory)]
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolvedInput = Resolve-Path -LiteralPath $InputPath
$inputItem = Get-Item -LiteralPath $resolvedInput
$jsonFile = if ($inputItem.PSIsContainer) {
    Get-ChildItem -LiteralPath $inputItem.FullName -Filter '*report-full.json' -File |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
}
else {
    $inputItem
}

if ($null -eq $jsonFile) {
    throw "No BenchmarkDotNet full JSON report found under $InputPath"
}

$source = Get-Content -LiteralPath $jsonFile.FullName -Raw | ConvertFrom-Json -Depth 100
$benchmarks = @($source.Benchmarks)
if ($benchmarks.Count -eq 0) {
    throw 'Benchmark report contains no cases.'
}

$failed = @($benchmarks | Where-Object { $null -eq $_.Statistics })
if ($failed.Count -gt 0) {
    $names = $failed | ForEach-Object { $_.FullName }
    throw "Benchmark report contains cases without statistics: $($names -join ', ')"
}

$normalizedBenchmarks = foreach ($benchmark in $benchmarks | Sort-Object Type, Method, Parameters) {
    [pscustomobject][ordered]@{
        type = $benchmark.Type
        method = $benchmark.Method
        parameters = $benchmark.Parameters
        displayInfo = $benchmark.DisplayInfo
        samples = $benchmark.Statistics.N
        meanNanoseconds = [Math]::Round([double]$benchmark.Statistics.Mean, 3)
        medianNanoseconds = [Math]::Round([double]$benchmark.Statistics.Median, 3)
        standardDeviationNanoseconds = [Math]::Round([double]$benchmark.Statistics.StandardDeviation, 3)
        allocatedBytes = [Math]::Round([double]$benchmark.Memory.BytesAllocatedPerOperation, 3)
    }
}

$repositoryRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')
$revision = (& git -C $repositoryRoot rev-parse HEAD 2>$null | Select-Object -First 1)
$dirty = @(& git -C $repositoryRoot status --porcelain 2>$null).Count -gt 0
$report = [ordered]@{
    schemaVersion = 1
    generatedAtUtc = [DateTime]::UtcNow.ToString('O')
    revision = $revision
    worktreeDirty = $dirty
    source = $jsonFile.Name
    title = $source.Title
    hostEnvironment = $source.HostEnvironmentInfo
    benchmarks = @($normalizedBenchmarks)
}

$outputDirectory = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

$report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $OutputPath -Encoding utf8NoBOM
Write-Output "Normalized $($normalizedBenchmarks.Count) benchmark cases to $OutputPath"
