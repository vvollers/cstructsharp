[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ResultsDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$RunUrl
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$culture = [System.Globalization.CultureInfo]::InvariantCulture

# Use one framework's reports: summing both would count the same test cases twice.
$coverageFiles = @(Get-ChildItem -LiteralPath $ResultsDirectory -Recurse -Filter coverage.cobertura.xml)
$testFiles = @(Get-ChildItem -LiteralPath $ResultsDirectory -Recurse -Filter *.trx)
# VSTest can also copy the attachment into its In/ directory. Accept identical copies only.
$coverageHashes = @($coverageFiles | Get-FileHash | Select-Object -ExpandProperty Hash -Unique)
if ($coverageHashes.Count -ne 1 -or $testFiles.Count -ne 1) {
    throw 'Expected exactly one .NET 10 coverage report and one TRX report.'
}
[xml]$coverage = Get-Content -LiteralPath $coverageFiles[0].FullName -Raw
[xml]$tests = Get-Content -LiteralPath $testFiles[0].FullName -Raw
$packages = @($coverage.SelectNodes('/coverage/packages/package'))
if ($packages.Count -ne 1 -or $packages[0].GetAttribute('name') -ne 'CStructSharp') {
    throw 'Coverage must contain only the CStructSharp library.'
}
$counters = $tests.SelectSingleNode('//*[local-name()="ResultSummary"]/*[local-name()="Counters"]')
if ($null -eq $counters) { throw 'TRX test counters are missing.' }

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
function Write-Badge([string]$Name, [string]$Label, [string]$Message, [string]$Color) {
    [ordered]@{ schemaVersion = 1; label = $Label; message = $Message; color = $Color } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory "$Name.json") -Encoding utf8
}

foreach ($metric in @('line', 'branch')) {
    $rate = [double]::Parse($coverage.coverage.GetAttribute("$metric-rate"), $culture)
    $countAttribute = if ($metric -eq 'line') { 'lines-valid' } else { 'branches-valid' }
    $valid = [long]::Parse($coverage.coverage.GetAttribute($countAttribute), $culture)
    if ($rate -lt 0 -or $rate -gt 1 -or $valid -le 0) { throw "Invalid $metric coverage totals." }
    $percent = ($rate * 100).ToString('0.0', $culture) + '%'
    $color = if ($rate -ge 0.9) { 'brightgreen' } elseif ($rate -ge 0.75) { 'yellowgreen' } else { 'orange' }
    Write-Badge "$metric-coverage" "C# $metric coverage" $percent $color
}

$passed = [int]$counters.GetAttribute('passed')
$failed = [int]$counters.GetAttribute('failed')
$skipped = [int]$counters.GetAttribute('notExecuted')
$total = [int]$counters.GetAttribute('total')
if ($total -le 0 -or $passed + $failed + $skipped -ne $total) { throw 'Incomplete or inconsistent test results.' }
Write-Badge 'tests' 'C# tests' "$passed passed / $failed failed / $skipped skipped" $(if ($failed) { 'red' } else { 'brightgreen' })

@"
# README quality statistics

Source: [CI run]($RunUrl).

Coverage measures only the CStructSharp managed library on .NET 10. Test counts include
parameterized cases from that framework once; they exclude the Vue and browser test suites.
The badges represent the latest successful main-branch CI run, not an in-progress or failed run.
Full TRX and Cobertura reports are available in that run's test-results artifact.
"@ | Set-Content -LiteralPath (Join-Path $OutputDirectory 'README.md') -Encoding utf8
