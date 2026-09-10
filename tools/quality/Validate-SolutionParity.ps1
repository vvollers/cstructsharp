[CmdletBinding()]
param(
    [string]$FullSolutionPath = (Join-Path $PSScriptRoot '../../CStructSharp.sln'),

    [string]$NonWebSolutionPath = (Join-Path $PSScriptRoot '../../CStructSharp.NonWeb.sln'),

    [string[]]$WebOnlyProjects = @('src\CStructSharp.Wasm\CStructSharpWeb.Wasm.csproj'),

    [string[]]$DeliberatelyExcludedProjects = @('tests/CStructSharp.PackageConsumer\CStructSharp.PackageConsumer.csproj')
)

<#
.SYNOPSIS
Guards that CStructSharp.sln and CStructSharp.NonWeb.sln reference the same projects except for the deliberate
CStructSharpWeb.Wasm exclusion, and that CStructSharp.PackageConsumer stays excluded from both.

.DESCRIPTION
The two solution files are hand-maintained; a newly added project could silently land in only one of them
(the architecture improvement plan, AP-4.5). This asserts CStructSharp.NonWeb.sln's project set equals
CStructSharp.sln's minus $WebOnlyProjects, and that neither solution references $DeliberatelyExcludedProjects.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '../CStructSharp.Tooling.psm1') -Force

function Get-SolutionProjectPaths {
    param(
        [Parameter(Mandatory)]
        [string]$SolutionPath
    )

    Assert-Condition (Test-Path $SolutionPath) "Solution file not found: $SolutionPath"

    $pattern = 'Project\("\{[0-9A-Fa-f-]+\}"\)\s*=\s*"[^"]+",\s*"([^"]+\.csproj)"'
    $matches = Select-String -Path $SolutionPath -Pattern $pattern -AllMatches
    $paths = [System.Collections.Generic.List[string]]::new()
    foreach ($match in $matches) {
        foreach ($groupMatch in $match.Matches) {
            $paths.Add($groupMatch.Groups[1].Value)
        }
    }

    return ,$paths
}

$fullProjects = [System.Collections.Generic.HashSet[string]]::new(
    [string[]](Get-SolutionProjectPaths -SolutionPath $FullSolutionPath),
    [System.StringComparer]::OrdinalIgnoreCase)
$nonWebProjects = [System.Collections.Generic.HashSet[string]]::new(
    [string[]](Get-SolutionProjectPaths -SolutionPath $NonWebSolutionPath),
    [System.StringComparer]::OrdinalIgnoreCase)

$expectedNonWebProjects = [System.Collections.Generic.HashSet[string]]::new(
    $fullProjects, [System.StringComparer]::OrdinalIgnoreCase)
foreach ($webOnlyProject in $WebOnlyProjects) {
    Assert-Condition $fullProjects.Contains($webOnlyProject) `
        "Expected web-only project '$webOnlyProject' was not found in $FullSolutionPath."
    [void]$expectedNonWebProjects.Remove($webOnlyProject)
}

$missingFromNonWeb = [System.Collections.Generic.List[string]]::new()
foreach ($project in $expectedNonWebProjects) {
    if (-not $nonWebProjects.Contains($project)) {
        $missingFromNonWeb.Add($project)
    }
}

Assert-Condition ($missingFromNonWeb.Count -eq 0) (
    "CStructSharp.NonWeb.sln is missing project(s) present in CStructSharp.sln: " +
    "$($missingFromNonWeb -join ', '). Add them to CStructSharp.NonWeb.sln, " +
    "or to `$WebOnlyProjects if the exclusion is deliberate.")

$unexpectedInNonWeb = [System.Collections.Generic.List[string]]::new()
foreach ($project in $nonWebProjects) {
    if (-not $expectedNonWebProjects.Contains($project)) {
        $unexpectedInNonWeb.Add($project)
    }
}

Assert-Condition ($unexpectedInNonWeb.Count -eq 0) (
    "CStructSharp.NonWeb.sln references project(s) not present in CStructSharp.sln: " +
    "$($unexpectedInNonWeb -join ', ').")

foreach ($excludedProject in $DeliberatelyExcludedProjects) {
    Assert-Condition (-not $fullProjects.Contains($excludedProject)) (
        "'$excludedProject' is deliberately excluded from both solutions but was found in $FullSolutionPath.")
    Assert-Condition (-not $nonWebProjects.Contains($excludedProject)) (
        "'$excludedProject' is deliberately excluded from both solutions but was found in $NonWebSolutionPath.")
}

Write-Host "Solution project-list parity verified: $($fullProjects.Count) project(s) in CStructSharp.sln, " `
    "$($nonWebProjects.Count) in CStructSharp.NonWeb.sln, deliberate exclusions confirmed."
