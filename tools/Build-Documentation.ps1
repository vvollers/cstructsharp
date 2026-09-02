[CmdletBinding()]
param(
    [switch]$NoBuild,
    [switch]$Clean,
    [switch]$Serve,
    [ValidateRange(1, 65535)]
    [int]$Port = 8080,
    [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$CoreDirectory = Join-Path $RepositoryRoot 'CStructSharp'
$CoreProject = Join-Path $CoreDirectory 'CStructSharp.csproj'
$CoreOutput = Join-Path $CoreDirectory 'bin/Release/net10.0'
$CoreAssembly = Join-Path $CoreOutput 'CStructSharp.dll'
$CoreXml = Join-Path $CoreOutput 'CStructSharp.xml'
$CorePdb = Join-Path $CoreOutput 'CStructSharp.pdb'
$DocumentationRoot = Join-Path $RepositoryRoot 'CStructSharp.Docs'
$DocfxConfig = Join-Path $DocumentationRoot 'docfx.json'
$ApiDirectory = Join-Path $DocumentationRoot 'api'
$SiteDirectory = Join-Path $DocumentationRoot '_site'

$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

function Assert-Condition {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition)
    {
        throw $Message
    }
}

function Assert-NoWebCommand {
    param([string[]]$Arguments)

    $commandText = $Arguments -join ' '
    if ($commandText -match '(?i)(^|[\\/])CStructSharpWeb(?:[\\/.]|$)')
    {
        throw "Documentation commands must not target CStructSharpWeb or CStructSharpWeb.Wasm: dotnet $commandText"
    }
}

function Format-CommandArgument {
    param([string]$Argument)

    if ($Argument -match '[\s"]')
    {
        return "'" + ($Argument -replace "'", "''") + "'"
    }

    return $Argument
}

function Invoke-DotNet {
    param(
        [string]$Label,
        [string[]]$Arguments
    )

    Assert-NoWebCommand -Arguments $Arguments
    $display = ($Arguments | ForEach-Object { Format-CommandArgument $_ }) -join ' '
    Write-Host "==> dotnet $display"
    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    $commandOutput = @(& dotnet @Arguments)
    $exitCode = $LASTEXITCODE
    $stopwatch.Stop()
    foreach ($line in $commandOutput)
    {
        Write-Host $line
    }
    Write-Host ("<== {0}: exit {1}, {2:N3} s" -f $Label, $exitCode, $stopwatch.Elapsed.TotalSeconds)
    if ($exitCode -ne 0)
    {
        throw "$Label failed with exit code $exitCode."
    }

    return $stopwatch.Elapsed.TotalSeconds
}

function Assert-SafeGeneratedDirectory {
    param(
        [string]$Path,
        [string]$ExpectedLeaf
    )

    $fullDocumentationRoot = [IO.Path]::GetFullPath($DocumentationRoot)
    $fullPath = [IO.Path]::GetFullPath($Path)
    $relative = [IO.Path]::GetRelativePath($fullDocumentationRoot, $fullPath)
    Assert-Condition (-not [IO.Path]::IsPathRooted($relative)) "Generated path must be below the docs project: $fullPath"
    Assert-Condition (-not $relative.StartsWith('..')) "Generated path escapes the docs project: $fullPath"
    Assert-Condition ([IO.Path]::GetFileName($fullPath) -eq $ExpectedLeaf) `
        "Generated path has unexpected leaf '$([IO.Path]::GetFileName($fullPath))': $fullPath"
}

function Remove-GeneratedSite {
    Assert-SafeGeneratedDirectory -Path $SiteDirectory -ExpectedLeaf '_site'
    if (Test-Path -LiteralPath $SiteDirectory)
    {
        Write-Host "==> removing generated site $SiteDirectory"
        Remove-Item -LiteralPath $SiteDirectory -Recurse -Force
    }
}

function Remove-GeneratedApiMetadata {
    Assert-SafeGeneratedDirectory -Path $ApiDirectory -ExpectedLeaf 'api'
    if (-not (Test-Path -LiteralPath $ApiDirectory))
    {
        return
    }

    foreach ($file in Get-ChildItem -LiteralPath $ApiDirectory -File -Filter '*.yml')
    {
        $fullPath = [IO.Path]::GetFullPath($file.FullName)
        Assert-Condition ([IO.Path]::GetDirectoryName($fullPath) -eq [IO.Path]::GetFullPath($ApiDirectory)) `
            "Refusing to remove API metadata outside the generated API directory: $fullPath"
        Write-Host "==> removing generated API metadata $fullPath"
        Remove-Item -LiteralPath $fullPath -Force
    }
}

function Assert-CurrentCoreOutput {
    foreach ($required in @($CoreAssembly, $CoreXml, $CorePdb))
    {
        Assert-Condition (Test-Path -LiteralPath $required) `
            "Fast documentation build requires '$required'. Run without -NoBuild first."
    }

    $assemblyTime = (Get-Item -LiteralPath $CoreAssembly).LastWriteTimeUtc
    $newerInputs = @(
        Get-ChildItem -LiteralPath $CoreDirectory -Recurse -File |
            Where-Object {
                $_.Extension -in @('.cs', '.csproj') -and
                $_.FullName -notmatch '[\\/](?:bin|obj)[\\/]'
            } |
            Where-Object { $_.LastWriteTimeUtc -gt $assemblyTime }
    )
    if ($newerInputs.Count -gt 0)
    {
        throw "The core assembly is older than source input '$($newerInputs[0].FullName)'. Run without -NoBuild."
    }
}

if ($SelfTest)
{
    $rejected = $false
    try
    {
        Assert-NoWebCommand @('build', (Join-Path $RepositoryRoot 'CStructSharpWeb/wasm/CStructSharpWeb.Wasm.csproj'))
    }
    catch
    {
        $rejected = $true
    }

    Assert-Condition $rejected 'The Web/WASM command guard did not reject a forbidden project.'
    Assert-NoWebCommand @('build', $CoreProject, '-f', 'net10.0')
    Assert-SafeGeneratedDirectory -Path $SiteDirectory -ExpectedLeaf '_site'
    Assert-SafeGeneratedDirectory -Path $ApiDirectory -ExpectedLeaf 'api'
    Write-Host 'Build-Documentation self-test passed.'
    return
}

foreach ($requiredPath in @($CoreProject, $DocfxConfig, $ApiDirectory))
{
    Assert-Condition (Test-Path -LiteralPath $requiredPath) "Required documentation input does not exist: $requiredPath"
}

[void](Invoke-DotNet -Label 'tool restore' -Arguments @('tool', 'restore'))

$cleanSite = $Clean -or -not $NoBuild
if ($cleanSite)
{
    Remove-GeneratedSite
}

if ($NoBuild)
{
    Assert-CurrentCoreOutput
}
else
{
    Remove-GeneratedApiMetadata
    [void](Invoke-DotNet -Label 'core restore' -Arguments @('restore', $CoreProject))
    [void](Invoke-DotNet -Label 'core build' -Arguments @(
        'build',
        $CoreProject,
        '-c',
        'Release',
        '-f',
        'net10.0',
        '--no-restore'
    ))
}

$apiMetadata = @(
    Get-ChildItem -LiteralPath $ApiDirectory -File -Filter '*.yml' -ErrorAction SilentlyContinue
)
$metadataIsStale = $apiMetadata.Count -eq 0
if (-not $metadataIsStale)
{
    $newestMetadata = ($apiMetadata | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1).LastWriteTimeUtc
    $metadataIsStale = $newestMetadata -lt (Get-Item -LiteralPath $CoreAssembly).LastWriteTimeUtc
}

if ($NoBuild -and -not $metadataIsStale)
{
    $docfxSeconds = Invoke-DotNet -Label 'DocFX content build' -Arguments @(
        'tool',
        'run',
        'docfx',
        'build',
        $DocfxConfig,
        '--warningsAsErrors',
        '--log',
        (Join-Path $DocumentationRoot 'docfx-content.log')
    )
    $docfxBudgetSeconds = 5
}
else
{
    $docfxSeconds = Invoke-DotNet -Label 'DocFX metadata and content build' -Arguments @(
        'tool',
        'run',
        'docfx',
        $DocfxConfig,
        '--warningsAsErrors',
        '--log',
        (Join-Path $DocumentationRoot 'docfx-build.log')
    )
    $docfxBudgetSeconds = 10
}

$siteFiles = @(Get-ChildItem -LiteralPath $SiteDirectory -Recurse -File)
$siteBytes = ($siteFiles | Measure-Object -Property Length -Sum).Sum
Assert-Condition ($docfxSeconds -le $docfxBudgetSeconds) (
    "DocFX exceeded the $docfxBudgetSeconds second budget: $($docfxSeconds.ToString('N3')) seconds.")
Assert-Condition ($siteBytes -le 32MB) (
    "Documentation artifact exceeds the 32 MiB budget: $siteBytes bytes.")
Write-Host (
    "Documentation artifact: {0} files, {1:N0} bytes; DocFX {2:N3}/{3} s budget." -f
    $siteFiles.Count,
    $siteBytes,
    $docfxSeconds,
    $docfxBudgetSeconds)

if ($Serve)
{
    [void](Invoke-DotNet -Label 'DocFX local server' -Arguments @(
        'tool',
        'run',
        'docfx',
        'serve',
        $SiteDirectory,
        '--hostname',
        'localhost',
        '--port',
        $Port.ToString()
    ))
}
