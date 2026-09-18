# Changelog

Notable changes to CStructSharp, newest first. Release versions and dates were reconstructed from the original
Git history preserved before the repository history reset. Entries focus on features, fixes, and migration steps.
Related changes are consolidated; routine formatting and benchmark bookkeeping are omitted.

## Unreleased

- **Breaking (JavaScript, contract v8):** every operation returns one camelCase envelope - `contractVersion` (8),
  `operation`, `success`, `root`, `data`, `debug`, `error` - and a parse's `data` is the selected value itself
  (`result.data.kind`, no `Data.header` wrapper), exactly what C# `Parse` returns; `root` names the root or path
  the operation selected. Debug items are `{ start, end, path, type, value }`, errors are `{ code, message, path,
  offset, member, memberType, line, column }`, and `error.message` is the library's own diagnostic verbatim; the
  new `redactDiagnostics` option keeps only the category text. Tagged values carry a `kind`: `{ kind: "enum",
  enum, name, value, names?, remainder? }`, `{ kind: "union", union, rawStorage, members, selectedMember }`, and
  `{ kind: "pointer", address, depth, dereferenced, value }`. Options are one camelCase object per call: `root`
  replaces `rootTypeName`, and `bitfieldPacking`, `bitfieldAllocation`, `cLongWidth`, `trimFixedText`, and
  `unknownMembers` are exposed. New `resolveAddress(definition, source, path, options)`; `update` accepts any
  binary source; a compiled layout reports its `root` and gains `serialize`, `update`, and `resolveAddress`. The
  canonical TypeScript declarations live in `packages/cstructsharp/index.d.ts` (the ZIP and both apps re-export
  them), `node tools/quality/browser-contract.mjs` replaces `Validate-BrowserContract.ps1`, and the benchmark
  fixture tool writes the same tagged shapes (fixtures re-recorded; the primitive-root fixture now reads through
  `ReadValue`). The Explorer and Inspector apps, the starters, the npm/ZIP consumer checks, and the browser guides
  use the new envelope. The JavaScript fast path formats a float32 with a bisection over the decimal precision
  (2.6-3.6x faster than the exact formatter introduced with the parity fix, same output).
- Compiler comparison fixture: `tools/compiler-fixtures/portable-host-facts.c` now records twenty-two layout shapes
  (the bitfield shapes where compiler families diverge, zero-width separators, a signed bitfield, `uint64`/`double`
  after a byte, native `long`, a large enum, `_Bool`, `#pragma pack(2)` with an array, nested-struct alignment, union
  size, and `#pragma pack(1)` bitfields). `node tools/quality/compiler-fixture.mjs record|validate|table` replaces
  the two PowerShell scripts and also drives `cl`/`clang-cl`; the new `compiler-fixtures` workflow (weekly, manual)
  records GCC and Clang on Linux (x64 and 32-bit), Clang on macOS, and MSVC and clang-cl on Windows. The checked-in
  baseline is GCC 15.2 on Linux x64; `CompilerDifferentialFixtureTests` verifies, for every baseline, that the
  library in the `BitfieldPacking` mode of the baseline's ABI family reproduces every shape byte for byte, and
  `differences-from-c.md` carries a generated "Portable versus real compilers" table.
- **Breaking (behaviour):** `CStructCompilationOptions.BitfieldPacking` chooses how adjacent bitfields of
  different declared sizes share storage, and the default is now the GCC/Clang rule. `BitfieldPacking.SysV` (the
  default) allocates bits contiguously from the struct start - with aligned placement a field joins the run while
  it stays inside one type-aligned cell of its own size, packed placement never splits - so `uint8 a:4; uint16
  b:4;` is two bytes aligned and one byte packed, exactly as `gcc` lays it out. `BitfieldPacking.Msvc` starts a
  new unit of the declared size on every size change and keeps whole units (Visual C++ and dissect.cstruct).
  Previously the library always kept whole units and additionally split on a *type* change, which matched neither
  compiler (`uint16 a:4; int16 b:4;` took two units). An unnamed zero-width declarator, `uint16 : 0;`, is now
  accepted as a storage-unit separator. The bitfields reference documents both rules; `BitfieldPackingTests`
  checks twenty-three shapes against bytes recorded from GCC in both placements. The option is part of the
  compiled-layout cache key; the JavaScript API keeps the default until contract v8 exposes the option.
- **Breaking (API):** `ICustomCodec` is span-based. `OperationStatus Read(ReadOnlySpan<byte> source, out object?
  value, out int bytesConsumed)` sees the bytes from the value's start (the whole remaining input for memory
  sources; a window that grows on `NeedMoreData` for other streams, bounded by `MaxStringBytes`) and reports the
  encoded length; `OperationStatus Write(Span<byte> destination, object value, out int bytesWritten)` fills a
  window and answers `DestinationTooSmall` for a larger one. `InvalidData`, a thrown exception, or an impossible
  byte count become the operation's read or write error naming the codec and the field. The custom-codec recipe
  and the dissect migration guide show the new shape.
- Trimming and Native AOT: the package declares `IsTrimmable` (both targets) and `IsAotCompatible` (.NET 10),
  and publishes with zero trim/AOT warnings. `StructValue` and `UnionValue` implement `IDynamicMetaObjectProvider`
  directly instead of deriving from `DynamicObject` (which requires dynamic code); `dynamic` member access works as
  before, `GetDynamicMemberNames`/`TryGetMember` overrides are gone. Typed reads and POCO writes carry
  `[DynamicallyAccessedMembers]` annotations; nested mapped classes and objects handed to a write are preserved
  by the application (one attribute on the class - see the typed-values guide, "Trimming and Native AOT"). A
  collection-interface member (`IList<T>`) needs a run-time `List<T>` and fails under Native AOT with a message
  naming the `List<T>`/`T[]` declaration to use. `tests/CStructSharp.AotConsumer` publishes with
  `PublishAot=true` and runs the starter, nested POCO reads, `Get<T>`, writes, and diagnostics in CI. The WASM
  bridge no longer suppresses trim-analysis warnings.
- `ReadOptions.TrimFixedText` (default `false`) drops the trailing NUL padding from fixed-capacity text - `char[N]`,
  `wchar[N]`, bounded `utf8 name[N]` buffers, and string tables - so `61 62 00 00` reads as `"ab"`; embedded NULs
  stay and writing still zero-pads. `WriteOptions.UnknownMembers` (`UnknownMemberPolicy.Ignore`, the default, or
  `Reject`) makes a supplied member the struct does not declare fail the write before any byte is written, naming
  the member and the declared ones; it checks dictionaries, `StructValue`s, and .NET objects, nested structs
  included, and applies to `UpdateOptions`. The errors guide gained a "What is not an error" table listing the
  quiet behaviours (trailing bytes, unknown members, NUL padding, unnamed enum values, undereferenced pointers)
  and how to opt into strictness for each.
- `StructValue.Get<T>(path)` / `TryGet<T>(path, out value)` (and the same on `UnionValue`) read one member - or a
  nested value through a dotted, indexed, or pointer path such as `"items[2].tag"` or `"next.value.id"` - with the
  checked conversion `ReadValue<T>` uses, so a parsed struct can stay typed without `dynamic`:
  `header.Get<ushort>("kind")`. A path that selects nothing throws `CStructPathException` naming the failing segment
  and the members that exist. `UnionValue.ToString()` now prints the union name, the selected member, every decoded
  member, and the raw storage length. The README and starters use `StructValue` with `Get<T>`.
- **Breaking (API):** one operation vocabulary for every input kind. Each operation takes the input first
  (`Stream`, `ReadOnlySpan<byte>`, `ReadOnlyMemory<byte>`, or `byte[]`), then the path, then optional `variables`
  and options, and has the same name for every input:

  | Was | Now |
  | --- | --- |
  | `ParseStream`, `ParseMemory`, `Parse` (returning `dynamic`) | `StructValue Parse(input, string? path = null, ...)` - struct roots only; a union or scalar path throws `CStructPathException` pointing to `ReadValue` |
  | `ParseStreamWithDebug` (returning `(List<DebugData>, dynamic)` with a root wrapper) | `ParseResult ParseWithDebug(...)` - `Value` is the same `StructValue` that `Parse` returns, `Debug` the records; deconstructs as `(value, debug)` |
  | - | `ReadResult ReadValueWithDebug(...)` - any selection (union, array, scalar) plus its debug records |
  | `ReadMemoryValue`, `ReadStreamValue`, `ReadValue` | `ReadValue(input, string? path = null, ...)` and `ReadValue<T>` |
  | `TryReadValue(input, out T, path, ...)` | `TryReadValue<T>(input, string? path, out T value, ...)` |
  | `GetDynamicArrayLength` | `GetArrayLength(input, path, ...)` |
  | `ResolveAddress`, `ResolveMemoryAddress` | `ResolveAddress(input, path, ...)` |
  | `SerializeToMemory`, `SerializeToBufferWriter` | `Serialize(Span<byte>, path, value, ...)`, `Serialize(IBufferWriter<byte>, path, value, ...)` |
  | `WriteStream` | `Write(Stream, path, value, ...)` |
  | `UpdateStream` | `Update(Stream, path, value, ...)` and `Update(Span<byte>, path, value, ...)` |

  `CStruct.DefaultRoot` names the first declared struct - the root every read selects when `path` is `null`
  and the name to pass to `Serialize` for a whole record. Debug and non-debug results now have the same shape
  (the declaration-name wrapper around debug results is gone). The generated C# in the Explorer and the docs use
  the new names.
- Browser/WASM: the bridge's curated diagnostics match the library's messages by prefix again, so a truncated
  input reports `Unexpected end of binary input ...` and an oversized array `The array length exceeds
  MaxArrayElements ...` instead of the generic category text.
- Diagnostics: read, write, and path failures now say what failed and where in the message itself -
  `Not enough bytes: needed 4, available 1 (field 'length' (uint32), in 'header', offset 3).`,
  `Value 70000 does not fit: uint16 accepts 0 to 65535 (...)`, `No value was supplied for 'length' (...)`,
  `Unknown root 'Header'. Names are case-sensitive; did you mean 'header'?`,
  `Array length 2147483647 exceeds MaxArrayElements (1000000) (...)`. `CStructException` exposes the innermost
  field as `Member`/`MemberType` next to `Path` and `Offset`; the message is composed from them, so a caller that
  only logs the message sees the same facts.
- Diagnostics: a layout error about a declaration now names the field and its struct and reports the source
  position - `Unknown type 'foo' for field 'z' in struct 'c'. (line 3, column 12)` - through the new
  `CStructLayoutException.Line`/`Column` properties and the message. A multi-word type spelling that starts with a
  known type is reported as a probable missing `;` (`a ';' may be missing after 'kind'`). Duplicate names,
  built-in name collisions, and by-value recursion carry positions as well; parser errors keep their own text.
- Browser/WASM: a float that is NaN or infinite no longer fails the whole parse with `invalid-input`; it arrives
  as the string `"NaN"`, `"Infinity"`, or `"-Infinity"` (the convention already used for integers beyond
  `Number`'s exact range), and `serialize`/`update` accept those strings for float fields.
- Browser/WASM: the JavaScript static plan formats `float32` values with the same tie-to-even shortest round-trip
  rule as the managed projection, so the fast path and the WASM path return identical numbers for every input. A
  differential test in the npm package checks (benchmark fixtures plus seeded random inputs) now guards the two paths.
- Fix: a count, offset, or conditional selector that cannot be evaluated because of the *data* now fails as that
  operation - `CStructReadException` for reads (`CStructWriteException` for writes) - and names the value:
  `Cannot evaluate array length for data: 'n' is 4294967295, which is outside the 32-bit range that layout
  expressions support.` Previously a decoded `uint32` at or above 2^31, any wide `uint64`, or an overflowing
  `a * b` surfaced as `CStructLayoutException` with "Undefined expression identifier" or a bare overflow message.
  `CStructLayoutException` is now raised only for the layout text itself.
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
