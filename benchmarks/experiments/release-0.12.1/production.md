# Development performance comparison

**Production out-of-process: provisional measurements, not a regression verdict.** 30 cases; 3 fresh launch(es) per side; CPU 16.
Canary: drift. Practical timing margin: ±3.0%. Spread and launch ranges below are observations, not confidence intervals.
Δ time is the median of paired launch ratios; before/after columns summarize each side independently. These estimates can differ when launches drift.

| Case | Before ns/op | After ns/op | Δ time | Observed launch Δ range | Spread | Bytes/op before → after | Timing signal |
| --- | ---: | ---: | ---: | ---: | ---: | --- | --- |
| `CStructSharp.Benchmarks.BitfieldBenchmarks.Generated_BitfieldLeaf_Parse` | 220.59 | 16.71 | -92.3% | -94.2…-71.9% | 105.0% | 32,32,32 → 32,32,32 | inconclusive (canary drift) |
| `CStructSharp.Benchmarks.GeneratedBenchmarks.Generated_Nested256_Parse` | 3637.34 | 3698.79 | 2.0% | -1.3…7.9% | 10.3% | 57392,57392,57392 → 57392,57392,57392 | inconclusive (canary drift) |
| `CStructSharp.Benchmarks.GeneratedBenchmarks.Generated_PrimRecord_Parse` | 10.35 | 8.87 | -13.1% | -27.7…-2.2% | 704.9% | 48,48,48 → 48,48,48 | inconclusive (canary drift) |
| `CStructSharp.Benchmarks.GeneratedBenchmarks.Generated_PrimRecord_Serialize` | 8.59 | 7.49 | -10.2% | -13.5…-5.3% | 765.3% | 56,56,56 → 56,56,56 | inconclusive (canary drift) |
| `CStructSharp.Benchmarks.GeneratedBenchmarks.HandWritten_PrimRecord` | 2.21 | 2.23 | 0.2% | -1.3…21.1% | 1253.1% | 0,0,0 → 0,0,0 | inconclusive (canary drift) |
| `CStructSharp.Benchmarks.GeneratedBenchmarks.Runtime_PrimRecord_Parse` | 39.55 | 33.53 | -12.8% | -20.8…157.6% | 208.7% | 256,256,256 → 256,256,256 | inconclusive (canary drift) |
| `CStructSharp.Benchmarks.GeneratedBenchmarks.Runtime_PrimRecord_Serialize` | 45.57 | 34.33 | -24.7% | -25.4…-23.5% | 8.5% | 56,56,56 → 56,56,56 | inconclusive (canary drift) |
| `CStructSharp.Benchmarks.MaterializationBenchmarks.Generated_Matrix256_Parse` | 55972.46 | 3774.06 | -93.7% | -93.8…-83.3% | 209.4% | 270446,270445,270449 → 139312,139312,139312 (changed; confirm) | inconclusive (canary drift) |
| `CStructSharp.Benchmarks.MaterializationBenchmarks.Generated_Matrix256_Serialize` | 117389.51 | 46569.12 | -60.2% | -60.5…-60.0% | 19.4% | 131137,131135,131137 → 131133,131138,131134 (changed; confirm) | inconclusive (canary drift) |
| `CStructSharp.Benchmarks.MaterializationBenchmarks.Generated_Text_Parse` | 706.56 | 522.29 | -26.1% | -31.7…-23.7% | 1431.9% | 12528,12528,12528 → 10432,10432,10432 (changed; confirm) | inconclusive (canary drift) |
| `CStructSharp.Benchmarks.MaterializationBenchmarks.Runtime_Matrix256_Parse` | 547764.30 | 308811.13 | -42.9% | -50.3…-41.9% | 31.7% | 2638223,2638214,2638222 → 2113745,2113745,2113744 (changed; confirm) | inconclusive (canary drift) |
| `CStructSharp.Benchmarks.MaterializationBenchmarks.Runtime_Matrix256_Serialize` | 930451.72 | 566677.27 | -39.3% | -80.2…-33.9% | 315.8% | 1178256,1178265,1178325 → 655807,655817,655796 (changed; confirm) | inconclusive (canary drift) |
| `CStructSharp.Benchmarks.MaterializationBenchmarks.Runtime_Text_Parse` | 8806.64 | 3882.30 | -84.3% | -90.4…-46.7% | 514.8% | 26952,26952,26952 → 12568,12568,12568 (changed; confirm) | inconclusive (canary drift) |
| `CStructSharp.Benchmarks.MemoryWorkloadBenchmarks.CompileRepeatedScalars` | 24190200.00 | 623074.17 | -97.4% | -97.5…-97.4% | 30.9% | 19009464,19089864,19089864 → 514817,539057,538897 (changed; confirm) | inconclusive (canary drift) |
| `CStructSharp.Benchmarks.MemoryWorkloadBenchmarks.CompileTinyScalar` | 27057.91 | 9038.92 | -60.2% | -67.7…-55.5% | 56.3% | 28857,28777,29016 → 14152,14232,14072 (changed; confirm) | inconclusive (canary drift) |
| `CStructSharp.Benchmarks.MemoryWorkloadBenchmarks.PlanUpdateScalar` | 300.07 | 548.73 | 109.2% | -14.1…110.6% | 494.5% | 1112,1112,1112 → 1112,1112,1112 | inconclusive (canary drift) |
| `CStructSharp.Benchmarks.MemoryWorkloadBenchmarks.ReadBytes16` | 462.16 | 385.96 | -17.6% | -62.9…-16.5% | 126.7% | 1912,1912,1912 → 224,224,224 (changed; confirm) | inconclusive (canary drift) |
| `CStructSharp.Benchmarks.MemoryWorkloadBenchmarks.ReadBytes4096` | 111077.26 | 86258.78 | -22.3% | -25.6…-18.8% | 271.4% | 430313,430313,430313 → 4304,4304,4304 (changed; confirm) | inconclusive (canary drift) |
| `CStructSharp.Benchmarks.MemoryWorkloadBenchmarks.SelectTiny` | 58.52 | 93.79 | 55.4% | 0.7…84.9% | 135.7% | 288,288,288 → 288,288,288 | inconclusive (canary drift) |
| `CStructSharp.Benchmarks.MemoryWorkloadBenchmarks.SelectWide` | 3714.32 | 647.76 | -82.7% | -93.9…-81.6% | 1231.8% | 26400,26400,26400 → 288,288,288 (changed; confirm) | inconclusive (canary drift) |
| `CStructSharp.Benchmarks.MemoryWorkloadBenchmarks.SerializeScalar` | 130.73 | 161.96 | 84.3% | -82.3…131.2% | 855.0% | 120,120,120 → 120,120,120 | inconclusive (canary drift) |
| `CStructSharp.Benchmarks.PacketBenchmarks.ReadValueMapped` | 2412.40 | 2320.01 | -3.8% | -67.9…1.9% | 76.4% | 552,552,552 → 552,552,552 | inconclusive (canary drift) |
| `CStructSharp.Benchmarks.Scenarios.PathAndTypedBenchmarks.ReadValue_Scalar_Natural(Index: 0)` | 445.50 | 412.82 | 2.9% | -62.6…136.5% | 457.2% | 136,136,136 → 136,136,136 | inconclusive (canary drift) |
| `CStructSharp.Benchmarks.Scenarios.PathAndTypedBenchmarks.ReadValue_Scalar_Natural(Index: 127)` | 188.10 | 257.62 | 2.5% | -1.1…37.0% | 483.6% | 136,136,136 → 136,136,136 | inconclusive (canary drift) |
| `CStructSharp.Benchmarks.Scenarios.PathAndTypedBenchmarks.ReadValue_Scalar_Natural(Index: 9999)` | 153.69 | 231.65 | 55.0% | -2.9…207.7% | 127.8% | 136,136,136 → 136,136,136 | inconclusive (canary drift) |
| `CStructSharp.Benchmarks.TextOutputBenchmarks.Span` | 45850.26 | 74640.28 | 69.4% | -20.5…96.1% | 425.6% | 67584,67584,67584 → 0,0,0 (changed; confirm) | inconclusive (canary drift) |
| `CStructSharp.Benchmarks.TextRecordBenchmarks.Generated_Parse(Count: 1)` | 677.21 | 288.47 | -57.4% | -87.0…-34.5% | 527.8% | 472,472,472 → 304,304,304 (changed; confirm) | inconclusive (canary drift) |
| `CStructSharp.Benchmarks.TextRecordBenchmarks.Generated_Parse(Count: 256)` | 146253.60 | 74654.67 | -49.0% | -73.0…-34.6% | 139.2% | 106553,106553,106553 → 63544,63544,63544 (changed; confirm) | inconclusive (canary drift) |
| `CStructSharp.Benchmarks.TextRecordBenchmarks.Runtime_Parse(Count: 1)` | 927.17 | 616.36 | -44.1% | -49.5…-23.5% | 287.3% | 1448,1448,1448 → 512,512,512 (changed; confirm) | inconclusive (canary drift) |
| `CStructSharp.Benchmarks.TextRecordBenchmarks.Runtime_Parse(Count: 256)` | 464191.25 | 40519.48 | -84.2% | -91.3…-73.4% | 384.6% | 325810,325810,325810 → 86192,86192,86192 (changed; confirm) | inconclusive (canary drift) |

No signal does not demonstrate equivalence. A possible change, allocation change, canary drift or instability needs targeted confirmation in fresh launches. Repeatable direction still does not establish suite-adjusted statistical confidence.
Setup/build: 0.00 s; measurement/report so far: 1296.54 s. Total invocation time is printed after report files are written.

Raw BDN samples, immutable bundle hashes, runtime controls and launch order are retained beside this report.
