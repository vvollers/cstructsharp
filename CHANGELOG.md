# Changelog

All notable changes to CStructSharp are documented here.

## Unreleased

- Browser/WASM: `parse` reads a layout whose members are all statically placed directly in JavaScript. A new
  managed export, `GetStaticPlan`, describes such a root's member offsets, codecs, counts, nested plans and enum
  tables once (the compiler's own static read plan); the adapter then executes each parse as a `DataView` walk
  with no WebAssembly call. Byte inputs up to 64 KiB with layout-only options take this path: a small record
  49 → 2.7 µs, a 256-record nested value 2.2 ms → 0.13 ms, a PNG header 53 → 2.7 µs, with zero managed allocation.
  Values, envelopes, error codes and `parseWithDebug` are unchanged; read limits, pointer settings, cancellation,
  streamed or larger inputs and every other layout use the managed parse as before. The library's compiled POCO
  accessors sit behind a `CStructSharp.CompiledAccessors` feature switch that the bundle turns off, so
  `System.Linq.Expressions` stays out of the publication (4.25 MB).
- Performance: `ReadValue<T>` of a struct whose members are all statically placed fills the POCO straight from the
  bytes instead of parsing to a `StructValue` and mapping it by name: a 256-record nested read into POCOs
  412 → 75 µs (−81 % allocation), a small record 942 → 339 ns. Member resolution (exact name, then a single
  case-insensitive match), conversions, member order, failure messages and paths are unchanged; other layouts and
  member shapes take the previous path. POCO instances are created through a compiled factory, so a constructor
  that throws surfaces its own exception instead of a `TargetInvocationException` wrapper.
- Performance: a runtime-sized array of a struct whose members are all statically placed is read by looping the
  struct's read plan over one span (a 1 024-element array 55 → 28 µs); values, captured counts, limits and
  truncation failures are unchanged.
- Performance: a struct whose members are all statically placed (the same shape the static read plan covers:
  fixed-width numbers, enums, fixed numeric arrays, nested such structs and fixed arrays of them) is serialized by
  the same per-struct plan: member values are looked up once (directly by slot for a parsed value), encoded at
  their compile-time offsets into one block, and written in one call. `Serialize`/`WriteStream` of such structs
  run 2–7× faster (a 256-record nested value 209 → 30 µs with 94 % less allocation; a small record from a parsed
  value 415 → 180 ns) and typed arrays from a parse are copied in bulk. Bytes, budgets, limits, published layout
  variables and every error message are unchanged; the one visible difference is that a value error inside such a
  struct leaves the destination untouched instead of partially written. Structs with `char[N]` buffers, bitfields,
  pointers, unions, conditionals or dynamic arrays keep the field-by-field writer, as do all updates.
- Performance: `UpdateStream` stages a contiguous replacement in one pooled buffer instead of a map of 1 KiB
  chunks, so scalar, array-element, bitfield, pointer-target and union updates run 25–50 % faster with about 25 %
  less allocation; validation-before-commit and the position contract are unchanged.
- Performance: POCO members are read through a compiled accessor cached per type and name instead of reflection on
  every write (`Serialize` from a POCO −30…−37 %). A member getter that throws now surfaces its own exception
  instead of a `TargetInvocationException` wrapper.
- Performance: layout definitions are recognized by a hand-written recursive-descent parser instead of the Pidgin
  parser-combinator grammar. Compiling a layout is 3–7× faster (a 2-field struct 22.6 → 7.5 µs, a 128-field struct
  656 → 97 µs, a 512-field struct 2.8 ms → 0.43 ms, real-format headers 160–230 µs → 26–37 µs) with 8–23 % less
  allocation; in the browser the first compile after startup drops 25–40 % (250 → 160 ms in Node) and each further
  fresh compile 71–85 %. The accepted language and the produced declarations are unchanged - a differential test
  parses every fixture, contract, demo and documentation layout plus thousands of mutations through both parsers -
  with one exception: a `/* */` block comment may now contain a lone `*` (`/* a * b */`), as the documented grammar
  always stated. Syntax errors are still `CStructLayoutException`s starting with
  `Layout definition contains invalid syntax: `, now with a one-line `unexpected … at line L, column C; expected …`
  detail and no inner exception. The library has no runtime package dependencies anymore: the NuGet package and
  the browser bundle (−107 KB raw) no longer include Pidgin, and the npm package no longer ships its license notice.
- **Breaking (browser contract version 7):** a successful `parse`/`parseWithDebug` result carries `Data` as the
  parsed value itself (an object with the selected root wrapper, e.g. `result.Data.header.kind`) instead of JSON
  text to `JSON.parse`. Values, options, error codes and `DebugData` items are unchanged; `serialize`/`update`
  still return a `Uint8Array`. The envelope is now written in one pass without escaping the payload into a
  string and the browser parses it once: a mid-size parse (`nested-x256`) drops from 3.2 ms to about 2.3 ms in
  the interpreter bundle. Replace `JSON.parse(result.Data)` with `result.Data`; TypeScript users get the new
  `ParsedData`/`ParsedValue` types.
- Browser/WASM: the publication is fully trimmed and no longer ships the C# runtime binder (`Microsoft.CSharp`):
  33 → 27 files, 5.35 → 4.36 MB raw, 2.05 → 1.66 MB gzip. The first `parse` call after startup no longer pays
  the binder's ~125 ms initialization (142 → 17 ms in Node); results, options, and the contract are unchanged.
- Performance: a struct whose members are all statically placed (fixed-width numbers, enums, `char[N]` buffers,
  fixed numeric arrays, nested such structs and fixed arrays of them) is read by a per-struct plan built once at
  first use and executed over its bytes, instead of interpreting the declaration per field. Fully fixed layouts
  parse 55–97 % faster (a 1,024-record file 407 → 94 µs; a tar header 18 µs → 0.6 µs) with 20–90 % less
  allocation; layouts mixing fixed and dynamic parts gain for their fixed sub-structs. Values, variables, limits,
  failure positions and debug output are unchanged (debug parses use the general reader).
- **Breaking (managed API):** a one-dimensional array of a fixed-width numeric primitive (`uint8`…`uint64`,
  `int8`…`int64`, `int24`/`uint24`, `bool`, `float32`, `float64`, any byte order) now parses to
  `CStructSharp.PrimitiveArray<T>` instead of a `List<object?>`. It is still the documented `IList<object?>`
  (enumeration, indexing, `Count`, and passing it back to `Serialize`/`UpdateStream` are unchanged; elements can
  be replaced through the indexer) but has a fixed size like a .NET array, and `Span`/`ToArray()` expose the values
  without boxing. Only explicit `List<object?>` casts need to change. Such arrays decode 80–500× faster with
  80–97 % less allocation (a 1 MiB `uint8` array: 59 ms → 0.1 ms; a 16 MiB stream: 780 ms → 2 ms); their JSON
  projection in the browser bridge is written straight from the typed values.
- Performance: a field's value is published as a layout variable only when an expression in the layout (array
  count, condition, switch selector, offset assertion) can name it; every other field skips the per-value
  allocation and dictionary write. Record and nested-struct parses run 20–45 % faster with 20–50 % less
  allocation, writes from parsed values about 25 % faster, updates 10–20 % faster. Expression results are
  unchanged; a text field named by an expression keeps the previous capture-everything behavior because its value
  is itself resolved as a name.
- **Breaking (managed API):** parsed structs are `CStructSharp.StructValue` objects instead of
  `System.Dynamic.ExpandoObject`. `dynamic` member access (`parsed.header.length`), `IDictionary<string, object?>`
  / `IReadOnlyDictionary<string, object?>` casts, enumeration in declaration order, mutation, and passing the value
  back to `Serialize`/`UpdateStream` all keep working; only explicit `ExpandoObject` casts and declarations need to
  change to `StructValue` (or `dynamic`). A `StructValue` shares its member table with every other value parsed
  from the same composite, so nested-struct parses allocate substantially less and run faster (see below).
- Fixed: address resolution and `UpdateStream` measured a pointer-to-struct field by the pointee's size instead
  of the pointer width, so a sibling after `item *p` resolved to the wrong offset and updating a field inside a
  self-referential `node *next` target failed with "Maximum nested struct depth exceeded".
- Performance: arrays of fixed-width numeric primitives (`uint8`…`uint64`, `int8`…`int64`, `bool`, `float32`,
  `float64`, either byte order) are read in blocks and decoded from memory instead of element by element; large
  byte and integer arrays parse 3–5× faster natively and about 8× faster in the browser with half to two-thirds
  less allocation. Values, positions, and captured variables are unchanged; when a `MaxTotalBytesRead` limit is
  exceeded inside such an array, the position reported with the failure may now be up to 64 KiB later.
- Performance: terminated strings (`char[]`, `utf8_string_*`, `unicode_string_*`, `ascii_string_*`) are scanned
  per chunk and decoded once per chunk instead of byte by byte, making string-heavy reads up to 8× faster;
  terminator, budget, and invalid-sequence behavior is unchanged and now pinned by tests. Writing terminated
  strings allocates about 35 % less.
- Performance: compiling a layout no longer builds frozen dictionaries for its symbol tables; small definitions
  compile about 30 % faster and allocate a third less. The tables stay immutable after construction.
- Performance: field paths are parsed without intermediate strings and cached per process, and per-operation
  debug and pointer-cycle bookkeeping is allocated only when used; selected-value reads, address resolution, and
  updates allocate 20–45 % less and run 20–50 % faster on small inputs.
- JavaScript `parse()` and `compile().parse()` now run byte inputs of at most 64 KiB (without a `signal`) directly
  on the calling thread through the new `ParseBytes` managed export instead of a worker round trip, and the worker
  path transfers its input snapshot instead of cloning it (browsers use transferred bytes up to 64 MiB rather
  than a `Blob` read back in pages). Small parses are 2–8× faster; results and cancellation semantics are
  unchanged.
- **Breaking (browser contract version 6):** `DebugData` items returned by `parseWithDebug` no longer carry a
  `Buffer` field (the field's bytes as comma-separated decimal text); use `CurPos`/`EndPos` to slice the input you
  supplied. The parse envelope is now written without indentation directly from the parse result, which makes
  result projection 35–45 % faster and allocates about 80 % less; `Data` is still JSON text with the root wrapper
  and every value is unchanged. The managed `DebugData.Buffer` is now a `byte[]` (previously `int[]`).
- Added `CStruct.GetOrCompile(...)` (same parameters and defaults as the constructor) and `CStruct.ClearCompiledCache()`:
  a process-wide, bounded (64 layouts / 8 M source characters), most-recently-used cache keyed by the definition
  text and every option. The JavaScript/WASM bridge now uses it for every `parse`, `parseWithDebug`, `serialize`,
  and `update` call instead of recompiling the definition each time, which makes repeated public calls on small
  inputs 4–15× faster. Native callers that compile the same text per message can opt in; callers that keep a
  `CStruct` instance are unaffected. A working set larger than the cache (dozens of distinct definitions cycling)
  pays roughly 15 % over uncompiled construction because the retained layouts stay alive across collections.
- Performance: layout-variable capture (the step that lets a parsed or written scalar feed later array-count
  expressions) no longer raises and catches an exception for every value outside the Int32 range or not
  convertible to it; the accept/reject decision and rounding are unchanged. Parsing wide-integer or floating-point
  arrays and serializing POCOs with byte-array members are several times faster (up to 20× on .NET 8) and allocate
  up to 70 % less.
- Added JavaScript `compile(definition, options)` with retained `parse`, `parseWithDebug`, and async `dispose`.
  Compiled handles own a worker/runtime and queue reads; cancellation terminates an active worker and the next
  read recreates it. Ordinary source parsing reuses an idle-expiring worker while source staging remains independent.
  Existing operation signatures and version-5 result envelopes are unchanged.
- Reduced layout compilation, conditional selection/scope, codec recognition and indexed debug-path overhead.
  Primitive metadata is shared immutably; conditional decisions and mutable read/write state remain isolated.
- Temporary browser source cleanup now retries transient file-lock failures after aborting a writer,
  preserving source errors and removing staged files when lock release is delayed.

- **Breaking:** Renamed `UpdateOptions.AllowPointerDereference` to `DereferencePointers`, matching
  `ReadOptions.DereferencePointers` (both already collapsed into the same internal setting). No compatibility
  alias is provided; update any `new UpdateOptions { AllowPointerDereference = ... }` call sites to
  `DereferencePointers`. The WASM browser bridge's option key was renamed identically; use `dereferencePointers`
  for both parse and update requests.
- **Breaking:** `BufferWriterStream` and `FixedBufferStream` now raise `CStructWriteException`/`CStructReadException`
  for internal-contract violations (window-boundary crossings, capacity overflow, a misbehaving `IBufferWriter`)
  instead of plain `IOException`/`InvalidOperationException`. This matches `SparseUpdateStream`'s existing behavior
  and the library's documented design that every expected failure is `CStructException`-derived; catches written
  against the old plain exception types should switch to the `CStructException` hierarchy.
- **Breaking:** Removed Base64 from the WASM interop boundary (browser contract version 5). Binary input now
  crosses as a native `Uint8Array`/`byte[]`, and `Serialize`/`UpdateStream` return the encoded bytes directly
  as a `Uint8Array` instead of a Base64 string inside a JSON envelope; failure
  is reported by throwing rather than through an envelope `Error` field. The public `cstructsharp-wasm.js`
  wrapper (`parseWithDebug`/`serialize`/`update`) and the explorer's internal `cstruct-wasm.ts` layer both
  reconstruct the same `{ContractVersion, Operation, Success, Data, DebugData, Error}` envelope shape from the
  new boundary, so callers of those layers see no shape change; only direct callers of the raw `CStructSharpWasm`
  export object or `RawWasmAdapter` type are affected. `SerializeToBase64`/`UpdateStreamToBase64` are renamed to
  `Serialize`/`UpdateStream` accordingly.
- Added a third GitHub Pages application, the binary inspector (`apps/inspector`, deployed to
  `inspector/`): a desktop-focused, VS Code-style 4-column tool for parsing a real binary file against an
  editable CStruct schema. Ships a 9-entry well-known-format catalog (BMP, WAV, ZIP, PNG, JPG/JFIF, PE
  EXE/DLL, TAR, ICO), each verified against the real managed library by a matching
  `tests/CStructSharpTests/WellKnownFormatFixtures.cs` test. The result panel (`json-editor-vue`) and the hex
  panel (`vuehex`) cross-highlight: selecting a parsed field highlights its byte range, and clicking a byte
  selects its field. Shares the explorer's already-published WASM bundle rather than publishing its own.

## 0.2.12 - 2026-09-07

- Add byte-array overloads for Parse, untyped and typed ReadValue, and TryReadValue. Ordinary array calls
  now compile under C# 12 without span/memory ambiguity. Null arrays throw ArgumentNullException;
  non-null inputs use the existing span implementation without copying. Existing overloads are retained.
- Route Examples to the task catalog, correct dated onboarding/testing guidance, and support matching local
  documentation/explorer links through explicit preview configuration.
- Include public TypeScript declarations with the WASM bundle, with shared option types and distinct JSON-text
  read versus Uint8Array write results. Extracted-package checks compile a strict external TypeScript consumer.
- Fix the mutation-report validation gate so changes to the test count do not cause a false failure.

## 0.2.0-preview

- Completed the consolidated browser release phase. Contract v4 replaces preview positional exports with one bounded
  options object, exposes parse/serialize/update in an editable componentized workbench, preserves exact integer and
  union transport, validates complete success/error envelopes, chunks large Base64 input, and makes bootstrap retry
  script ownership deterministic. A frozen browser-rc1 baseline and 8-test real-browser matrix now block drift.
- Reduced the release WASM publication from 184 files / 24,967,276 raw / 9,365,811 gzip bytes to 32 /
  5,374,392 / 2,031,274. The complete frontend fell from 28,062,847 / 10,576,592 to 5,667,946 / 2,100,267, while
  main JavaScript fell from 621,907 / 161,857 to 283,401 / 66,131. Partial trimming is real-browser tested; eager
  clang-format, Shiki, and vuehex dependencies and the duplicate Vite configuration were removed.
- Integrated Web/WASM, npm audit, browser compatibility/e2e, payload/startup, full-solution, and publication gates
  into candidate production. Every external release action is pinned to an immutable commit, and candidate packaging
  now depends on the Web job. Automation remains candidate-only and performs no publish or release action.
- Hardened the candidate-only release path around an explicit Web-free job graph. Tag/manual metadata validation,
  repository contracts, both framework coverage-risk gates, managed API compatibility, fuzz replay, permanent
  mutation, controlled performance, dependency audit, package metadata/symbols, isolated consumers, package budgets,
  and Source Link must pass before a NuGet candidate is uploaded. The workflow contains no publication action and
  now also requires the independent integrated Web job.
- Raised the core quality gates to the final 1.0 policy: 78% line coverage, 80% branch coverage, zero critical/high
  coverage-risk files, and a 75% permanent mutation floor. The final 34-file non-Web Stryker audit detects all
  3,131/3,131 valid mutants (3,118 killed, 13 timed out, zero survivors/uncovered/runtime errors); a machine validator
  locks the allowlist, thresholds, 562-test inventory, and the explicitly retained Pidgin parser instrumentation
  limitation instead of excluding it.
- Froze the completed 20-type managed release-candidate API. A pinned generator now compares exact 227-line
  signatures on net8/net10 against one reviewed canonical baseline, including defaults, nullability/attributes,
  generic constraints, enum values, init/accessor shape, and assembly metadata. CI rejects accidental additions,
  removals, or signature drift and requires append-only baseline/version-impact history for deliberate changes. The
  browser wire is frozen independently as browser-rc1.
- Added a dependency-free bounded managed fuzz harness for layout definitions, expressions, public paths, binary
  parse/write round trips, and pointer/union graphs. Twenty retained seeds plus 640 deterministic mutations produce
  identical replay digests on both TFMs under strict compilation/read/write limits; undocumented exceptions fail with
  complete replay coordinates. Single-input mode supports reducers/external engines; direct malformed-envelope,
  large-input, and real-browser boundary matrices cover the separate JSON/WASM surface.
- Added reproducible compiler-differential evidence without adding an ABI profile. A strict C11 fixture emits native
  sizes, alignments, offsets, and deterministic byte images; the runner records compiler/version/target/flags and a
  source hash; reviewed Clang 21.1.7 and GCC 14.2.0 Windows observations plus rolling Linux CI artifacts are validated
  against exact Portable fixtures. Native `long` and bitfield differences remain explicit observation-only facts.
- Published the canonical Portable grammar/type/ABI contract. The reference now includes complete EBNF, all 45 fixed
  and terminated primitive spellings, host-C differences, enum/array/string/bitfield/pointer rules, executable
  packed/aligned/nested/union/endian/bitfield/pointer byte images, the exception matrix, and a 17-case valid-C
  unsupported corpus. Portable remains the sole shipped profile; a machine-readable contract and dedicated validator
  keep the documentation synchronized with runtime tests and the feature matrix.
- Finalized the managed result/error contract. `Pointer` now has unambiguous null, unresolved, and dereferenced states;
  address-only construction defaults to unresolved and contradictory address/value/depth/follow combinations are
  rejected. Natural/typed values, unknown enums, untagged unions, debug-data sensitivity, stable error codes,
  path/offset diagnostics, `TryReadValue<T>` recovery, and the deferred final Web/WASM mapping are documented together.
- Added synchronous `ReadOnlySpan<byte>`/`ReadOnlyMemory<byte>` parse and natural/typed read entry points plus
  caller-owned `Span<byte>` and `IBufferWriter<byte>` serialization. The overloads use the existing compiled
  reader/writer, retain no caller buffer, define pointers relative to the supplied/new output region, leave excess
  span capacity unchanged, and return initialized/appended byte counts. Caller-owned output deliberately provides no
  rollback guarantee after writing begins. The Web/WASM bridge remains unchanged until the consolidated final
  integration phase.
- Consolidated root, nested, selected, struct-array-element, and pointer-target reads into one compiled struct
  traversal observed by debug capture. Selected aligned composites now consume their complete runtime extent and tail
  padding without requiring a fixed-size lookup, and a struct used as a union member advances through its child fields
  instead of rewinding each child to the overlapping union address.
- Unified parse, debug, address, dynamic-length, natural/typed-read, selected-read, pointer, and update-address state
  in one per-call operation context. Read/write/update settings are captured before caller callbacks, target location
  and selected consumption share budgets and depths, and pointer-cycle keys are allocation-free value tuples instead
  of composite strings.
- Removed the superseded construction-time layout cache, parsed-field alias walker, and parsed-field sizing engine.
  One recursive compiled-model binder now owns type validation, by-value cycle detection, alignment, fixed/runtime
  sizing, field placement, and immutable symbol publication. The cleanup removes private implementation only;
  `StringPointer` and unused pointer identifiers were already deliberately removed by the earlier public-surface
  cleanup.
- Added natural and strongly typed selected reads through `ReadValue`, `ReadValue<T>`, and position-safe
  `TryReadValue<T>`. Scalar, enum, pointer, array, struct, union, root, nested, array-index, and pointer-accessor paths
  share the compiled semantic resolver, endian/alignment rules, and read budgets. Typed projection supports checked
  numerics, CLR enums, arrays/common list abstractions, nullable values, and mutable POCO properties/fields through
  cached reflection metadata with no serializer dependency. Conversion failures are normalized to
  `CStructReadException` with path context. The shared resolver can now also measure a named terminated string when a
  later selected field follows it.
- Compiled and validated layouts when a `CStruct` instance is created, so unknown types, illegal recursive
  by-value structures, invalid bitfields, and duplicate structure declarations fail before stream access.
- Corrected nested-structure storage, union-array stride, and aligned tail-padding calculations; these rules now
  apply consistently to named and inline nested fields, reading, writing, updates, debug ranges, and public size queries.
- Replaced ambiguous union expandos with an explicit shallowly immutable `UnionValue`. Untagged reads retain the
  complete raw extent plus decoded views without inventing a selected member; raw values round-trip byte-for-byte,
  edited/new writes select one declared member, whole-union legacy objects are rejected, inactive pointer views are
  not dereferenced, and complete union writes are staged before destination submission. Browser contract version 4
  uses the same tagged union shape and Base64 raw storage.
- Normalized expected public failures under one `CStructException` base with stable `CStructErrorCode` values and
  optional safe path/offset context. Layout, path, read, read-limit, write, and write-limit failures now have focused
  subtypes; invalid arguments and stream capabilities remain argument exceptions, while cancellation and unexpected
  defects pass through. Browser contract version 4 returns only stable code, generic message, path, and offset fields
  for failures and does not expose CLR type names, stacks, inner exceptions, or raw diagnostic messages.
- Made `UpdateStream` validation-before-mutation by running the compiled writer once against bounded chunked sparse
  staging. Late binding, collection, conversion, pointer, union, preservation-read, and budget failures now leave
  destination content and length unchanged; updates cannot extend or truncate existing storage. Validated ranges are
  coalesced and committed in address order without flushing. A physical destination failure may retain its committed
  prefix because generic streams cannot guarantee rollback, and a later position-restoration failure no longer hides
  that primary write error.
- Made compilation, read, write, and update options immutable after object initialization. Every public configurable
  property is init-only, so one configured value can be reused while each operation captures its policy without an
  option lock. Object-initializer syntax, property names/types/defaults, and the browser contract remain unchanged;
  callers create another options value to use different policy.
- Made each successfully constructed `CStruct` an immutable, lock-free compiled layout that can serve concurrent
  parse, typed/natural read, debug, address, length, serialize, write, update, pointer, and metadata operations. Construction tables and
  recursive symbols are frozen before publication, while streams and mutable payloads remain caller-owned and require
  exclusive operation access or caller synchronization.
- Preserved the complete signed/unsigned 8/16/32/64-bit enum domain. Enum declarations use bounded exact
  `BigInteger` expressions with backing-width range and implicit-increment checks; compiled members retain compact raw
  bits; and `EnumValueResult` exposes exact value, raw bits, width, signedness, canonical storage type, enum name, and
  optional first matching member. Reads, debug, paths, arrays, pointers, unions, writes, and updates share one checked
  codec. Writer inputs reject coercive/fractional, contradictory, and out-of-domain values with
  `CStructWriteException`; browser values use JSON numbers only inside JavaScript's exact range and invariant decimal
  strings otherwise.
- Unified path traversal around an internal semantic target descriptor shared by address lookup, selected reads,
  dynamic-length lookup, and updates. Indexed updates now write one array element, later bitfield updates preserve
  their resolved bit offset, and multi-level pointer updates use the codec remaining after each `.value` accessor.
- Made selected serialization use an indexed array element's codec and shape, kept resolved pointer targets exact in
  aligned layouts, and checked relative pointer-address conversion before mutating the stream. Relative pointer writes
  now encode explicit null as zero and reject non-null targets whose origin-relative representation would be zero.
- Centralized pointer-address arithmetic across reads, paths, serialization, writes, and updates. Relative addition
  and subtraction are checked, physical targets must be non-negative signed stream positions, stored values must fit
  the configured pointer width, and invalid write values now raise `CStructWriteException` before output instead of
  wrapping, silently encoding a negative target, or leaking conversion/argument exceptions.
- Defined portable bitfields as unsigned slices even with signed backing primitives, preserved independent union-member
  bit offsets, and changed negative, fractional, non-convertible, or oversized writes from truncation/runtime overflow
  to `CStructWriteException` before the selected storage unit is changed.
- Restricted bitfield storage to explicitly registered built-in scalar integral codecs. Strings, user typedefs, enums,
  structs, pointers, arrays, and floating-point names now fail during layout construction, while explicit `<`/`>`
  integer and `wchar` suffixes now control shared-storage byte order consistently across read, write, address, and
  update operations.
- Made neutral `wchar` buffers and pointer targets follow the configured layout byte order; added explicit
  `wchar<`/`wchar>` and UTF-16 string suffixes across scalar, fixed, terminated, pointer, parse, write, and update paths.
  ASCII, UTF-8, and UTF-16 decoding/writing is now strict: malformed data, unpaired surrogates, embedded terminators,
  and lossy narrow-character writes fail explicitly instead of replacing or truncating values.
- Hardened binary reads against partial stream reads.
- Added bounded array, string, total-byte, and nesting-depth read policies, plus explicit stream capability checks.
- Added `CStructReadLimitException` so caller-configured safety-budget failures are distinguishable from truncated or
  otherwise unsafe reads while remaining compatible with existing `CStructReadException` catches.
- Unified read budgets across selected target location and decoding. Address resolution, selected/debug reads,
  dynamic-length lookup, and update-path traversal now enforce array, nesting, pointer, target-size, string, and total
  physical-read limits without resetting counters; traversal-limit query/update failures restore stream position and
  leave update bytes unchanged.
- Added finite write budgets for complete encoded string storage, cumulative physical output/new stream extent, active
  struct/union nesting, and bounded single-pass enumerable materialization. Update operations inherit the output
  budgets independently of their read-side traversal limits.
- Unified definitions, enum values, bit widths, fixed-array counts, and runtime variables behind one cached postfix
  expression evaluator with configurable depth/work limits. Arithmetic and literal signs are checked, shift counts are
  unmasked and restricted to `0..31`, deep inputs fail without unbounded recursion, static definitions are reused, and
  caller overrides invalidate only their transitive dependents.
- Gave every variable-bearing parse, natural/typed read, debug, address, length, serialize, write, and update operation one
  `IReadOnlyDictionary<string, int>` input. Caller values retain precedence, are snapshotted into internal state, and
  are never modified; parser expression nodes no longer leak through the public API.
- Reduced the greenfield managed API to 20 deliberate types. Parser combinators and syntax nodes, raw declaration and
  handler maps, debug syntax stacks, and web bootstrap metadata are internal; the dead `StringPointer`, permissive hex
  parser, pointer-relocation no-op, and expando pretty-printer are removed. `CStruct` and `Pointer` are sealed, and
  Pidgin no longer appears in any public signature. This is an intentional pre-freeze break with no compatibility
  bridge.
- Froze that reviewed 20-type surface as managed release-candidate baseline `managed-rc1` revision 1. A pinned
  PublicApiGenerator comparison now fails CI on any unexplained net8/net10 declaration drift; intentional additions,
  removals, signature/default/nullability/attribute corrections require version-impact review and append-only
  baseline history. Browser-wire compatibility remains deferred to the consolidated Web/WASM phase.
- Added blocking non-Web release budgets for a controlled 14-case BenchmarkDotNet timing/allocation subset and the
  NuGet package/symbol pair. CI requires stable repeated samples, exact reviewed cases, coarse cross-runner timing
  guardrails, tighter allocation caps, and raw/gzip-equivalent artifact limits while retaining full diagnostics.
- Fixed bitfield updates on legal short reads, C-like expression precedence, strict hexadecimal parsing, and removed
  library writes to the process error stream.
- Made pointer reading/writing endian-correct and constrained pointer traversal with bounds, cycle, depth, and size checks.
- Unified null write binding across POCO properties/fields, dictionaries, expandos, selected writes, and browser JSON:
  a null scalar pointer now encodes address zero, while null non-pointer values fail with `CStructWriteException`
  instead of throwing during POCO reflection or being silently converted to numeric zero. Null root structs/unions
  and null collection values fail in the same domain, while selected scalar pointer-array elements retain null-pointer
  encoding.
- Added explicit errors for unresolved expression identifiers, circular definitions, and unsupported expression calls.
- Preserved unknown enum payloads and made update semantics safer for null pointers and unions.
- Rejected duplicate struct/union fields and enum members during layout construction, defined case-sensitive lexical
  member scopes and one global declaration namespace, and reserved built-in codec names so operations cannot resolve
  the same spelling to different declarations.
- Scoped inline and typedef-backing structs by declaration identity instead of globally registering their diagnostic
  names. Unrelated inline fields and backing tags may now reuse spellings, while anonymous inline names no longer leak
  into `CStructElements` or become referenceable field types.
- Added an immutable compiled intermediate representation for operation-time type and layout facts. Typedefs now
  resolve to canonical symbols once; fields carry direct primitive/enum/string codecs, alignment, fixed/runtime array
  and size strategies, offsets/stride, pointer depth, and bit slices; composite fields have immutable lexical indexes.
  Parse, debug, address, dynamic-length, serialize, write, update, and pointer traversal no longer reinterpret the
  legacy declaration/handler maps. Also fixed flexible `wchar<`/`wchar>` array serialization and updates so their
  complete two-byte terminator is emitted.
- Added layout/safety reference material, nullable API annotations, XML documentation, symbols, deterministic package
  builds, and regression/property-style robustness tests.
- Added `net8.0` alongside `net10.0`, MIT/package provenance metadata, Source Link for CI packages, package-content
  validation, cross-platform CI, and automated dependency update configuration.
- Multi-targeted the complete behavior suite across `net8.0` and `net10.0`, split CI results and coverage by runtime,
  and added an isolated installed-package consumer that verifies matching package assets before exercising parse,
  debug, address, serialize, write, update, and pointer operations on both advertised frameworks. Package-only artifact
  manifests now also handle absent frontend-only assets instead of failing.
- Unified the browser parse/serialize/update contract, preserved exact 64-bit JSON values, bound every managed export,
  shared its TypeScript DTOs, and added mock adapter tests plus real Playwright WebAssembly contract tests.
- Added line-coverage and mutation non-regression gates, a tag-only package workflow, symbol-package inspection, and
  remote Source Link verification for release artifacts.
- Raised the preview quality gates to require 70% branch coverage, a 50% mutation score, and zero critical/high
  per-file coverage risks in addition to the existing 78% line floor. Added the public path grammar to permanent
  mutation scope, removed its unreachable generic traversal helpers, covered its complete lexical contract, and fixed
  structurally equal call expressions to produce equal hash codes.
- Added a CI-validated cross-operation feature matrix covering all eight read/write/query entry points, 45 registered
  primitive/string spellings, supported dimensions, known compatibility limits, and deliberately rejected common C
  forms. Generated tests now exercise every fixed primitive in both byte orders plus representative composites,
  arrays, strings, pointers, alignment, and endian overrides. Named terminated-string handlers now also work with
  `GetDynamicArrayLength`.
- Classified semantic-value and canonical-byte round-trip guarantees for every feature row. Added 808 deterministic
  generated trials per target framework across mixed fixed layouts, padding, enums, bitfields, arrays, composites,
  every terminated-string codec, and pointer chains, with stable replay seeds and automatic counterexample shrinking.
  Union round-trip now has executable raw-storage and explicit selected-member guarantees.
- Added a 28-case BenchmarkDotNet suite, normalized performance and allocation results, machine-readable per-file
  coverage risk, clean browser/WASM/package size manifests, a documented local baseline, and non-blocking CI artifact
  retention for comparison between commits.
- Replaced the platform-specific, additive WASM copy commands with one validated cross-platform publisher. Production
  output now contains only the boot-referenced framework resources and three root entry/configuration files, rejects
  stale and non-deployable build artifacts, swaps the destination safely, emits a path/size/SHA-256 manifest, enforces
  a 35 MiB raw limit, and compares Windows/Linux manifests in CI. Removing the demo manifest's build timestamp also
  makes repeated frontend builds byte-for-byte reproducible.
- Routed pointer-target structs and unions through the same compiled composite reader used by selected paths, so every
  union member starts at the pointer target instead of being consumed sequentially. Added a CI-validated inventory for
  all nine confirmed review failures and shared regression fixtures for byte order, partial reads, pointer layouts,
  operation matrices, restored positions, and untouched-stream assertions.
- Repaired the frontend lockfile after the expanded publication toolchain exposed missing optional N-API dependency
  entries, restoring deterministic `npm ci` installs without changing the declared dependency versions.

## Earlier development versions

Prior commits were development snapshots without a published compatibility contract.
