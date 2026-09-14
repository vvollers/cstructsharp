param(
    [Parameter(Mandatory = $true)]
    [string] $PackagePath,

    [Parameter(Mandatory = $true)]
    [string] $SymbolPackagePath
)

$ErrorActionPreference = 'Stop'

$resolvedPackage = (Resolve-Path -LiteralPath $PackagePath).Path
$resolvedSymbolPackage = (Resolve-Path -LiteralPath $SymbolPackagePath).Path
if (-not $resolvedPackage.EndsWith('.nupkg', [StringComparison]::OrdinalIgnoreCase) -or
    $resolvedPackage.EndsWith('.snupkg', [StringComparison]::OrdinalIgnoreCase)) {
    throw "Expected a NuGet .nupkg file, but received '$resolvedPackage'."
}

if (-not $resolvedSymbolPackage.EndsWith('.snupkg', [StringComparison]::OrdinalIgnoreCase)) {
    throw "Expected a NuGet .snupkg file, but received '$resolvedSymbolPackage'."
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($resolvedPackage)
$symbolArchive = [System.IO.Compression.ZipFile]::OpenRead($resolvedSymbolPackage)

try {
    $entryNames = @($archive.Entries | ForEach-Object FullName)
    $nuspecEntry = $archive.Entries |
        Where-Object { $_.FullName.EndsWith('.nuspec', [StringComparison]::OrdinalIgnoreCase) } |
        Select-Object -First 1
    if ($null -eq $nuspecEntry) {
        throw 'The package does not contain a .nuspec manifest.'
    }

    $reader = [System.IO.StreamReader]::new($nuspecEntry.Open())
    try {
        [xml] $nuspec = $reader.ReadToEnd()
    }
    finally {
        $reader.Dispose()
    }

    $namespaceManager = [System.Xml.XmlNamespaceManager]::new($nuspec.NameTable)
    $namespaceManager.AddNamespace('n', $nuspec.DocumentElement.NamespaceURI)
    $metadata = $nuspec.SelectSingleNode('/n:package/n:metadata', $namespaceManager)
    if ($null -eq $metadata) {
        throw 'The package manifest has no metadata element.'
    }

    function Require-MetadataText {
        param(
            [string] $XPath,
            [string] $Description
        )

        $node = $metadata.SelectSingleNode($XPath, $namespaceManager)
        if ($null -eq $node -or [string]::IsNullOrWhiteSpace($node.InnerText)) {
            throw "Package metadata is missing $Description."
        }

        return $node
    }

    $license = Require-MetadataText 'n:license' 'a license'
    if ($license.GetAttribute('type') -ne 'expression' -or $license.InnerText.Trim() -ne 'MIT') {
        throw "Expected the MIT license expression, found '$($license.InnerText)'."
    }

    $repository = $metadata.SelectSingleNode('n:repository', $namespaceManager)
    if ($null -eq $repository) {
        throw 'Package metadata is missing repository provenance.'
    }

    if ($repository.GetAttribute('type') -ne 'git' -or
        -not $repository.GetAttribute('url').StartsWith('https://github.com/vvollers/CStructSharp', [StringComparison]::Ordinal)) {
        throw 'Repository metadata does not identify the canonical Git repository.'
    }

    if ([string]::IsNullOrWhiteSpace($repository.GetAttribute('commit'))) {
        throw 'Repository metadata does not identify the source commit.'
    }

    $projectUrl = Require-MetadataText 'n:projectUrl' 'a project URL'
    if ($projectUrl.InnerText.Trim() -ne 'https://github.com/vvollers/CStructSharp') {
        throw "Unexpected project URL '$($projectUrl.InnerText)'."
    }

    $readme = Require-MetadataText 'n:readme' 'a package readme'
    $releaseNotes = Require-MetadataText 'n:releaseNotes' 'release notes'
    if (-not $releaseNotes.InnerText.Contains(
            'https://github.com/vvollers/CStructSharp/issues',
            [StringComparison]::Ordinal)) {
        throw 'Package release metadata does not include the canonical issue tracker.'
    }

    $requiredEntries = @(
        $readme.InnerText.Trim(),
        'CHANGELOG.md',
        'LICENSE.txt',
        'MUTATION_TESTING.md',
        'lib/net8.0/CStructSharp.dll',
        'lib/net8.0/CStructSharp.xml',
        'lib/net10.0/CStructSharp.dll',
        'lib/net10.0/CStructSharp.xml'
    )
    foreach ($requiredEntry in $requiredEntries) {
        if ($entryNames -cnotcontains $requiredEntry) {
            throw "The package is missing required entry '$requiredEntry'."
        }
    }

    $symbolEntryNames = @($symbolArchive.Entries | ForEach-Object FullName)
    $requiredSymbolEntries = @(
        'lib/net8.0/CStructSharp.pdb',
        'lib/net10.0/CStructSharp.pdb'
    )
    foreach ($requiredSymbolEntry in $requiredSymbolEntries) {
        if ($symbolEntryNames -cnotcontains $requiredSymbolEntry) {
            throw "The symbol package is missing required entry '$requiredSymbolEntry'."
        }
    }

    Write-Host (
        "Validated package metadata, $($requiredEntries.Count) package entries, " +
        "and $($requiredSymbolEntries.Count) portable symbol entries."
    )
}
finally {
    $archive.Dispose()
    $symbolArchive.Dispose()
}
