[CmdletBinding()]
param(
    [string]$WasmDirectory,

    [string]$FrontendDirectory,

    [string]$PackageDirectory,

    [Parameter(Mandatory)]
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($WasmDirectory) -and
    [string]::IsNullOrWhiteSpace($FrontendDirectory) -and
    [string]::IsNullOrWhiteSpace($PackageDirectory)) {
    throw 'Specify at least one directory to measure.'
}

$repositoryRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')

function Get-GzipLength {
    param([Parameter(Mandatory)][string]$Path)

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $output = [System.IO.MemoryStream]::new()
    try {
        $gzip = [System.IO.Compression.GZipStream]::new(
            $output,
            [System.IO.Compression.CompressionLevel]::Optimal,
            $true)
        try {
            $gzip.Write($bytes, 0, $bytes.Length)
        }
        finally {
            $gzip.Dispose()
        }

        return $output.Length
    }
    finally {
        $output.Dispose()
    }
}

function Measure-Directory {
    param([Parameter(Mandatory)][string]$Path)

    $root = Resolve-Path -LiteralPath $Path
    $entries = foreach ($file in Get-ChildItem -LiteralPath $root -File -Recurse | Sort-Object FullName) {
        [pscustomobject][ordered]@{
            relativePath = [System.IO.Path]::GetRelativePath($root, $file.FullName).Replace('\', '/')
            bytes = $file.Length
            gzipBytes = Get-GzipLength -Path $file.FullName
            extension = if ([string]::IsNullOrEmpty($file.Extension)) { '(none)' } else { $file.Extension.ToLowerInvariant() }
        }
    }

    $entries = @($entries)
    $extensionSummary = foreach ($group in $entries | Group-Object extension | Sort-Object Name) {
        [pscustomobject][ordered]@{
            extension = $group.Name
            files = $group.Count
            bytes = ($group.Group | Measure-Object -Property bytes -Sum).Sum
            gzipBytes = ($group.Group | Measure-Object -Property gzipBytes -Sum).Sum
        }
    }

    return [pscustomobject]@{
        Report = [pscustomobject][ordered]@{
            path = [System.IO.Path]::GetRelativePath($repositoryRoot, $root).Replace('\', '/')
            files = $entries.Count
            bytes = ($entries | Measure-Object -Property bytes -Sum).Sum
            gzipBytes = ($entries | Measure-Object -Property gzipBytes -Sum).Sum
            byExtension = @($extensionSummary)
            largestFiles = @($entries | Sort-Object bytes -Descending | Select-Object -First 25)
        }
        Entries = $entries
    }
}

$wasmMeasurement = if ([string]::IsNullOrWhiteSpace($WasmDirectory)) {
    $null
}
else {
    Measure-Directory -Path $WasmDirectory
}

$frontendMeasurement = if ([string]::IsNullOrWhiteSpace($FrontendDirectory)) {
    $null
}
else {
    Measure-Directory -Path $FrontendDirectory
}

$packageMeasurement = if ([string]::IsNullOrWhiteSpace($PackageDirectory)) {
    $null
}
else {
    Measure-Directory -Path $PackageDirectory
}

$mainJavaScript = $null
$clangFormatAssets = @()
if ($null -ne $frontendMeasurement) {
    $mainJavaScript = $frontendMeasurement.Entries |
        Where-Object { $_.relativePath -match '^assets/index-[^/]+\.js$' } |
        Sort-Object bytes -Descending |
        Select-Object -First 1
    $clangFormatAssets = @(
        $frontendMeasurement.Entries |
            Where-Object { $_.relativePath -match '(?i)clang-format' } |
            Sort-Object bytes -Descending
    )
}

$clangFormatBytes = 0
$clangFormatGzipBytes = 0
if ($clangFormatAssets.Count -gt 0) {
    $clangFormatBytes = ($clangFormatAssets | Measure-Object -Property bytes -Sum).Sum
    $clangFormatGzipBytes = ($clangFormatAssets | Measure-Object -Property gzipBytes -Sum).Sum
}

$revision = (& git -C $repositoryRoot rev-parse HEAD 2>$null | Select-Object -First 1)
$dirty = @(& git -C $repositoryRoot status --porcelain 2>$null).Count -gt 0
$nodeVersion = if (Get-Command node -ErrorAction SilentlyContinue) { (& node --version).Trim() } else { $null }
$npmVersion = if (Get-Command npm -ErrorAction SilentlyContinue) { (& npm --version 2>$null).Trim() } else { $null }
$dotnetVersion = (& dotnet --version).Trim()

$report = [ordered]@{
    schemaVersion = 1
    generatedAtUtc = [DateTime]::UtcNow.ToString('O')
    revision = $revision
    worktreeDirty = $dirty
    compression = 'sum of each file compressed independently with GZipStream CompressionLevel.Optimal'
    environment = [ordered]@{
        os = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
        processArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
        dotnetSdk = $dotnetVersion
        node = $nodeVersion
        npm = $npmVersion
    }
    wasm = if ($null -eq $wasmMeasurement) { $null } else { $wasmMeasurement.Report }
    frontend = if ($null -eq $frontendMeasurement) { $null } else { $frontendMeasurement.Report }
    mainJavaScript = $mainJavaScript
    clangFormat = [ordered]@{
        files = $clangFormatAssets.Count
        bytes = $clangFormatBytes
        gzipBytes = $clangFormatGzipBytes
        assets = $clangFormatAssets
    }
    packages = if ($null -eq $packageMeasurement) { $null } else { $packageMeasurement.Report }
}

$outputDirectory = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

$report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $OutputPath -Encoding utf8NoBOM
Write-Output "Artifact baseline written to $OutputPath"
if ($null -ne $wasmMeasurement) {
    Write-Output "WASM: $($wasmMeasurement.Report.files) files, $($wasmMeasurement.Report.bytes) bytes, $($wasmMeasurement.Report.gzipBytes) gzip bytes"
}

if ($null -ne $frontendMeasurement) {
    Write-Output "Frontend: $($frontendMeasurement.Report.files) files, $($frontendMeasurement.Report.bytes) bytes, $($frontendMeasurement.Report.gzipBytes) gzip bytes"
}
