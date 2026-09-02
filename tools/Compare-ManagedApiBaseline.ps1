[CmdletBinding()]
param(
    [string]$ManifestPath = (Join-Path $PSScriptRoot '..\CStructSharp.Docs\contracts\api\managed-rc1\manifest.json'),
    [string]$ProjectPath = (Join-Path $PSScriptRoot '..\CStructSharp\CStructSharp.csproj'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\api-compat\managed-current')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path

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

function Normalize-ApiText {
    param(
        [Parameter(Mandatory)]
        [string]$Text
    )

    return $Text.Replace("`r`n", "`n").TrimEnd("`n") + "`n"
}

function Get-NormalizedHash {
    param(
        [Parameter(Mandatory)]
        [string]$Text
    )

    $bytes = [System.Text.Encoding]::UTF8.GetBytes((Normalize-ApiText $Text))
    return [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($bytes))
}

function Write-ApiDiff {
    param(
        [Parameter(Mandatory)]
        [string]$Expected,

        [Parameter(Mandatory)]
        [string]$Actual,

        [Parameter(Mandatory)]
        [string]$Path
    )

    $expectedLines = (Normalize-ApiText $Expected).TrimEnd("`n") -split "`n"
    $actualLines = (Normalize-ApiText $Actual).TrimEnd("`n") -split "`n"
    $maximum = [Math]::Max($expectedLines.Count, $actualLines.Count)
    $differences = [System.Collections.Generic.List[string]]::new()

    for ($index = 0; $index -lt $maximum -and $differences.Count -lt 300; $index++) {
        $expectedLine = if ($index -lt $expectedLines.Count) { $expectedLines[$index] } else { '<missing>' }
        $actualLine = if ($index -lt $actualLines.Count) { $actualLines[$index] } else { '<missing>' }
        if ($expectedLine -cne $actualLine) {
            $differences.Add("line $($index + 1)")
            $differences.Add("- $expectedLine")
            $differences.Add("+ $actualLine")
        }
    }

    $differences | Set-Content -LiteralPath $Path -Encoding utf8
}

Assert-Condition (Test-Path -LiteralPath $ManifestPath -PathType Leaf) `
    "Frozen managed API manifest '$ManifestPath' does not exist."
Assert-Condition (Test-Path -LiteralPath $ProjectPath -PathType Leaf) `
    "Managed API project '$ProjectPath' does not exist."

$manifest = Get-Content -Raw -LiteralPath $ManifestPath | ConvertFrom-Json -Depth 100
Assert-Condition ($manifest.schemaVersion -eq 1) 'Unsupported managed API baseline schema.'
Assert-Condition ($manifest.baselineId -eq 'managed-rc1') 'Unexpected managed API baseline id.'
Assert-Condition ($manifest.status -eq 'frozen') 'The managed API baseline is not frozen.'
Assert-Condition ($manifest.workItem -eq 'QA-07') 'The managed API baseline is not assigned to QA-07.'
Assert-Condition ($manifest.generator.package -eq 'PublicApiGenerator.Tool') `
    'The managed API baseline names an unexpected generator package.'
Assert-Condition ($manifest.generator.version -eq '11.5.4') `
    'The managed API baseline generator version is not pinned to 11.5.4.'
Assert-Condition ($manifest.generator.command -eq 'generate-public-api') `
    'The managed API baseline names an unexpected generator command.'
Assert-Condition ($manifest.assembly -eq 'CStructSharp.dll') `
    'The managed API baseline names an unexpected assembly.'
Assert-Condition ($manifest.exportedTypes -eq 20) `
    'The managed API baseline must retain the reviewed 20-type surface.'

[xml]$project = Get-Content -Raw -LiteralPath $ProjectPath
$versionPrefix = [string]$project.Project.PropertyGroup.VersionPrefix
$versionSuffix = [string]$project.Project.PropertyGroup.VersionSuffix
$packageVersion = if ([string]::IsNullOrWhiteSpace($versionSuffix)) {
    $versionPrefix
}
else {
    "$versionPrefix-$versionSuffix"
}
Assert-Condition ($manifest.packageVersion -eq $packageVersion) `
    "Managed API baseline version '$($manifest.packageVersion)' does not match package '$packageVersion'."

$entries = @($manifest.frameworks)
$frameworks = @($entries | ForEach-Object { [string]$_.tfm })
Assert-Condition (@(Compare-Object @('net8.0', 'net10.0') $frameworks).Count -eq 0) `
    'The managed API baseline must contain exactly net8.0 and net10.0.'

$canonicalPath = Join-Path $repositoryRoot ([string]$manifest.canonical.path)
Assert-Condition (Test-Path -LiteralPath $canonicalPath -PathType Leaf) `
    "Canonical managed API baseline '$($manifest.canonical.path)' does not exist."
$canonicalText = Normalize-ApiText (Get-Content -Raw -LiteralPath $canonicalPath)
Assert-Condition ($canonicalText.Contains('<TARGET_FRAMEWORK>', [StringComparison]::Ordinal)) `
    'The canonical managed API baseline has no target-framework placeholder.'
Assert-Condition ($canonicalText.Contains('<FRAMEWORK_DISPLAY>', [StringComparison]::Ordinal)) `
    'The canonical managed API baseline has no framework-display placeholder.'
Assert-Condition ((Get-NormalizedHash $canonicalText) -eq $manifest.canonical.normalizedSha256) `
    'The canonical managed API baseline does not match its recorded SHA-256.'
Assert-Condition (($canonicalText.TrimEnd("`n") -split "`n").Count -eq $manifest.canonical.lines) `
    'The canonical managed API baseline does not match its recorded line count.'

$baselineTexts = [ordered]@{}
$combinedHashes = [System.Collections.Generic.List[string]]::new()
foreach ($entry in $entries) {
    Assert-Condition (-not [string]::IsNullOrWhiteSpace([string]$entry.targetFramework)) `
        "Managed API framework '$($entry.tfm)' has no target-framework moniker."
    Assert-Condition (-not [string]::IsNullOrWhiteSpace([string]$entry.frameworkDisplayName)) `
        "Managed API framework '$($entry.tfm)' has no display name."
    $expected = $canonicalText.Replace(
        '<TARGET_FRAMEWORK>',
        [string]$entry.targetFramework).Replace(
            '<FRAMEWORK_DISPLAY>',
            [string]$entry.frameworkDisplayName)
    $hash = Get-NormalizedHash $expected
    Assert-Condition ($hash -eq $entry.normalizedSha256) `
        "Managed API framework '$($entry.tfm)' does not match its recorded SHA-256."
    $baselineTexts[[string]$entry.tfm] = $expected
    $combinedHashes.Add("$($entry.tfm):$hash")
}

$combinedText = [string]::Join("`n", $combinedHashes) + "`n"
$combinedHash = Get-NormalizedHash $combinedText
$history = @($manifest.history)
Assert-Condition ($history.Count -ge 1) 'The managed API baseline has no review history.'
for ($index = 0; $index -lt $history.Count; $index++) {
    $entry = $history[$index]
    Assert-Condition ($entry.revision -eq $index + 1) `
        'Managed API baseline history revisions must be contiguous and one-based.'
    Assert-Condition ($entry.kind -in @('freeze', 'additive', 'breaking', 'correction')) `
        "Managed API history revision '$($entry.revision)' has an invalid change kind."
    Assert-Condition ($entry.date -match '^\d{4}-\d{2}-\d{2}$') `
        "Managed API history revision '$($entry.revision)' has an invalid date."
    Assert-Condition (-not [string]::IsNullOrWhiteSpace([string]$entry.rationale)) `
        "Managed API history revision '$($entry.revision)' has no rationale."
    Assert-Condition (-not [string]::IsNullOrWhiteSpace([string]$entry.releaseImpact)) `
        "Managed API history revision '$($entry.revision)' has no release impact."
}

$latestHistory = $history[-1]
Assert-Condition ($manifest.baselineRevision -eq $latestHistory.revision) `
    'The managed API baseline revision does not match its latest history entry.'
Assert-Condition ($latestHistory.packageVersion -eq $manifest.packageVersion) `
    'The latest managed API history entry names a different package version.'
Assert-Condition ($latestHistory.combinedSha256 -eq $combinedHash) `
    'The latest managed API history entry does not approve the current baseline hashes.'

$toolManifestPath = Join-Path $repositoryRoot '.config\dotnet-tools.json'
Assert-Condition (Test-Path -LiteralPath $toolManifestPath -PathType Leaf) `
    'The repository .NET tool manifest does not exist.'
$toolManifest = Get-Content -Raw -LiteralPath $toolManifestPath | ConvertFrom-Json -Depth 20
$tool = $toolManifest.tools.'publicapigenerator.tool'
Assert-Condition ($null -ne $tool -and $tool.version -eq $manifest.generator.version) `
    'The repository tool manifest does not pin PublicApiGenerator.Tool to the baseline version.'

$globalPackagesOutput = [string]::Join(
    "`n",
    @(& dotnet nuget locals global-packages --list 2>&1))
if ($LASTEXITCODE -ne 0 -or $globalPackagesOutput -notmatch '(?m)^[^:]+:\s*(.+)$') {
    throw "Cannot locate the NuGet global-packages directory.`n$globalPackagesOutput"
}

$globalPackagesDirectory = $Matches[1].Trim()
$generatorAssembly = Join-Path $globalPackagesDirectory (
    "publicapigenerator.tool/$($manifest.generator.version)/tools/net6.0/any/PublicApiGenerator.Tool.dll")
if (Test-Path -LiteralPath $generatorAssembly -PathType Leaf) {
    $generatorExecutable = 'dotnet'
    $generatorPrefix = @($generatorAssembly)
}
else {
    $localGenerator = Join-Path $repositoryRoot (
        'artifacts\tools\public-api-generator\generate-public-api.exe')
    Assert-Condition (Test-Path -LiteralPath $localGenerator -PathType Leaf) `
        "PublicApiGenerator.Tool is not restored. Run 'dotnet tool restore'."
    $generatorExecutable = $localGenerator
    $generatorPrefix = @()
}

$generatorVersionOutput = [string]::Join(
    "`n",
    @(& $generatorExecutable @generatorPrefix --version 2>&1)).Trim()
Assert-Condition ($LASTEXITCODE -eq 0 -and
    $generatorVersionOutput.StartsWith($manifest.generator.version, [StringComparison]::Ordinal)) `
    "PublicApiGenerator.Tool version '$generatorVersionOutput' does not match the frozen baseline."

$outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
$runDirectory = Join-Path $outputRoot ('run-' + [Guid]::NewGuid().ToString('N'))
$workDirectory = Join-Path $runDirectory 'work'
$generatedDirectory = Join-Path $runDirectory 'generated'
[void](New-Item -ItemType Directory -Path $workDirectory -Force)
[void](New-Item -ItemType Directory -Path $generatedDirectory -Force)

& $generatorExecutable @generatorPrefix `
    --target-frameworks net8.0 net10.0 `
    --project-path (Resolve-Path -LiteralPath $ProjectPath).Path `
    --assembly $manifest.assembly `
    --generator-version $manifest.generator.version `
    --working-directory $workDirectory `
    --output-directory $generatedDirectory
if ($LASTEXITCODE -ne 0) {
    throw "Managed API generation failed with exit code $LASTEXITCODE."
}

$failures = [System.Collections.Generic.List[string]]::new()
foreach ($entry in $entries) {
    $generatedPath = Join-Path $generatedDirectory "CStructSharp.$($entry.tfm).received.txt"
    Assert-Condition (Test-Path -LiteralPath $generatedPath -PathType Leaf) `
        "Managed API generator did not produce '$generatedPath'."
    $actual = Get-Content -Raw -LiteralPath $generatedPath
    $expected = $baselineTexts[[string]$entry.tfm]
    if ((Normalize-ApiText $actual) -cne $expected) {
        $diffPath = Join-Path $runDirectory "CStructSharp.$($entry.tfm).diff.txt"
        Write-ApiDiff $expected $actual $diffPath
        $failures.Add("$($entry.tfm) differs; inspect '$diffPath'.")
    }
}

if ($failures.Count -gt 0) {
    throw "Managed API compatibility failed.`n$([string]::Join("`n", $failures))"
}

Write-Host 'Frozen managed API compatibility validation passed.'
Write-Host "Baseline: $($manifest.baselineId) revision $($manifest.baselineRevision)"
Write-Host "Package version: $packageVersion"
Write-Host "Frameworks: $([string]::Join(', ', $frameworks))"
Write-Host "Exported types: $($manifest.exportedTypes)"
Write-Host "Generated evidence: $runDirectory"
