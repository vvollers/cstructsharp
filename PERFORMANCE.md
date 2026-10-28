# Discovery performance

Measured on 28 October 2026 on Windows x64, AMD Ryzen 9 9950X, SDK 10.0.403/runtime 10.0.14. Release JIT uses default tiering/PGO; Native AOT is published separately. Five serial process pairs alternate order after one warmup pair. Each process inherits affinity mask 65536 (logical CPU 16); the OS cache is retained. No builds, tests or source edits overlap measurements. The probes warm 50,000 calls for repeated library cases; preparation and first-use cases perform one measured call in a fresh process.

The reference is the immutable 0.17.0 assembly from revision 3f0d53ad. Candidate binaries and all samples are retained under ignored artifacts and agentdocs/discovery-program. The [reproduction guide](benchmarks/experiments/discovery/README.md) describes separate bundles, inputs and sample collection. Measurements consume complete results; they are workload observations, not universal speed guarantees.

Times below are mean (minimum–maximum) nanoseconds per complete operation; allocations are bytes per operation. Preparation is outside repeated timings and reported separately.

| Case | Reference ns | Candidate ns | Allocations B |
| --- | ---: | ---: | ---: |
| JIT Runtime match success | 893.5 (848.6–1030.3) | 325.7 (317.9–335.8) | 848 → 0 |
| JIT Prepared match success versus original runtime | 870.4 (863.6–882.4) | 223.8 (211.9–233.1) | 848 → 0 |
| JIT Runtime rejection | 832.4 (825.5–841.8) | 325.0 (317.4–342.4) | 816 → 0 |
| JIT Runtime short input | 517.2 (515.4–519.3) | 205.9 (203.7–207.8) | 536 → 0 |
| JIT Coalesced two fields versus prepared fields | 533.0 (531.5–534.5) | 667.7 (662.5–680.8) | 32 → 568 |
| JIT Coalesced 32 fields versus prepared fields | 7888.7 (7845.6–7908.4) | 4051.1 (4004.1–4174.4) | 152 → 808 |
| JIT Prepared 16-byte raw storage plus owned ASCII text | 2918.7 (2914.2–2925.8) | 269.0 (267.3–270.9) | 320 → 96 |
| AOT Runtime match success | 252.6 (250.5–254.9) | 83.7 (81.5–89.6) | 800 → 0 |
| AOT Prepared match success versus original runtime | 254.1 (250.7–261.2) | 48.9 (48.5–49.4) | 800 → 0 |
| AOT Runtime rejection | 250.5 (246.9–253.4) | 82.4 (81.2–84.0) | 768 → 0 |
| AOT Runtime short input | 166.3 (164.2–168.8) | 56.8 (56.4–57.2) | 504 → 0 |
| AOT Coalesced two fields versus prepared fields | 77.4 (76.7–78.5) | 121.0 (117.3–126.7) | 32 → 568 |
| AOT Coalesced 32 fields versus prepared fields | 1097.5 (1093.1–1101.4) | 478.0 (472.5–492.1) | 152 → 808 |
| AOT Prepared 16-byte raw storage plus owned ASCII text | 503.1 (496.5–507.9) | 60.7 (59.8–61.6) | 320 → 96 |

Prepared execution holds typed slots rather than scalar boxes or per-call dictionaries. Preparation alone costs 4.70 ms/3592 B JIT and 17.98 µs/3432 B AOT. Cached-plan lookup alone is 132.7 ns JIT and 36.9 ns AOT with no allocation; lookup plus execution is 320.9/81.7 ns. Explicit prepared rejection is 213.9/49.1 ns and short input 101.2/23.2 ns (JIT/AOT), both zero allocation.

First runtime use on an already compiled layout regresses from 2.41 to 7.09 ms JIT and 6.20 to 22.92 µs AOT; candidate first prepared execution excludes planning and costs 2.37 ms/4.86 µs. Cold compile plus recognition regresses 68.59 to 73.60 ms JIT (7.3%); AOT cold ranges overlap, so that result is inconclusive. First-use allocations include pool initialization. Repeated-operation gains do not remove those preparation costs.

Generated success controls are 41.1 to 44.4 ns JIT (overlapping ranges) and 2.8 to 3.0 ns AOT; short controls are 13.2 to 15.5 ns and 2.6 to 2.8 ns. The generated implementation is unchanged. Tiny differences are retained as control drift, not attributed to runtime matching. A validity-aware buffer probe costs 99.9 ns JIT/12.8 ns AOT versus 14.1/2.4 ns for a specialized manual scalar gate, with zero allocations. Use it for known-byte semantics and reusable requirements; it does not outperform the specialized gate.

Coalescing reduces 32 leaf requests/128 bytes to one request/128 bytes; the two-field control goes from two requests/eight bytes to one/eight. Capture and all owned numeric results are included. The two-field case slows 25% JIT/56% AOT and allocates more. Elementwise snapshots are also slower than direct prepared field reads: 1.50–2.00 times with 608 B for two fields and 3088 B for 32. Keep snapshots explicit when owned validity or grouped transport is useful.

Preparing the byte binding on an already compiled schema is measured separately: first binding is 2.51 ms (2.41–2.55)/10,320 B JIT and 11.48 µs (9.90–13.10)/1736 B AOT. Repeated fresh binding costs 512.4 ns (440.7–773.2, unstable)/488 B JIT and 142.6 ns (141.0–143.7)/488 B AOT. Bind once; these preparation costs are excluded from repeated reads. The 20 measured component samples and four warmups are in byte-preparation-samples.jsonl.

The 16-byte text comparison includes raw ownership, ASCII decoding and all NUL code units. A direct decode from an already available span is the counterexample: 86.4 ns/56 B JIT and 26.1 ns/56 B AOT. Adding the prepared raw read costs 341.8 ns/96 B JIT (269.1–591.6, unstable) and 59.7 ns/96 B AOT. No general text decoder was added: encoding, termination, truncation and custom-codec policy remain explicit caller decisions.

Full evidence: library-samples.jsonl (48 cases, 480 measured samples plus 96 warmup samples), text-samples.jsonl (20 measured samples plus four warmups), immutable-manifest.json and the summary JSON files in agentdocs/discovery-program. Read-set output memory is bounded by its prepared envelope; matching scratch is pooled and operation-local. Unknown-input coverage, gap recovery, final unsigned addresses and cancellation are correctness tests, not timing claims.

The repository-wide Impact screen and targeted SerializeScalar confirmation retained unstable/canary warnings. Those results are inconclusive and establish neither improvement nor equivalence. Reports remain under artifacts/perf/development/runs/2026-10-28T13-23-03-743Z-babed2 and 2026-10-28T13-25-11-044Z-83f8d1. Client complete-operation findings, rejected indexes/shared filters, sparse coverage and cancellation are in the client's PERFORMANCE.md.
