[CmdletBinding()]
param(
    [string]$SiteDirectory,
    [string]$ArtifactPath,
    [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$DocumentationRoot = Join-Path $RepositoryRoot 'CStructSharp.Docs'
$ContractPath = Join-Path $DocumentationRoot 'contracts/documentation/pages-v1.json'
if ([string]::IsNullOrWhiteSpace($SiteDirectory))
{
    $SiteDirectory = Join-Path $DocumentationRoot '_site'
}
$SiteDirectory = [IO.Path]::GetFullPath($SiteDirectory)

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

function Assert-SafeArchiveEntries {
    param([string[]]$Entries)

    foreach ($entry in $Entries)
    {
        $normalized = $entry.Replace('\', '/')
        if ($normalized -eq './')
        {
            continue
        }
        while ($normalized.StartsWith('./', [StringComparison]::Ordinal))
        {
            $normalized = $normalized.Substring(2)
        }
        Assert-Condition (-not [string]::IsNullOrWhiteSpace($normalized)) `
            'Pages archive contains an empty entry.'
        Assert-Condition (-not [IO.Path]::IsPathRooted($entry)) `
            "Pages archive contains a rooted entry: $entry"
        Assert-Condition ($normalized -notmatch '(^|/)\.\.(/|$)') `
            "Pages archive contains a parent traversal: $entry"
    }
}

if ($SelfTest)
{
    $rejected = $false
    try
    {
        Assert-SafeArchiveEntries -Entries @('../outside.txt')
    }
    catch
    {
        $rejected = $true
    }
    Assert-Condition $rejected 'Pages archive traversal fail-first fixture was not rejected.'
    Assert-SafeArchiveEntries -Entries @('./index.html', './api/CStructSharp.CStruct.html')
    Write-Host 'Pages artifact self-test passed: traversal rejected and safe entries accepted.'
    return
}

Assert-Condition (Test-Path -LiteralPath $ContractPath -PathType Leaf) `
    "Pages contract does not exist: $ContractPath"
$contract = Get-Content -LiteralPath $ContractPath -Raw | ConvertFrom-Json -Depth 20
Assert-Condition ($contract.schemaVersion -eq 1) "Unsupported Pages contract schema '$($contract.schemaVersion)'."
Assert-Condition (Test-Path -LiteralPath $SiteDirectory -PathType Container) `
    "Generated Pages directory does not exist: $SiteDirectory"

foreach ($requiredPath in @($contract.requiredPaths))
{
    Assert-Condition (Test-Path -LiteralPath (Join-Path $SiteDirectory ([string]$requiredPath)) -PathType Leaf) `
        "Pages output is missing '$requiredPath'."
}

$siteFiles = @(Get-ChildItem -LiteralPath $SiteDirectory -Recurse -File -Force)
$reparsePoints = @(
    Get-ChildItem -LiteralPath $SiteDirectory -Recurse -Force |
        Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }
)
Assert-Condition ($reparsePoints.Count -eq 0) 'Pages output must not contain symbolic links or reparse points.'
$siteBytes = ($siteFiles | Measure-Object Length -Sum).Sum
Assert-Condition ($siteBytes -le [long]$contract.budgets.uncompressedBytes) `
    "Pages output exceeds its uncompressed budget: $siteBytes bytes."

[xml]$sitemap = Get-Content -LiteralPath (Join-Path $SiteDirectory 'sitemap.xml') -Raw
$namespace = [Xml.XmlNamespaceManager]::new($sitemap.NameTable)
$namespace.AddNamespace('s', 'http://www.sitemaps.org/schemas/sitemap/0.9')
$locations = @($sitemap.SelectNodes('//s:loc', $namespace) | ForEach-Object { $_.InnerText })
Assert-Condition ($locations.Count -ge 80) "Expected at least 80 canonical sitemap URLs, found $($locations.Count)."
Assert-Condition (@($locations | Sort-Object -Unique).Count -eq $locations.Count) `
    'Canonical sitemap URLs are not unique.'
foreach ($location in $locations)
{
    Assert-Condition ($location.StartsWith(
        [string]$contract.publicationBaseUrl,
        [StringComparison]::Ordinal)) "Unexpected sitemap URL '$location'."
}
foreach ($relative in @('index.html', '404.html', 'guides/index.html', 'api/CStructSharp.CStruct.html'))
{
    Assert-Condition ($locations -contains "$($contract.publicationBaseUrl)$relative") `
        "Sitemap lacks canonical URL '$($contract.publicationBaseUrl)$relative'."
}

$apiPage = Get-Content -LiteralPath (Join-Path $SiteDirectory 'api/CStructSharp.CStruct.html') -Raw
$templateScript = Get-Content -LiteralPath (
    Join-Path $DocumentationRoot 'templates/cstructsharp/public/main.js') -Raw
Assert-Condition (
    $apiPage -match 'https://github\.com/vvollers/CStructSharp/blob/[0-9a-f]{40}/CStructSharp/CStruct\.cs' -or
    $templateScript.Contains(
        "$($contract.repositoryUrl)/blob/$($contract.defaultBranch)/CStructSharp/CStruct.cs",
        [StringComparison]::Ordinal)) `
    'Generated API reference lacks a source/edit link or reviewed local fallback.'
Assert-Condition ($templateScript.Contains(
    [string]$contract.publicationBaseUrl,
    [StringComparison]::Ordinal)) 'Template script lacks the canonical publication root.'
Assert-Condition ($templateScript.Contains(
    "$($contract.repositoryUrl)/edit/$($contract.defaultBranch)/CStructSharp.Docs/",
    [StringComparison]::Ordinal)) 'Template script lacks the conceptual edit-link root.'

$readme = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'README.md') -Raw
$contributing = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'CONTRIBUTING.md') -Raw
$pullRequestTemplatePath = Join-Path $RepositoryRoot '.github/pull_request_template.md'
[xml]$coreProject = Get-Content -LiteralPath (
    Join-Path $RepositoryRoot 'CStructSharp/CStructSharp.csproj') -Raw
$packageReleaseNotes = [string](
    $coreProject.Project.PropertyGroup |
        Where-Object { $null -ne $_.PackageReleaseNotes } |
        Select-Object -First 1).PackageReleaseNotes
Assert-Condition ($readme.Contains(
    [string]$contract.publicationBaseUrl,
    [StringComparison]::Ordinal)) 'README does not link to the canonical documentation site.'
Assert-Condition ($packageReleaseNotes.Contains(
    [string]$contract.publicationBaseUrl,
    [StringComparison]::Ordinal) -and
    $packageReleaseNotes.Contains('CHANGELOG.md', [StringComparison]::Ordinal) -and
    $packageReleaseNotes.Contains('/issues', [StringComparison]::Ordinal)) `
    'Package release notes do not link documentation, changelog, and issue reporting.'
Assert-Condition ($contributing.Contains(
    '## Documentation ownership and update triggers',
    [StringComparison]::Ordinal)) 'Contributor guidance lacks documentation ownership and update triggers.'
Assert-Condition (Test-Path -LiteralPath $pullRequestTemplatePath -PathType Leaf) `
    'Pull-request guidance is missing.'
$pullRequestTemplate = Get-Content -LiteralPath $pullRequestTemplatePath -Raw
Assert-Condition ($pullRequestTemplate.Contains(
    '## Documentation impact',
    [StringComparison]::Ordinal)) 'Pull-request guidance lacks documentation impact review.'

$cacheLayers = @(
    rg -n -i 'serviceWorker\.register|service-worker\.js|appcache' $SiteDirectory -g '*.html' -g '*.js'
)
Assert-Condition ($cacheLayers.Count -eq 0) 'Pages output contains an unreviewed project-owned browser cache layer.'

$archiveBytes = 0
if (-not [string]::IsNullOrWhiteSpace($ArtifactPath))
{
    $fullArtifactPath = [IO.Path]::GetFullPath($ArtifactPath)
    Assert-Condition (Test-Path -LiteralPath $fullArtifactPath -PathType Leaf) `
        "Pages archive does not exist: $fullArtifactPath"
    $entries = @(& tar -tzf $fullArtifactPath)
    Assert-Condition ($LASTEXITCODE -eq 0) 'Could not list the Pages archive.'
    Assert-SafeArchiveEntries -Entries $entries
    foreach ($requiredPath in @($contract.requiredPaths))
    {
        $required = ([string]$requiredPath).Replace('\', '/')
        Assert-Condition (@($entries | Where-Object {
            $candidate = $_.Replace('\', '/')
            while ($candidate.StartsWith('./', [StringComparison]::Ordinal))
            {
                $candidate = $candidate.Substring(2)
            }
            $candidate -eq $required
        }).Count -eq 1) "Pages archive is missing '$required'."
    }
    $archiveBytes = (Get-Item -LiteralPath $fullArtifactPath).Length
    Assert-Condition ($archiveBytes -le [long]$contract.budgets.compressedBytes) `
        "Pages archive exceeds its compressed budget: $archiveBytes bytes."
}

Write-Host (
    'Pages artifact validation passed: {0} files, {1:N0} bytes, {2} canonical URLs{3}.' -f
    $siteFiles.Count,
    $siteBytes,
    $locations.Count,
    $(if ($archiveBytes -gt 0) { ", $archiveBytes compressed bytes" } else { '' })
)
