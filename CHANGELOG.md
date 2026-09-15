# Changelog

Notable changes to CStructSharp, newest first. Release versions and dates were reconstructed from the original
Git history preserved before the repository history reset. Entries focus on features, fixes, and migration steps.
Related changes are consolidated; routine formatting and benchmark bookkeeping are omitted.

## 0.4.3 — 2026-09-15

- Highlighted the core .NET library's zero runtime package dependencies in the README and website landing page.
- Added repository-wide agent guidance for source documentation, current-API teaching material, changelog upkeep,
  and focused validation.
- Added centered README badges for license, package versions and sizes, CI status, managed coverage, and test
  totals. Custom badge data and measurement details are hosted on GitHub Pages and refreshed on site/release builds.
- Consolidated API guidance into current-state reading and JavaScript guides, corrected inspector schema coverage
  and contributor procedures, and kept project migration notes in this changelog.

## 0.4.2 — 2026-09-15

- Allowed larger configurable browser read budgets and distant pointers without treating file size or pointer
  distance as decoded work. Added regression coverage for sparse sources and pointers beyond 4 GiB.
- Consolidated the binary inspector's format definitions, extension matching, and teaching samples into one schema
  catalog. Detected schemas are self-contained CStruct definitions, with conditions and stored offsets interpreted
  by the library. Format discovery no longer generates layouts or combines separate parse results.
- Expanded CAB file tables and paths, LHA first-entry names, RAR first-header metadata, LZ4 and Zstandard frame
  fields, ISO primary-volume metadata, MIDI track chunks, and ASF child objects. Compressed payloads remain opaque.
- Simplified inspector component and state responsibilities, grouped related helpers, and clarified source comments.
- Added native C memory-layout teaching material and current-API examples; documented the parser's intentional
  empty-alignment difference from its reference grammar and refreshed the workshop catalog baseline.

## 0.4.1 — 2026-09-14

- Rebuilt and published NuGet, npm, standalone WASM, and website artifacts from the reset repository history.
- Replaced the historical changelog with concise release entries and migration notes. No library behavior changes.

## 0.4.0 — 2026-09-14

### Breaking changes

- Parsed structs now use `StructValue` instead of `ExpandoObject`. Dynamic member access and dictionary access
  remain supported; replace explicit `ExpandoObject` casts with `StructValue` or a dictionary interface.
- One-dimensional fixed-width numeric arrays now use `PrimitiveArray<T>` instead of `List<object?>`. They still
  implement `IList<object?>`, but have a fixed size. Replace explicit list casts; use `Span` or `ToArray()` for
  access to typed elements without boxing.
- Browser contract version 7 returns the parsed object directly in `Data`. Replace `JSON.parse(result.Data)`
  with `result.Data`. Serialization and update results are `Uint8Array` values; remove `atob(result.Data)` from older wrappers.
  Keep the parse root wrapper (`result.Data.header`), but pass only the selected root's fields to serialization.
  Deploy the wrapper, declarations, and runtime assets from the same package or release ZIP.
- Browser debug records no longer include `Buffer`. Use `CurPos` and `EndPos` to select bytes from the original
  input. This change was introduced in contract version 6 and remains in version 7.

### Added and improved

- Added `CStruct.GetOrCompile` and `CStruct.ClearCompiledCache` for bounded compiled-layout caching. The WASM
  bridge also reuses compiled layouts across operations.
- Replaced the Pidgin grammar with a dedicated layout parser, improving compilation speed and removing the
  library's final runtime package dependency. Syntax errors now include line, column, and expected-token details.
- Added optimized read and write plans for fixed layouts, direct typed reads into C# objects, and faster reads
  of runtime-sized arrays whose struct elements have fixed layouts.
- Improved numeric-array decoding, terminated-string scanning, layout-variable capture, object-member access,
  and update staging to reduce operation time and allocations.
- Added a JavaScript parsing path for eligible fixed layouts and byte inputs up to 64 KiB. Other inputs and
  options continue through the managed parser.
- Reduced browser startup and payload overhead through bundle trimming, removal of the runtime binder,
  more efficient JSON projection, and improved small-input and worker handling.
- Added interactive lessons explaining conditional field decisions and variable scope.

### Fixed

- Corrected pointer-to-struct field sizing during address resolution, including sibling offsets and updates
  through self-referential pointers.
- Allowed lone `*` characters inside block comments, as specified by the layout grammar.
- Updated the standalone starter and inspector pages to consume the parsed `Data` object directly.

### Behavior notes

- Errors from compiled C# object getters and constructors now surface the original exception rather than a
  `TargetInvocationException` wrapper.
- Value validation failures in the optimized fixed-struct writer leave the destination untouched.
- A read-budget failure inside a block-decoded numeric array may report a position up to 64 KiB later than before.

## 0.3.4 — 2026-09-12

- Added conditional fields using `if`/`else` and `switch`, with comparison and logical expressions. Reading and
  writing select the active fields; in-place updates cannot change the active storage plan.
- Added 24-bit integers, bounded UTF-8/Latin-1/CP437/UTF-16 text, signed and unsigned LEB128 integers,
  fixed-point numbers, and UUID/GUID fields.
- Added large-input support to the JavaScript/WASM API, including paging and worker-backed reads.
- Added reusable JavaScript compiled layouts with cancellable workers. Fixed cancellation during initialization,
  queued input ownership, and preservation of compiled root defaults.
- Expanded the binary inspector's schemas, file detection, and support for binary metadata types.
- Reduced compilation, conditional-field, primitive-codec, and debug-path overhead.
- Fixed root-union debug paths and byte ranges.

## 0.3.3 — 2026-09-10

- Reorganized the repository into `src`, `apps`, `tests`, `benchmarks`, `contracts`, and focused tooling directories.
- Updated website documentation with npm onboarding instructions.
- Softened the binary inspector's byte-field and selection highlighting.
- Fixed CI project paths and missing Node.js type declarations on clean workshop builds.

## 0.3.2 — 2026-09-10

- Added an npm package with Node.js and browser support, including the JavaScript adapter and WASM assets.
- Fixed managed API baseline checks across package version changes.

## 0.3.1 — 2026-09-09

- Added the binary inspector example app with a dockable workspace, schema-editor completion, and parsed-field
  highlighting in the hex view.
- Fixed whole-array highlighting and selection so clicking an array activates all its elements.
- Redesigned the GitHub Pages landing page and corrected the inspector's browser-test setup for releases.

## 0.3.0 — 2026-09-09

### Breaking changes

- Renamed `UpdateOptions.AllowPointerDereference` to `DereferencePointers`.
- Converted `WriteOptions` and `UpdateOptions` to records, supporting copies with `with` expressions.
- Replaced Base64 payloads at the WASM boundary with native byte-array marshaling. Consumers of the raw bridge
  must use the updated binary contract.
- Normalized buffer-boundary and insufficient-destination failures to `CStructReadException` or
  `CStructWriteException`, replacing the previous `IOException`/`InvalidOperationException` behavior on those paths.

### Added and improved

- Added fixed multidimensional arrays across reading, writing, size queries, and indexed paths.
- Added `bool`, `float32`, and `float64`, with `float` and `double` aliases.
- Added anonymous promoted struct members and unnamed nonzero-width bitfields for padding.
- Added explicit field offsets with `@N` and field/struct/union alignment overrides with `@align(N)`.
- Expanded accepted C-style syntax: tagged type references, anonymous typedef structs/unions, multiple declarators,
  wider integer type spellings, integer literal suffixes, and ignored `const`/`volatile`/`restrict` qualifiers.
- Added `UpdateOptions.MaxTraversalArrayElements` to control update-path traversal independently of the write
  array limit. Use this property where `MaxArrayElements` previously served both purposes.
- Improved fixed-array address lookup, primitive I/O, terminated-string reads, C# object mapping, and union storage
  allocations.

### Fixed

- Corrected root selection for typedef structs and binary marshaling in the browser bridge.
- Corrected a construction-time issue in compiled size queries.

## 0.2.12 — 2026-09-07

- Added explicit `byte[]` overloads for `Parse`, `ReadValue`, `ReadValue<T>`, and `TryReadValue<T>`.
- Strengthened browser integration and TypeScript compatibility checks.

## 0.2.11 — 2026-09-06

- Fixed mobile workbench panels overflowing the viewport when fallback fonts are used.

## 0.2.10 — 2026-09-06

- Stabilized the mobile layout browser check by waiting for the editor to finish resizing. No library API changes.

## 0.2.9 — 2026-09-06

- Expanded onboarding documentation, runnable examples, and interactive workbench lessons.
- Added generated C# and JavaScript examples, editor improvements, and clearer operation controls and results.
- Added standalone browser starter and inspector examples with package-consumer verification.

## 0.2.8 — 2026-09-03

- Added a standalone WASM library bundle for use outside the workshop app.

## 0.2.7 — 2026-09-02

- Fixed duplicate NuGet symbol uploads in the release workflow.

## 0.2.6 — 2026-09-02

- Updated package versions only; no functional changes from 0.2.5.

## 0.2.5 — 2026-09-02

- Switched NuGet releases to trusted publishing and repaired documentation validation in CI.

## 0.2.4 — 2026-09-02

- Adjusted documentation CI checks to tolerate normal build variance.

## 0.2.3 — 2026-09-02

- Corrected published documentation links to use the canonical GitHub Pages URLs.

## 0.2.2 — 2026-09-02

- Corrected GitHub Pages paths used by the project sites.

## 0.2.1 — 2026-09-02

- First tagged release in the preserved history, with automated release artifacts and project-site publication.
- Included the existing .NET 8/.NET 10 binary parsing and writing library: structs, unions, enums, typedefs,
  arrays, strings, bitfields, expressions, and pointers; dynamic and typed reads; serialization and path-based
  updates; stream and memory APIs; configurable operation limits; and reusable compiled layouts.
- Included the browser workbench, documentation, examples, tests, fuzz harness, and benchmarks.
