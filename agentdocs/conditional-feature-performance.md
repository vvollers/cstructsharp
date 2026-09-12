# Conditional-feature performance comparison and improvement plan

This is the preserved pre-optimization report for `d30ee60`. See the [implementation report](conditional-feature-performance-implementation.md) for completed changes, final measurements and validation limitations.

The branch has material regressions on existing layouts, and conditional records have a substantial additional cost. Across eight common fixtures, C# compiled stream parsing is **6–21% slower** and JavaScript/WASM core parsing is **21–34% slower**. C# debug parsing of the tested nested/128-record arrays is **48–52% slower**, partly because it now produces correct indexed field paths.

For an equivalent 128-record layout, `if`/`switch` cost **6.5–6.8× C# compiled parsing** and **5.3× WASM core parsing**. Compiling that conditional layout costs **23–30% more in C#** and **25–31% more in WASM** than its plain variant. With one record, fixed per-call work dilutes the parse ratio to 3.3–3.4× C# / 2.5× WASM. These ratios describe equal active payloads with tag 1, not every possible conditional layout.

The implementation and benchmark/report work are saved on `perf/conditional-feature-comparison`. Production parser code remains the feature snapshot; optimization experiments were confined to a separate diagnostic checkout.

## Versions and scope

- Feature snapshot: `d30ee60`, including all prior working changes, conditional fields/expressions, new binary metadata codecs, and indexed struct-array debug paths.
- Baseline: local `main`, `2c4ad4c`. No remote main update was fetched.
- Machine: Windows VM, AMD Ryzen 9 9950X (16 cores / 32 logical processors); .NET SDK 10.0.204, Release net10.0; browser-wasm with native linking and no AOT; Node 26.8.1.
- Exact commits, native/WASM binary hashes and sizes: [environment.json](benchmark-results/environment.json).

Branch-versus-main measures the entire change set. The plain-versus-conditional pairs isolate the additional cost of choosing conditional layouts within the branch; main cannot compile their syntax. Fixtures cover small headers, 128 scalar fields, 1 KiB byte arrays, nested struct arrays, runtime-sized arrays and fixed character buffers. The conditional axis uses one or 128 packed, little-endian records with the same active bytes and values: uint8 tag, uint32 value, uint16 tail. Conditional schemas also contain an inactive uint16 alternative. Each active record is seven bytes.

## Method and practical limits

[Harness and reproduction instructions](../benchmarks/ConditionalComparison/README.md), [all result tables](benchmark-results/tables.md), and raw JSON samples are saved alongside this report. Measurements ran serially. Each operation warmed for at least 600 ms, calibrated a batch targeting 200 ms, and recorded nine batches. Two independent process launches per version ran in opposite version order. Results use the median of launch medians. C# records managed thread allocations and forces GC outside each timed batch; JS includes natural GC and an awaited call per iteration.

The original first C# constructor samples showed tiered-JIT drift. Their raw observations remain in the original files, but final header-compilation results use two replacement launches per version with five seconds of process warmup, saved under `stabilized-header`. The harness now includes that process warmup by default. Other C# operations and JS observations did not show this particular first-constructor trend.

Setup and correctness checks are outside timing. C# verifies exact byte consumption and serialize roundtrip. JS verifies consumption and equality between retained-object and public parsed data. Actual result objects are materialized. Compilation means the complete `new CStruct(...)` constructor: source parsing, normalization, validation and compiled model construction. Parsing means reuse of that compiled object; both MemoryStream and span APIs were measured in C#. Debug collection is separate.

The public JS API has no compiled-object handle. Identical opt-in benchmark exports were added to both WASM builds without changing normal builds or the public API. `parseCore` includes Uint8Array copying, MemoryStream creation and managed result materialization, returning consumed bytes without JSON. `parseJson` adds the real managed JSON projection and JavaScript JSON.parse. These exports demonstrate retained-object performance, not a finished proposed public API.

Public `parseWithDebug()` uses a warm shared runtime for these small Uint8Arrays but recompiles the definition every time. Public `parse()` creates a worker and .NET runtime for every call through large-source.js/source-worker.js; its timings include that startup. They are different execution paths, not a pure debug on/off comparison. Public calls leave their Data payload as JSON text, matching the real API.

This is a custom comparative harness, not a BenchmarkDotNet release-gate run. Two launches provide replication, not strong statistical confidence. Treat changes below about 10% cautiously, especially worker-startup variation. A repeated slowdown around 20%, substantial added allocation, or a several-fold conditional cost warrants action. Browser engines, AOT, .NET 8 performance, aligned records, pointer graphs, mixed tags and many-case switches were not benchmarked.

## Key results

All times below are microseconds; larger is slower.

| Existing scenario / operation | Main | Branch | Change |
|---|---:|---:|---:|
| C# 1 KiB array, compile | 40.47 | 50.03 | +23.6% |
| C# 1 KiB array, compiled stream parse | 26.74 | 32.28 | +20.7% |
| C# 1 KiB array, compiled span parse | 31.17 | 36.68 | +17.7% |
| C# 16 nested records, compiled debug parse | 4.39 | 6.68 | +52.3% |
| C# 128 plain records, compiled stream parse | 30.53 | 36.74 | +20.3% |
| WASM 1 KiB array, compile | 715.73 | 883.42 | +23.4% |
| WASM 1 KiB array, compiled core parse | 491.63 | 635.38 | +29.2% |
| WASM 16 nested records, compiled core parse | 70.24 | 93.99 | +33.8% |
| WASM 128 plain records, compiled parse + JSON | 975.52 | 1,156.01 | +18.5% |
| JS public debug, 128 plain records (includes compile) | 3,723.52 | 4,590.21 | +23.3% |

Small-definition compilation generally regresses about 13–24% in C# and 12–23% in WASM. The 128-scalar-field definition has a smaller relative compilation regression: 4% C# and 10% WASM. See the full table for the stabilized tiny-header result and every fixture.

| 128-record layout on the branch | Plain | If | Switch |
|---|---:|---:|---:|
| C# compile | 63.20 | 77.52 | 82.20 |
| C# compiled stream parse | 36.74 | 248.28 | 239.83 |
| C# compiled span parse | 39.32 | 245.08 | 245.37 |
| C# compiled debug parse | 62.36 | 279.70 | 279.91 |
| WASM compile | 1,144.31 | 1,431.16 | 1,495.02 |
| WASM compiled core parse | 808.07 | 4,303.48 | 4,302.76 |
| WASM compiled parse + JSON | 1,156.01 | 4,822.42 | 4,767.70 |
| JS public debug (includes compile) | 4,590.21 | 8,737.20 | 9,308.84 |

C#'s 128-record stream parse allocates approximately **120 KB plain versus 712 KB if / 708 KB switch**, an extra ~4.6 KB per conditional record. Main's plain parse allocates ~84 KB. The 1 KiB byte-array parse allocates **92,408 → 125,368 bytes**, a 35.7% increase.

Public JS `parse()` takes about **209–243 ms on main and 226–264 ms on the branch**. Changes range from −4% to +9%, with substantial launch-to-launch startup variation. This does not establish a major regression in that API. It does expose a large pre-existing worker/runtime-startup cost that hides the clearer warm-parser regression and much of the conditional price. The uncompressed runtime JS/WASM/JSON bundle grows only ~44 KB (5,229,231 → 5,273,263 bytes); bundle size alone does not explain startup variance.

## Root causes and diagnostic evidence

The experiments and all raw results are recorded in [diagnostic tables](benchmark-results/diagnostic-tables.md). Each ablation starts from the feature snapshot, changes only the named mechanism, and runs the same five ordinary fixtures. Unmodified controls before and after the ablations bracket timing drift. Attribution runs use one process per experiment with nine batches, so their exact percentages are exploratory. Saved patches and `ablate.py` make each experiment reviewable and reproducible. These are diagnostic changes, not production fixes.

The focused results below compare each experiment with the midpoint of the before/after controls; the full tables retain both control times.

| Isolated change | Fixture / operation | C# timing change | WASM timing change | C# allocated bytes/op, control → changed |
|---|---|---:|---:|---:|
| Main primitive registry only | Tiny-header compile | −11.9% | −10.4% | 135,976 → 94,888 |
| Skip unconditional reader bookkeeping | 128 plain records, parse | −4.8% | −11.8% | 120,179 → 96,443 |
| Allocation-free fixed-point recognition | 1 KiB array, parse | −4.0% | −8.8% | 125,368 → 92,600 |
| Allocation-free fixed-point recognition | 128 plain records, parse | +1.1% | −12.5% | 120,179 → 107,890 |

The last row illustrates why an allocation reduction must not be reported as a guaranteed percentage speedup. C# controls drifted approximately 9–12% faster by the end; some changed timings overlap that range. WASM controls also drifted for some fixtures, though the 128-record control was stable. The byte-array allocation reduction is exact, and the bookkeeping reduction is 23,736 bytes across 129 struct instances (184 bytes each). Registry removal saves 41,088 bytes on header compilation and reduces compilation below both controls. These are direct mechanism checks; production optimization percentages require tightly interleaved follow-up measurements. The isolated changes were not combined, and their percentages must not be added.

### Per-value codec recognition allocates

[CStructReader.cs](../src/CStructSharp/CStructReader.cs) calls `FixedPointCodec.IsType(compiledField.CodecName)` for every numeric result before adding a layout variable. [FixedPointCodec.cs](../src/CStructSharp/FixedPointCodec.cs) calls `name.TrimEnd('<', '>')`. Its params argument allocates a two-character array even for an ordinary byte or uint32. This adds 32 bytes per numeric value; suffixed names can also create a trimmed string. Of the byte-array regression's extra 32,960 bytes, 32,768 come from the 1,024 checks. The remaining helper/state cost is per operation.

The fixedpoint experiment replaces TrimEnd with exact spelling matches, retaining support for all neutral and explicit-endian fixed-point names. It isolates this allocation and recognition cost without removing conditional support.

### Unconditional structs create conditional state

[ReadCompiledStructInto](../src/CStructSharp/CStructSelectedReader.cs) creates ConditionalVariableScope and ConditionalFieldSelection for every struct instance. Their dictionaries initialize before determining whether the struct has conditions. The scope also scans the fields with LINQ Any on every instance, and every field goes through activation/completion calls. The bookkeeping experiment caches direct conditional presence in CompiledCompositeType and avoids these objects/calls on ordinary reads. Similar construction exists in measuring, address resolution and writing; those operations were inspected, not timed here.

### Conditional execution is much more than one comparison

[ConditionalFieldSelection.cs](../src/CStructSharp/ConditionalFieldSelection.cs) snapshots the whole expression dictionary for a newly encountered group, then re-evaluates predicates per field, including inactive alternatives. It freezes the environment but does not cache the selected arm. LayoutExpressionEvaluator creates a new evaluation session with multiple sets/dictionaries. Dependency validation, execution and error-context string creation repeat. Numeric reads create new Literal objects; [ExpressionEvaluator.cs](../src/CStructSharp/ExpressionEvaluator.cs) compiles programs for those fresh identities through its ConditionalWeakTable during dependency lookup. Retaining CStruct therefore does not eliminate all runtime expression compilation.

[ConditionalVariableScope.cs](../src/CStructSharp/ConditionalVariableScope.cs) also discovers visible names through iterator/Stack objects. After every active field it walks every local name to restore/remove entries in the shared variable dictionary. For a flat conditional record with F fields, that restoration alone is O(F²), on top of repeated predicates and allocations.

The separate wide-record check compares equal active payloads with 8, 32 and 128 fields under one if group. C# conditional/plain parse ratios are approximately **5.1×, 5.7×, 6.7×**; WASM ratios are **4.5×, 6.8×, 10.8×**. At 128 fields, C# takes 31.18 → 208.90 µs and WASM 283.22 → 3,054.33 µs. Plain ExpandoObject materialization also grows faster than field count, so these results do not attribute all nonlinear growth exclusively to scope restoration. The source establishes its additional quadratic work.

Group decisions must still freeze at entry; nested groups must see newly read fields; inactive local names must mask stale caller/previous-element values. Removing the scope or sharing an evaluation cache across changing environments would violate those semantics.

### Every compilation builds a larger primitive universe

Base primitive reader entries grew from 31 to 54; explicit big-endian spellings grew from 11 to 17, bringing additional neutral aliases. [CStructPrimitiveCodecs.cs](../src/CStructSharp/CStructPrimitiveCodecs.cs) reconstructs reader/writer/alignment maps for every layout. [CStructCompiledModel.cs](../src/CStructSharp/CStructCompiledModel.cs) builds symbols for every registered primitive and freezes snapshots, even for a uint8-only source. Small schemas pay a larger percentage cost because registry work is largely source-size independent.

The registry experiment restores only main's primitive registry file, leaving the new parser, normalization and conditional implementation in place. New metadata types are unavailable in this experiment; that is an intentional isolation technique, not an acceptable production solution. Ordinary schema compilation still pays new case-normalization dictionaries/LINQ projections, grammar alternatives and expression precedence work after that restoration.

### Indexed debug paths deliberately do extra work

[CStructReader.cs](../src/CStructSharp/CStructReader.cs) now computes array indices, clones the debug stack and constructs an Identifier/Field per struct-array element. The resulting paths correctly identify entries such as `root.items[127].value`. Main did less work and returned less precise paths. The extra time matters for inspector consumers, but removing the indices would undo a correctness improvement.

## Prioritized improvement plan

1. **Remove per-value type-name allocation.** Replace TrimEnd recognition with allocation-free matching, then store a codec classification or `CanBeLayoutVariable` flag in each compiled field. Cover neutral/endian fixed-point names, Guid/UUID and out-of-range numeric values. Acceptance: remove at least the measured 32 bytes per numeric element and recover the corresponding parse time without changing stale-variable semantics.

2. **Add a cheap path for unconditional composites.** Compute `HasDirectConditionalFields` once. Avoid helper objects, dictionaries, repeated field scans and activation/completion work when false. Extend the principle consistently to selected reads, measuring, addresses and writes, retaining per-operation state ownership. Acceptance: ordinary allocations approach main and no common fixture retains a repeatable >10% slowdown after steps 1–2, unless a separate measured cause remains.

3. **Compile branch groups and evaluate their selected arm once.** Preserve group identity/order in the compiled model, retain selected arms in compact operation-local slots, and evaluate an if predicate or switch selector only at entry. Cache the decision, not a mutable environment used by other groups. Precompile predicates, specialize checked literal/identifier comparisons, and retrieve numeric variable literals without compiling fresh programs. Preserve depth/node budgets. Acceptance: substantially reduce conditional-array allocations/time; start with a goal below 2× plain parsing, revising only against measured unavoidable work. This is a target, not a demonstrated result.

4. **Replace full local-map restoration after every field.** Precompute visible names and anonymous-promotion relationships. Use indexed local slots or scoped lookup layers; mask inactive names at entry and restore only names affected by nested/promoted traversal. Remove per-field Stack/iterator discovery. Acceptance: eliminate the scope's quadratic restoration and reduce the wide-record conditional/plain ratio, while preserving frozen decisions, nested tag collisions, anonymous promotion, missing counts and consecutive array-element behavior.

5. **Share immutable primitive descriptors.** Cache immutable reader/writer/alignment/symbol descriptors keyed by relevant byte-order/pointer settings; bind user types per layout. Do not share mutable symbol maps or operation state. Skip case-normalization infrastructure for ordinary declarations. Acceptance: recover most of the registry experiment's compilation savings while all new types remain supported. Measure both tiny and 128-field definitions.

6. **Keep precise debug paths with cheaper construction.** Represent indexed segments directly or use a structured per-traversal path builder instead of cloning declaration AST nodes. Materialize strings only where DebugData requires them. Acceptance: StructArrayDebugTests and inspector path tests pass, with improved struct-array debug time and unchanged byte mappings/paths.

7. **Expose compiled reuse to JavaScript.** Design an owned compiled-layout handle with parse/parseWithDebug/dispose, immutable compilation options, bounded lifetime/cache policy and concurrency rules. Separately assess a persistent worker or warm byte-array no-debug path, preserving cancellation by worker termination/replacement where needed. The benchmark exports establish potential savings, not a complete API design. Acceptance: repeated calls avoid schema compilation and runtime startup while preserving envelopes, limits, cancellation and cleanup.

Implement and measure each step separately. Before merging production fixes, run native tests on net8.0/net10.0, conditional/expression/property/operation-matrix tests, JS bridge tests and browser/inspector indexed-path tests. Follow with a longer BenchmarkDotNet comparison and repeated Node/browser runs on a quiet machine. Preserve expression limits, inactive-field validation, branch-stable updates and debug correctness throughout.

## Validation and saved artifacts

All fixture consumption/roundtrip/data-equality checks passed in the primary, wide and diagnostic benchmark runs. Release native tests passed **976/976 on net8.0 and 976/976 on net10.0**, with no skips or failures. The complete source JS bridge suite passed **8/8 tests**. See [native validation output](benchmark-results/native-tests.txt) and [JS validation output](benchmark-results/js-tests.txt). Browser end-to-end tests were not run for this benchmark-only work.

The main checkout and diagnostic checkout are separate from the feature branch; both finished with clean tracked files. The diagnostic checkout was reset to the feature snapshot by the final unchanged control. Opt-in benchmark exports never modify the production WASM project file. The original working changes are committed at d30ee60; the follow-up commit contains only the harness, report and results.
