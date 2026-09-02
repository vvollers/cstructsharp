[CmdletBinding()]
param(
    [string]$ConfigPath = (Join-Path $PSScriptRoot '..\stryker-config.json'),

    [Parameter(Mandatory)]
    [string]$ReportPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-Condition {
    param(
        [Parameter(Mandatory)]
        [bool]$Condition,

        [Parameter(Mandatory)]
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Get-MutantCount {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [object[]]$Mutants,

        [Parameter(Mandatory)]
        [string]$Status
    )

    return @($Mutants | Where-Object { [string]$_.status -eq $Status }).Count
}

$resolvedConfig = (Resolve-Path -LiteralPath $ConfigPath).Path
$resolvedReport = (Resolve-Path -LiteralPath $ReportPath).Path
$configRoot = Get-Content -Raw -LiteralPath $resolvedConfig | ConvertFrom-Json -Depth 100
$config = $configRoot.'stryker-config'
$report = Get-Content -Raw -LiteralPath $resolvedReport | ConvertFrom-Json -Depth 100

Assert-Condition ([string]$config.project -eq 'CStructSharp/CStructSharp.csproj') `
    'The permanent mutation gate must target only the core library project.'
Assert-Condition (@($config.'test-projects').Count -eq 1 -and
                  [string]$config.'test-projects'[0] -eq 'CStructSharpTests/CStructSharpTests.csproj') `
    'The permanent mutation gate must use only the core test project.'
Assert-Condition ([string]$config.'coverage-analysis' -eq 'off') `
    'Coverage-based test selection must remain disabled for the permanent mutation gate.'

$reporters = @($config.reporters | ForEach-Object { [string]$_ })
foreach ($requiredReporter in @('progress', 'json', 'html')) {
    Assert-Condition ($reporters -contains $requiredReporter) `
        "The permanent mutation gate is missing the '$requiredReporter' reporter."
}

Assert-Condition ([int]$config.thresholds.high -eq 75) `
    'The permanent mutation high threshold must be 75%.'
Assert-Condition ([int]$config.thresholds.low -eq 75) `
    'The permanent mutation low threshold must be 75%.'
Assert-Condition ([int]$config.thresholds.break -eq 75) `
    'The permanent mutation break threshold must be 75%.'

$configuredFiles = @($config.mutate | ForEach-Object { ([string]$_).Replace('\', '/') })
Assert-Condition ($configuredFiles.Count -eq 34) `
    "The permanent mutation allowlist must contain exactly 34 semantic files; found $($configuredFiles.Count)."
Assert-Condition ($configuredFiles.Count -eq @($configuredFiles | Select-Object -Unique).Count) `
    'The permanent mutation allowlist contains duplicate files.'
Assert-Condition ($configuredFiles -contains 'CStructDefinitionParser.cs') `
    'The public definition parser must remain in the permanent mutation allowlist.'

Assert-Condition ([string]$report.schemaVersion -eq '2') `
    "Unsupported Stryker report schema '$($report.schemaVersion)'."
Assert-Condition ([int]$report.thresholds.high -eq 75 -and [int]$report.thresholds.low -eq 75) `
    'The report was not produced with the final 75% mutation thresholds.'

$testCount = 0
foreach ($testFile in $report.testFiles.PSObject.Properties) {
    $testCount += @($testFile.Value.tests).Count
}
Assert-Condition ($testCount -eq 562) `
    "The mutation report must contain the final 562-test inventory; found $testCount tests."

$allMutants = [System.Collections.Generic.List[object]]::new()
$configuredReportPaths = [System.Collections.Generic.HashSet[string]]::new(
    [StringComparer]::OrdinalIgnoreCase)

foreach ($fileProperty in $report.files.PSObject.Properties) {
    $reportPath = $fileProperty.Name.Replace('\', '/')
    $mutants = @($fileProperty.Value.mutants)
    foreach ($mutant in $mutants) {
        $allMutants.Add($mutant)
    }

    $validCount = (Get-MutantCount -Mutants $mutants -Status 'Killed') +
        (Get-MutantCount -Mutants $mutants -Status 'Timeout') +
        (Get-MutantCount -Mutants $mutants -Status 'Survived') +
        (Get-MutantCount -Mutants $mutants -Status 'NoCoverage') +
        (Get-MutantCount -Mutants $mutants -Status 'RuntimeError')
    if ($validCount -eq 0) {
        continue
    }

    $match = @(
        $configuredFiles |
            Where-Object {
                $reportPath.EndsWith(
                    "/CStructSharp/$_",
                    [StringComparison]::OrdinalIgnoreCase)
            }
    )
    Assert-Condition ($match.Count -eq 1) `
        "Report file '$reportPath' has tested mutants but is outside the reviewed permanent allowlist."
    [void]$configuredReportPaths.Add($match[0])
}

$parserCompileErrors = 0
foreach ($configuredFile in $configuredFiles) {
    $suffix = "/CStructSharp/$configuredFile"
    $fileProperty = @(
        $report.files.PSObject.Properties |
            Where-Object {
                $_.Name.Replace('\', '/').EndsWith(
                    $suffix,
                    [StringComparison]::OrdinalIgnoreCase)
            }
    )
    Assert-Condition ($fileProperty.Count -eq 1) `
        "The report is missing configured file '$configuredFile'."

    $mutants = @($fileProperty[0].Value.mutants)
    $validCount = (Get-MutantCount -Mutants $mutants -Status 'Killed') +
        (Get-MutantCount -Mutants $mutants -Status 'Timeout') +
        (Get-MutantCount -Mutants $mutants -Status 'Survived') +
        (Get-MutantCount -Mutants $mutants -Status 'NoCoverage') +
        (Get-MutantCount -Mutants $mutants -Status 'RuntimeError')

    if ($configuredFile -eq 'CStructDefinitionParser.cs') {
        $parserCompileErrors = Get-MutantCount -Mutants $mutants -Status 'CompileError'
        Assert-Condition ($validCount -eq 0 -and $parserCompileErrors -gt 0) `
            'The known definition-parser instrumentation limitation changed and requires review.'
        continue
    }

    Assert-Condition ($validCount -gt 0) `
        "Configured semantic file '$configuredFile' produced no valid mutants."
}

$mutantArray = @($allMutants)
$killed = Get-MutantCount -Mutants $mutantArray -Status 'Killed'
$timedOut = Get-MutantCount -Mutants $mutantArray -Status 'Timeout'
$survived = Get-MutantCount -Mutants $mutantArray -Status 'Survived'
$noCoverage = Get-MutantCount -Mutants $mutantArray -Status 'NoCoverage'
$runtimeErrors = Get-MutantCount -Mutants $mutantArray -Status 'RuntimeError'
$compileErrors = Get-MutantCount -Mutants $mutantArray -Status 'CompileError'
$ignored = Get-MutantCount -Mutants $mutantArray -Status 'Ignored'
$valid = $killed + $timedOut + $survived + $noCoverage + $runtimeErrors
$detected = $killed + $timedOut

Assert-Condition ($valid -gt 0) 'The mutation report has no valid mutants.'
$score = 100 * $detected / $valid
$scoreText = $score.ToString('F2', [Globalization.CultureInfo]::InvariantCulture)
Assert-Condition ($score -ge 75) `
    "Permanent mutation score $scoreText% is below the 75% release gate."
Assert-Condition ($survived -eq 0) "The final report contains $survived surviving mutants."
Assert-Condition ($noCoverage -eq 0) "The final report contains $noCoverage uncovered mutants."
Assert-Condition ($runtimeErrors -eq 0) "The final report contains $runtimeErrors runtime-error mutants."

$hash = (Get-FileHash -LiteralPath $resolvedReport -Algorithm SHA256).Hash
Write-Output (
    "Permanent mutation gate passed: $detected/$valid detected " +
    "($scoreText%), $killed killed, $timedOut timed out, " +
    "$survived survived, $noCoverage uncovered, $runtimeErrors runtime errors; " +
    "$compileErrors compile errors ($parserCompileErrors in CStructDefinitionParser.cs), " +
    "$ignored ignored; $testCount tests; 34 configured files; SHA-256 $hash.")
