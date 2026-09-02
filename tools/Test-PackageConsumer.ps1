param(
    [string] $PackageDirectory = (
        Join-Path (Join-Path $PSScriptRoot '..') (Join-Path 'artifacts' 'package'))
)

$ErrorActionPreference = 'Stop'

function Invoke-DotNet {
    param(
        [Parameter(Mandatory = $true)]
        [string[]] $Arguments
    )

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

$resolvedPackageDirectory = (Resolve-Path -LiteralPath $PackageDirectory).Path
$packages = @(
    Get-ChildItem -LiteralPath $resolvedPackageDirectory -Filter '*.nupkg' -File |
        Where-Object { -not $_.Name.EndsWith('.snupkg', [StringComparison]::OrdinalIgnoreCase) }
)
if ($packages.Count -ne 1) {
    throw "Expected exactly one package under '$resolvedPackageDirectory', found $($packages.Count)."
}

$package = $packages[0]
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)
try {
    $nuspecEntry = $archive.Entries |
        Where-Object { $_.FullName.EndsWith('.nuspec', [StringComparison]::OrdinalIgnoreCase) } |
        Select-Object -First 1
    if ($null -eq $nuspecEntry) {
        throw "Package '$($package.FullName)' has no .nuspec manifest."
    }

    $reader = [System.IO.StreamReader]::new($nuspecEntry.Open())
    try {
        [xml] $nuspec = $reader.ReadToEnd()
    }
    finally {
        $reader.Dispose()
    }
}
finally {
    $archive.Dispose()
}

$namespaceManager = [System.Xml.XmlNamespaceManager]::new($nuspec.NameTable)
$namespaceManager.AddNamespace('n', $nuspec.DocumentElement.NamespaceURI)
$packageId = $nuspec.SelectSingleNode('/n:package/n:metadata/n:id', $namespaceManager).InnerText
$packageVersion = $nuspec.SelectSingleNode('/n:package/n:metadata/n:version', $namespaceManager).InnerText
if ($packageId -ne 'CStructSharp' -or [string]::IsNullOrWhiteSpace($packageVersion)) {
    throw "Expected a versioned CStructSharp package, found '$packageId' '$packageVersion'."
}

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$projectDirectory = (Resolve-Path -LiteralPath (
    Join-Path $repositoryRoot 'CStructSharp.PackageConsumer')).Path
$project = Join-Path $projectDirectory 'CStructSharp.PackageConsumer.csproj'
$nugetConfig = Join-Path $projectDirectory 'NuGet.config'
$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) (
    'cstructsharp-package-consumer-' + [Guid]::NewGuid().ToString('N'))
$packageCache = Join-Path $temporaryRoot 'packages'
New-Item -ItemType Directory -Path $packageCache | Out-Null

$previousPackageSource = [Environment]::GetEnvironmentVariable('CSTRUCTSHARP_PACKAGE_SOURCE', 'Process')
$previousPackageCache = [Environment]::GetEnvironmentVariable('NUGET_PACKAGES', 'Process')
$previousPackageVersion = [Environment]::GetEnvironmentVariable('CStructSharpPackageVersion', 'Process')
[Environment]::SetEnvironmentVariable('CSTRUCTSHARP_PACKAGE_SOURCE', $resolvedPackageDirectory, 'Process')
[Environment]::SetEnvironmentVariable('NUGET_PACKAGES', $packageCache, 'Process')
[Environment]::SetEnvironmentVariable('CStructSharpPackageVersion', $packageVersion, 'Process')

try {
    Invoke-DotNet -Arguments @(
        'restore',
        $project,
        '--configfile',
        $nugetConfig,
        '--force',
        '--no-cache'
    )

    $assetsPath = Join-Path (Join-Path $projectDirectory 'obj') 'project.assets.json'
    $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json -AsHashtable
    $libraryKey = "CStructSharp/$packageVersion"
    foreach ($framework in @('net8.0', 'net10.0')) {
        if (-not $assets.targets.ContainsKey($framework) -or
            -not $assets.targets[$framework].ContainsKey($libraryKey)) {
            throw "Restored assets do not contain '$libraryKey' for '$framework'."
        }

        $targetAssets = $assets.targets[$framework][$libraryKey]
        $expectedAssembly = "lib/$framework/CStructSharp.dll"
        if (-not $targetAssets.compile.ContainsKey($expectedAssembly) -or
            -not $targetAssets.runtime.ContainsKey($expectedAssembly)) {
            throw "The '$framework' consumer did not select '$expectedAssembly'."
        }
    }

    $metadataPath = Join-Path (
        Join-Path $packageCache 'cstructsharp') (
        Join-Path $packageVersion.ToLowerInvariant() '.nupkg.metadata')
    $metadata = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json -AsHashtable
    $resolvedSource = $metadata.source
    if ([string]::IsNullOrWhiteSpace($resolvedSource)) {
        throw 'The restored package metadata does not identify its source.'
    }

    $sourcePath = [System.IO.Path]::GetFullPath($resolvedSource)
    if ($sourcePath -ne $package.FullName -and $sourcePath -ne $resolvedPackageDirectory) {
        throw "CStructSharp restored from '$resolvedSource' instead of the package under test."
    }

    Invoke-DotNet -Arguments @(
        'format',
        $project,
        '--no-restore',
        '--verify-no-changes'
    )

    foreach ($framework in @('net8.0', 'net10.0')) {
        Invoke-DotNet -Arguments @(
            'run',
            '--project',
            $project,
            '-c',
            'Release',
            '-f',
            $framework,
            '--no-restore'
        )
    }

    Write-Host (
        "Validated package consumer behavior for net8.0 and net10.0 against " +
        "'$($package.Name)' from an isolated package cache."
    )
}
finally {
    [Environment]::SetEnvironmentVariable('CSTRUCTSHARP_PACKAGE_SOURCE', $previousPackageSource, 'Process')
    [Environment]::SetEnvironmentVariable('NUGET_PACKAGES', $previousPackageCache, 'Process')
    [Environment]::SetEnvironmentVariable('CStructSharpPackageVersion', $previousPackageVersion, 'Process')
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
