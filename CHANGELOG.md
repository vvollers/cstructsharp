# Changelog

Notable changes to CStructSharp, newest first. Release versions and dates were reconstructed from the original
Git history preserved before the repository history reset. Entries focus on features, fixes, and migration steps.
Related changes are consolidated; routine formatting and benchmark bookkeeping are omitted.

## Unreleased

- **Breaking (API):** the library source is organized into folders whose names match their namespaces, and the
  public types outside the `CStruct` facade moved with their folders. `CStruct`, `CStructCompilationOptions`,
  `ReadOptions`, `WriteOptions`, `UpdateOptions`, `BitfieldAllocation`, and `StaticHelpers` stay in `CStructSharp`;
  the memory namespaces are unchanged. No member signature or behavior changed. Add the matching `using` directives:

  | Types | New namespace |
  | --- | --- |
  | `StructValue`, `UnionValue`, `EnumValueResult`, `FlagValueResult`, `Pointer`, `PrimitiveArray<T>` | `CStructSharp.Values` |
  | `CStructException` and its subclasses, `CStructErrorCode`, `DebugData` | `CStructSharp.Diagnostics` |
  | `LayoutInfo`, `LayoutDeclarationInfo`, `LayoutFieldInfo`, `LayoutEnumMemberInfo`, `LayoutConstant`, and their kind enums | `CStructSharp.Introspection` |
  | `ICustomCodec` | `CStructSharp.Codecs` |

  Internal code moved into `Syntax` (formerly `Structure`), `Parsing`, `Expressions`, `Compilation`, `Codecs`,
  `Streams`, `Addressing`, `Reading`, and `Writing`; `src/CStructSharp/README.md` maps every folder to its role.

## 0.5.0 — 2026-09-17

- Memory: new `CStructSharp.Memory` namespace for analyzing memory images with unsigned 64-bit addresses.
  `IMemorySource`/`IWritableMemorySource` define caller-owned address spaces, with `ByteArrayMemorySource`,
  `StreamMemorySource`, `MappedMemorySource` (logical-to-backing translation with reads split across mappings),
  `CachedMemorySource` (generation-invalidated exact-range cache), and `OverlayMemorySource` (copy-on-write edits
  over an unchanged image). `MemorySchema` takes explicit sizes, offsets, and bit slices from metadata rather than
  inferring a host ABI; `PortableMemorySchema.Create` projects a fixed compiled layout, and
  `CStructSharp.Memory.Metadata` imports BTF v1 (including split tables) and Volatility ISF 6.2.0 into schemas.
  `MemorySession` resolves, reads, inspects (with backing-range provenance), serializes, and plans updates; pointers
  are returned as `StoredPointer` bits and followed only through explicit `.value` paths via a caller resolver.
  `MemoryWalker` walks sentinel lists and trees with node limits and identity tracking; `MemoryPatch` previews
  physical fragments, validates generations and expected bytes before writing, and reports uncertain fragments
  through `MemoryPatchCommitException`. `MemoryAccessContext` bounds bytes, requests, depth, and cancellation
  across all layers, and `MemoryAccessException` carries stable `MemoryFailure` categories. The `contracts/memory`
  contract, a package-consumer test, a mutation-testing configuration, and benchmarks accompany the namespace.
- Documentation: add a worked memory-analysis guide series with executable examples for address translation,
  metadata import, pointer traversal, offline patches, budgets, and caching. The overview introduces memory images,
  address spaces, and the layered object model before the API; each guide explains its concepts before showing
  syntax and ends with exercises and answers. The memory source files carry expanded XML documentation and
  algorithm comments, including the BTF record layout and the compiled-view construction in `MemorySchema`.
- Fix directive whitespace handling so non-breaking spaces after `#define` agree with ordinary layout whitespace;
  physical CR/LF line boundaries remain significant.
- Language: the C, C99, Windows SDK, Linux kernel, IDA, and dissect primitive spellings (`DWORD`, `BYTE`, `WCHAR`,
  `__u32`, `u8`, `wchar_t`, `unsigned __int64`, `uleb128`, ...) are built in and resolve to their canonical codecs
  at construction time; a compiled field never sees the alias, so nothing changes at read or write time. Aliases of
  one codec now share a bitfield storage unit (`DWORD a : 4; unsigned int b : 4;` is one unit, as in C), an enum's
  backing type and a bitfield's storage may be any accepted integer spelling or a typedef of one, and `size_t`,
  `ssize_t`, `intptr_t`, `uintptr_t`, `ptrdiff_t`, and their Windows spellings are as wide as the layout's pointer
  size. A layout may redeclare an alias spelling (`typedef uint16 DWORD;`) and its declaration wins.
  `tools/documentation/sync-primitive-spellings.mjs` regenerates the contract, matrix, and documentation views of
  the one source table. Debug records and `EnumValueResult.StorageType` now report the canonical name for a field
  declared with an alias (`short` reads as `int16`).
- Language: `CStructCompilationOptions.CLongWidth` selects the width of the C `long` family (64 by default, the
  LP64 reading; 32 for ILP32/LLP64 headers and dissect definitions). Windows `LONG`/`ULONG` are always 32 bits.
- Language: typedef forms from real headers - `typedef struct _X { ... } X, *PX;` declarator lists, a typedef body
  with no alias (`typedef struct NAME { ... };`), the tag of a tagged typedef usable as a type (a duplicate tag is
  now an error, as in C), `typedef struct tag alias;`, `typedef T name[N];` array typedefs, multi-word plain typedefs
  (`typedef unsigned long long ticks;`), and `typedef uint8 byte_t, *pbyte_t;`.
- Language: top-level `struct { ... } name;` declares `name` as the type, `struct X { ... } variable;` declares `X`
  and ignores the variable, and a forward declaration (`struct node;`) is accepted.
- Language: preprocessor lines - `#define` with a text, byte (`b"..."`), bare, or function-like value (published on
  the new `CStruct.Constants` as `LayoutConstant`), `#undef`, `#include` (recorded on `CStruct.Includes`, never
  read), `#pragma pack(push|pop|N)` as a composite alignment clamp, other pragmas ignored, and
  `#ifdef`/`#ifndef`/`#else`/`#endif` over defined names plus `CStructCompilationOptions.Defined`. A backslash
  before a line end joins the lines anywhere.
- Language: an enum member name may start with, or consist of, digits (`32BIT_MACHINE`, `0 = 0x30`), the comma
  between members is optional (members separated by line breaks alone are unambiguous), and a body may end with a
  trailing comma. `typedef enum|flag [Tag] [: type] { ... } Alias [, *Alias2];` and `typedef enum Tag Alias;`
  mirror the struct typedef forms.
- **Breaking (language):** an enum or flag declared without a backing type is 32 bits wide - `uint32`, or `int32`
  when a member is written as a negative number (the rule C compilers apply; dissect.cstruct assumes `uint32`) -
  instead of one unsigned byte. `CStructCompilationOptions.DefaultEnumStorage` pins any spelling (`"byte"` restores
  the old default) and is part of the compiled-layout cache key.
- **Breaking (language):** a field named `_` is unnamed padding, like an anonymous bitfield: read and skipped,
  written as zeroes, absent from results, and free to repeat (`uint32 _; uint64 base; uint32 _;`). It must be a
  fixed-size primitive or primitive array.
- Language: a tagged inline body (`struct gen { ... } gen;`, `union version_information { ... };` inside another
  body) declares its tag as a global type, as C and dissect do; without a member name the body is promoted.
- Language: `#define` keeps a value that is not an integer expression as a text constant instead of failing, a
  definition beyond the 32-bit domain (`(1 << 63)`) is published on `CStruct.Constants` with its exact value and
  fails only where a count selects it, a definition naming an unknown identifier fails only when used, and a
  quoted literal may span lines. Keywords are recognized only as whole tokens (`structX` is an identifier) and the
  semicolon after `typedef struct NAME { ... }` is optional before the next declaration.
- Language: `PSTR`/`LPSTR`/`PCSTR`/`LPCSTR` (`char *`), `PWSTR`/`LPWSTR`/`PCWSTR`/`LPCWSTR` (`wchar *`), and
  `time_t`/`off_t` (the `long` family) are built in.
- Language: inline unions inside structs (`union { ... } u;`), inline structs and unions inside unions, and
  anonymous unions whose members (and the members of anonymous structs inside them) are promoted into the
  containing struct - the NTFS/PE header shape. A promoted union reads as its decoded member values and writes back
  through the widest member the data supplies, clearing the rest of the extent.
- Language: `flag` declarations (bitmask enums: an omitted value is the next unused bit) read as
  `FlagValueResult` - an `EnumValueResult` with `Names`, `Remainder`, and `Has(name)` - and write from a result,
  a member name, `"A|B"`, a name sequence, or a number; typed reads map to `[Flags]` CLR enums. Enums and flags
  may be bitfield storage (`kind type : 2;`), sharing the storage unit of their backing type. An anonymous
  `enum { ... };` or `flag { ... };` declares constants, as in C.
- Language: data-sized arrays - `T values[EOF]` reads every whole element to the end of the input and
  `T values[]` on a non-character type reads until an all-zero element (consumed) - through parse, debug, address,
  length, serialize, write, and update.
- Language: expressions gain `%`, `^`, the conditional operator `c ? a : b` (only the selected arm is evaluated),
  `sizeof(type)` and `offsetof(type, field)` in array dimensions (folded at construction), qualified enum
  members (`kind.Max`) as constants, and dotted references to a nested struct's field (`uint8 v[hdr.n]`,
  `a.b.n`) whose value is published under the path while the struct field is read, written, or measured.
- Language: `int48`/`uint48`, `int128`/`uint128` (with `OWORD`, `__int128`, `u128` spellings; `Int128`/`UInt128`
  results), `float16` (`Half`), `void *` (and `PVOID`/`LPVOID`/`HANDLE`) as an opaque pointer that is never
  dereferenced, and function-pointer declarators (`uint8 (*callback)(uint8)`) stored the same way.
- Browser/WASM: a flag value adds `Names` and `Remainder` to the enum envelope; wide integers render like
  `uint64` (safe integers or decimal strings). Structs containing a flag or a wide primitive take the managed
  parse path, never the JavaScript static plan.
- Contract: `contracts/language/portable-v1.json` revision 2 - alias spellings move to `aliasSpellings`,
  `include-directive`, `packing-pragma`, `forward-declaration`, `typedef-array`, `typedef-tag-alias`,
  `inline-union`, `general-flexible-array`, and `function-pointer` are no longer rejected constructs, and new
  rejected constructs document the remaining limits.
- Tests: `ParserDifferentialTests` is a one-way oracle - everything the frozen Pidgin reference grammar accepts must
  parse identically, while the extended language may accept what the reference rejects.

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
