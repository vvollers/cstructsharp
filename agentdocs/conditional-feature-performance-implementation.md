# Conditional-feature performance implementation

Implementation branch: `perf/conditional-feature-comparison`. The fixed controls are main `2c4ad4c` and the unoptimized feature snapshot `d30ee60`. The [original report](conditional-feature-performance.md) and its [original raw measurements](benchmark-results/) are preserved. This report describes production changes, not diagnostic ablations.

## Implementation coverage

| Plan item | Production implementation | Evidence |
|---|---|---|
| 1. Codec recognition | Exact allocation-free spelling checks; compiled fields cache fixed-point classification. | Zero-allocation classifier regression; native allocation tables. |
| 2. Unconditional composites | Compiled direct-conditional flag skips selection and local-scope state in read, write, sizing and address traversal. | Existing operation matrix and roundtrip/address tests; ordinary-layout controls. |
| 3. Conditional groups | Shared compiled selectors, integer group/arm slots and constant switch maps; select once per group entry and retain the arm. Simple bounded expression execution avoids evaluation-session allocation. | Checked evaluator/session differential tests across overflow and depth/work limits; mixed switches and nested conditions. |
| 4. Scoped locals | Precompute visible names, anonymous promotion, capture slots and names affected by nested traversal. Mask local names at entry; restore only overwritten slots after each field. | Concurrent nested tag/count collisions and mixed records; wide 8/32/128-field measurements. |
| 5. Primitive descriptors | Lazy immutable registries per byte order share codec maps, bitfield metadata and frozen primitive symbols. User symbols, pointer widths and operation state remain per-layout/per-call. Ordinary schemas skip conditional normalization setup. | Parallel layouts with conflicting user aliases, endian and pointer settings; compilation and allocation comparisons. |
| 6. Debug paths | Persistent parent/segment paths replace cloned path arrays and declaration nodes. Format lazily into one string. | Multidimensional/pointer paths, retained paths formatted concurrently, Inspector byte-selection tests. |
| 7. JavaScript API | Supported `compile` handle with `parse`, `parseWithDebug`, async idempotent `dispose`, declarations and examples. Dedicated retained workers queue operations; ordinary source calls reuse an idle-expiring worker. | Real installed Node/browser package consumers, cancellation/recovery/cleanup and compiled-layout tests; real API benchmarks. |

All conditional source arms still undergo compilation validation. No branch is removed from validation to improve runtime numbers. Read decisions are frozen within an entry, while subsequent entries have independent decisions. Writes and updates retain inactive-field and branch-stability checks. The scalar expression optimization uses the same checked arithmetic as the full evaluator and enforces dependency depth/work limits.

## Measurement method

[Reproduction instructions](../benchmarks/ConditionalComparison/README.md), [three-version runner](../benchmarks/ConditionalComparison/run-implementation.ps1), [full tables](performance-improvements/tables.md), [machine/build hashes](performance-improvements/results/environment.json).

Fresh Release .NET 10 and non-AOT WASM builds use the same harness and fixtures. Runs are serial, with no concurrent builds/tests/profilers. Native constructor warmup is five seconds; each operation warms for at least 600 ms, calibrates a roughly 200 ms batch and records nine batches. Two process launches run in reverse version order: main/feature/optimized, then optimized/feature/main. Tables use the median of process medians and expose per-launch ratios. Native allocations use managed per-thread allocation counters, measured outside timing. JS timing includes natural garbage collection.

The original equal-payload plain/if/switch fixtures and wide records remain. Additional fixtures use explicit mixed tags, four switch choices and nested if groups with equal active byte widths. Native setup checks consumption and exact serialize roundtrip. JS setup checks retained/public result equality and consumed length. Compiled API startup is outside warm compiled-read timings; baseline public `parse` includes its per-call startup, as that API actually behaves.

WASM core parsing, JSON projection, public source calls and compiled public calls represent different boundaries. Native allocations do not measure JavaScript heap allocation or worker resident memory. Main cannot compile conditional syntax, so conditional/plain prices use matched feature or optimized fixtures. Small changes below roughly 10% on this VM deserve caution.

The primary native/WASM build records commit `d15ac07`; its optimized JavaScript bridge includes the source-staging
fix at `3328729`, identified by per-launch bridge hashes. Main's first JS launch preceded sidecar hash recording;
the unchanged fixed checkout and its later launch provide provenance, but that first launch has no contemporaneous
bridge sidecar. The final compilation and parsing controls use freshly rebuilt binaries after `b8ba213` and the
root-default fix at `ab7514c`. Later commits add tests, mutation-tooling corrections and the reproduction harness.
These phases are kept separate rather than presenting measurements from an earlier implementation as final results.

## Results

The primary comparison found **no repeatable ordinary-layout slowdown above 10% against main**. The largest aggregate native ratio was 1.061 (1 KiB span parsing), with launch ratios 1.085 and 1.038. The largest aggregate JavaScript ratio was 1.024 (mixed-tag plain core parsing), with launch ratios 1.009 and 1.039. These small differences are consistent with timing drift and modest remaining costs; they do not reproduce the original 20–50% regressions.

Primary measurements, microseconds:

| Operation | Main | Feature | Optimized |
|---|---:|---:|---:|
| Native tiny-header compilation | 40.18 | 47.62 | 19.79 |
| Native 128-field compilation | 389.19 | 397.96 | 378.87 |
| Native 1 KiB numeric-array parsing | 25.22 | 30.10 | 25.75 |
| Native 16-record debug parsing | 4.29 | 6.39 | 4.55 |
| Native 128 plain records, parse | 30.88 | 34.77 | 29.56 |
| Native 128 if records, parse | — | 235.39 | 44.18 |
| Native 128 switch records, parse | — | 230.44 | 43.41 |
| Native wide 128-field if record, parse | — | 214.80 | 31.90 |
| Native mixed four-way switch records, parse | — | 307.63 | 47.74 |
| Native nested mixed if records, parse | — | 399.22 | 51.33 |
| WASM tiny-header compilation | 750.66 | 847.91 | 458.88 |
| WASM 1 KiB numeric-array core parsing | 472.61 | 599.05 | 472.36 |
| WASM 128 plain records, core parse | 617.87 | 799.35 | 621.83 |
| WASM 128 if records, core parse | — | 4,140.67 | 876.49 |
| JS public header parse | 209,747.80 | 211,550.45 | 714.25 |
| JS compiled header parse | — | — | 127.49 |
| JS public 128-record plain parse | 221,072.85 | 226,631.85 | 1,870.42 |
| JS compiled 128-record plain parse | — | — | 1,123.58 |
| JS compiled 128-record if parse | — | — | 1,421.80 |

The standard if/switch price is now approximately **1.5× native / 1.4× WASM core**, versus about **6.6–6.8× / 5.2×** in this run's feature control. Mixed switches and nested if groups remain below 2× in both launches. The wide-record ratio drops to **1.06× native / 1.16–1.17× WASM** at 128 fields. The supported JS compiled API also stays below 2× across these fixtures; fixed worker/envelope costs dilute its conditional ratio.

The 1 KiB numeric-array allocation falls from **125,368 to 91,904 bytes/read**, below main's 92,408. The eliminated codec-recognition term alone was 1,024 × 32 = 32,768 bytes; additional scope/expression savings explain the remaining difference. Native 128-record conditional parsing falls from about **712,482 to 103,096 bytes/read** (85.5% less); nested mixed conditions fall from **1,327,766 to 106,179** (92% less).

Debug correctness remains stronger than main's old non-indexed struct-array paths. The small remaining main-relative debug differences therefore include the cost of correct element identity. The substantial feature-snapshot debug regression is recovered without reverting those paths.

### Compilation refinement

The primary run exposed one allocation tradeoff: wide-if compilation allocated 465,816 bytes versus the feature snapshot's 388,936, although timing was only 2.7% higher natively and 3.8% higher in WASM. Inspection located unnecessary per-primitive name-discovery stacks, shadow-traversal sets/stacks, and oversized conditional-branch builders. A follow-up removes those temporary objects while retaining identical compiled decisions and scope slots. Final wide-if compilation allocates about **340,464 bytes**: **26.9% less than the primary optimized build and 12.5% less than the feature control**. Native source compilation is roughly at parity with the feature control in the full rerun; short controls below confirm no material large-layout timing regression.

Large-schema compilation does not enjoy the same percentage gain as tiny schemas. Its source-size-dependent parsing, normalization and model construction remain; removing fixed registry work cannot eliminate those costs. The 128-field primary WASM compilation result was essentially flat (8.30 ms optimized versus 8.54 ms feature and 8.19 ms main). This remains a reason to reuse compiled layouts rather than repeatedly compile large definitions.

### Final verification on the refined binaries

The [full compilation rerun](performance-improvements/refinement/tables.md) and [final parsing runs](performance-improvements/final-parsing/tables.md) retain their own raw samples and build provenance. Long-run compilation controls drifted: the unchanged main WASM wide-plain constructor moved from 8.36 to 11.47 ms. This biased the long-run aggregate comparison against optimized, whose two launches were both in the slower period. [Short interleaved compilation controls](performance-improvements/compilation-controls/tables.md) on the same verified binaries resolve that ambiguity: wide-plain WASM compilation is 11.496 ms optimized versus 11.498 ms main. No samples were discarded.

Final compilation controls, microseconds:

| Operation | Main | Feature | Optimized |
|---|---:|---:|---:|
| Native header | 55.51 | 68.15 | 29.42 |
| Native 128 fields | 598.18 | 646.35 | 598.66 |
| Native wide-if 128 | — | 688.56 | 664.57 |
| WASM header | 755.14 | 855.16 | 464.45 |
| WASM 128 fields | 11,142.80 | 11,527.60 | 11,358.67 |
| WASM wide-if 128 | — | 12,436.05 | 12,250.97 |

Final retained parsing and real API calls, microseconds:

| Operation | Main | Feature | Optimized |
|---|---:|---:|---:|
| Native 1 KiB numeric array | 46.57 | 55.29 | 48.16 |
| Native plain 128 | 52.58 | 61.06 | 53.40 |
| Native if 128 | — | 352.49 | 79.96 |
| Native switch 128 | — | 341.31 | 78.35 |
| Native wide-if 128 | — | 352.39 | 54.91 |
| Native mixed switches | — | 466.77 | 82.09 |
| Native nested mixed if | — | 580.30 | 88.62 |
| WASM plain 128 core | 853.12 | 1,085.16 | 877.16 |
| WASM if 128 core | — | 5,045.45 | 1,200.65 |
| JS public header | 263,040.90 | 271,443.15 | 870.83 |
| JS compiled header | — | — | 127.61 |
| JS compiled plain 128 | — | — | 1,485.03 |
| JS compiled if 128 | — | — | 1,836.04 |

The final native conditional speedups against the feature snapshot range from **4.4× to 6.5×** in these retained-record scenarios; standard WASM if/switch core parsing improves about **4.2×**. Conditional/plain ratios remain below 2×: standard records are **1.47–1.50× native / 1.37–1.38× WASM core**; wide records are **1.08× / 1.15×**; mixed/nested records peak at **1.66× / 1.51×**. Compiled JS reads peak at 1.32×, and compiled debug calls at 1.15×.

No final ordinary parsing case shows a repeatable slowdown above 10% against main. The largest aggregate native ratio is 1.069 (mixed plain debug; launches 1.089 and 1.050); the largest JS ratio is 1.029 (header core; 1.040 and 1.019). Short compilation controls put ordinary 128-field work within 3% of main in both launches. These bounds describe the measured fixtures, not every possible schema.

Header source compilation is **57% faster natively and 46% faster in WASM than the feature control**. Large-schema compilation time remains broadly flat against main: this target is achieved strongly for fixed setup costs, not uniformly for source-size-dependent parser work. Wide-if compilation improves allocations substantially, but its small timing gain is not claimed as a major speedup.

The 32-byte codec-recognition allocation is eliminated; final numeric-array allocation is 91,904 bytes versus main 92,408 and feature 125,368. Returned values, boxing and layout variables still allocate. Repeated public JS header calls drop from about 271 ms to 0.871 ms versus the feature snapshot; a warm compiled handle takes 0.128 ms. Cold worker/handle initialization is deliberately outside the compiled-read column.

## Validation

Final normal Release verification passed with zero build warnings/errors. Native tests use `DOTNET_ROLL_FORWARD=LatestPatch`, so the two target frameworks execute on their respective installed major runtimes. Only the API-baseline utility temporarily uses `LatestMajor`.

| Check | Result |
|---|---|
| Native .NET 8 and .NET 10 | 992 passed on each; zero failed/skipped |
| Coverage / risk | 96.58% lines, 91.48% branches; zero high/critical risk files |
| Managed API | Frozen revision 4 matches both frameworks; 20 exported types |
| JavaScript bridge unit tests | 8 passed |
| Inspector / Workshop unit tests | 45 / 29 passed |
| Installed npm consumers | Node and browser pass: Vite dev/build, root/nested paths, SSR, static copy, CSP |
| Public declarations | Strict TypeScript examples and rejected invalid per-read options pass |
| Compiled lifecycle | Mixed concurrent requests, cancellation/recovery, cleanup, disposal, limits, root defaults and independent source staging pass |
| Inspector browser | Full suite: 53 passed, one existing skip; final relevant rerun: 15 passed |
| NuGet and symbols | Package validation and isolated consumers pass on both frameworks |
| Contracts / tooling | Browser v5, canonical language, feature matrix, fuzz corpus, compiler fixture, solution parity and release-budget validator self-tests pass |
| Formatting / lint | Source JS, Inspector and Workshop checks pass |
| Documentation | 102 source pages, 24 API pages and 418 site files validated; all six browser/accessibility tests passed |
| Mutation | 75% score threshold passes; additional zero-survivor gate fails, detailed below |

[Saved validation logs](performance-improvements/validation/) include final builds, native tests, coverage, API comparison, packaging, lifecycle and browser evidence. The benchmark-only exports were removed by rebuilding the normal WASM package before installed-consumer testing. No package was published and no branch was merged.

### Mutation evidence and the outstanding release gate

The original permanent mutation configuration named six files that no longer existed. The scope now follows their
current implementations and includes the new performance helpers (46 files). More seriously, the API export-list
test rejected Stryker's injected public instrumentation and falsely killed unrelated mutations. An attempted
environment-based exception did not fix full runs. The final configuration excludes exactly that reflection test
from instrumented runs; normal tests and the managed API baseline retain the full export check without exceptions.
The report validator pins the one-test filter and rejects reports containing that test.

The first genuine full behavioral run recorded **76.58%**: 2,647 killed, 15 timeouts, **814 survivors**, no uncovered
or runtime-error mutants, and 1,294 compiler-rejected mutations. It passes Stryker's configured 75% score threshold
but **fails the additional zero-survivor repository release gate**. Earlier 100% results are invalid behavioral
evidence. Compiler errors, including the existing definition-parser instrumentation limitation, are not counted as
detected behavior. [Raw report](performance-improvements/validation/mutation-behavior.json.gz),
[audit](performance-improvements/validation/mutation-audit.json),
[survivor inventory](performance-improvements/validation/mutation-survivors.json).

Of those survivors, 54 overlap changed lines and 760 do not, compared with `d30ee60`. This is a source-diff
classification, not proof that a mutant is equivalent or that a behavior belongs to one change. The findings include
untested diagnostic strings, checked-expression mutations with equivalent runtime conversion behavior, optimization
removal, and behavioral coverage gaps. No blanket exclusions or lower thresholds were applied to make the gate green.

Follow-up tests cover entirely conditional composites, inactive locals overwritten by nested fields, integer inputs
to fixed-point writers, selector diagnostic context, allocation savings from simple expression evaluation, frozen
group decisions when a nested field changes an external selector, conditional sizing, and root-union debug paths.
A six-file focused run detected 378 of 483 mutations (376 kills, two timeouts), with 105 survivors; it is not a
replacement for the full gate. Four direct single-mutation checks separately verified that the fixed-point writer,
group-cache identity, selected sizing and root-union path tests fail for the corresponding faulty implementation.
All original source bytes were restored and the 992-test suite then passed on both frameworks.

The wider mutation coverage gap remains a release-readiness limitation. Passing functional and performance checks
must not be presented as a green mutation release gate. The raw reports preserve the evidence for a subsequent
library-wide audit; this performance implementation does not silently redefine that gate.

## Lifecycle and remaining costs

Each JavaScript compiled handle owns a full worker/runtime. Explicit disposal is required to release it promptly; this is intentionally not an unbounded layout cache. Calls on one handle are serialized. Separate handles are isolated. Cancellation of an active operation terminates the worker; the next read recreates it and recompiles. Queued cancellation leaves the active read alone. Active rejection awaits source cleanup; disposal awaits all outstanding cleanup. Ordinary source parsing shares a worker and closes it after 30 seconds idle. Idle Node workers do not keep the process alive.

Compilation retains immutable metadata, not streams, result objects or evaluation state. Parsing still allocates returned object graphs, numeric boxes and layout variable literals. Wide ExpandoObject records have their own non-linear member-insertion costs; removing quadratic scope restoration does not make all output materialization linear. Debug output still costs paths, byte copies and result projection. Worker messaging, source staging and JSON remain in the supported JS API even after compilation/startup reuse.

Performance figures cover this Windows VM, .NET 10 and Node/V8 WASM without AOT. Correctness is tested on .NET 8 and .NET 10 and in Chromium; these are not cross-engine or .NET 8 performance claims.

## Commit coverage

The seven production changes are in `312ec8c`, `e520628`, `006e448`, `da1cbb3` and `d15ac07`; worker lifecycle fixes are in `4ed3434`, `3328729` and `ab7514c`; the compilation refinement is `b8ba213`. Focused test, tooling and benchmark commits follow them. The branch remains `perf/conditional-feature-comparison`; the fixed baseline checkouts and original raw measurements are unchanged.
