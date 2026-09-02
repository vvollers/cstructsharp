[CmdletBinding()]
param(
    [string]$PolicyPath = (Join-Path $PSScriptRoot '..\CStructSharp.Docs\contracts\release\rc1.json')
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

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
Assert-Condition (Test-Path -LiteralPath $PolicyPath -PathType Leaf) `
    "Release policy '$PolicyPath' does not exist."

$policy = Get-Content -Raw -LiteralPath $PolicyPath | ConvertFrom-Json -Depth 20
Assert-Condition ([int]$policy.schemaVersion -eq 1) 'Unsupported release-policy schema version.'
Assert-Condition ([string]$policy.policyId -eq 'release-artifacts') 'Unexpected release-policy id.'
Assert-Condition ([string]$policy.publishMode -eq 'artifact-build-only') `
    'Release automation must only build artifacts.'

$expectedArtifacts = @('documentation', 'nuget-package', 'wasm-test-explorer')
$declaredArtifacts = @($policy.requiredArtifacts | ForEach-Object { [string]$_ })
Assert-Condition ((($declaredArtifacts | Sort-Object) -join '|') -eq
                  (($expectedArtifacts | Sort-Object) -join '|')) `
    'The release policy must declare exactly the three release artifacts.'

[xml]$project = Get-Content -Raw -LiteralPath (
    Join-Path $repositoryRoot 'CStructSharp\CStructSharp.csproj')
$properties = @($project.Project.PropertyGroup) |
    Where-Object { $null -ne $_.VersionPrefix } |
    Select-Object -First 1
$version = [string]$properties.VersionPrefix
if (-not [string]::IsNullOrWhiteSpace([string]$properties.VersionSuffix)) {
    $version += "-$([string]$properties.VersionSuffix)"
}
Assert-Condition ($version -eq [string]$policy.packageVersion) `
    "Project package version '$version' does not match policy '$($policy.packageVersion)'."

$releaseWorkflow = Get-Content -Raw -LiteralPath (
    Join-Path $repositoryRoot '.github\workflows\release.yml')
$ciWorkflow = Get-Content -Raw -LiteralPath (
    Join-Path $repositoryRoot '.github\workflows\ci.yml')

foreach ($requiredText in @(
        'workflow_dispatch:',
        'dotnet pack ./CStructSharp/CStructSharp.csproj',
        './tools/Build-Documentation.ps1',
        'npm run build',
        'name: nuget-package',
        'name: documentation',
        'name: wasm-test-explorer')) {
    Assert-Condition ($releaseWorkflow.Contains($requiredText, [StringComparison]::Ordinal)) `
        "Release workflow is missing '$requiredText'."
}

Assert-Condition (-not [regex]::IsMatch(
        $releaseWorkflow,
        'uses:\s+[^@\s]+@v\d+',
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) `
    'Release workflow contains a moving major-version action tag.'

foreach ($forbiddenText in @(
        'dotnet test',
        'dotnet nuget push',
        'gh release create',
        'softprops/action-gh-release',
        'ncipollo/release-action')) {
    Assert-Condition (-not $releaseWorkflow.Contains($forbiddenText, [StringComparison]::OrdinalIgnoreCase)) `
        "Release workflow contains out-of-scope action '$forbiddenText'."
}

foreach ($requiredText in @(
        'dotnet restore ./CStructSharp.NonWeb.sln',
        'dotnet build ./CStructSharp.NonWeb.sln',
        'dotnet test ./CStructSharpTests/CStructSharpTests.csproj',
        'name: test-results')) {
    Assert-Condition ($ciWorkflow.Contains($requiredText, [StringComparison]::Ordinal)) `
        "CI workflow is missing '$requiredText'."
}

foreach ($forbiddenText in @(
        'dotnet pack',
        'Build-Documentation.ps1',
        'npm run build')) {
    Assert-Condition (-not $ciWorkflow.Contains($forbiddenText, [StringComparison]::OrdinalIgnoreCase)) `
        "CI workflow contains release action '$forbiddenText'."
}

Write-Output (
    "Release workflow policy passed: package $version, three build-only artifacts, and build/test-only CI.")
