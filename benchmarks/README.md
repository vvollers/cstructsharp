# Performance measurement

`CStructSharp.Benchmarks/` contains BenchmarkDotNet timing/allocation cases that reference core. Run `dotnet run --project benchmarks/CStructSharp.Benchmarks -c Release -- --filter "*" --exporters json` from the root. Normalize full JSON with `tools/quality/Convert-BenchmarkBaseline.ps1`, then apply `tools/quality/Validate-NonWebReleaseBudgets.ps1 -BenchmarkSummaryPath PATH`. Baselines live under `contracts/performance/`; benchmark output is ignored. Performance checks are maintained manual measurements, not an automatic timing gate on every push.
