[CmdletBinding()]
param(
    [string[]]$EvidencePath = @(
        (Join-Path $PSScriptRoot '..\CStructSharp.Docs\contracts\quality\compiler-fixtures\baselines')
    ),
    [string]$SourcePath = (Join-Path $PSScriptRoot 'compiler-fixtures\portable-host-facts.c'),
    [string]$MatrixPath = (Join-Path $PSScriptRoot '..\CStructSharp.Docs\contracts\quality\feature-operation-matrix.json')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$usingDefaultEvidence = -not $PSBoundParameters.ContainsKey('EvidencePath')

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

function Assert-PositiveLayout {
    param(
        [Parameter(Mandatory)]
        [object]$Layout,

        [Parameter(Mandatory)]
        [string]$Context,

        [switch]$RequireBytes
    )

    Assert-Condition ($Layout.size -ge 1) "$Context has an invalid size."
    Assert-Condition ($Layout.alignment -ge 1) "$Context has an invalid alignment."
    Assert-Condition (($Layout.size % $Layout.alignment) -eq 0) `
        "$Context size is not a multiple of its alignment."

    foreach ($offset in @($Layout.offsets.PSObject.Properties)) {
        Assert-Condition ($offset.Value -ge 0 -and $offset.Value -lt $Layout.size) `
            "$Context offset '$($offset.Name)' is outside the object."
    }

    if ($RequireBytes) {
        Assert-Condition ($Layout.bytes -match '^(?:[0-9A-F]{2})+$') `
            "$Context bytes must be uppercase hexadecimal octets."
        Assert-Condition (($Layout.bytes.Length / 2) -eq $Layout.size) `
            "$Context byte-image length does not match its size."
    }
}

Assert-Condition (Test-Path -LiteralPath $SourcePath -PathType Leaf) `
    "Compiler fixture source '$SourcePath' does not exist."
Assert-Condition (Test-Path -LiteralPath $MatrixPath -PathType Leaf) `
    "Feature-operation matrix '$MatrixPath' does not exist."

$resolvedSource = (Resolve-Path -LiteralPath $SourcePath).Path
$sourceHash = (Get-FileHash -LiteralPath $resolvedSource -Algorithm SHA256).Hash
$matrix = Get-Content -Raw -LiteralPath $MatrixPath | ConvertFrom-Json -Depth 100
$contract = $matrix.compilerEvidence
Assert-Condition ($contract.schemaVersion -eq 1) `
    'The feature-operation matrix names an unsupported compiler-evidence schema.'
Assert-Condition ($contract.claim -eq 'observation-only') `
    'The feature-operation matrix must keep compiler evidence observation-only.'
Assert-Condition ($contract.source -eq 'tools/compiler-fixtures/portable-host-facts.c') `
    'The feature-operation matrix names an unexpected compiler fixture source.'
Assert-Condition ($contract.runner -eq 'tools/Invoke-CompilerDifferentialFixture.ps1') `
    'The feature-operation matrix names an unexpected compiler fixture runner.'
Assert-Condition ($contract.validator -eq 'tools/Validate-CompilerFixture.ps1') `
    'The feature-operation matrix names an unexpected compiler fixture validator.'
Assert-Condition ($contract.guide -eq 'CStructSharp.Docs/project/testing.md') `
    'The feature-operation matrix names an unexpected compiler fixture guide.'
Assert-Condition ($contract.workItem -eq 'QA-03') `
    'The feature-operation matrix must assign compiler evidence to QA-03.'

foreach ($relativePath in @(
             $contract.source,
             $contract.runner,
             $contract.validator,
             $contract.guide
         ) + @($contract.baselines)) {
    Assert-Condition (Test-Path -LiteralPath (Join-Path $repositoryRoot $relativePath) -PathType Leaf) `
        "Compiler-evidence file '$relativePath' does not exist."
}

foreach ($testReference in @($contract.tests)) {
    $parts = ([string]$testReference).Split('#', 2)
    Assert-Condition ($parts.Count -eq 2) `
        "Compiler test reference '$testReference' must use path#method format."
    $testPath = Join-Path $repositoryRoot $parts[0]
    Assert-Condition (Test-Path -LiteralPath $testPath -PathType Leaf) `
        "Compiler test source '$($parts[0])' does not exist."
    $testSource = Get-Content -Raw -LiteralPath $testPath
    Assert-Condition ($testSource -match ('\b' + [regex]::Escape($parts[1]) + '\s*\(')) `
        "Compiler test method '$($parts[1])' does not exist in '$($parts[0])'."
}

$evidenceFiles = [System.Collections.Generic.List[System.IO.FileInfo]]::new()

foreach ($path in $EvidencePath) {
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        $evidenceFiles.Add((Get-Item -LiteralPath $path))
        continue
    }

    if (Test-Path -LiteralPath $path -PathType Container) {
        foreach ($file in Get-ChildItem -LiteralPath $path -Filter '*.json' -File -Recurse) {
            $evidenceFiles.Add($file)
        }

        continue
    }

    throw "Compiler evidence path '$path' does not exist."
}

Assert-Condition ($evidenceFiles.Count -gt 0) 'No compiler evidence JSON files were found.'
$compilerFamilies = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase)
$fixtureIds = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::Ordinal)

foreach ($file in @($evidenceFiles | Sort-Object FullName -Unique)) {
    $evidence = Get-Content -Raw -LiteralPath $file.FullName | ConvertFrom-Json -Depth 100
    $context = "Compiler evidence '$($file.FullName)'"

    Assert-Condition ($evidence.schemaVersion -eq 1) "$context has an unsupported schema version."
    Assert-Condition ($evidence.evidenceKind -eq 'compiler-observation') `
        "$context has an invalid evidence kind."
    Assert-Condition ($evidence.claim -eq 'observation-only') `
        "$context must be explicitly observation-only."
    Assert-Condition (-not $evidence.PSObject.Properties['profile']) `
        "$context must not claim an implemented ABI profile."

    Assert-Condition ($evidence.fixture.id -eq 'portable-host-facts') `
        "$context has an unexpected fixture id."
    Assert-Condition ($evidence.fixture.source -eq 'tools/compiler-fixtures/portable-host-facts.c') `
        "$context has an unexpected fixture source."
    Assert-Condition ($evidence.fixture.sha256 -eq $sourceHash) `
        "$context is stale: its source SHA-256 does not match the checked-in fixture."
    [void]$fixtureIds.Add([string]$evidence.fixture.id)

    Assert-Condition ($evidence.compiler.family -in @('Clang', 'GCC')) `
        "$context names an unsupported compiler family."
    Assert-Condition (-not [string]::IsNullOrWhiteSpace([string]$evidence.compiler.executable)) `
        "$context has no compiler executable."
    Assert-Condition (-not [string]::IsNullOrWhiteSpace([string]$evidence.compiler.version)) `
        "$context has no compiler version."
    Assert-Condition (-not [string]::IsNullOrWhiteSpace([string]$evidence.compiler.versionOutput)) `
        "$context has no compiler version output."
    Assert-Condition (-not [string]::IsNullOrWhiteSpace([string]$evidence.compiler.target)) `
        "$context has no compiler target."
    Assert-Condition ($evidence.compiler.language -eq 'C11') `
        "$context must record C11 as its language mode."
    Assert-Condition (@($evidence.compiler.flags).Count -ge 4) `
        "$context does not record the complete strict compilation flags."
    [void]$compilerFamilies.Add([string]$evidence.compiler.family)

    Assert-Condition ($evidence.host.os -in @('Linux', 'macOS', 'Windows')) `
        "$context has an unsupported host OS label."
    Assert-Condition (-not [string]::IsNullOrWhiteSpace([string]$evidence.host.architecture)) `
        "$context has no host architecture."

    Assert-Condition ($evidence.facts.endian -in @('little', 'big')) `
        "$context has an unsupported byte order."
    Assert-Condition ($evidence.facts.char.signed -is [bool]) `
        "$context does not report plain-char signedness."
    foreach ($scalarName in @('char', 'short', 'int', 'long', 'longLong', 'wchar', 'pointer', 'enum')) {
        $scalar = $evidence.facts.$scalarName
        Assert-Condition ($null -ne $scalar) "$context is missing scalar '$scalarName'."
        Assert-Condition ($scalar.size -ge 1) "$context scalar '$scalarName' has an invalid size."
        Assert-Condition ($scalar.alignment -ge 1) `
            "$context scalar '$scalarName' has an invalid alignment."
    }

    Assert-PositiveLayout $evidence.facts.fixedWidthAggregate "$context fixed-width aggregate" -RequireBytes
    Assert-PositiveLayout $evidence.facts.nestedArray "$context nested array" -RequireBytes
    Assert-PositiveLayout $evidence.facts.union "$context union" -RequireBytes
    Assert-PositiveLayout $evidence.facts.bitfield "$context bitfield" -RequireBytes
    Assert-PositiveLayout $evidence.facts.pointerAggregate "$context pointer aggregate"
}

if ($usingDefaultEvidence) {
    Assert-Condition ($evidenceFiles.Count -ge 2) `
        'The checked-in baseline set must contain at least two compiler observations.'
    Assert-Condition ($compilerFamilies.Contains('Clang') -and $compilerFamilies.Contains('GCC')) `
        'The checked-in baseline set must contain both Clang and GCC observations.'
}

Write-Host 'Compiler-differential fixture validation passed.'
Write-Host "Evidence files: $($evidenceFiles.Count)"
Write-Host "Compiler families: $([string]::Join(', ', @($compilerFamilies | Sort-Object)))"
Write-Host "Fixture ids: $([string]::Join(', ', @($fixtureIds | Sort-Object)))"
