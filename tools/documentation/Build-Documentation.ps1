[CmdletBinding()]
param(
    [switch]$NoBuild,
    [switch]$Clean,
    [switch]$Serve,
    [ValidateRange(1, 65535)]
    [int]$Port = 8080,
    [uri]$ExplorerUrl,
    [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '../CStructSharp.Tooling.psm1') -Force

if ($ExplorerUrl -and (-not $ExplorerUrl.IsAbsoluteUri -or $ExplorerUrl.Scheme -notin @('http', 'https') -or
    $ExplorerUrl.Query -or $ExplorerUrl.Fragment -or $ExplorerUrl.UserInfo)) {
    throw 'ExplorerUrl must be an absolute HTTP(S) directory URL without credentials, query, or fragment.'
}

$RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$CoreDirectory = Join-Path $RepositoryRoot 'src/CStructSharp'
$CoreProject = Join-Path $CoreDirectory 'CStructSharp.csproj'
$CoreOutput = Join-Path $CoreDirectory 'bin/Release/net10.0'
$CoreAssembly = Join-Path $CoreOutput 'CStructSharp.dll'
$CoreXml = Join-Path $CoreOutput 'CStructSharp.xml'
$CorePdb = Join-Path $CoreOutput 'CStructSharp.pdb'
$DocumentationRoot = Join-Path $RepositoryRoot 'docs'
$DocfxConfig = Join-Path $DocumentationRoot 'docfx.json'
$ApiDirectory = Join-Path $DocumentationRoot 'api'
$SiteDirectory = Join-Path $DocumentationRoot '_site'

$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

function Assert-NoWebCommand {
    param([string[]]$Arguments)

    $commandText = $Arguments -join ' '
    if ($commandText -match '(?i)(^|[\\/])CStructSharpWeb(?:[\\/.]|$)')
    {
        throw "Documentation commands must not target CStructSharpWeb or CStructSharpWeb.Wasm: dotnet $commandText"
    }
}

function Invoke-DotNet {
    param(
        [string]$Label,
        [string[]]$Arguments
    )

    Assert-NoWebCommand -Arguments $Arguments
    return CStructSharp.Tooling\Invoke-DotNet -Label $Label -Arguments $Arguments
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
        Assert-NoWebCommand @('build', (Join-Path $RepositoryRoot 'src/CStructSharp.Wasm/CStructSharpWeb.Wasm.csproj'))
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

& node (Join-Path $PSScriptRoot 'export-documentation-examples.mjs')
Assert-Condition ($LASTEXITCODE -eq 0) 'Documentation example export failed.'

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
    # Hosted runners can be slower than a warm local build. Keep this as a
    # regression guard without making a valid release depend on runner load.
    $docfxBudgetSeconds = 30
}

if ($ExplorerUrl) {
    # Rewrite only generated preview links; source and publication defaults stay canonical.
    $previewExplorer = $ExplorerUrl.AbsoluteUri.TrimEnd('/') + '/'
    foreach ($page in Get-ChildItem -LiteralPath $SiteDirectory -Recurse -File -Filter '*.html') {
        $content = [IO.File]::ReadAllText($page.FullName)
        $updated = $content.Replace('https://vvollers.github.io/cstructsharp/explorer/', $previewExplorer)
        if ($updated -cne $content) { [IO.File]::WriteAllText($page.FullName, $updated) }
    }
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
