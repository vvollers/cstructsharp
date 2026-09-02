[CmdletBinding()]
param(
    [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$DocumentationRoot = Join-Path $RepositoryRoot 'CStructSharp.Docs'
$SiteDirectory = Join-Path $DocumentationRoot '_site'
$ApiDirectory = Join-Path $DocumentationRoot 'api'
$FixturePath = Join-Path $DocumentationRoot 'contracts/documentation/validator-fixtures.json'

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

function Get-PageRuleCodes {
    param([string]$Text)

    $codes = [Collections.Generic.List[string]]::new()
    $frontMatter = [regex]::Match($Text, '\A---\r?\n(?<yaml>(?s:.*?))\r?\n---(?:\r?\n|$)')
    if (-not $frontMatter.Success)
    {
        $codes.Add('front-matter')
        return $codes
    }

    $yaml = $frontMatter.Groups['yaml'].Value
    if (-not [regex]::IsMatch($yaml, '(?m)^title:\s*\S.+$'))
    {
        $codes.Add('title')
    }
    if (-not [regex]::IsMatch($yaml, '(?m)^description:\s*\S.+$'))
    {
        $codes.Add('description')
    }
    if ([regex]::Matches($Text, '(?m)^#\s+').Count -ne 1)
    {
        $codes.Add('h1')
    }
    if ($Text -match '(?i)\b(?:TODO|TBD|FIXME|lorem ipsum|coming soon)\b')
    {
        $codes.Add('placeholder')
    }

    return $codes
}

function Get-CollectionRuleCodes {
    param([object[]]$Pages)

    $codes = [Collections.Generic.List[string]]::new()
    $titles = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $urls = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($page in $Pages)
    {
        if (-not $titles.Add([string]$page.title))
        {
            $codes.Add('duplicate-title')
        }
        if (-not $urls.Add([string]$page.url))
        {
            $codes.Add('duplicate-url')
        }
    }

    return $codes
}

function Get-LinkRuleCodes {
    param(
        [string[]]$Targets,
        [string[]]$Existing
    )

    $existingSet = [Collections.Generic.HashSet[string]]::new(
        [string[]]$Existing,
        [StringComparer]::OrdinalIgnoreCase)
    if (@($Targets | Where-Object { -not $existingSet.Contains($_) }).Count -gt 0)
    {
        return @('broken-link')
    }
    return @()
}

function Get-TocRuleCodes {
    param(
        [string[]]$Targets,
        [string[]]$Existing
    )

    $codes = [Collections.Generic.List[string]]::new()
    $existingSet = [Collections.Generic.HashSet[string]]::new(
        [string[]]$Existing,
        [StringComparer]::OrdinalIgnoreCase)
    $destinations = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($target in $Targets)
    {
        if (-not $existingSet.Contains($target))
        {
            $codes.Add('missing-toc-target')
        }
        if (-not $destinations.Add($target))
        {
            $codes.Add('duplicate-toc-target')
        }
    }
    return $codes
}

function Get-ReachabilityRuleCodes {
    param(
        [string[]]$Pages,
        [string[]]$Reachable,
        [string[]]$Exempt
    )

    $reachableSet = [Collections.Generic.HashSet[string]]::new(
        [string[]]$Reachable,
        [StringComparer]::OrdinalIgnoreCase)
    $exemptSet = [Collections.Generic.HashSet[string]]::new(
        [string[]]$Exempt,
        [StringComparer]::OrdinalIgnoreCase)
    if (@($Pages | Where-Object {
        -not $reachableSet.Contains($_) -and -not $exemptSet.Contains($_)
    }).Count -gt 0)
    {
        return @('orphan')
    }
    return @()
}

function Get-SearchRuleCodes {
    param([string[]]$Entries)

    $codes = [Collections.Generic.List[string]]::new()
    if (@($Entries | Where-Object { $_ -match '^(?:project|guides|language|examples)/' }).Count -eq 0)
    {
        $codes.Add('search-conceptual')
    }
    if (@($Entries | Where-Object { $_ -match '^api/CStructSharp(?:\.|/)' }).Count -eq 0)
    {
        $codes.Add('search-api')
    }
    return $codes
}

function Get-TrackingRuleCodes {
    param([bool]$Ignored)

    if ($Ignored)
    {
        return @('ignored-source')
    }
    return @()
}

function Get-CaseRuleCodes {
    param([object]$Case)

    switch ($Case.kind)
    {
        'page' {
            return @(Get-PageRuleCodes -Text ([string]$Case.text))
        }
        'collection' {
            return @(Get-CollectionRuleCodes -Pages @($Case.pages))
        }
        'links' {
            return @(Get-LinkRuleCodes -Targets @($Case.targets) -Existing @($Case.existing))
        }
        'toc' {
            return @(Get-TocRuleCodes -Targets @($Case.targets) -Existing @($Case.existing))
        }
        'reachability' {
            return @(Get-ReachabilityRuleCodes `
                -Pages @($Case.pages) `
                -Reachable @($Case.reachable) `
                -Exempt @($Case.exempt))
        }
        'search' {
            return @(Get-SearchRuleCodes -Entries @($Case.entries))
        }
        'tracking' {
            return @(Get-TrackingRuleCodes -Ignored ([bool]$Case.ignored))
        }
        default {
            throw "Unknown documentation validator fixture kind '$($Case.kind)'."
        }
    }
}

Assert-Condition (Test-Path -LiteralPath $FixturePath -PathType Leaf) `
    "Documentation validator fixture does not exist: $FixturePath"
$fixtures = Get-Content -LiteralPath $FixturePath -Raw | ConvertFrom-Json -Depth 20
Assert-Condition ($fixtures.schemaVersion -eq 1) `
    "Unsupported documentation validator fixture schema '$($fixtures.schemaVersion)'."
Assert-Condition (@($fixtures.cases).Count -eq 14) `
    "Expected 14 fail-first documentation validator cases, found $(@($fixtures.cases).Count)."

if ($SelfTest)
{
    foreach ($case in @($fixtures.cases))
    {
        $codes = @(Get-CaseRuleCodes -Case $case)
        Assert-Condition ($codes -contains [string]$case.expected) `
            "Fail-first fixture '$($case.id)' did not trigger '$($case.expected)'; got '$($codes -join ', ')'."
    }
    Write-Host 'Documentation quality validator self-test passed: 14/14 invalid fixtures rejected.'
    return
}

$sourcePages = @(
    foreach ($directory in @('project', 'guides', 'language', 'examples', 'api'))
    {
        Get-ChildItem -LiteralPath (Join-Path $DocumentationRoot $directory) -Recurse -File -Filter '*.md'
    }
    Get-Item -LiteralPath (Join-Path $DocumentationRoot 'index.md')
    Get-Item -LiteralPath (Join-Path $DocumentationRoot '404.md')
)

$pageRecords = [Collections.Generic.List[object]]::new()
$qualityErrors = [Collections.Generic.List[string]]::new()
foreach ($page in $sourcePages)
{
    $text = Get-Content -LiteralPath $page.FullName -Raw
    foreach ($code in @(Get-PageRuleCodes -Text $text))
    {
        $qualityErrors.Add("$code`: $($page.FullName)")
    }

    $titleMatch = [regex]::Match($text, '(?m)^title:\s*(.+?)\s*$')
    $relativePath = [IO.Path]::GetRelativePath($DocumentationRoot, $page.FullName).Replace('\', '/')
    $pageRecords.Add([pscustomobject]@{
        path = $relativePath
        title = if ($titleMatch.Success) { $titleMatch.Groups[1].Value.Trim() } else { $relativePath }
        url = [IO.Path]::ChangeExtension($relativePath, '.html').Replace('\', '/')
    })

    $relativeTargets = [Collections.Generic.List[string]]::new()
    $existingTargets = [Collections.Generic.List[string]]::new()
    foreach ($match in [regex]::Matches($text, '\[[^\]]+\]\((?<target>[^)]+)\)'))
    {
        $href = $match.Groups['target'].Value.Trim().Trim('<', '>')
        $href = $href.Split('#', 2)[0]
        if ([string]::IsNullOrWhiteSpace($href) -or
            $href -match '^(?:https?:|xref:|mailto:)')
        {
            continue
        }

        $relativeTargets.Add($href)
        $target = [IO.Path]::GetFullPath((Join-Path $page.DirectoryName $href))
        if ($href.EndsWith('/'))
        {
            $target = Join-Path $target 'index.md'
        }
        if (Test-Path -LiteralPath $target)
        {
            $existingTargets.Add($href)
        }
    }
    if (@(Get-LinkRuleCodes -Targets $relativeTargets.ToArray() -Existing $existingTargets.ToArray()).Count -gt 0)
    {
        $qualityErrors.Add("broken-link: $($page.FullName)")
    }
}

foreach ($code in @(Get-CollectionRuleCodes -Pages $pageRecords.ToArray()))
{
    $qualityErrors.Add("$code`: documentation page collection")
}

$sourceTocs = @(
    Get-ChildItem -LiteralPath $DocumentationRoot -Recurse -File -Filter 'toc.yml' |
        Where-Object { $_.DirectoryName -ne [IO.Path]::GetFullPath($ApiDirectory) }
)
$reachablePages = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($toc in $sourceTocs)
{
    $tocTargets = [Collections.Generic.List[string]]::new()
    $existingTocTargets = [Collections.Generic.List[string]]::new()
    foreach ($match in Select-String -LiteralPath $toc.FullName -Pattern '^\s*href:\s*(.+?)\s*$')
    {
        $href = $match.Matches[0].Groups[1].Value.Trim().Trim('"').Trim("'")
        if ($href -match '^(?:https?:|xref:)')
        {
            continue
        }
        $tocTargets.Add($href)
        $target = [IO.Path]::GetFullPath((Join-Path $toc.DirectoryName $href))
        if ($href.EndsWith('/'))
        {
            $target = Join-Path $target 'index.md'
        }
        if (Test-Path -LiteralPath $target)
        {
            $existingTocTargets.Add($href)
            if ([IO.Path]::GetExtension($target) -eq '.md')
            {
                [void]$reachablePages.Add($target)
            }
        }
    }
    foreach ($code in @(Get-TocRuleCodes -Targets $tocTargets.ToArray() -Existing $existingTocTargets.ToArray()))
    {
        $qualityErrors.Add("$code`: $($toc.FullName)")
    }
}

foreach ($page in $sourcePages)
{
    $text = Get-Content -LiteralPath $page.FullName -Raw
    foreach ($match in [regex]::Matches($text, '\[[^\]]+\]\((?<target>[^)]+)\)'))
    {
        $href = $match.Groups['target'].Value.Trim().Trim('<', '>').Split('#', 2)[0]
        if ([string]::IsNullOrWhiteSpace($href) -or
            $href -match '^(?:https?:|xref:|mailto:)')
        {
            continue
        }
        $target = [IO.Path]::GetFullPath((Join-Path $page.DirectoryName $href))
        if ($href.EndsWith('/'))
        {
            $target = Join-Path $target 'index.md'
        }
        if ([IO.Path]::GetExtension($target) -eq '.md' -and
            (Test-Path -LiteralPath $target -PathType Leaf))
        {
            [void]$reachablePages.Add($target)
        }
    }
}

$pagePaths = @($sourcePages | ForEach-Object { $_.FullName })
$exemptPaths = @(
    [IO.Path]::GetFullPath((Join-Path $DocumentationRoot 'index.md')),
    [IO.Path]::GetFullPath((Join-Path $DocumentationRoot '404.md'))
)
foreach ($code in @(Get-ReachabilityRuleCodes `
    -Pages $pagePaths `
    -Reachable @($reachablePages) `
    -Exempt $exemptPaths))
{
    $qualityErrors.Add("$code`: documentation page collection")
}

foreach ($sourceFile in @(
    Get-ChildItem -LiteralPath $DocumentationRoot -Recurse -File |
        Where-Object {
            $_.FullName -notmatch '[\\/](?:_site|\.tmp|bin|obj|browser-report|test-results|node_modules)[\\/]' -and
            -not (
                $_.DirectoryName -eq [IO.Path]::GetFullPath($ApiDirectory) -and
                $_.Name -ne 'index.md'
            ) -and
            $_.Extension -notin @('.log', '.binlog')
        }
))
{
    $relative = [IO.Path]::GetRelativePath($RepositoryRoot, $sourceFile.FullName).Replace('\', '/')
    & git -C $RepositoryRoot check-ignore --quiet -- $relative
    foreach ($code in @(Get-TrackingRuleCodes -Ignored ($LASTEXITCODE -eq 0)))
    {
        $qualityErrors.Add("$code`: $relative")
    }
}

$searchIndexPath = Join-Path $SiteDirectory 'index.json'
Assert-Condition (Test-Path -LiteralPath $searchIndexPath -PathType Leaf) `
    "Generated search index does not exist: $searchIndexPath"
$searchIndex = Get-Content -LiteralPath $searchIndexPath -Raw | ConvertFrom-Json
$searchEntries = @($searchIndex.PSObject.Properties.Name)
foreach ($code in @(Get-SearchRuleCodes -Entries $searchEntries))
{
    $qualityErrors.Add("$code`: $searchIndexPath")
}

Assert-Condition ($qualityErrors.Count -eq 0) (
    "Documentation quality validation failed:`n" +
    [string]::Join("`n", $qualityErrors))

Write-Host (
    'Documentation quality passed: {0} pages, {1} TOCs, {2} search entries, 14 fail-first rules available.' -f
    $sourcePages.Count,
    $sourceTocs.Count,
    $searchEntries.Count
)
