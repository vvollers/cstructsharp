[CmdletBinding()]
param(
    [string]$ContractPath = (Join-Path $PSScriptRoot '..\CStructSharp.Docs\contracts\language\portable-v1.json'),
    [string]$ReferencePath = (Join-Path $PSScriptRoot '..\CStructSharp.Docs\language\index.md'),
    [string]$MatrixPath = (Join-Path $PSScriptRoot '..\CStructSharp.Docs\contracts\quality\feature-operation-matrix.json')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path

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

Assert-Condition (Test-Path -LiteralPath $ContractPath -PathType Leaf) `
    "Canonical Portable contract '$ContractPath' does not exist."
Assert-Condition (Test-Path -LiteralPath $ReferencePath -PathType Leaf) `
    "Canonical layout reference '$ReferencePath' does not exist."
Assert-Condition (Test-Path -LiteralPath $MatrixPath -PathType Leaf) `
    "Feature-operation matrix '$MatrixPath' does not exist."

$contract = Get-Content -Raw -LiteralPath $ContractPath | ConvertFrom-Json -Depth 100
$matrix = Get-Content -Raw -LiteralPath $MatrixPath | ConvertFrom-Json -Depth 100
$referenceDirectory = Split-Path -Parent (Resolve-Path -LiteralPath $ReferencePath).Path
$referenceFiles = @(
    Get-ChildItem -LiteralPath $referenceDirectory -Recurse -File -Filter '*.md' |
        Sort-Object -Property FullName
)
Assert-Condition ($referenceFiles.Count -ge 23) `
    "The split language manual is incomplete; found only $($referenceFiles.Count) pages."
$reference = [string]::Join(
    "`n",
    @($referenceFiles | ForEach-Object { Get-Content -Raw -LiteralPath $_.FullName }))

Assert-Condition ($contract.schemaVersion -eq 1) 'Unsupported canonical Portable contract schema version.'
Assert-Condition ($contract.contractRevision -eq 1) 'Unsupported canonical Portable contract revision.'
Assert-Condition ($contract.profile -eq 'Portable') 'The canonical contract must describe the Portable profile.'
Assert-Condition (@($contract.shippedProfiles).Count -eq 1 -and $contract.shippedProfiles[0] -eq 'Portable') `
    'Portable must be the sole shipped profile.'
Assert-Condition ($matrix.canonicalReference.profile -eq $contract.profile) `
    'The feature-operation matrix names a different canonical profile.'
Assert-Condition ($matrix.canonicalReference.contractRevision -eq $contract.contractRevision) `
    'The feature-operation matrix names a different canonical contract revision.'
Assert-Condition ($matrix.canonicalReference.contract -eq 'CStructSharp.Docs/contracts/language/portable-v1.json') `
    'The feature-operation matrix has an unexpected canonical contract path.'
Assert-Condition ($matrix.canonicalReference.manualFixtures -eq
                  'CStructSharp.Docs/contracts/language/manual-fixtures-v1.json') `
    'The feature-operation matrix has an unexpected manual-fixture contract path.'
Assert-Condition ($matrix.canonicalReference.reference -eq 'CStructSharp.Docs/language/index.md') `
    'The feature-operation matrix has an unexpected human reference path.'
Assert-Condition ($matrix.canonicalReference.validator -eq 'tools/Validate-CanonicalReference.ps1') `
    'The feature-operation matrix has an unexpected canonical validator path.'
Assert-Condition ($matrix.canonicalReference.workItem -eq 'DOC-01') `
    'The feature-operation matrix must assign the canonical reference to DOC-01.'

$fixedSpellings = @($contract.fixedPrimitives | ForEach-Object { [string]$_.spelling })
$terminatedSpellings = @($contract.terminatedPrimitives | ForEach-Object { [string]$_.spelling })
$matrixFixed = @($matrix.primitiveSpellings.fixed | ForEach-Object { [string]$_ })
$matrixTerminated = @($matrix.primitiveSpellings.terminated | ForEach-Object { [string]$_ })

Assert-Condition ($fixedSpellings.Count -eq @($fixedSpellings | Select-Object -Unique).Count) `
    'The canonical fixed-primitive table contains duplicate spellings.'
Assert-Condition ($terminatedSpellings.Count -eq @($terminatedSpellings | Select-Object -Unique).Count) `
    'The canonical terminated-primitive table contains duplicate spellings.'
Assert-Condition (@(Compare-Object $matrixFixed $fixedSpellings).Count -eq 0) `
    'The canonical fixed-primitive spellings differ from the feature-operation matrix.'
Assert-Condition (@(Compare-Object $matrixTerminated $terminatedSpellings).Count -eq 0) `
    'The canonical terminated-primitive spellings differ from the feature-operation matrix.'

foreach ($primitive in @($contract.fixedPrimitives)) {
    $context = "Fixed primitive '$($primitive.spelling)'"
    Assert-Condition (-not [string]::IsNullOrWhiteSpace([string]$primitive.canonical)) `
        "$context has no canonical codec."
    Assert-Condition ($primitive.bytes -in @(1, 2, 4, 8)) "$context has an invalid byte width."
    Assert-Condition ($primitive.alignment -eq $primitive.bytes) `
        "$context alignment must equal its Portable byte width."
    Assert-Condition ($primitive.signedness -in @('signed', 'unsigned', 'code-unit')) `
        "$context has an invalid signedness classification."
    Assert-Condition ($primitive.endian -in @('independent', 'layout', 'little', 'big')) `
        "$context has an invalid endian classification."
    Assert-Condition (-not [string]::IsNullOrWhiteSpace([string]$primitive.clr)) "$context has no CLR result type."
    Assert-Condition (-not [string]::IsNullOrWhiteSpace([string]$primitive.writerDomain)) `
        "$context has no writer domain."
}

foreach ($primitive in @($contract.terminatedPrimitives)) {
    $context = "Terminated primitive '$($primitive.spelling)'"
    Assert-Condition (-not [string]::IsNullOrWhiteSpace([string]$primitive.encoding)) `
        "$context has no encoding."
    Assert-Condition ($primitive.terminator -in @('NUL', 'LF')) "$context has an invalid terminator."
    Assert-Condition ($primitive.endian -in @('independent', 'layout', 'little', 'big')) `
        "$context has an invalid endian classification."
    Assert-Condition ($primitive.alignment -eq 1) "$context alignment must be one."
    Assert-Condition ($primitive.clr -eq 'String') "$context must return System.String."
}

$exampleIds = @($contract.layoutExamples | ForEach-Object { [string]$_.id })
Assert-Condition ($exampleIds.Count -ge 6) 'The canonical contract must contain at least six predictive examples.'
Assert-Condition ($exampleIds.Count -eq @($exampleIds | Select-Object -Unique).Count) `
    'The canonical layout examples contain duplicate ids.'
foreach ($example in @($contract.layoutExamples)) {
    $context = "Layout example '$($example.id)'"
    Assert-Condition ($example.id -match '^[a-z0-9]+(?:-[a-z0-9]+)*$') `
        "$context id must be lowercase kebab-case."
    Assert-Condition (-not [string]::IsNullOrWhiteSpace([string]$example.definition)) `
        "$context has no definition."
    Assert-Condition (-not [string]::IsNullOrWhiteSpace([string]$example.root)) "$context has no root."
    Assert-Condition ($example.pointerSize -in @(1, 2, 4, 8)) "$context has an invalid pointer size."
    Assert-Condition ($example.size -ge 0) "$context has a negative size."
    Assert-Condition ($example.alignment -ge 1) "$context has an invalid alignment."
    Assert-Condition (@($example.offsets.PSObject.Properties).Count -gt 0) "$context has no predicted offsets."
    Assert-Condition (@($example.values.PSObject.Properties).Count -gt 0) "$context has no predicted values."
    Assert-Condition ($example.bytes -match '^(?:[0-9A-F]{2})*$') `
        "$context bytes must be uppercase hexadecimal octets without separators."
    Assert-Condition (($example.bytes.Length / 2) -eq $example.size) `
        "$context byte image length does not match its predicted size."
    Assert-Condition ($reference.Contains('`' + $example.id + '`', [StringComparison]::Ordinal)) `
        "$context is not named in the canonical reference."
}

$unsupportedIds = @($contract.unsupportedConstructs | ForEach-Object { [string]$_.id })
Assert-Condition ($unsupportedIds.Count -ge 15) `
    'The canonical contract must contain a representative valid-C unsupported corpus.'
Assert-Condition ($unsupportedIds.Count -eq @($unsupportedIds | Select-Object -Unique).Count) `
    'The canonical unsupported corpus contains duplicate ids.'
foreach ($item in @($contract.unsupportedConstructs)) {
    $context = "Unsupported construct '$($item.id)'"
    Assert-Condition ($item.id -match '^[a-z0-9]+(?:-[a-z0-9]+)*$') `
        "$context id must be lowercase kebab-case."
    Assert-Condition (-not [string]::IsNullOrWhiteSpace([string]$item.definition)) `
        "$context has no definition."
    Assert-Condition (-not [string]::IsNullOrWhiteSpace([string]$item.category)) `
        "$context has no category."
}

foreach ($testReference in @($matrix.canonicalReference.tests)) {
    $parts = ([string]$testReference).Split('#', 2)
    Assert-Condition ($parts.Count -eq 2) `
        "Canonical test reference '$testReference' must use path#method format."
    $sourcePath = Join-Path $repositoryRoot $parts[0]
    Assert-Condition (Test-Path -LiteralPath $sourcePath -PathType Leaf) `
        "Canonical test source '$($parts[0])' does not exist."
    $source = Get-Content -Raw -LiteralPath $sourcePath
    Assert-Condition ($source -match ('\b' + [regex]::Escape($parts[1]) + '\s*\(')) `
        "Canonical test method '$($parts[1])' does not exist in '$($parts[0])'."
}

foreach ($heading in @(
             '# Portable v1 rules',
             '# Complete Portable grammar',
             '# Primitive types',
             '## Checked layout examples',
             '# Differences from C'
         )) {
    Assert-Condition ($reference.Contains($heading, [StringComparison]::Ordinal)) `
        "The canonical reference is missing heading '$heading'."
}

foreach ($spelling in @($fixedSpellings + $terminatedSpellings)) {
    Assert-Condition ($reference.Contains('`' + $spelling + '`', [StringComparison]::Ordinal)) `
        "The canonical reference does not name primitive spelling '$spelling'."
}

Write-Host 'Canonical Portable reference validation passed.'
Write-Host "Fixed primitives: $($fixedSpellings.Count)"
Write-Host "Terminated primitives: $($terminatedSpellings.Count)"
Write-Host "Predictive layout examples: $($exampleIds.Count)"
Write-Host "Unsupported C constructs: $($unsupportedIds.Count)"
