# Reproduce discovery comparisons

Build reference and candidate executables before timing. Use the SDK from global.json. Retain the exact source revisions, package versions, dotnet --info, source patches and executable/DLL hashes. Never rebuild a measured directory. Output belongs in ignored artifacts/, not in this directory.

## Library probes

The Probe project uses the current core project unless ReferenceLibrary is supplied. That property points to an immutable reference CStructSharp.dll and excludes candidate-only APIs. The source generator is compiled normally. These commands run from the library root; substitute absolute output/reference paths:

```powershell
dotnet publish benchmarks/experiments/discovery/Probe -c Release -r win-x64 -p:ReferenceLibrary=REFERENCE_DLL -o REFERENCE_JIT
dotnet publish benchmarks/experiments/discovery/Probe -c Release -r win-x64 -p:ReferenceLibrary=REFERENCE_DLL -p:NativeProbe=true -o REFERENCE_AOT
dotnet publish benchmarks/experiments/discovery/Probe -c Release -r win-x64 -o CANDIDATE_JIT
dotnet publish benchmarks/experiments/discovery/Probe -c Release -r win-x64 -p:NativeProbe=true -o CANDIDATE_AOT
```

Use NativeProbe for this project: setting PublishAot globally would incorrectly apply AOT to the netstandard source-generator project. No runtime code generation is used.

Repeated modes are runtime, prepared, lookup (cached planning only), plan (lookup plus execution), manual, generated and probe. Recognition scenarios are success, reject and short. fields, elementwise and coalesced compare complete two-field (small) and 32-field (large) owned results. bind-bytes measures repeated fresh byte-binding preparation; first-bytes measures the first binding on an already compiled schema in a fresh process. strings and bytes include complete 16-byte raw ownership plus ASCII decoding, including NUL storage. text-only decodes an already available span as a counterexample.

cold includes compilation plus first matching; first-runtime compiles the layout outside timing but includes lazy preparation and first execution. preparation times only preparing an already compiled layout. first-prepared prepares outside timing and includes the first execution. Those cases run one call in each fresh process. Repeated library cases perform 50,000 untimed calls before the measured loop. Source request/byte counts exclude warmup. All returned values enter the result checksum; correctness tests independently exercise missing variables, expression limits, codec widths, unknown input and schema identity.

## Client probes

The client's tools/discovery-probe uses the production history, pattern, credential and AES recovery code. Copy identical probe sources to a saved reference checkout, add InternalsVisibleTo DiscoveryProbe to that checkout's Linux Os/Analyses projects, and publish it with ReferenceProbe=true against the released library. Publish the candidate against a unique local package using CStructSharpVersion and RestoreAdditionalProjectSources. Never commit a checkout/feed path. The client README describes these properties.

Use -p:PublishAot=false for JIT and -p:PublishAot=true for AOT. Keep each output directory independent. The reference fixture includes the runtime-adoption working tree that existed before this program; its saved patch and source snapshot are retained with ignored execution evidence, and the client preserves that exact adoption state in local commit 2f99009. A bare older commit without that patch is a different baseline.

history/dense construct 16/4096 complete records in a seeded 1 MiB heap; history-gated/dense-gated and history-reverse/dense-reverse compare complete owned records, including the index build. patterns is a common-first-byte wildcard control. markers includes complete credential result construction. multi-repeated/shared and common-repeated/shared retain exact signature ID and overlapping start pairs. graph-unique/shared and their -valid variants execute production bounded traversal and verify every byte of a planted 64-byte synthetic key. Append -retained for separate forced-GC live-memory sampling; these samples are never timing evidence. retained-index[-dense] observes the retained target index separately.

credential-transport[-sparse] includes the complete physical workflow, actual view/leaf calls, owned findings and coverage. The sparse fixture has one unavailable page and explicitly labels the reference's lost later island. Do not present different coverage as equivalent work. Transport cases warm three calls and measure ten complete operations per process; candidate first pool rent is excluded and labeled. Other client cases warm 100 calls (five for common markers, 30 for graphs).

bootstrap and cancel-bootstrap open an image only when an explicit path is supplied. Use synthetic images unless the user authorized that exact capture. Aggregate symbol hashes and stage counters can be retained; never commit recovered capture contents or credentials. Cancellation reports the actual request timestamp and cooperative response separately from timer scheduling delay.

## Serial collection

Generate configurations by replacing {runtime} in each directory pattern with jit and aot:

```powershell
node benchmarks/experiments/discovery/make-cases.mjs library 'REFERENCE_{runtime}' 'CANDIDATE_{runtime}' artifacts/library-cases.json
pwsh -File benchmarks/experiments/discovery/compare.ps1 -Configuration artifacts/library-cases.json -Output artifacts/library-samples.jsonl -Pairs 5 -Affinity 65536
```

The client, text, transport and byte-preparation kinds generate their corresponding controls. Supply an authorized image as the final optional argument only for client bootstrap/cancellation cases. Use text-control binaries built from the same candidate for the text kind. Change the affinity mask to an allowed logical CPU on your machine and record it; do not reuse another machine's mask blindly.

The runner pins itself and inherited child processes, executes one warmup pair, then five pairs in alternating order. It retains every stdout/stderr/exit code, process-wall time and executable hash, plus hardware/runtime/cache metadata. Retain DLL hashes too for JIT bundles. No build, test, source edit, profiler or other performance command may overlap a campaign. Leave the OS cache intact and disclose that condition.

Interpret workload ns/op separately from process-wall time. Report means and all sample ranges, allocations, retained memory, coverage and source requests. Overlapping ranges or unstable samples are inconclusive. Preparation and borrowed-buffer/captured-result ownership must stay explicit. The root PERFORMANCE.md records supported library findings; the client PERFORMANCE.md records complete discovery results, regressions and rejected alternatives.
