[CmdletBinding()]
param([Parameter(Mandatory)][string]$ArchivePath)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$web = Join-Path $root 'CStructSharpWeb'
$hostRoot = Join-Path $web 'artifacts/onboarding-host'
$resolved = [IO.Path]::GetFullPath($hostRoot)
$expectedParent = [IO.Path]::GetFullPath((Join-Path $web 'artifacts'))
if ([IO.Path]::GetDirectoryName($resolved) -ne $expectedParent) { throw 'Unsafe browser test staging directory.' }
if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
New-Item -ItemType Directory -Path $resolved | Out-Null
$bundle = Join-Path $resolved 'tools/binary'
Expand-Archive -LiteralPath $ArchivePath -DestinationPath $bundle
Copy-Item -LiteralPath (Join-Path $bundle 'serve.mjs') -Destination (Join-Path $resolved 'serve.mjs')
Push-Location $web
try {
    & node scripts/test-public-types.mjs $bundle
    if ($LASTEXITCODE -ne 0) { throw 'Packaged public TypeScript declarations failed.' }
    & npx playwright test --config playwright.starter.config.ts
    if ($LASTEXITCODE -ne 0) { throw 'Packaged browser onboarding checks failed.' }
} finally { Pop-Location }
