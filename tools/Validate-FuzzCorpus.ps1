[CmdletBinding()]
param(
    [string]$CorpusPath = (Join-Path $PSScriptRoot '..\CStructSharp.Fuzz\corpus\fuzz-corpus.json'),
    [string]$MatrixPath = (Join-Path $PSScriptRoot '..\CStructSharp.Docs\contracts\quality\feature-operation-matrix.json')
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

Assert-Condition (Test-Path -LiteralPath $CorpusPath -PathType Leaf) `
    "Managed fuzz corpus '$CorpusPath' does not exist."
Assert-Condition (Test-Path -LiteralPath $MatrixPath -PathType Leaf) `
    "Feature-operation matrix '$MatrixPath' does not exist."

$corpus = Get-Content -Raw -LiteralPath $CorpusPath | ConvertFrom-Json -Depth 100
$matrix = Get-Content -Raw -LiteralPath $MatrixPath | ConvertFrom-Json -Depth 100
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
Assert-Condition ($corpus.schemaVersion -eq 1) 'Unsupported managed fuzz corpus schema version.'
Assert-Condition ($corpus.seed -match '^0x[0-9A-F]{16}$') `
    'The managed fuzz seed must be a 16-digit uppercase hexadecimal UInt64.'
Assert-Condition ($corpus.iterationsPerTarget -ge 1 -and $corpus.iterationsPerTarget -le 4096) `
    'Iterations per target must be between 1 and 4096.'
Assert-Condition ($corpus.maxInputBytes -ge 1 -and $corpus.maxInputBytes -le 4096) `
    'Maximum input bytes must be between 1 and 4096.'

$limits = $corpus.limits
foreach ($property in @(
             'maxDefinitionLength',
             'maxLayoutNestingDepth',
             'maxExpressionNestingDepth',
             'maxExpressionTokens',
             'maxArrayElements',
             'maxStringBytes',
             'maxTotalBytesRead',
             'maxNestingDepth',
             'maxPointerDepth',
             'maxPointerTargetBytes',
             'maxTotalBytesWritten'
         )) {
    Assert-Condition ($null -ne $limits.PSObject.Properties[$property]) `
        "Managed fuzz limit '$property' is missing."
    Assert-Condition ($limits.$property -ge 1) "Managed fuzz limit '$property' must be positive."
}

Assert-Condition ($limits.maxDefinitionLength -le $corpus.maxInputBytes * 4) `
    'The definition limit is not meaningfully bounded by the input limit.'
Assert-Condition ($limits.maxArrayElements -le 256) 'The fuzz array limit must not exceed 256.'
Assert-Condition ($limits.maxStringBytes -le 4096) 'The fuzz string-byte limit must not exceed 4096.'
Assert-Condition ($limits.maxTotalBytesRead -le 4096) 'The fuzz read-byte limit must not exceed 4096.'
Assert-Condition ($limits.maxTotalBytesWritten -le 4096) 'The fuzz write-byte limit must not exceed 4096.'

$expectedTargets = @('binary-roundtrip', 'definition', 'expression', 'path', 'pointer-union')
$targetIds = @($corpus.targets | ForEach-Object { [string]$_.id })
Assert-Condition ($targetIds.Count -eq @($targetIds | Select-Object -Unique).Count) `
    'The managed fuzz corpus contains duplicate target ids.'
Assert-Condition (@(Compare-Object $expectedTargets $targetIds).Count -eq 0) `
    'The managed fuzz corpus does not contain exactly the five QA-04 targets.'

$seedCount = 0
foreach ($target in @($corpus.targets)) {
    Assert-Condition (@($target.seeds).Count -ge 4) `
        "Fuzz target '$($target.id)' must retain at least four seeds."
    foreach ($seed in @($target.seeds)) {
        $context = "Fuzz seed '$($target.id)/$($seed.id)'"
        Assert-Condition ($seed.id -match '^[a-z0-9]+(?:-[a-z0-9]+)*$') `
            "$context id must be lowercase kebab-case."
        Assert-Condition ($seed.encoding -in @('hex', 'utf8')) "$context has an invalid encoding."
        Assert-Condition (-not [string]::IsNullOrEmpty([string]$seed.data)) "$context is empty."

        if ($seed.encoding -eq 'hex') {
            Assert-Condition ($seed.data -match '^(?:[0-9A-F]{2})+$') `
                "$context must use uppercase hexadecimal octets."
            $byteCount = $seed.data.Length / 2
        }
        else {
            $byteCount = [System.Text.Encoding]::UTF8.GetByteCount([string]$seed.data)
        }

        Assert-Condition ($byteCount -le $corpus.maxInputBytes) `
            "$context exceeds the configured maximum input size."
        $seedCount++
    }
}

$contract = $matrix.fuzzEvidence
Assert-Condition ($contract.schemaVersion -eq $corpus.schemaVersion) `
    'The feature-operation matrix names a different fuzz schema.'
Assert-Condition ($contract.corpus -eq 'CStructSharp.Fuzz/corpus/fuzz-corpus.json') `
    'The feature-operation matrix names an unexpected fuzz corpus.'
Assert-Condition ($contract.project -eq 'CStructSharp.Fuzz/CStructSharp.Fuzz.csproj') `
    'The feature-operation matrix names an unexpected fuzz project.'
Assert-Condition ($contract.validator -eq 'tools/Validate-FuzzCorpus.ps1') `
    'The feature-operation matrix names an unexpected fuzz validator.'
Assert-Condition ($contract.guide -eq 'CStructSharp.Docs/project/testing.md') `
    'The feature-operation matrix names an unexpected fuzz guide.'
Assert-Condition ($contract.workItem -eq 'QA-04') `
    'The feature-operation matrix must assign fuzz evidence to QA-04.'
Assert-Condition (@(Compare-Object $expectedTargets @($contract.targets)).Count -eq 0) `
    'The feature-operation matrix names a different managed fuzz target set.'
Assert-Condition (@($contract.deferredTargets).Count -eq 1 -and $contract.deferredTargets[0] -eq 'json-wasm') `
    'The feature-operation matrix must defer only the JSON/WASM fuzz target.'

foreach ($relativePath in @(
             $contract.corpus,
             $contract.project,
             $contract.validator,
             $contract.guide
         )) {
    Assert-Condition (Test-Path -LiteralPath (Join-Path $repositoryRoot $relativePath) -PathType Leaf) `
        "Managed fuzz evidence file '$relativePath' does not exist."
}

foreach ($testReference in @($contract.tests)) {
    $parts = ([string]$testReference).Split('#', 2)
    Assert-Condition ($parts.Count -eq 2) `
        "Managed fuzz test reference '$testReference' must use path#method format."
    $testPath = Join-Path $repositoryRoot $parts[0]
    Assert-Condition (Test-Path -LiteralPath $testPath -PathType Leaf) `
        "Managed fuzz test source '$($parts[0])' does not exist."
    $testSource = Get-Content -Raw -LiteralPath $testPath
    Assert-Condition ($testSource -match ('\b' + [regex]::Escape($parts[1]) + '\s*\(')) `
        "Managed fuzz test method '$($parts[1])' does not exist in '$($parts[0])'."
}

Write-Host 'Managed fuzz corpus validation passed.'
Write-Host "Targets: $($targetIds.Count)"
Write-Host "Seeds: $seedCount"
Write-Host "Iterations per target: $($corpus.iterationsPerTarget)"
Write-Host "Maximum input bytes: $($corpus.maxInputBytes)"
