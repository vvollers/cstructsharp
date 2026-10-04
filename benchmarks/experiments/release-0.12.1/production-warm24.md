# Development performance comparison

**Production out-of-process: provisional measurements, not a regression verdict.** 30 cases; 3 fresh launch(es) per side; CPU 16.
Canary: within screening margin. Practical timing margin: ±3.0%. Spread and launch ranges below are observations, not confidence intervals.
Δ time is the median of paired launch ratios; before/after columns summarize each side independently. These estimates can differ when launches drift.

| Case | Before ns/op | After ns/op | Δ time | Observed launch Δ range | Spread | Bytes/op before → after | Timing signal |
| --- | ---: | ---: | ---: | ---: | ---: | --- | --- |
| `CStructSharp.Benchmarks.BitfieldBenchmarks.Generated_BitfieldLeaf_Parse` | 57.72 | 16.47 | -71.5% | -71.8…-71.0% | 3.5% | 32,32,32 → 32,32,32 | repeatable speedup; confirm |
| `CStructSharp.Benchmarks.GeneratedBenchmarks.Generated_Nested256_Parse` | 3643.10 | 3650.85 | 0.2% | -1.3…0.9% | 9.9% | 57392,57392,57392 → 57392,57392,57392 | inconclusive (unstable) |
| `CStructSharp.Benchmarks.GeneratedBenchmarks.Generated_PrimRecord_Parse` | 8.95 | 8.91 | -0.1% | -1.7…0.3% | 4.1% | 48,48,48 → 48,48,48 | inconclusive (within margin) |
| `CStructSharp.Benchmarks.GeneratedBenchmarks.Generated_PrimRecord_Serialize` | 7.27 | 7.72 | 4.1% | 2.3…6.2% | 4.4% | 56,56,56 → 56,56,56 | inconclusive (unstable) |
| `CStructSharp.Benchmarks.GeneratedBenchmarks.HandWritten_PrimRecord` | 2.19 | 2.18 | -0.5% | -1.6…1.7% | 3.5% | 0,0,0 → 0,0,0 | inconclusive (within margin) |
| `CStructSharp.Benchmarks.GeneratedBenchmarks.Runtime_PrimRecord_Parse` | 40.45 | 32.57 | -18.6% | -19.8…-15.8% | 9.7% | 256,256,256 → 256,256,256 | inconclusive (unstable) |
| `CStructSharp.Benchmarks.GeneratedBenchmarks.Runtime_PrimRecord_Serialize` | 44.76 | 34.09 | -23.8% | -24.1…-22.5% | 3.6% | 56,56,56 → 56,56,56 | repeatable speedup; confirm |
| `CStructSharp.Benchmarks.MaterializationBenchmarks.Generated_Matrix256_Parse` | 55707.67 | 3482.91 | -93.7% | -93.8…-93.7% | 3.2% | 270449,270446,270448 → 139312,139312,139312 (changed; confirm) | repeatable speedup; confirm |
| `CStructSharp.Benchmarks.MaterializationBenchmarks.Generated_Matrix256_Serialize` | 116174.04 | 46536.75 | -59.9% | -60.1…-59.7% | 2.7% | 131137,131135,131133 → 131135,131133,131138 (changed; confirm) | repeatable speedup; confirm |
| `CStructSharp.Benchmarks.MaterializationBenchmarks.Generated_Text_Parse` | 693.63 | 524.67 | -24.4% | -86.9…-23.9% | 483.4% | 12528,12528,12528 → 10432,10432,10432 (changed; confirm) | inconclusive (unstable) |
| `CStructSharp.Benchmarks.MaterializationBenchmarks.Runtime_Matrix256_Parse` | 526797.85 | 285870.19 | -45.9% | -49.5…-43.8% | 14.9% | 2638212,2638222,2638225 → 2113745,2113745,2113745 (changed; confirm) | inconclusive (unstable) |
| `CStructSharp.Benchmarks.MaterializationBenchmarks.Runtime_Matrix256_Serialize` | 712493.10 | 580549.44 | -18.5% | -22.2…-15.5% | 5.4% | 1178306,1178299,1178302 → 655817,655749,655760 (changed; confirm) | repeatable speedup; confirm |
| `CStructSharp.Benchmarks.MaterializationBenchmarks.Runtime_Text_Parse` | 5417.95 | 848.36 | -84.4% | -84.4…-84.3% | 2.3% | 26952,26952,26952 → 12568,12568,12568 (changed; confirm) | repeatable speedup; confirm |
| `CStructSharp.Benchmarks.MemoryWorkloadBenchmarks.CompileRepeatedScalars` | 22979611.11 | 284232.81 | -98.7% | -99.1…-97.5% | 216.5% | 18666099,18665779,19033864 → 514977,514977,515056 (changed; confirm) | inconclusive (unstable) |
| `CStructSharp.Benchmarks.MemoryWorkloadBenchmarks.CompileTinyScalar` | 19473.66 | 7804.34 | -61.0% | -61.2…-55.4% | 52.3% | 28473,28328,28408 → 14040,14040,14200 (changed; confirm) | inconclusive (unstable) |
| `CStructSharp.Benchmarks.MemoryWorkloadBenchmarks.PlanUpdateScalar` | 361.13 | 252.65 | -32.4% | -44.8…8.2% | 58.7% | 1112,1112,1112 → 1112,1112,1112 | inconclusive (unstable) |
| `CStructSharp.Benchmarks.MemoryWorkloadBenchmarks.ReadBytes16` | 468.20 | 386.63 | -17.4% | -21.1…-17.3% | 3.4% | 1912,1912,1912 → 224,224,224 (changed; confirm) | repeatable speedup; confirm |
| `CStructSharp.Benchmarks.MemoryWorkloadBenchmarks.ReadBytes4096` | 108395.02 | 86156.22 | -20.5% | -21.8…-18.6% | 2.9% | 430313,430313,430313 → 4304,4304,4304 (changed; confirm) | repeatable speedup; confirm |
| `CStructSharp.Benchmarks.MemoryWorkloadBenchmarks.SelectTiny` | 57.79 | 58.70 | 1.0% | -18.3…3.4% | 24.5% | 288,288,288 → 288,288,288 | inconclusive (unstable) |
| `CStructSharp.Benchmarks.MemoryWorkloadBenchmarks.SelectWide` | 3517.70 | 650.43 | -81.5% | -82.8…-74.5% | 604.3% | 26400,26400,26400 → 288,288,288 (changed; confirm) | inconclusive (unstable) |
| `CStructSharp.Benchmarks.MemoryWorkloadBenchmarks.SerializeScalar` | 75.45 | 67.65 | -10.8% | -52.9…-3.1% | 97.9% | 120,120,120 → 120,120,120 | inconclusive (unstable) |
| `CStructSharp.Benchmarks.PacketBenchmarks.ReadValueMapped` | 763.69 | 903.89 | 18.4% | -33.1…25.7% | 53.6% | 552,552,552 → 552,552,552 | inconclusive (unstable) |
| `CStructSharp.Benchmarks.Scenarios.PathAndTypedBenchmarks.ReadValue_Scalar_Natural(Index: 0)` | 153.65 | 150.73 | -0.6% | -3.2…1.3% | 4.2% | 136,136,136 → 136,136,136 | inconclusive (within margin) |
| `CStructSharp.Benchmarks.Scenarios.PathAndTypedBenchmarks.ReadValue_Scalar_Natural(Index: 127)` | 150.14 | 151.28 | 0.8% | 0.5…0.8% | 2.3% | 136,136,136 → 136,136,136 | inconclusive (within margin) |
| `CStructSharp.Benchmarks.Scenarios.PathAndTypedBenchmarks.ReadValue_Scalar_Natural(Index: 9999)` | 155.59 | 152.13 | -0.4% | -3.2…-0.2% | 3.9% | 136,136,136 → 136,136,136 | inconclusive (within margin) |
| `CStructSharp.Benchmarks.TextOutputBenchmarks.Span` | 33093.77 | 24904.78 | -25.7% | -55.7…-23.5% | 74.4% | 67584,67584,67584 → 0,0,0 (changed; confirm) | inconclusive (unstable) |
| `CStructSharp.Benchmarks.TextRecordBenchmarks.Generated_Parse(Count: 1)` | 222.11 | 119.60 | -41.8% | -46.4…-36.6% | 32.3% | 472,472,472 → 304,304,304 (changed; confirm) | inconclusive (unstable) |
| `CStructSharp.Benchmarks.TextRecordBenchmarks.Generated_Parse(Count: 256)` | 19530.47 | 25160.17 | 28.8% | -30.0…29.9% | 87.7% | 106553,106553,106552 → 63544,63544,63544 (changed; confirm) | inconclusive (unstable) |
| `CStructSharp.Benchmarks.TextRecordBenchmarks.Runtime_Parse(Count: 1)` | 498.00 | 233.31 | -55.1% | -67.8…-38.3% | 47.9% | 1448,1448,1448 → 512,512,512 (changed; confirm) | inconclusive (unstable) |
| `CStructSharp.Benchmarks.TextRecordBenchmarks.Runtime_Parse(Count: 256)` | 105144.34 | 35760.24 | -66.0% | -67.0…-64.9% | 7.2% | 325810,325810,325810 → 86192,86192,86192 (changed; confirm) | inconclusive (unstable) |

No signal does not demonstrate equivalence. A possible change, allocation change, canary drift or instability needs targeted confirmation in fresh launches. Repeatable direction still does not establish suite-adjusted statistical confidence.
Setup/build: 0.00 s; measurement/report so far: 1596.59 s. Total invocation time is printed after report files are written.

Raw BDN samples, immutable bundle hashes, runtime controls and launch order are retained beside this report.
