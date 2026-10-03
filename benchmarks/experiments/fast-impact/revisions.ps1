# Measures a real rebuild and a source-edited benchmark in isolated, immutable bundles.
# Usage (repository root, after other timing finishes): ./benchmarks/experiments/fast-impact/revisions.ps1 -Node node
param([string]$Node = 'node')
$ErrorActionPreference = 'Stop'
$repo = (Get-Location).Path
$base = Join-Path $repo 'artifacts/perf/fast-impact/revision-checkout'
$output = Join-Path $repo 'artifacts/perf/fast-impact'
$records = [Collections.Generic.List[object]]::new()
if (Test-Path -LiteralPath $base) { throw 'The isolated checkout already exists; retain it and choose a new path for another study.' }

# Records one completed child command, failing before another experiment starts on an error.
function Invoke-Measured([string]$Label, [string]$Exe, [string[]]$Arguments) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    & $Exe @Arguments *> (Join-Path $output "$Label.log")
    $code = $LASTEXITCODE
    $records.Add(@{label=$Label; seconds=$timer.Elapsed.TotalSeconds; exit=$code; args=$Arguments})
    $records | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $output 'revisions.json')
    if ($code -ne 0) { throw "$Label failed: $code" }
}

# Copies a freshly built host and its dependencies into a research worker bundle; no running bundle is overwritten.
function Copy-Bundle([string]$Destination) {
    if (Test-Path -LiteralPath $Destination) { throw "Bundle already exists: $Destination" }
    New-Item -ItemType Directory -Path $Destination | Out-Null
    Copy-Item -Path (Join-Path $repo 'benchmarks/experiments/fast-impact/bin/Release/net10.0/*') -Destination $Destination -Recurse
    Copy-Item -Path (Join-Path $base 'benchmarks/CStructSharp.Benchmarks/bin/Release/net10.0/*') -Destination $Destination -Recurse -Force
}

$zip = Join-Path $output 'revision-source.zip'
$setupTimer = [Diagnostics.Stopwatch]::StartNew()
git archive --format=zip --output=$zip HEAD
if ($LASTEXITCODE -ne 0) { throw 'git archive failed' }
Expand-Archive -LiteralPath $zip -DestinationPath $base
$records.Add(@{label='snapshot'; seconds=$setupTimer.Elapsed.TotalSeconds})
Invoke-Measured 'snapshot-build' 'dotnet' @('build', (Join-Path $base 'CStructSharp.NonWeb.slnf'), '-c', 'Release')
$before = Join-Path $output 'revision-before'
Copy-Bundle $before

# A comment edit recompiles core and its dependents without changing library behavior.
$core = Join-Path $base 'src/CStructSharp/CStruct.cs'
$coreSource = [IO.File]::ReadAllText($core)
$loopTimer = [Diagnostics.Stopwatch]::StartNew()
[IO.File]::WriteAllText($core, $coreSource + "`n// Research rebuild marker; no behavior change.`n")
Invoke-Measured 'edit-build' 'dotnet' @('build', (Join-Path $base 'CStructSharp.NonWeb.slnf'), '-c', 'Release')
$edited = Join-Path $output 'revision-edited'
Copy-Bundle $edited
Invoke-Measured 'edit-compare' $Node @('benchmarks/experiments/fast-impact/compare.mjs', '--before', (Join-Path $before 'FastImpact.dll'), '--after', (Join-Path $edited 'FastImpact.dll'), '--out', (Join-Path $output 'edit-compare.json'))
$records.Add(@{label='edit-build-compare'; seconds=$loopTimer.Elapsed.TotalSeconds})
[IO.File]::WriteAllText($core, $coreSource)

# The sole behavioral cost injection is redundant validation in a benchmark, preserving its output and the library.
$packet = Join-Path $base 'benchmarks/CStructSharp.Benchmarks/PacketBenchmarks.cs'
$packetSource = [IO.File]::ReadAllText($packet)
$needle = 'public StructValue ParseSpan() => this.layout.Parse(this.bytes, "packet");'
if (-not $packetSource.Contains($needle)) { throw 'Packet benchmark source no longer matches the experiment' }
$replacement = @'
public StructValue ParseSpan()
    {
        StructValue value = this.layout.Parse(this.bytes, "packet");
        if (!value.ContainsKey("kind"))
        {
            throw new InvalidOperationException("Missing packet tag.");
        }

        return value;
    }
'@
[IO.File]::WriteAllText($packet, $packetSource.Replace($needle, $replacement).Replace('/// <summary>Parses the packet from its in-memory bytes.</summary>', '/// <summary>Parses the packet and repeats tag validation for the isolated performance control.</summary>'))
Invoke-Measured 'candidate-build' 'dotnet' @('build', (Join-Path $base 'benchmarks/CStructSharp.Benchmarks/CStructSharp.Benchmarks.csproj'), '-c', 'Release', '-f', 'net10.0')
$after = Join-Path $output 'revision-after'
Copy-Bundle $after
if ((Get-FileHash (Join-Path $before 'CStructSharp.dll')).Hash -ne (Get-FileHash (Join-Path $after 'CStructSharp.dll')).Hash) { throw 'Core binary changed; this is not the intended benchmark-only control' }

# Control setup compares both parser results with the reference fingerprint, outside all measurements.
'{"op":"quit"}' | & dotnet (Join-Path $after 'FastImpact.dll') worker controls *> (Join-Path $output 'revision-verification.log')
if ($LASTEXITCODE -ne 0) { throw 'Candidate verification failed' }

Invoke-Measured 'revisions-forward' $Node @('benchmarks/experiments/fast-impact/compare.mjs', '--before', (Join-Path $before 'FastImpact.dll'), '--after', (Join-Path $after 'FastImpact.dll'), '--repeats', '10', '--out', (Join-Path $output 'revisions-forward.json'))
Invoke-Measured 'revisions-reverse' $Node @('benchmarks/experiments/fast-impact/compare.mjs', '--before', (Join-Path $after 'FastImpact.dll'), '--after', (Join-Path $before 'FastImpact.dll'), '--repeats', '10', '--out', (Join-Path $output 'revisions-reverse.json'))
$env:DOTNET_TieredCompilation = '0'
foreach ($side in @('before', 'after')) {
    $bundle = Join-Path $output "revision-$side"
    $confirmation = Join-Path $output "revision-long-$side"
    Invoke-Measured "revision-long-$side" 'dotnet' @((Join-Path $bundle 'FastImpact.dll'), 'bdn', '100', '30', 'true', 'true', $confirmation, 'packet-only')
}
$records | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $output 'revisions.json')
