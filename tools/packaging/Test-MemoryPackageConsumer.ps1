<#
.SYNOPSIS
Runs the synthetic memory consumer using only the CStructSharp NuGet package.
.DESCRIPTION
Uses a fresh package cache and verifies package provenance. Does not publish any package.
.PARAMETER PackageDirectory
Directory containing exactly one CStructSharp package and its symbols. If omitted, builds a local package.
.OUTPUTS
Writes consumer evidence under artifacts/memory-package and throws on a failed command or contract.
#>
[CmdletBinding()]
param([string] $PackageDirectory)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$output = Join-Path $repositoryRoot ('artifacts/memory-package/' + [Guid]::NewGuid().ToString('N'))
$consumer = Join-Path $repositoryRoot 'tests/CStructSharp.Memory.PackageConsumer/CStructSharp.Memory.PackageConsumer.csproj'
if ([string]::IsNullOrWhiteSpace($PackageDirectory)) {
    $PackageDirectory = Join-Path $output 'feed'
    & dotnet pack (Join-Path $repositoryRoot 'src/CStructSharp/CStructSharp.csproj') -c Release -o $PackageDirectory
    if ($LASTEXITCODE -ne 0) { throw 'CStructSharp package creation failed.' }
}
$feed = (Resolve-Path -LiteralPath $PackageDirectory).Path
$packages = @(Get-ChildItem -LiteralPath $feed -Filter '*.nupkg' -File)
if ($packages.Count -ne 1) { throw "Expected one CStructSharp package in $feed." }
$archive = [IO.Compression.ZipFile]::OpenRead($packages[0].FullName)
try {
    $entry = $archive.GetEntry('CStructSharp.nuspec')
    if ($null -eq $entry) { throw 'Expected the CStructSharp package manifest.' }
    $reader = [IO.StreamReader]::new($entry.Open())
    try { [xml]$nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
    $version = $nuspec.SelectSingleNode('//*[local-name()="metadata"]/*[local-name()="version"]').InnerText
}
finally { $archive.Dispose() }

& (Join-Path $PSScriptRoot 'Validate-Package.ps1') -PackagePath $packages[0].FullName -SymbolPackagePath (Join-Path $feed "CStructSharp.$version.snupkg")
$previousSource = [Environment]::GetEnvironmentVariable('CSTRUCTSHARP_MEMORY_PACKAGE_SOURCE', 'Process')
try {
    [Environment]::SetEnvironmentVariable('CSTRUCTSHARP_MEMORY_PACKAGE_SOURCE', $feed, 'Process')
    & dotnet restore $consumer --configfile (Join-Path $repositoryRoot 'tests/CStructSharp.Memory.PackageConsumer/NuGet.config') --packages (Join-Path $output 'packages') "-p:MemoryPackageVersion=$version"
    if ($LASTEXITCODE -ne 0) { throw 'Memory consumer restore failed; framework reference packs may require network access.' }
}
finally {
    [Environment]::SetEnvironmentVariable('CSTRUCTSHARP_MEMORY_PACKAGE_SOURCE', $previousSource, 'Process')
}

$metadata = Get-Content -Raw -LiteralPath (Join-Path $output "packages/cstructsharp/$version/.nupkg.metadata") | ConvertFrom-Json
if ([IO.Path]::GetFullPath($metadata.source) -ne $feed) { throw 'Unexpected source for CStructSharp.' }
if (Test-Path -LiteralPath (Join-Path $output 'packages/cstructsharp.memory')) { throw 'Consumer restored a separate memory package.' }
foreach ($framework in @('net8.0', 'net10.0')) {
    & dotnet run --project $consumer -c Release -f $framework --no-restore "-p:MemoryPackageVersion=$version"
    if ($LASTEXITCODE -ne 0) { throw "Memory consumer failed on $framework." }
}
Write-Host "Memory consumer passed using only CStructSharp. Evidence: $output"
