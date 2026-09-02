[CmdletBinding()]
param(
    [switch]$NoBuild,
    [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$DocumentationRoot = Join-Path $RepositoryRoot 'CStructSharp.Docs'
$BuildScript = Join-Path $PSScriptRoot 'Build-Documentation.ps1'
$ApiValidationScript = Join-Path $PSScriptRoot 'Validate-ApiDocumentation.ps1'
$LanguageValidationScript = Join-Path $PSScriptRoot 'Validate-LanguageDocumentation.ps1'
$QualityValidationScript = Join-Path $PSScriptRoot 'Validate-DocumentationQuality.ps1'
$ExternalLinkValidationScript = Join-Path $PSScriptRoot 'Test-DocumentationExternalLinks.ps1'
$WorkflowValidationScript = Join-Path $PSScriptRoot 'Validate-DocumentationWorkflow.ps1'
$PagesValidationScript = Join-Path $PSScriptRoot 'Validate-PagesArtifact.ps1'
$PagesArtifactScript = Join-Path $PSScriptRoot 'New-DocumentationPagesArtifact.ps1'
$CanonicalValidationScript = Join-Path $PSScriptRoot 'Validate-CanonicalReference.ps1'
$FeatureMatrixValidationScript = Join-Path $PSScriptRoot 'Validate-FeatureOperationMatrix.ps1'
$DocfxConfigPath = Join-Path $DocumentationRoot 'docfx.json'
$ToolManifestPath = Join-Path $RepositoryRoot '.config/dotnet-tools.json'
$CoreProjectPath = Join-Path $RepositoryRoot 'CStructSharp/CStructSharp.csproj'
$TestProjectPath = Join-Path $RepositoryRoot 'CStructSharpTests/CStructSharpTests.csproj'
$ExampleProjectPath = Join-Path $DocumentationRoot 'examples/CStructSharp.Docs.Examples.csproj'
$NodeManifestPath = Join-Path $DocumentationRoot 'package.json'
$NodeLockPath = Join-Path $DocumentationRoot 'package-lock.json'
$SiteDirectory = Join-Path $DocumentationRoot '_site'
$ApiDirectory = Join-Path $DocumentationRoot 'api'

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

function Get-IgnoredDocumentationDependencies {
    $repositoryFiles = @(
        & git -C $RepositoryRoot ls-files --cached --others --exclude-standard
    )
    Assert-Condition ($LASTEXITCODE -eq 0) 'Could not enumerate repository source files.'

    $textExtensions = @(
        '.cs',
        '.csproj',
        '.js',
        '.json',
        '.md',
        '.mjs',
        '.props',
        '.ps1',
        '.targets',
        '.ts',
        '.txt',
        '.xml',
        '.yaml',
        '.yml'
    )
    $ignoredDocsPattern = '(?i)(?:^|[\s"''`()=:])docs[\\/]'
    $violations = [Collections.Generic.List[string]]::new()

    foreach ($relativePath in $repositoryFiles)
    {
        if ([IO.Path]::GetExtension($relativePath) -notin $textExtensions)
        {
            continue
        }

        $fullPath = Join-Path $RepositoryRoot $relativePath
        if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf))
        {
            continue
        }

        foreach ($match in Select-String -LiteralPath $fullPath -Pattern $ignoredDocsPattern)
        {
            $violations.Add((
                '{0}:{1}:{2}' -f
                    $relativePath.Replace('\', '/'),
                    $match.LineNumber,
                    $match.Line.Trim()))
        }
    }

    return $violations
}

function Get-BrokenRepositoryMarkdownLinks {
    $repositoryFiles = @(
        & git -C $RepositoryRoot ls-files --cached --others --exclude-standard
    )
    Assert-Condition ($LASTEXITCODE -eq 0) 'Could not enumerate repository Markdown files.'

    $broken = [Collections.Generic.List[string]]::new()
    foreach ($relativePath in @($repositoryFiles | Where-Object { [IO.Path]::GetExtension($_) -eq '.md' }))
    {
        $fullPath = Join-Path $RepositoryRoot $relativePath
        if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf))
        {
            continue
        }

        $text = Get-Content -LiteralPath $fullPath -Raw
        foreach ($match in [regex]::Matches($text, '\[[^\]]+\]\((?<target>[^)]+)\)'))
        {
            $href = $match.Groups['target'].Value.Trim().Trim('<', '>')
            if ($href -match '^(?:https?:|mailto:|xref:)')
            {
                continue
            }

            $href = $href.Split('#', 2)[0]
            if ([string]::IsNullOrWhiteSpace($href))
            {
                continue
            }

            $target = [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $fullPath) $href))
            if (-not (Test-Path -LiteralPath $target))
            {
                $broken.Add("$relativePath -> $href")
            }
        }
    }

    return $broken
}

if ($SelfTest)
{
    $caught = $false
    try
    {
        Assert-Condition $false 'expected self-test failure'
    }
    catch
    {
        $caught = $_.Exception.Message -eq 'expected self-test failure'
    }

    Assert-Condition $caught 'The documentation assertion self-test did not observe the expected failure.'
    & $BuildScript -SelfTest
    Assert-Condition $? 'The documentation build self-test failed.'
    & $QualityValidationScript -SelfTest
    Assert-Condition $? 'The documentation quality self-test failed.'
    & $ExternalLinkValidationScript -SelfTest
    Assert-Condition $? 'The external-link self-test failed.'
    & $WorkflowValidationScript -SelfTest
    Assert-Condition $? 'The documentation workflow self-test failed.'
    & $PagesValidationScript -SelfTest
    Assert-Condition $? 'The Pages artifact self-test failed.'
    Write-Host 'Validate-Documentation self-test passed.'
    return
}

$ignoredDocumentationDependencies = @(Get-IgnoredDocumentationDependencies)
Assert-Condition ($ignoredDocumentationDependencies.Count -eq 0) (
    "Repository source still depends on ignored local-documentation paths:`n" +
    [string]::Join("`n", $ignoredDocumentationDependencies))
$brokenRepositoryMarkdownLinks = @(Get-BrokenRepositoryMarkdownLinks)
Assert-Condition ($brokenRepositoryMarkdownLinks.Count -eq 0) (
    "Repository Markdown contains missing local link targets:`n" +
    [string]::Join("`n", $brokenRepositoryMarkdownLinks))

foreach ($requiredPath in @(
    $BuildScript,
    $ApiValidationScript,
    $LanguageValidationScript,
    $QualityValidationScript,
    $ExternalLinkValidationScript,
    $WorkflowValidationScript,
    $PagesValidationScript,
    $PagesArtifactScript,
    $CanonicalValidationScript,
    $FeatureMatrixValidationScript,
    $DocfxConfigPath,
    $ToolManifestPath,
    $CoreProjectPath,
    $TestProjectPath,
    $ExampleProjectPath,
    $NodeManifestPath,
    $NodeLockPath))
{
    Assert-Condition (Test-Path -LiteralPath $requiredPath) "Required validation input does not exist: $requiredPath"
}

$buildParameters = @{}
if ($NoBuild)
{
    $buildParameters.NoBuild = $true
    $buildParameters.Clean = $true
}

Write-Host "==> $BuildScript$(if ($NoBuild) { ' -NoBuild -Clean' })"
& $BuildScript @buildParameters
Assert-Condition $? 'The documentation build wrapper failed.'

Write-Host "==> $ApiValidationScript"
& $ApiValidationScript
Assert-Condition $? 'Generated API documentation validation failed.'

Write-Host "==> $LanguageValidationScript"
& $LanguageValidationScript
Assert-Condition $? 'Language documentation validation failed.'

Write-Host "==> $CanonicalValidationScript"
& $CanonicalValidationScript
Assert-Condition $? 'Canonical Portable reference validation failed.'

Write-Host "==> $FeatureMatrixValidationScript"
& $FeatureMatrixValidationScript
Assert-Condition $? 'Feature-operation matrix validation failed.'

Write-Host "==> $QualityValidationScript"
& $QualityValidationScript
Assert-Condition $? 'Documentation quality validation failed.'

Write-Host "==> $WorkflowValidationScript"
& $WorkflowValidationScript
Assert-Condition $? 'Documentation workflow validation failed.'

Write-Host "==> $PagesValidationScript"
& $PagesValidationScript
Assert-Condition $? 'Pages output validation failed.'

Write-Host "==> dotnet restore $TestProjectPath"
& dotnet restore $TestProjectPath
Assert-Condition ($LASTEXITCODE -eq 0) 'Language fixture test restore failed.'
foreach ($framework in @('net8.0', 'net10.0'))
{
    Write-Host "==> dotnet test $TestProjectPath -c Release -f $framework --no-restore --filter language fixtures"
    & dotnet test $TestProjectPath `
        -c Release `
        -f $framework `
        --no-restore `
        --filter 'FullyQualifiedName~ManualLanguageFixtureTests|FullyQualifiedName~CanonicalPortableReferenceTests'
    Assert-Condition ($LASTEXITCODE -eq 0) "Language fixtures failed for $framework."
}

Write-Host "==> dotnet restore $ExampleProjectPath"
& dotnet restore $ExampleProjectPath
Assert-Condition ($LASTEXITCODE -eq 0) 'Documentation example restore failed.'
Write-Host "==> dotnet run --project $ExampleProjectPath -c Release --no-restore"
& dotnet run --project $ExampleProjectPath -c Release --no-restore
Assert-Condition ($LASTEXITCODE -eq 0) 'Documentation examples failed.'

Push-Location $DocumentationRoot
try
{
    Write-Host '==> npm ci --ignore-scripts'
    & npm ci --ignore-scripts
    Assert-Condition ($LASTEXITCODE -eq 0) 'Pinned documentation Node dependency restore failed.'

    Write-Host '==> npm audit --audit-level=high'
    & npm audit --audit-level=high
    Assert-Condition ($LASTEXITCODE -eq 0) 'Documentation Node dependency audit failed.'

    foreach ($script in @(
        'lint:workflow-yaml',
        'lint:markdown',
        'lint:spelling',
        'install:browser',
        'test:browser'))
    {
        Write-Host "==> npm run $script"
        & npm run $script
        Assert-Condition ($LASTEXITCODE -eq 0) "Documentation Node script '$script' failed."
    }
}
finally
{
    Pop-Location
}

$docfxConfigText = Get-Content -LiteralPath $DocfxConfigPath -Raw
$docfxConfig = $docfxConfigText | ConvertFrom-Json
$toolManifest = Get-Content -LiteralPath $ToolManifestPath -Raw | ConvertFrom-Json
[xml]$coreProject = Get-Content -LiteralPath $CoreProjectPath -Raw
[xml]$exampleProject = Get-Content -LiteralPath $ExampleProjectPath -Raw

Assert-Condition ($toolManifest.tools.docfx.version -eq '2.78.5') 'DocFX must remain pinned to reviewed version 2.78.5.'
Assert-Condition (-not $toolManifest.tools.docfx.rollForward) 'DocFX tool roll-forward must remain disabled.'
Assert-Condition ($coreProject.SelectNodes('//ProjectReference').Count -eq 0) `
    'The API input project must not acquire a project reference.'
$exampleReferences = @($exampleProject.SelectNodes('//ProjectReference'))
Assert-Condition ($exampleReferences.Count -eq 1 -and
                  $exampleReferences[0].Include -eq '..\..\CStructSharp\CStructSharp.csproj') `
    'The documentation examples must reference only the core project.'

$metadataSources = @($docfxConfig.metadata | ForEach-Object { $_.src } | ForEach-Object { $_.src })
Assert-Condition ($metadataSources.Count -eq 1) 'DocFX must have exactly one managed metadata source root.'
Assert-Condition ($metadataSources[0] -eq '../CStructSharp/bin/Release/net10.0') `
    "Unexpected DocFX metadata source '$($metadataSources[0])'."
Assert-Condition ($docfxConfigText -notmatch '(?i)CStructSharpWeb') `
    'DocFX configuration must not select CStructSharpWeb or CStructSharpWeb.Wasm.'
Assert-Condition ($docfxConfig.build.globalMetadata._enableSearch -eq $true) 'DocFX local search must remain enabled.'

$sourcePages = @(
    foreach ($directory in @('project', 'guides', 'language', 'examples', 'api'))
    {
        Get-ChildItem -LiteralPath (Join-Path $DocumentationRoot $directory) -Recurse -File -Filter '*.md'
    }
    Get-Item -LiteralPath (Join-Path $DocumentationRoot 'index.md')
    Get-Item -LiteralPath (Join-Path $DocumentationRoot '404.md')
)

$titles = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($page in $sourcePages)
{
    $text = Get-Content -LiteralPath $page.FullName -Raw
    Assert-Condition ($text -match '\A---\r?\n(?s:.*?)\r?\n---\r?\n') `
        "Content page lacks YAML front matter: $($page.FullName)"
    $titleMatch = [regex]::Match($text, '(?m)^title:\s*(.+?)\s*$')
    Assert-Condition $titleMatch.Success "Content page lacks a title: $($page.FullName)"
    $title = $titleMatch.Groups[1].Value.Trim()
    Assert-Condition (-not $titles.ContainsKey($title)) `
        "Duplicate documentation title '$title': '$($titles[$title])' and '$($page.FullName)'."
    $titles.Add($title, $page.FullName)
    $h1Count = [regex]::Matches($text, '(?m)^#\s+').Count
    Assert-Condition ($h1Count -eq 1) "Content page must have exactly one H1: $($page.FullName)"
    Assert-Condition ($text -notmatch '(?i)\b(?:TODO|TBD|FIXME|lorem ipsum|coming soon)\b') `
        "Content page contains a prohibited placeholder: $($page.FullName)"
}

$sourceTocs = @(
    Get-ChildItem -LiteralPath $DocumentationRoot -Recurse -File -Filter 'toc.yml' |
        Where-Object { $_.DirectoryName -ne [IO.Path]::GetFullPath($ApiDirectory) }
)
$reachablePages = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($toc in $sourceTocs)
{
    $tocDestinations = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($match in Select-String -LiteralPath $toc.FullName -Pattern '^\s*href:\s*(.+?)\s*$')
    {
        $href = $match.Matches[0].Groups[1].Value.Trim().Trim('"').Trim("'")
        if ($href -match '^(?:https?:|xref:)')
        {
            continue
        }

        $target = [IO.Path]::GetFullPath((Join-Path $toc.DirectoryName $href))
        if ($href.EndsWith('/'))
        {
            $target = Join-Path $target 'index.md'
        }

        Assert-Condition (Test-Path -LiteralPath $target) `
            "TOC target '$href' does not exist for '$($toc.FullName)'."
        Assert-Condition ($tocDestinations.Add($target)) `
            "TOC '$($toc.FullName)' contains duplicate destination '$href'."
        if ([IO.Path]::GetExtension($target) -eq '.md')
        {
            [void]$reachablePages.Add($target)
        }
    }
}

foreach ($page in $sourcePages)
{
    $text = Get-Content -LiteralPath $page.FullName -Raw
    foreach ($match in [regex]::Matches($text, '\[[^\]]+\]\((?<target>[^)]+)\)'))
    {
        $href = $match.Groups['target'].Value.Trim().Trim('<', '>')
        $href = $href.Split('#', 2)[0]
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

$rootPage = [IO.Path]::GetFullPath((Join-Path $DocumentationRoot 'index.md'))
$notFoundPage = [IO.Path]::GetFullPath((Join-Path $DocumentationRoot '404.md'))
foreach ($page in $sourcePages)
{
    Assert-Condition (
        $page.FullName -eq $rootPage -or
        $page.FullName -eq $notFoundPage -or
        $reachablePages.Contains($page.FullName)) `
        "Documentation page is orphaned from authored navigation and links: $($page.FullName)"
}

$sourceFiles = @(
    Get-ChildItem -LiteralPath $DocumentationRoot -Recurse -File |
        Where-Object {
            $_.FullName -notmatch '[\\/](?:_site|\.tmp|bin|obj|browser-report|node_modules|playwright-report|test-results)[\\/]' -and
            -not (
                $_.DirectoryName -eq [IO.Path]::GetFullPath($ApiDirectory) -and
                $_.Name -ne 'index.md'
            ) -and
            $_.Extension -notin @('.log', '.binlog')
        }
)
foreach ($sourceFile in $sourceFiles)
{
    $relative = [IO.Path]::GetRelativePath($RepositoryRoot, $sourceFile.FullName).Replace('\', '/')
    & git -C $RepositoryRoot check-ignore --quiet -- $relative
    Assert-Condition ($LASTEXITCODE -ne 0) "Documentation source is ignored and cannot be committed: $relative"
}

foreach ($generatedPath in @(
    (Join-Path $SiteDirectory 'index.html'),
    (Join-Path $ApiDirectory 'CStructSharp.yml'),
    (Join-Path $DocumentationRoot 'docfx-build.log')
))
{
    Assert-Condition (Test-Path -LiteralPath $generatedPath) "Expected generated file is missing: $generatedPath"
    $relative = [IO.Path]::GetRelativePath($RepositoryRoot, $generatedPath).Replace('\', '/')
    & git -C $RepositoryRoot check-ignore --quiet -- $relative
    Assert-Condition ($LASTEXITCODE -eq 0) "Generated documentation file is not ignored: $relative"
}

$contractDirectory = Join-Path $DocumentationRoot 'contracts'
$contractSourceFiles = @(
    Get-ChildItem -LiteralPath $contractDirectory -Recurse -File |
        Where-Object { $_.Extension -in @('.json', '.txt') }
)
Assert-Condition ($contractSourceFiles.Count -eq 15) `
    "Expected 15 tracked machine contract files, found $($contractSourceFiles.Count)."
foreach ($contractSource in $contractSourceFiles)
{
    $relativeContract = [IO.Path]::GetRelativePath($DocumentationRoot, $contractSource.FullName)
    $publishedContract = Join-Path $SiteDirectory $relativeContract
    Assert-Condition (Test-Path -LiteralPath $publishedContract -PathType Leaf) `
        "Published documentation contract is missing: $relativeContract"
    $sourceHash = (Get-FileHash -LiteralPath $contractSource.FullName -Algorithm SHA256).Hash
    $publishedHash = (Get-FileHash -LiteralPath $publishedContract -Algorithm SHA256).Hash
    Assert-Condition ($sourceHash -eq $publishedHash) `
        "Published documentation contract differs from its source: $relativeContract"
}

$apiPages = @(Get-ChildItem -LiteralPath (Join-Path $SiteDirectory 'api') -File -Filter '*.html')
Assert-Condition ($apiPages.Count -ge 21) "Expected at least 21 generated API pages, found $($apiPages.Count)."

$searchIndexPath = Join-Path $SiteDirectory 'index.json'
Assert-Condition (Test-Path -LiteralPath $searchIndexPath) 'Generated search index is missing.'
$searchIndex = Get-Content -LiteralPath $searchIndexPath -Raw | ConvertFrom-Json
foreach ($expectedPage in @(
    'index.html',
    'project/index.html',
    'guides/index.html',
    'language/index.html',
    'language/portable-v1-reference.html',
    'language/tutorial/01-first-layout.html',
    'language/operation-matrix.html',
    'examples/index.html',
    'api/browser-contract.html',
    'api/CStructSharp.CStruct.html'
))
{
    Assert-Condition ($null -ne $searchIndex.PSObject.Properties[$expectedPage]) `
        "Search index does not contain '$expectedPage'."
}

$absoluteRootUrls = @(
    rg -n '(?:href|src)="/' $SiteDirectory -g '*.html'
)
Assert-Condition ($absoluteRootUrls.Count -eq 0) 'Generated HTML contains root-absolute asset or content URLs.'

$siteFiles = @(Get-ChildItem -LiteralPath $SiteDirectory -Recurse -File)
$siteBytes = ($siteFiles | Measure-Object -Property Length -Sum).Sum
Assert-Condition ($siteBytes -le 32MB) "Documentation artifact exceeds the initial 32 MiB budget: $siteBytes bytes."

Write-Host (
    'Documentation validation passed: {0} source pages, {1} source TOCs, {2} API pages, {3} site files, {4:N0} bytes.' -f
    $sourcePages.Count,
    $sourceTocs.Count,
    $apiPages.Count,
    $siteFiles.Count,
    $siteBytes
)
