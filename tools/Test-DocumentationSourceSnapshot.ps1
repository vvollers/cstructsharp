[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$SnapshotParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$SnapshotRoot = Join-Path $SnapshotParent (
    'CStructSharp-documentation-snapshot-' + [Guid]::NewGuid().ToString('N'))
$completed = $false

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

function Assert-SnapshotPath {
    param([string]$Path)

    $fullPath = [IO.Path]::GetFullPath($Path)
    $relative = [IO.Path]::GetRelativePath($SnapshotRoot, $fullPath)
    Assert-Condition (-not [IO.Path]::IsPathRooted($relative) -and
                      -not $relative.StartsWith('..')) `
        "Snapshot path escapes its root: $fullPath"
}

try
{
    Write-Host "==> git clone --no-hardlinks $RepositoryRoot $SnapshotRoot"
    & git clone --no-hardlinks --quiet -- $RepositoryRoot $SnapshotRoot
    Assert-Condition ($LASTEXITCODE -eq 0) 'Could not create the isolated repository checkout.'
    & git -C $SnapshotRoot remote set-url origin https://github.com/vvollers/CStructSharp.git
    Assert-Condition ($LASTEXITCODE -eq 0) 'Could not set the publication repository URL in the isolated checkout.'

    $sourceFiles = @(& git -C $RepositoryRoot ls-files --cached --others --exclude-standard)
    Assert-Condition ($LASTEXITCODE -eq 0) 'Could not enumerate prospective tracked source.'
    foreach ($relativePath in $sourceFiles)
    {
        $sourcePath = Join-Path $RepositoryRoot $relativePath
        $targetPath = Join-Path $SnapshotRoot $relativePath
        Assert-SnapshotPath -Path $targetPath
        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf))
        {
            continue
        }
        [void](New-Item -ItemType Directory -Path (Split-Path -Parent $targetPath) -Force)
        Copy-Item -LiteralPath $sourcePath -Destination $targetPath -Force
    }

    $deletedFiles = @(& git -C $RepositoryRoot diff --name-only --diff-filter=D)
    Assert-Condition ($LASTEXITCODE -eq 0) 'Could not enumerate prospective source deletions.'
    foreach ($relativePath in $deletedFiles)
    {
        $targetPath = Join-Path $SnapshotRoot $relativePath
        Assert-SnapshotPath -Path $targetPath
        if (Test-Path -LiteralPath $targetPath -PathType Leaf)
        {
            Remove-Item -LiteralPath $targetPath -Force
        }
    }

    Assert-Condition (-not (Test-Path -LiteralPath (
        Join-Path $SnapshotRoot ([IO.Path]::Combine('docs', 'DOCUMENTATION_PLAN.md'))))) `
        'The isolated source snapshot unexpectedly contains ignored local planning documentation.'
    foreach ($required in @(
        'CStructSharp.Docs/docfx.json',
        'CStructSharp.Docs/package-lock.json',
        '.github/workflows/docs.yml',
        'tools/Validate-Documentation.ps1'))
    {
        Assert-Condition (Test-Path -LiteralPath (Join-Path $SnapshotRoot $required) -PathType Leaf) `
            "The isolated source snapshot is missing '$required'."
    }

    Push-Location $SnapshotRoot
    try
    {
        Write-Host '==> prospective source snapshot documentation validation'
        & pwsh -NoProfile -File './tools/Validate-Documentation.ps1'
        Assert-Condition ($LASTEXITCODE -eq 0) 'Documentation validation failed in the isolated source snapshot.'
        $apiPage = Get-Content -LiteralPath (
            Join-Path $SnapshotRoot 'CStructSharp.Docs/_site/api/CStructSharp.CStruct.html') -Raw
        Assert-Condition ($apiPage -match
            'https://github\.com/vvollers/CStructSharp/blob/[0-9a-f]{40}/CStructSharp/CStruct\.cs') `
            'The Git-visible source snapshot did not produce a commit-pinned API source link.'

        Write-Host '==> prospective source snapshot Pages artifact'
        & pwsh -NoProfile -File './tools/New-DocumentationPagesArtifact.ps1'
        Assert-Condition ($LASTEXITCODE -eq 0) 'Pages artifact creation failed in the isolated source snapshot.'

        $artifact = Join-Path $SnapshotRoot 'artifacts/documentation/cstructsharp-pages.tar.gz'
        $artifactBytes = (Get-Item -LiteralPath $artifact).Length
        $artifactHash = (Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash.ToLowerInvariant()
        Write-Host (
            'Prospective source snapshot passed: {0} source files, Pages artifact {1} bytes, SHA-256 {2}.' -f
            $sourceFiles.Count,
            $artifactBytes,
            $artifactHash)
    }
    finally
    {
        Pop-Location
    }

    $completed = $true
}
finally
{
    if ($completed -and (Test-Path -LiteralPath $SnapshotRoot))
    {
        Assert-SnapshotPath -Path $SnapshotRoot
        Write-Host "==> removing successful isolated snapshot $SnapshotRoot"
        Remove-Item -LiteralPath $SnapshotRoot -Recurse -Force
    }
    elseif (Test-Path -LiteralPath $SnapshotRoot)
    {
        Write-Warning "Retained failed isolated snapshot for diagnosis: $SnapshotRoot"
    }
}
