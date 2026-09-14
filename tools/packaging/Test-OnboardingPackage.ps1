[CmdletBinding()]
param([Parameter(Mandatory)][string]$PackageDirectory)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$packages = @(Get-ChildItem -LiteralPath $PackageDirectory -Filter '*.nupkg' -File)
if ($packages.Count -ne 1) { throw 'Provide a directory containing exactly one CStructSharp .nupkg.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($packages[0].FullName)
try {
    $entry = @($archive.Entries | Where-Object FullName -Like '*.nuspec')[0]
    $reader = [IO.StreamReader]::new($entry.Open())
    try { [xml]$manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
    $version = $manifest.SelectSingleNode('//*[local-name()="metadata"]/*[local-name()="version"]').InnerText
    $readmeEntry = $archive.GetEntry('README.md')
    if ($null -eq $readmeEntry) { throw 'The package README is missing.' }
    $reader = [IO.StreamReader]::new($readmeEntry.Open())
    try { $readme = $reader.ReadToEnd() } finally { $reader.Dispose() }
} finally { $archive.Dispose() }

& node (Join-Path $PSScriptRoot '../documentation/export-documentation-examples.mjs')
if ($LASTEXITCODE -ne 0) { throw 'Recipe generation failed.' }
if (@(Get-ChildItem (Join-Path $repository 'docs/examples/recipes') -Filter '*.cs').Count -ne 28) { throw 'Expected all 28 recipes.' }
$starter = Join-Path $repository 'docs/examples/starter'
$documentedCode = [regex]::Match($readme, '(?s)```csharp\r?\n(.*?)\r?\n```').Groups[1].Value.Trim()
if ($documentedCode -ne (Get-Content (Join-Path $starter 'Program.cs') -Raw).Trim()) {
    throw 'The packaged README code does not match the tested starter.'
}

$temporaryBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$work = Join-Path $temporaryBase ('cstructsharp-onboarding-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
$source = [Security.SecurityElement]::Escape($packages[0].DirectoryName)
Set-Content (Join-Path $work 'NuGet.config') @"
<configuration><packageSources><clear/><add key="candidate" value="$source"/><add key="nuget" value="https://api.nuget.org/v3/index.json"/></packageSources><packageSourceMapping><packageSource key="candidate"><package pattern="CStructSharp"/></packageSource><packageSource key="nuget"><package pattern="*"/></packageSource></packageSourceMapping></configuration>
"@
function Invoke-CheckedDotnet([string[]]$Arguments) {
    $output = @(& dotnet @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) { throw ($output -join "`n") }
    return $output -join "`n"
}
try {
    Push-Location $work
    foreach ($framework in @('net8.0', 'net10.0')) {
        [void](Invoke-CheckedDotnet @('new', 'console', '-n', 'Starter', '-o', '.', '-f', 'net10.0', '--no-restore', '--force'))
        if ($framework -eq 'net8.0') {
            Set-Content 'Starter.csproj' ((Get-Content 'Starter.csproj' -Raw).Replace('<TargetFramework>net10.0</TargetFramework>', '<TargetFramework>net8.0</TargetFramework>'))
        }
        [void](Invoke-CheckedDotnet @('add', 'Starter.csproj', 'package', 'CStructSharp', '--version', $version, '--no-restore'))
        Set-Content -LiteralPath 'Program.cs' -Value (Get-Content (Join-Path $starter 'Program.cs') -Raw)
        [void](Invoke-CheckedDotnet @('restore', 'Starter.csproj', '--packages', (Join-Path $work 'packages'), '--force'))
        $output = Invoke-CheckedDotnet @('run', '--project', 'Starter.csproj', '--no-restore')
        if ($output.Trim() -ne "kind = 2`nlength = 6" -and $output.Trim() -ne "kind = 2`r`nlength = 6") { throw "Unexpected starter output: $output" }
        Set-Content -LiteralPath 'Program.cs' -Value (Get-Content (Join-Path $starter 'Next.cs') -Raw)
        $output = Invoke-CheckedDotnet @('run', '--project', 'Starter.csproj', '--no-restore')
        foreach ($line in @('Created: 020006000000', 'Updated: 030006000000', 'Kind = 3; Length = 6', 'Truncated read succeeds = False')) {
            if (-not $output.Contains($line)) { throw "Missing expected continuation output '$line': $output" }
        }
        Write-Host "PASS external $framework starter and continuation against package $version"
        $languageVersion = if ($framework -eq 'net8.0') { '12.0' } else { 'latest' }
        Set-Content -LiteralPath 'Program.cs' -Value (Get-Content (Join-Path $repository 'tools/fixtures/byte-array-consumer.cs') -Raw)
        $output = Invoke-CheckedDotnet @('run', '--project', 'Starter.csproj', '--no-restore', "-p:LangVersion=$languageVersion")
        if (-not $output.Contains('PASS byte-array consumer')) { throw "Byte-array consumer failed: $output" }
        Write-Host "PASS external $framework byte-array consumer with C# $languageVersion"
    }
    foreach ($recipe in Get-ChildItem (Join-Path $repository 'docs/examples/recipes') -Filter '*.cs') {
        Set-Content -LiteralPath 'Program.cs' -Value (Get-Content $recipe.FullName -Raw)
        $output = Invoke-CheckedDotnet @('run', '--project', 'Starter.csproj', '--no-restore')
        if (-not $output.Contains("PASS $($recipe.BaseName)")) { throw "Recipe did not report success: $output" }
        Write-Host "PASS external recipe $($recipe.BaseName)"
    }
} finally {
    Pop-Location
    $resolved = [IO.Path]::GetFullPath($work)
    $relative = [IO.Path]::GetRelativePath($temporaryBase, $resolved)
    if ($relative.StartsWith('..') -or [IO.Path]::IsPathRooted($relative) -or -not $relative.StartsWith('cstructsharp-onboarding-')) {
        throw "Refusing cleanup outside the temporary onboarding directory: $resolved"
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}

