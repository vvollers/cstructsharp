[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Compiler,

    [Parameter(Mandatory)]
    [string]$OutputPath,

    [string]$SourcePath = (Join-Path $PSScriptRoot 'compiler-fixtures\portable-host-facts.c')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-CompilerMetadata {
    param(
        [Parameter(Mandatory)]
        [string]$CompilerPath,

        [Parameter(Mandatory)]
        [string[]]$Arguments,

        [Parameter(Mandatory)]
        [string]$Description
    )

    $output = @(& $CompilerPath @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE.`n$([string]::Join("`n", $output))"
    }

    return [string]::Join("`n", @($output | ForEach-Object { "$_".TrimEnd() })).Trim()
}

if (Test-Path -LiteralPath $Compiler -PathType Leaf) {
    $compilerPath = (Resolve-Path -LiteralPath $Compiler).Path
}
else {
    $command = Get-Command $Compiler -CommandType Application -ErrorAction Stop | Select-Object -First 1
    $compilerPath = $command.Source
}

if (-not (Test-Path -LiteralPath $SourcePath -PathType Leaf)) {
    throw "Compiler fixture source '$SourcePath' does not exist."
}

$resolvedSource = (Resolve-Path -LiteralPath $SourcePath).Path
$versionOutput = Invoke-CompilerMetadata $compilerPath @('--version') 'Compiler version query'
$target = Invoke-CompilerMetadata $compilerPath @('-dumpmachine') 'Compiler target query'
if ($versionOutput -match '(?i)clang version\s+([^\s]+)') {
    $compilerFamily = 'Clang'
    $compilerVersion = $Matches[1]
}
elseif ($versionOutput -match '(?i)\b(?:gcc|g\+\+)(?:\.exe)?\b.*?\s(\d+\.\d+(?:\.\d+)?)') {
    $compilerFamily = 'GCC'
    $compilerVersion = $Matches[1]
}
else {
    throw "Only Clang and GCC are supported by this fixture runner.`n$versionOutput"
}

$compileFlags = @('-std=c11', '-Wall', '-Wextra', '-Werror', '-pedantic')
$temporaryRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$temporaryDirectory = Join-Path $temporaryRoot (
    'cstructsharp-compiler-fixture-' + [Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $temporaryDirectory)

try {
    $binaryName = if ($IsWindows) { 'portable-host-facts.exe' } else { 'portable-host-facts' }
    $binaryPath = Join-Path $temporaryDirectory $binaryName
    $compileOutput = @(& $compilerPath @compileFlags $resolvedSource '-o' $binaryPath 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Fixture compilation failed with exit code $LASTEXITCODE.`n$([string]::Join("`n", $compileOutput))"
    }

    $factOutput = @(& $binaryPath 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Fixture execution failed with exit code $LASTEXITCODE.`n$([string]::Join("`n", $factOutput))"
    }

    $factsJson = [string]::Join("`n", $factOutput).Trim()
    try {
        $facts = $factsJson | ConvertFrom-Json -Depth 100
    }
    catch {
        throw "Fixture output was not valid JSON.`n$factsJson"
    }
}
finally {
    if (Test-Path -LiteralPath $temporaryDirectory -PathType Container) {
        $resolvedTemporary = (Resolve-Path -LiteralPath $temporaryDirectory).Path
        if (-not $resolvedTemporary.StartsWith(
                $temporaryRoot,
                [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove temporary directory outside '$temporaryRoot': '$resolvedTemporary'."
        }

        Remove-Item -LiteralPath $resolvedTemporary -Recurse -Force
    }
}

$hostOs = if ($IsWindows) {
    'Windows'
}
elseif ($IsLinux) {
    'Linux'
}
elseif ($IsMacOS) {
    'macOS'
}
else {
    throw 'The fixture runner requires Windows, Linux, or macOS.'
}

$record = [ordered]@{
    schemaVersion = 1
    evidenceKind = 'compiler-observation'
    claim = 'observation-only'
    fixture = [ordered]@{
        id = 'portable-host-facts'
        source = 'tools/compiler-fixtures/portable-host-facts.c'
        sha256 = (Get-FileHash -LiteralPath $resolvedSource -Algorithm SHA256).Hash
    }
    compiler = [ordered]@{
        family = $compilerFamily
        executable = [System.IO.Path]::GetFileName($compilerPath)
        version = $compilerVersion
        versionOutput = $versionOutput
        target = $target
        language = 'C11'
        flags = $compileFlags
    }
    host = [ordered]@{
        os = $hostOs
        architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    }
    facts = $facts
}

$outputDirectory = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    [void](New-Item -ItemType Directory -Path $outputDirectory -Force)
}

$record | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $OutputPath -Encoding utf8
Write-Host "Compiler fixture evidence written to '$OutputPath'."
Write-Host "Compiler: $compilerFamily $compilerVersion"
Write-Host "Target: $target"
