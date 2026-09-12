#!/usr/bin/env bash
# CPU-samples one benchmark scenario with Linux perf and prints the top inclusive frames.
#   benchmarks/profiling/profile-dotnet.sh <scenario> [seconds] [outdir]
# Scenarios: CompileSmall, CompileMedium, ParsePrimitiveArray1KiB, ParseNestedUnaligned, SerializePocoToSpan,
#            ParseRealPng, ParseArrayU32Be (see benchmarks/CStructSharp.Benchmarks/ProfileDriver.cs).
# Requires: perf, a Release build of the benchmark project (net10.0), and permission to record
# (kernel.perf_event_paranoid <= 1 or sudo). DOTNET_PerfMapEnabled=1 makes JIT frames symbolizable.
set -euo pipefail
scenario="${1:-ParsePrimitiveArray1KiB}"
seconds="${2:-10}"
outdir="${3:-artifacts/profiles}"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
mkdir -p "$outdir"
dll="$root/benchmarks/CStructSharp.Benchmarks/bin/Release/net10.0/CStructSharp.Benchmarks.dll"
[ -f "$dll" ] || { echo "Build first: dotnet build benchmarks/CStructSharp.Benchmarks -c Release" >&2; exit 1; }
data="$outdir/$scenario.perf.data"
# W^X double-mapping places JIT code in "memfd:doublemapper" regions that perf cannot match to the perf map;
# disabling it for the profiled process keeps JIT'd frames symbolizable (it does not change generated code).
export DOTNET_PerfMapEnabled=1 DOTNET_EnableWriteXorExecute=0
# -F 999 Hz, call graphs via frame pointers (the .NET JIT keeps frame pointers by default).
perf record -F 999 -g -o "$data" -- dotnet "$dll" --profile "$scenario" "$seconds" > "$outdir/$scenario.driver.log" 2>&1
perf report -i "$data" --children --stdio --no-demangle --percent-limit 0.5 2>/dev/null > "$outdir/$scenario.perf-report.txt"
# Flat inclusive summary: top symbols by children%.
perf report -i "$data" --children --stdio -g none --sort sym --percent-limit 0.5 2>/dev/null \
  | grep -vE '^#|^$' | head -80 > "$outdir/$scenario.inclusive.txt" || true
perf report -i "$data" --no-children --stdio -g none --sort dso,sym --percent-limit 0.5 2>/dev/null \
  | grep -vE '^#|^$' | head -40 > "$outdir/$scenario.self.txt" || true
echo "Wrote $outdir/$scenario.perf-report.txt and $outdir/$scenario.inclusive.txt"
head -30 "$outdir/$scenario.inclusive.txt"
