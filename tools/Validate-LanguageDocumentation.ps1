[CmdletBinding()]
param(
    [switch]$SelfTest,
    [string]$ContractPath = (Join-Path $PSScriptRoot '..\CStructSharp.Docs\contracts\language\portable-v1.json'),
    [string]$FixturePath = (Join-Path $PSScriptRoot '..\CStructSharp.Docs\contracts\language\manual-fixtures-v1.json'),
    [string]$MatrixPath = (Join-Path $PSScriptRoot '..\CStructSharp.Docs\contracts\quality\feature-operation-matrix.json'),
    [string]$LanguageRoot = (Join-Path $PSScriptRoot '..\CStructSharp.Docs\language')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

function Assert-Condition {
    param(
        [Parameter(Mandatory)]
        [bool]$Condition,

        [Parameter(Mandatory)]
        [string]$Message
    )

    if (-not $Condition)
    {
        throw $Message
    }
}

function Get-MarkdownAnchor {
    param([Parameter(Mandatory)][string]$Heading)

    $anchor = $Heading.Trim().ToLowerInvariant()
    $anchor = [regex]::Replace($anchor, '<[^>]+>', '')
    $anchor = [regex]::Replace($anchor, '[^a-z0-9 _-]', '')
    $anchor = [regex]::Replace($anchor, '\s+', '-')
    return $anchor.Trim('-')
}

function Assert-ManualReference {
    param(
        [Parameter(Mandatory)]
        [string]$Reference,

        [Parameter(Mandatory)]
        [string]$Context
    )

    $parts = $Reference.Split('#', 2)
    Assert-Condition ($parts.Count -eq 2) `
        "$Context manual reference '$Reference' must use repository-path#anchor format."
    Assert-Condition ($parts[0] -match '^CStructSharp\.Docs/language/.+\.md$') `
        "$Context manual reference '$Reference' must target the tracked language manual."
    Assert-Condition ($parts[1] -match '^[a-z0-9]+(?:-[a-z0-9]+)*$') `
        "$Context manual reference '$Reference' has an invalid anchor."

    $pagePath = Join-Path $RepositoryRoot $parts[0]
    Assert-Condition (Test-Path -LiteralPath $pagePath -PathType Leaf) `
        "$Context manual page '$($parts[0])' does not exist."
    $page = Get-Content -LiteralPath $pagePath -Raw
    $anchors = @(
        [regex]::Matches($page, '(?m)^#{1,6}\s+(?<heading>.+?)\s*$') |
            ForEach-Object { Get-MarkdownAnchor $_.Groups['heading'].Value }
    )
    Assert-Condition ($parts[1] -in $anchors) `
        "$Context manual anchor '#$($parts[1])' does not exist in '$($parts[0])'."
}

if ($SelfTest)
{
    $caught = $false
    try
    {
        Assert-Condition $false 'expected language-validator self-test failure'
    }
    catch
    {
        $caught = $_.Exception.Message -eq 'expected language-validator self-test failure'
    }

    Assert-Condition $caught 'The language-validator self-test did not observe its expected failure.'
    Write-Host 'Validate-LanguageDocumentation self-test passed.'
    return
}

foreach ($path in @($ContractPath, $FixturePath, $MatrixPath, $LanguageRoot))
{
    Assert-Condition (Test-Path -LiteralPath $path) "Required language-documentation input does not exist: $path"
}

$requiredPages = @(
    'index.md',
    'tutorial/index.md',
    'tutorial/01-first-layout.md',
    'tutorial/02-composites-and-layout.md',
    'tutorial/03-runtime-data.md',
    'lexical-rules.md',
    'grammar.md',
    'primitive-types.md',
    'structs-unions-enums-typedefs.md',
    'names-and-scopes.md',
    'arrays-and-strings.md',
    'bitfields.md',
    'expressions-defines-and-variables.md',
    'layout-alignment-and-padding.md',
    'pointers-and-addressing.md',
    'paths-and-selection.md',
    'values-reading-and-writing.md',
    'writing-and-updating.md',
    'compilation-and-operations.md',
    'operation-matrix.md',
    'limits-and-diagnostics.md',
    'differences-from-c.md',
    'cookbook/index.md'
)
foreach ($relativePath in $requiredPages)
{
    Assert-Condition (Test-Path -LiteralPath (Join-Path $LanguageRoot $relativePath) -PathType Leaf) `
        "Required language-manual page is missing: $relativePath"
}

$contract = Get-Content -LiteralPath $ContractPath -Raw | ConvertFrom-Json -Depth 100
$fixtures = Get-Content -LiteralPath $FixturePath -Raw | ConvertFrom-Json -Depth 100
$matrix = Get-Content -LiteralPath $MatrixPath -Raw | ConvertFrom-Json -Depth 100

Assert-Condition ($fixtures.schemaVersion -eq 1) 'Unsupported manual-fixture schema version.'
Assert-Condition ($fixtures.profile -eq 'Portable') 'Manual fixtures must describe the Portable profile.'
Assert-Condition ($fixtures.contractRevision -eq $contract.contractRevision) `
    'Manual fixtures and the canonical Portable contract use different revisions.'

$featureIds = @($matrix.features | ForEach-Object { [string]$_.id })
$fixturePairs = @($fixtures.featurePairs)
$fixtureIds = @($fixturePairs | ForEach-Object { [string]$_.id })
$fixtureFeatureIds = @($fixturePairs | ForEach-Object { [string]$_.featureId })
Assert-Condition ($fixtureIds.Count -eq @($fixtureIds | Select-Object -Unique).Count) `
    'Manual fixture-pair ids are not unique.'
Assert-Condition ($fixtureFeatureIds.Count -eq @($fixtureFeatureIds | Select-Object -Unique).Count) `
    'Each operation-matrix feature must have exactly one manual fixture pair.'
Assert-Condition (@(Compare-Object $featureIds $fixtureFeatureIds).Count -eq 0) `
    'Manual fixture pairs do not cover exactly the operation-matrix feature ids.'

$unsupportedById = @{}
foreach ($unsupported in @($contract.unsupportedConstructs))
{
    $unsupportedById[[string]$unsupported.id] = $unsupported
}

foreach ($pair in $fixturePairs)
{
    $context = "Manual fixture '$($pair.id)'"
    Assert-Condition ($pair.id -match '^[a-z0-9]+(?:-[a-z0-9]+)*$') "$context id is not kebab-case."
    Assert-Condition ($pair.featureId -in $featureIds) "$context names an unknown matrix feature."
    Assert-ManualReference -Reference ([string]$pair.manual) -Context $context

    Assert-Condition (-not [string]::IsNullOrWhiteSpace([string]$pair.valid.definition)) `
        "$context has no valid definition."
    Assert-Condition (-not [string]::IsNullOrWhiteSpace([string]$pair.valid.root)) `
        "$context has no valid root."
    Assert-Condition ($pair.valid.pointerSize -in @(1, 2, 4, 8)) `
        "$context has an invalid pointer width."
    Assert-Condition ($pair.valid.bytes -match '^(?:[0-9A-F]{2})+$') `
        "$context valid bytes must be non-empty uppercase hexadecimal octets."
    Assert-Condition (@($pair.valid.offsets.PSObject.Properties).Count -gt 0) `
        "$context has no exact offset prediction."
    Assert-Condition (@($pair.valid.values.PSObject.Properties).Count -gt 0) `
        "$context has no exact value prediction."

    Assert-Condition ($pair.invalid.stage -in @('compile', 'read')) `
        "$context has an unsupported invalid-fixture stage."
    Assert-Condition ($pair.invalid.errorCode -in @(
            'InvalidLayout',
            'ReadFailed',
            'ReadLimitExceeded')) `
        "$context has an unsupported stable error category."
    if ($pair.invalid.stage -eq 'compile')
    {
        $unsupportedId = [string]$pair.invalid.unsupportedId
        Assert-Condition ($unsupportedById.ContainsKey($unsupportedId)) `
            "$context references unknown unsupported construct '$unsupportedId'."
    }
    else
    {
        Assert-Condition (-not [string]::IsNullOrWhiteSpace([string]$pair.invalid.definition)) `
            "$context read failure has no definition."
        Assert-Condition ($pair.invalid.bytes -match '^(?:[0-9A-F]{2})*$') `
            "$context invalid read bytes are not uppercase hexadecimal octets."
    }
}

foreach ($feature in @($matrix.features))
{
    $context = "Feature '$($feature.id)'"
    Assert-Condition ($feature.PSObject.Properties.Name -contains 'manual') `
        "$context has no manual reference."
    Assert-Condition ($feature.PSObject.Properties.Name -contains 'fixture') `
        "$context has no executable manual-fixture id."
    Assert-ManualReference -Reference ([string]$feature.manual) -Context $context

    $pair = @($fixturePairs | Where-Object { $_.id -eq $feature.fixture })
    Assert-Condition ($pair.Count -eq 1) `
        "$context fixture '$($feature.fixture)' does not resolve exactly once."
    Assert-Condition ($pair[0].featureId -eq $feature.id) `
        "$context fixture belongs to '$($pair[0].featureId)'."
    Assert-Condition ($pair[0].manual -eq $feature.manual) `
        "$context manual reference differs from its fixture."
}

$grammarPath = Join-Path $LanguageRoot 'grammar.md'
$grammar = Get-Content -LiteralPath $grammarPath -Raw
$productions = @(
    [regex]::Matches($grammar, '(?m)^(?<name>[a-z][a-z0-9-]*)\s*=') |
        ForEach-Object { $_.Groups['name'].Value } |
        Select-Object -Unique
)
Assert-Condition ($productions.Count -ge 35) `
    "The complete source/path/lexical grammar must expose at least 35 productions; found $($productions.Count)."
foreach ($production in $productions)
{
    Assert-Condition ($grammar.Contains("| ``$production`` |", [StringComparison]::Ordinal)) `
        "Grammar production '$production' has no production-index explanation."
}

$primitiveReference = Get-Content -LiteralPath (Join-Path $LanguageRoot 'primitive-types.md') -Raw
foreach ($primitive in @($contract.fixedPrimitives) + @($contract.terminatedPrimitives))
{
    Assert-Condition ($primitiveReference.Contains('`' + $primitive.spelling + '`', [StringComparison]::Ordinal)) `
        "Primitive reference does not name canonical spelling '$($primitive.spelling)'."
}

$differenceReference = Get-Content -LiteralPath (Join-Path $LanguageRoot 'differences-from-c.md') -Raw
foreach ($unsupported in @($contract.unsupportedConstructs))
{
    Assert-Condition ($differenceReference.Contains('`' + $unsupported.id + '`', [StringComparison]::Ordinal)) `
        "Differences-from-C reference does not name unsupported fixture '$($unsupported.id)'."
}
Assert-Condition ($differenceReference.Contains('`InvalidLayout`', [StringComparison]::Ordinal)) `
    'Differences-from-C reference must state the stable InvalidLayout category.'

foreach ($example in @($contract.layoutExamples))
{
    Assert-Condition (@($example.values.PSObject.Properties).Count -gt 0) `
        "Canonical predictive example '$($example.id)' has no exact value prediction."
}

Write-Host 'Language documentation validation passed.'
Write-Host "Grammar productions: $($productions.Count)"
Write-Host "Canonical primitive spellings: $(@($contract.fixedPrimitives).Count + @($contract.terminatedPrimitives).Count)"
Write-Host "Predictive layout examples: $(@($contract.layoutExamples).Count)"
Write-Host "Valid/invalid feature pairs: $($fixturePairs.Count)"
Write-Host "Unsupported C constructs: $(@($contract.unsupportedConstructs).Count)"
