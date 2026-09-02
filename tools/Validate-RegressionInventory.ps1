[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')
$inventoryPath = Join-Path $repositoryRoot 'CStructSharp.Docs/contracts/quality/review-regressions.json'
$adrDirectory = Join-Path $repositoryRoot 'CStructSharp.Docs/decisions/product'

function Require-Text {
    param(
        [Parameter(Mandatory)]$Value,
        [Parameter(Mandatory)][string]$Description
    )

    if ($Value -isnot [string] -or [string]::IsNullOrWhiteSpace($Value)) {
        throw "Regression inventory is missing $Description."
    }
}

function Assert-FocusedTest {
    param(
        [Parameter(Mandatory)]$Test,
        [Parameter(Mandatory)][string]$FindingId
    )

    Require-Text -Value $Test.file -Description "$FindingId test file"
    Require-Text -Value $Test.class -Description "$FindingId test class"
    Require-Text -Value $Test.method -Description "$FindingId test method"
    Require-Text -Value $Test.summaryContains -Description "$FindingId test summary requirement"

    $testPath = Join-Path $repositoryRoot $Test.file
    if (-not (Test-Path -LiteralPath $testPath -PathType Leaf)) {
        throw "$FindingId test file does not exist: $($Test.file)"
    }

    $source = Get-Content -LiteralPath $testPath -Raw
    $classPattern = '(?m)^\s*public\s+class\s+' + [regex]::Escape($Test.class) + '\b'
    if ($source -notmatch $classPattern) {
        throw "$FindingId test class '$($Test.class)' was not found in $($Test.file)."
    }

    $methodPattern = '(?m)^\s*public\s+void\s+' + [regex]::Escape($Test.method) + '\s*\('
    $methodMatches = [regex]::Matches($source, $methodPattern)
    if ($methodMatches.Count -ne 1) {
        throw "$FindingId must resolve to exactly one test method '$($Test.method)'; found $($methodMatches.Count)."
    }

    $methodIndex = $methodMatches[0].Index
    $summaryStart = $source.LastIndexOf('/// <summary', $methodIndex, [StringComparison]::Ordinal)
    $testAttribute = $source.LastIndexOf('[TestMethod]', $methodIndex, [StringComparison]::Ordinal)
    if ($summaryStart -lt 0 -or $testAttribute -lt $summaryStart) {
        throw "$FindingId test '$($Test.method)' must have an XML summary immediately associated with a TestMethod."
    }

    $prefix = $source.Substring($summaryStart, $methodIndex - $summaryStart)
    if ($prefix -notmatch [regex]::Escape($Test.summaryContains)) {
        throw "$FindingId test summary does not contain '$($Test.summaryContains)'."
    }

    if ($prefix -match '\[(?:Ignore|TestCategory\(\"Skipped)') {
        throw "$FindingId test '$($Test.method)' is skipped or categorized as skipped."
    }
}

if (-not (Test-Path -LiteralPath $inventoryPath -PathType Leaf)) {
    throw "Review regression inventory not found: $inventoryPath"
}

$inventory = Get-Content -LiteralPath $inventoryPath -Raw | ConvertFrom-Json -Depth 20
if ($inventory.schemaVersion -ne 2) {
    throw "Unsupported review regression inventory schema: $($inventory.schemaVersion)"
}

$findings = @($inventory.findings)
$expectedIds = 1..9 | ForEach-Object { 'RF-{0:D2}' -f $_ }
$actualIds = @($findings | ForEach-Object id)
$idDifferences = @(Compare-Object -ReferenceObject $expectedIds -DifferenceObject $actualIds)
if ($findings.Count -ne 9 -or
    $idDifferences.Count -ne 0) {
    throw "The inventory must contain exactly RF-01 through RF-09 once each."
}

$fixedTests = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$fixedCount = 0
$openCount = 0

foreach ($finding in $findings) {
    Require-Text -Value $finding.title -Description "$($finding.id) title"
    Require-Text -Value $finding.status -Description "$($finding.id) status"
    foreach ($field in @('layout', 'input', 'operation', 'expected', 'observedBeforeFix')) {
        Require-Text -Value $finding.reproduction.$field -Description "$($finding.id) reproduction $field"
    }

    switch ($finding.status) {
    'fixed' {
            $fixedCount++
            if ($null -eq $finding.test) {
                throw "$($finding.id) is fixed but has no focused test."
            }

            Assert-FocusedTest -Test $finding.test -FindingId $finding.id
            $testIdentity = "$($finding.test.file)::$($finding.test.class).$($finding.test.method)"
            if (-not $fixedTests.Add($testIdentity)) {
                throw "Focused regression test is assigned to more than one finding: $testIdentity"
            }
        }
    'open' {
            $openCount++
            if ($null -ne $finding.test) {
                throw "$($finding.id) is open and must not claim focused fixed-test evidence yet."
            }

            Require-Text -Value $finding.acceptedDecision -Description "$($finding.id) accepted decision"
            $decisionAdrs = @($finding.decisionAdrs)
            if ($decisionAdrs.Count -eq 0) {
                throw "$($finding.id) is open but names no accepted ADR."
            }

            foreach ($adr in $decisionAdrs) {
                if ($adr -notmatch '^ADR-(?<number>\d{3})$') {
                    throw "$($finding.id) has an invalid ADR identifier: $adr"
                }

                $filePrefix = '{0:D4}-' -f [int]$Matches['number']
                $adrFiles = @(Get-ChildItem -LiteralPath $adrDirectory -Filter "$filePrefix*.md" -File)
                if ($adrFiles.Count -ne 1) {
                    throw "$($finding.id) expected one file for $adr; found $($adrFiles.Count)."
                }

                $adrText = Get-Content -LiteralPath $adrFiles[0].FullName -Raw
                if ($adrText -notmatch '(?m)^- Status: Accepted\r?$') {
                    throw "$adr must be Accepted for open implementation finding $($finding.id)."
                }
            }
        }
    default {
            throw "$($finding.id) has unsupported status '$($finding.status)'."
        }
    }
}

if ($fixedCount -ne 9 -or $openCount -ne 0) {
    throw "Expected all nine findings to be fixed; fixed=$fixedCount, open=$openCount."
}

Write-Output 'Regression inventory validation passed.'
Write-Output "Findings: $($findings.Count) ($fixedCount fixed, $openCount open)"
Write-Output "Focused tests: $($fixedTests.Count)"
