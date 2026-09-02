[CmdletBinding()]
param(
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$SiteDirectory = Join-Path $RepositoryRoot 'CStructSharp.Docs/_site'
$ArtifactDirectory = [IO.Path]::GetFullPath((Join-Path $RepositoryRoot 'artifacts/documentation'))
if ([string]::IsNullOrWhiteSpace($OutputPath))
{
    $OutputPath = Join-Path $ArtifactDirectory 'cstructsharp-pages.tar.gz'
}
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
$ValidationScript = Join-Path $PSScriptRoot 'Validate-PagesArtifact.ps1'

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

$relativeOutput = [IO.Path]::GetRelativePath($ArtifactDirectory, $OutputPath)
Assert-Condition (-not [IO.Path]::IsPathRooted($relativeOutput) -and
                  -not $relativeOutput.StartsWith('..')) `
    "Pages artifact must stay below '$ArtifactDirectory': $OutputPath"
Assert-Condition ($OutputPath.EndsWith('.tar.gz', [StringComparison]::OrdinalIgnoreCase)) `
    'Pages artifact must use the .tar.gz extension.'
Assert-Condition (Test-Path -LiteralPath $SiteDirectory -PathType Container) `
    "Generated site does not exist: $SiteDirectory"

& $ValidationScript -SiteDirectory $SiteDirectory
Assert-Condition $? 'Generated site failed Pages validation.'

[void](New-Item -ItemType Directory -Path $ArtifactDirectory -Force)
if (Test-Path -LiteralPath $OutputPath)
{
    Write-Host "==> removing generated Pages archive $OutputPath"
    Remove-Item -LiteralPath $OutputPath -Force
}

Write-Host "==> tar -czf $OutputPath -C $SiteDirectory ."
& tar -czf $OutputPath -C $SiteDirectory .
Assert-Condition ($LASTEXITCODE -eq 0) 'Creating the Pages archive failed.'

& $ValidationScript -SiteDirectory $SiteDirectory -ArtifactPath $OutputPath
Assert-Condition $? 'Generated Pages archive failed validation.'
$hash = (Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256).Hash.ToLowerInvariant()
$bytes = (Get-Item -LiteralPath $OutputPath).Length
Write-Host "Pages artifact created: $OutputPath, $bytes bytes, SHA-256 $hash"
