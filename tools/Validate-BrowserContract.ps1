param(
    [string] $RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

$baselinePath = Join-Path $RepositoryRoot 'CStructSharp.Docs/contracts/api/browser-rc1/contract.json'
$contractPath = Join-Path $RepositoryRoot 'CStructSharpWeb/src/wasm/cstruct-contract.ts'
$boundaryPath = Join-Path $RepositoryRoot 'CStructSharpWeb/wasm/CStructInteropBoundary.cs'
$exportsPath = Join-Path $RepositoryRoot 'CStructSharpWeb/wasm/CStructExports.cs'
$bootstrapPath = Join-Path $RepositoryRoot 'CStructSharpWeb/wasm/bootstrap.js'
$packagePath = Join-Path $RepositoryRoot 'CStructSharpWeb/package.json'

foreach ($path in @($baselinePath, $contractPath, $boundaryPath, $exportsPath, $bootstrapPath, $packagePath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Browser contract input is missing: $path"
    }
}

$baseline = Get-Content -LiteralPath $baselinePath -Raw | ConvertFrom-Json
$contract = Get-Content -LiteralPath $contractPath -Raw
$boundary = Get-Content -LiteralPath $boundaryPath -Raw
$exports = Get-Content -LiteralPath $exportsPath -Raw
$bootstrap = Get-Content -LiteralPath $bootstrapPath -Raw
$package = Get-Content -LiteralPath $packagePath -Raw | ConvertFrom-Json
$dtoSource = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'CStructSharpWeb/wasm/CStructInteropDtos.cs') -Raw
$managedSources = $exports + $boundary + $dtoSource

if ($baseline.schemaVersion -ne 1 -or $baseline.name -ne 'browser-rc1') {
    throw 'Browser baseline schema/name is not the supported browser-rc1 revision.'
}
if ($package.version -ne $baseline.packageVersion) {
    throw "Browser package version '$($package.version)' differs from '$($baseline.packageVersion)'."
}
if ($contract -notmatch "INTEROP_CONTRACT_VERSION\s*=\s*$($baseline.contractVersion)\s+as const") {
    throw 'TypeScript contract version differs from the browser baseline.'
}
if ($boundary -notmatch "InteropContractVersion\s*=\s*$($baseline.contractVersion)\s*;") {
    throw 'Managed contract version differs from the browser baseline.'
}

foreach ($name in $baseline.managedExports) {
    if ($exports -notmatch "(?s)\[JSExport\]\s+public static string $name\s*\(") {
        throw "Managed browser export is missing: $name"
    }
    if ($bootstrap -notmatch "['`"]$name['`"]") {
        throw "Bootstrap binding is missing managed export: $name"
    }
}

foreach ($operation in $baseline.operations) {
    if ($contract -notmatch "['`"]$operation['`"]") {
        throw "TypeScript operation is missing: $operation"
    }
    if ($managedSources -notmatch "['`"]$operation['`"]") {
        throw "Managed operation is missing: $operation"
    }
}

foreach ($field in $baseline.optionFields) {
    if ($contract -notmatch "(?m)^\s+$field\?:") {
        throw "TypeScript option is missing: $field"
    }
    $managedName = $field.Substring(0, 1).ToUpperInvariant() + $field.Substring(1)
    if ($managedSources -notmatch "\b$managedName\b") {
        throw "Managed option is missing: $managedName"
    }
}

foreach ($code in $baseline.errorCodes) {
    if ($boundary -notmatch [regex]::Escape('"' + $code + '"')) {
        throw "Managed browser error code is missing: $code"
    }
}

Write-Host "Browser contract validated: v$($baseline.contractVersion), $($baseline.managedExports.Count) exports, $($baseline.operations.Count) operations, $($baseline.optionFields.Count) options, and $($baseline.errorCodes.Count) error codes."
