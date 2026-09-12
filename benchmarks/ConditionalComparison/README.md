# Conditional feature comparison

Run from the repository root with .NET 10 SDK + `wasm-tools`, Node.js, and Python:

```powershell
git worktree add --detach C:/projects/github/cstructsharp-benchmark-main main
./benchmarks/ConditionalComparison/run.ps1 -MainRoot C:/projects/github/cstructsharp-benchmark-main
```

This is a serial, steady-state comparative harness, not BenchmarkDotNet. Both implementations consume the same `cases.json`. Native processes first warm compilation for five seconds to let tiered JIT/PGO settle. Each operation then warms for at least 600 ms, calibrates a batch to approximately 200 ms, then records nine batches. Two fresh process launches per version are run in opposite version order. The summary uses the median of launch medians. Native batches collect managed allocation counts; explicit GC occurs before, outside, each timed native batch. JavaScript includes natural GC and an awaited call per iteration. Result materialization is included; source creation, input creation, initial WASM startup, and correctness checks are outside warm timings unless explicitly part of `publicParse`.

Native operations:
- `compile`: `new CStruct(definition, aligned: false)`, including parsing, normalization, validation, and compiled model construction.
- `parse`: reuse a compiled layout and MemoryStream, reset its position, return the parsed object.
- `span`: reuse a compiled layout, call `Parse(ReadOnlySpan<byte>, "root")`.
- `debug`: reuse a compiled layout, collect debug records.

JavaScript operations:
- `compile`: one benchmark-only JS export call constructs and retains a CStruct; replaces the previous object.
- `parseCore`: a benchmark-only export reuses that CStruct, copies the Uint8Array into managed memory, creates a MemoryStream and materializes the parse result, returning consumed bytes. No JSON projection.
- `parseJson`: the same retained object, with the real managed JSON projection and JavaScript JSON.parse.
- `publicParse`: actual asynchronous public API, including its worker/runtime creation on every call, schema compilation, source bridge, and result envelope. Its Data remains JSON text, as the API specifies.
- `publicDebug`: actual public parseWithDebug API, warm shared runtime, including compilation and debug projection/envelope parsing. Its Data remains JSON text.

The production JS API has no compiled-object handle. `Benchmark.targets` opts the same `BenchmarkExports.cs` into each WASM build through `CustomAfterMicrosoftCommonTargets`. These exports are NOT part of normal builds or the public API. run-js copies the repository's bridge modules into the freshly built AppBundle, as packaging does, so worker-relative runtime paths resolve correctly. No installed or previously copied runtime is benchmarked.

The plain/if/switch fixtures have identical active bytes and data: tag=1, uint32 value, uint16 tail, either one or 128 records. Conditional schemas also declare an inactive uint16 alternative. All layouts are packed and little-endian. Native checks exact consumption and serialize roundtrip before timing; JS checks consumption and public-versus-retained JSON equality. These checks throw on failures.

Results are written under `agentdocs/benchmark-results`, which is ignored by the repository and must be explicitly force-added when saving a report. `summarize.py` writes both complete regression and conditional-price tables. The harness deliberately avoids concurrency during measurements.

Diagnostic experiments use a separate detached checkout of the feature snapshot:

```powershell
python benchmarks/ConditionalComparison/ablate.py C:/projects/github/cstructsharp-benchmark-diagnostic registry
```

`registry` restores only main's primitive registry file (new binary types will be unavailable); `bookkeeping` caches direct conditional presence and avoids reader conditional state for ordinary structs; `fixedpoint` replaces TrimEnd with exact spelling matches. Each mode first resets its four candidate files to d30ee60 and applies only that mode. These are isolated attribution experiments, not production-ready fixes. Build the harness with LibraryRoot set to that checkout. Set BENCH_OPERATIONS to `compile,parse` for native or `compile,parseCore` for JS to focus the run. BENCH_CASES can select a reduced fixture JSON for JS; native accepts that file as its first argument.

The saved report replaces the original first-constructor measurements with longer-warmed header reruns. To regenerate those historical tables exactly, use `python benchmarks/ConditionalComparison/summarize.py --stabilized-header`. The default summary uses only the primary runs, suitable for new runs with the updated five-second process warmup.

Run `run-diagnostics.ps1` for the three ablations, bracketed by unchanged controls. `diagnostic-summary.py` reports changes against the control midpoint and shows both controls so drift is visible. `wide-cases.json` probes fields per conditional group; `header-case.json` is the dedicated constructor warmup check.
