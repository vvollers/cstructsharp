[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$CoveragePath,

    [Parameter(Mandatory)]
    [string]$OutputDirectory,

    [ValidateRange(0, 100)]
    [double]$MinimumLinePercent = 0,

    [ValidateRange(0, 100)]
    [double]$MinimumBranchPercent = 0,

    [ValidateRange(0, 2147483647)]
    [int]$MaximumCriticalRiskFiles = 2147483647,

    [ValidateRange(0, 2147483647)]
    [int]$MaximumHighRiskFiles = 2147483647
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolvedCoveragePath = Resolve-Path -LiteralPath $CoveragePath
$repositoryRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$resolvedOutputDirectory = Resolve-Path -LiteralPath $OutputDirectory

[xml]$coverage = Get-Content -LiteralPath $resolvedCoveragePath -Raw
$fileLines = [ordered]@{}

foreach ($classNode in $coverage.SelectNodes('//class')) {
    $filename = $classNode.GetAttribute('filename').Replace('\', '/')
    if ([string]::IsNullOrWhiteSpace($filename)) {
        continue
    }

    if (-not $fileLines.Contains($filename)) {
        $fileLines[$filename] = [ordered]@{}
    }

    $lines = $fileLines[$filename]
    foreach ($lineNode in $classNode.SelectNodes('lines/line')) {
        $number = [int]$lineNode.GetAttribute('number')
        $lineKey = $number.ToString([System.Globalization.CultureInfo]::InvariantCulture)
        $hits = [int]$lineNode.GetAttribute('hits')
        $branchesCovered = 0
        $branchesValid = 0
        $conditionCoverage = $lineNode.GetAttribute('condition-coverage')
        if ($lineNode.GetAttribute('branch') -eq 'true' -and
            $conditionCoverage -match '\((?<covered>\d+)\/(?<valid>\d+)\)') {
            $branchesCovered = [int]$Matches['covered']
            $branchesValid = [int]$Matches['valid']
        }

        if ($lines.Contains($lineKey)) {
            $existing = $lines[$lineKey]
            $existing.Hits = [Math]::Max($existing.Hits, $hits)
            $existing.BranchesCovered = [Math]::Max($existing.BranchesCovered, $branchesCovered)
            $existing.BranchesValid = [Math]::Max($existing.BranchesValid, $branchesValid)
        }
        else {
            $lines[$lineKey] = [pscustomobject]@{
                Hits = $hits
                BranchesCovered = $branchesCovered
                BranchesValid = $branchesValid
            }
        }
    }
}

$fileReports = foreach ($filename in $fileLines.Keys) {
    $lines = @($fileLines[$filename].Values)
    $linesValid = $lines.Count
    $linesCovered = @($lines | Where-Object { $_.Hits -gt 0 }).Count
    $branchesValid = ($lines | Measure-Object -Property BranchesValid -Sum).Sum
    $branchesCovered = ($lines | Measure-Object -Property BranchesCovered -Sum).Sum
    if ($null -eq $branchesValid) {
        $branchesValid = 0
    }

    if ($null -eq $branchesCovered) {
        $branchesCovered = 0
    }

    $lineRate = if ($linesValid -eq 0) { 1.0 } else { $linesCovered / $linesValid }
    $branchRate = if ($branchesValid -eq 0) { 1.0 } else { $branchesCovered / $branchesValid }
    $uncoveredLines = $linesValid - $linesCovered
    $uncoveredBranches = $branchesValid - $branchesCovered
    $riskScore = $uncoveredLines + (2 * $uncoveredBranches)
    $riskBand = if ($lineRate -lt 0.60 -or ($branchesValid -ge 10 -and $branchRate -lt 0.50)) {
        'critical'
    }
    elseif ($lineRate -lt 0.75 -or ($branchesValid -ge 10 -and $branchRate -lt 0.65)) {
        'high'
    }
    elseif ($lineRate -lt 0.90 -or ($branchesValid -ge 10 -and $branchRate -lt 0.80)) {
        'medium'
    }
    else {
        'low'
    }

    [pscustomobject][ordered]@{
        file = $filename
        riskBand = $riskBand
        riskScore = $riskScore
        linesCovered = $linesCovered
        linesValid = $linesValid
        lineRate = [Math]::Round($lineRate, 6)
        branchesCovered = $branchesCovered
        branchesValid = $branchesValid
        branchRate = [Math]::Round($branchRate, 6)
        uncoveredLines = $uncoveredLines
        uncoveredBranches = $uncoveredBranches
    }
}

$fileReports = @($fileReports | Sort-Object -Property @{ Expression = 'riskScore'; Descending = $true }, file)
$totalLinesValid = ($fileReports | Measure-Object -Property linesValid -Sum).Sum
$totalLinesCovered = ($fileReports | Measure-Object -Property linesCovered -Sum).Sum
$totalBranchesValid = ($fileReports | Measure-Object -Property branchesValid -Sum).Sum
$totalBranchesCovered = ($fileReports | Measure-Object -Property branchesCovered -Sum).Sum

$revision = (& git -C $repositoryRoot rev-parse HEAD 2>$null | Select-Object -First 1)
$dirty = @(& git -C $repositoryRoot status --porcelain 2>$null).Count -gt 0
$report = [ordered]@{
    schemaVersion = 1
    generatedAtUtc = [DateTime]::UtcNow.ToString('O')
    revision = $revision
    worktreeDirty = $dirty
    source = Split-Path -Leaf $resolvedCoveragePath
    riskFormula = 'uncoveredLines + (2 * uncoveredBranches); bands are informational and are not CI gates'
    summary = [ordered]@{
        files = $fileReports.Count
        linesCovered = $totalLinesCovered
        linesValid = $totalLinesValid
        lineRate = if ($totalLinesValid -eq 0) { 1.0 } else { [Math]::Round($totalLinesCovered / $totalLinesValid, 6) }
        branchesCovered = $totalBranchesCovered
        branchesValid = $totalBranchesValid
        branchRate = if ($totalBranchesValid -eq 0) { 1.0 } else { [Math]::Round($totalBranchesCovered / $totalBranchesValid, 6) }
        criticalRiskFiles = @($fileReports | Where-Object { $_.riskBand -eq 'critical' }).Count
        highRiskFiles = @($fileReports | Where-Object { $_.riskBand -eq 'high' }).Count
    }
    files = $fileReports
}

$jsonPath = Join-Path $resolvedOutputDirectory 'coverage-risk.json'
$csvPath = Join-Path $resolvedOutputDirectory 'coverage-risk.csv'
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $jsonPath -Encoding utf8NoBOM
$fileReports | Export-Csv -LiteralPath $csvPath -NoTypeInformation -Encoding utf8

Write-Output "Coverage risk report written to $jsonPath"
Write-Output "Files: $($fileReports.Count); line rate: $($report.summary.lineRate); branch rate: $($report.summary.branchRate)"

$linePercent = 100 * $report.summary.lineRate
$branchPercent = 100 * $report.summary.branchRate
if ($linePercent -lt $MinimumLinePercent) {
    throw "Line coverage $([Math]::Round($linePercent, 2))% is below the required $MinimumLinePercent%."
}

if ($branchPercent -lt $MinimumBranchPercent) {
    throw "Branch coverage $([Math]::Round($branchPercent, 2))% is below the required $MinimumBranchPercent%."
}

if ($report.summary.criticalRiskFiles -gt $MaximumCriticalRiskFiles) {
    throw "Coverage has $($report.summary.criticalRiskFiles) critical-risk files; at most $MaximumCriticalRiskFiles are allowed."
}

if ($report.summary.highRiskFiles -gt $MaximumHighRiskFiles) {
    throw "Coverage has $($report.summary.highRiskFiles) high-risk files; at most $MaximumHighRiskFiles are allowed."
}
