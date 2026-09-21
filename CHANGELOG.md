# Changelog

Notable changes to CStructSharp, newest first. Release versions and dates were reconstructed from the original
Git history preserved before the repository history reset. Entries focus on features, fixes, and migration steps.
Related changes are consolidated; routine formatting and benchmark bookkeeping are omitted.

## Unreleased

### Added

- `ReadCursor.BufferStreamAsync`, the awaitable form of the buffering every stream form that runs the span reader
  uses (a seekable stream up to its remaining length, any stream up to `MaxTotalBytesRead`, plus one byte so a
  value larger than the budget reports the budget failure rather than a short read); the generated `Parse(Stream)`
  and the runtime's async operations share the one implementation (`Streams/AsyncStreamBuffer`).
- `ReadOnlySequence<byte>` overloads of `Parse`, `ParseWithDebug`, `ReadValue`, `ReadValue<T>`, `ReadValueWithDebug`,
  `TryReadValue<T>`, `ResolveAddress`, and `GetArrayLength`, and generated `Parse<Name>`/`Parse` overloads, for input
  that arrives in segments (a `PipeReader`'s buffer): a single-segment sequence is read in place with the span
  path's cost; a multi-segment one is copied into a pooled buffer bounded by `MaxTotalBytesRead` plus one byte, so
  a sequence longer than the budget fails with the budget text. `ReadCursor.CopySequence` is the copy the runtime
  and generated code share. Coordinates are zero-based at the sequence's start.
- `ReadOptions.CancellationToken`, `WriteOptions.CancellationToken`, and (through `WriteOptions`)
  `UpdateOptions.CancellationToken`: a read observes the token when it enters a struct or union, follows a pointer,
  starts a 64 KiB block of a numeric array, an element of an array of structs, or a 256-byte chunk of a terminated
  string; a write when it enters a composite or an element of a composite array. Cancellation is an
  `OperationCanceledException` - never a `CStructException`, never `false` from `TryReadValue<T>` (which restores the
  stream position and rethrows). An update stages before it commits, so a cancelled update leaves the destination
  unchanged. Generated readers and writers observe the same token through `ReadCursor`/`WriteCursor`.

### Behaviour

- `ReadOptions` and `CStructCompilationOptions` are `sealed record`s, as `WriteOptions` and `UpdateOptions` already
  were: `options with { TrimFixedText = true }` copies every other member, and two option instances with the same
  members are equal (the collection members `Codecs` and `Defined` compare by reference). No signature changed.

## 0.7.0 — 2026-09-21

### Breaking changes

- **Breaking (API):** reflection-based mapping is gone. `ReadValue<T>`, `TryReadValue<T>`, `Get<T>`, and
  `TryGet<T>` map a struct to a class only when the class implements `ICStructMapped<T>` (a static `ReadFrom` and
  `WriteTo`) and is registered with `MappedTypes.Register<T>()` - which the `[CStructMapped]` source generator does
  for a `partial` class, and a hand-written class does from a module initializer. `Serialize`, `Write`, and
  `Update` accept a `StructValue`/`UnionValue`, a string-keyed dictionary (expando objects included), or a registered
  mapped class; anonymous objects and plain classes are rejected with a message that says what is writable.
  `PocoBindingMode`, `WriteOptions.BindingMode`, and `UpdateOptions.BindingMode` are removed, as are the
  `[DynamicallyAccessedMembers]` annotations on the typed-read generic parameters and the
  `CStructSharp.CompiledAccessors` feature switch. Collection-interface and `List<T>` targets are no longer
  converted: `Get<T>` hands out arrays (`T[]`), and a mapper copies them into whatever collection it wants. A
  failure inside a mapper is reported at the member's full path (`root.leaves[0].v`). The browser API's
  `bindingMode` option, which never had an observable effect through JavaScript, is removed from the option
  list.

### Added

- The `CStructSharp.Generators` source generator ships inside the package (`analyzers/dotnet/cs`, with
  `build/CStructSharp.props`/`.targets` and `THIRD-PARTY-NOTICES.md`; no new package and no runtime dependency):
  `[CStructLayout]` on a
  `static partial` class compiles the layout at build time with the same compiler the runtime uses and emits the
  layout's types and operations as C#; the runtime gains `CStructLayoutAttribute`, `CStructMappedAttribute`,
  `CStructMemberAttribute`, and `ICStructGenerated<TSelf>`. Diagnostics CSG001-CSG006 and CSG010 report a layout
  that does not compile (located inside a raw string literal at the runtime's line and column), a missing
  `.cstruct` additional file, a name collision after PascalCase conversion, an unknown `Root`, a class that is not
  `static partial`, an invalid custom codec declaration, and a C# language version below 12.
- `CStructSharp.Generated`, the runtime support the `[CStructLayout]` generator's output calls (an advanced
  surface; application code keeps using `CStruct` or a generated layout class): `Codec` - every byte-level rule
  as a span function (fixed-width integers in both byte orders, the 24- and 48-bit integers with their range
  checks, LEB128, fixed point, identifiers, bitfield slices, primitive arrays, bounded and fixed text, terminator
  search); `ReadCursor` and `WriteCursor` - position, options snapshot, the total byte budget, array/string
  limits, nesting and pointer depth, and failures carrying path and offset; `Pointer<T>`; `Expressions` - the
  layout expression operators. The runtime reader and writer now call the same `Codec` functions and the same
  diagnostic texts (`ReadFailures`/`WriteFailures`), so the two paths cannot drift; a cursor failure carries the
  runtime's context (the innermost field and its type, the operation path, the offset), and `FailExpression`/
  `FailUnwritable` reproduce the `Cannot evaluate ...` and `Value ... does not fit` texts.
- Generated readers: a `[CStructLayout]` class gets `Parse<Name>(ReadOnlySpan<byte> | byte[] | ReadOnlyMemory<byte>,
  variables, options)` for every named struct and union plus the root's plain `Parse`, reading straight into the
  generated classes with no `StructValue` in between - fixed and runtime-sized arrays (`[n]`, `[expr]`, `[]`,
  `[EOF]`, terminated), character arrays and tables, bounded and terminated text, bitfields with both packing
  rules, unions, promoted composites, offset assertions, and pointers to any depth with the runtime's addressing
  modes, cycle check, and target budget. Every failure carries the runtime's message, field, path, and offset:
  a parity suite in the generator tests parses every language-contract and benchmark fixture through both paths
  and compares the values member by member and every truncated prefix's exception. `ReadCursor` gains the
  per-kind `Take*` methods, `Seek`, union and composite bookkeeping, and `Complete`; `CompositeCursor` is the
  field placement rule (alignment, bitfield packing and allocation) as a struct the generated code drives.
- Generated conditionals: `if`/`else` and `switch` groups are emitted as C# with the runtime's rules - a group is
  decided once per instance when its first field is reached, an inner group is never evaluated while its outer
  arm is inactive, and a conditional composite's own member names hide caller and outer values until the member is
  read. A conditional member gets a `Has<Member>` flag; an unselected arm leaves it `false` with the property
  `null` (reference types) or default. Caller variables override the layout's defines, and a define that depends
  on an overridden name is re-evaluated where it is used, as at runtime. `Expressions` gains `Variable(variables,
  name, value)`, `TryVariable`, `Undefined`, and `Overflow` for these rules.
- Generated custom codecs: `[CStructLayout(Codecs = new[] { "blob", "rgb:3:1" })]` declares each `ICustomCodec`'s
  name, fixed size (`*` for variable length), and alignment, which is what the compiler places fields with; the
  class supplies the instances from `static partial IReadOnlyList<ICustomCodec> CreateCodecs()`, checked against
  the declaration on first use. The generated reader hands each value to the codec as the runtime's memory path
  does (`ReadCursor.TakeCustom`): the whole remaining input as the window, the consumed bytes charged to the
  budget, and the runtime's texts for a short read, a rejected value, a codec that throws, or an impossible length.
- Generated writers and operations: `Serialize<Name>(value)` → `byte[]`, `Serialize<Name>(value, Span<byte>)`,
  and `Write<Name>(Stream, value)` per composite plus the root's plain `Serialize`/`Write`, at parity with the
  runtime writer on every fixture (the same bytes for a value both readers produced; the same validation texts
  for a fixed array of the wrong length, a string past its buffer, a value outside its type's range, a bitfield
  past its width, a terminator inside a string, an inactive conditional arm supplied, a union without
  `SelectedMember` or `RawStorage`; and a destination too small fails with the runtime's capacity text). The root
  class implements `ICStructGenerated<Root>`; `Sizes.<Composite>` and `Offsets.<Member>` constants and typed
  `Update.<Member>(Span<byte>, value)` setters (nested by struct member; fixed arrays take an index) cover every
  statically placed scalar; `ParseWithDebug`, `ResolveAddress`, `GetArrayLength`, and `UpdatePath` run on the
  runtime layout with its path grammar. `WriteCursor` gains a growable mode and the per-kind `Write*` helpers.
- Generated views and stream parsing: `readonly ref struct <Name>View` per composite (`Views = false` skips them)
  decodes every statically placed scalar, bitfield, enum, pointer address, fixed numeric array element, and fixed
  text straight from the span with no allocation, exposes a nested static struct as a nested view and the value's
  `Bytes`, and reaches everything else through `ToObject()`; a source shorter than the value fails with the
  runtime's short-read text. `Parse<Name>(Stream)` buffers the stream up to the total read budget (or its remaining
  length) and takes the span path, leaving a seekable stream after the value - unlike the runtime's incremental
  stream reads, a non-seekable stream is consumed up to that budget.
- Layout files: `[CStructLayout(File = "layouts/png.cstruct")]` reads the layout from an `AdditionalFiles` entry;
  the package's `build/CStructSharp.props` and `.targets` (shipped from 0.7.0) add every `**/*.cstruct` file of a
  project as an additional file marked `CStructSharpLayout="true"` (a file with another extension can be marked
  the same way), and `<DisableCStructSharpGenerator>true</DisableCStructSharpGenerator>` keeps the runtime and skips
  generation. A class named like a generated member (`Layout`, `Parse`, `Sizes`, ...) is reported as CSG003.
- `[CStructMapped]` generator: a `partial` class or struct with public settable properties gains
  `ICStructMapped<TSelf>` - `ReadFrom` assigns every property by name with the conversions of `Get<T>` (scalars,
  strings, enums by value, nested mapped classes, arrays, `List<T>`/`IList<T>`/`ICollection<T>`, read-only
  collections, `Pointer<T>`, `StructValue`/`UnionValue`/`Pointer`/`object`, and `T?` for a member a conditional arm
  may leave out), `WriteTo` copies them back, and a module initializer registers the type. Names match the layout
  member exactly, then case-insensitively, then ignoring underscores (`bit_depth` ↔ `BitDepth`); `[CStructMember]`
  names one explicitly, and `[CStructMapped(Layout = "root")]` resolves the names at build time against a
  `[CStructLayout]` of the same compilation (CSG102 warns about a property without a counterpart). CSG100 (not
  `partial`, or no parameterless constructor) and CSG101 (a property whose class is not mapped) are errors. A CLR
  enum value is accepted wherever a layout enum is written (matched by value).
- The mapped bridge: `StructValue.ToMapped<T>()` maps a parsed struct through a mapped class's `ReadFrom`; a
  generated layout class gains `ToStructValue(value)` (the value's bytes parsed by the runtime layout; pointers keep
  their addresses and are not followed), `ToMapped<T>(value)`, `ParseMapped<T>(bytes)`, and
  `SerializeMapped<T>(value)` for any `ICStructMapped<T>` class over the same layout.
- Analyzer (in the same package): CSG200 warns when a constant path in a `CStruct` call (`Parse`, `ReadValue`,
  `ResolveAddress`, `GetArrayLength`, `Serialize`, `Write`, `Update`, ...) names a declaration or member the
  receiver's layout does not have - the layout is resolved only when it is plainly visible (`new CStruct("...")`
  or `CStruct.GetOrCompile("...")` initializing the local or field the call uses, an inline construction, or a
  `[CStructLayout]` class's `Layout`), and stays silent otherwise; CSG201 (info) notes `Parse` on a union or scalar
  root; CSG300 warns about `dynamic` over a `StructValue`/`UnionValue` in a project that publishes trimmed or AOT.

### Fixed

- `CStruct.Update(Span<byte>, path, value)` zeroed every byte of the region except the updated field: the fixed
  buffer stream it wrote through started with a zero length and cleared the rest when its length was set. The
  span form now keeps the region's bytes, like the stream form (`MemoryIoTests.SpanUpdate_KeepsTheOtherBytesOfTheRegion`).

### Performance

- Generated code against the runtime on the same bytes (Short job, this devbox, `GeneratedBenchmarks`; the
  [performance page](docs/guides/performance.md#typical-costs) has the full table): the 28-byte primitives record
  parses in 33 ns / 48 B generated against 249 ns / 776 B at run time, and a generated view reads every member in
  1.6 ns with no allocation (hand-written `BinaryPrimitives`: 1.9 ns); 256 nested records parse in 38.8 µs / 124 KiB
  against 78.2 µs / 243 KiB, and the view walks them in 738 ns; the record serializes in 79 ns / 168 B against
  188 ns / 768 B; a typed setter replaces one field in 16 ns / 112 B against 704 ns / 2,224 B for the path update;
  a conditional record with 128 arms parses in 1.7 µs / 5 KiB against 48 µs / 69 KiB; the pointer graph in 170 ns
  against 674 ns. `ParseWithDebug` on a generated class costs a runtime read on top (920 ns against 731 ns).
- Runtime paths against 0.6.0 (two-round A/B, allocations first): `ReadValue<T>` into a mapped class −31 % time
  and −35 % allocations (the hand-written or generated mapper over `StructValue` replaces the expression-tree plan),
  selected typed reads −7…−23 %, address resolution −5…−9 % allocations, every `Update` −5…−8 % allocations,
  `Parse` of a 16 MiB stream −15 %, dictionary serialization −11 %, compile allocations −3…−4 %; a mapped-class
  write allocates +192 B (the value is materialized as a `StructValue` before the one write path; the reflection
  binder read properties in place). The WASM publication shrinks from 4,829,857 to 4,338,990 bytes (−10.2 %) and
  the package grows from 689,036 to 909,142 bytes with the generator inside.

### Internal

- Runtime allocation trims, each measured: a plain parse no longer allocates its unused debug list (−32 B per
  operation); an address resolution keeps the traversal's own arrays instead of copying them twice (−150…−180 B,
  `ResolveFixedNestedArray` −9 %); `Get<T>` of a `T[]` copies a parsed primitive array's typed storage directly
  and formats an element's path only when a conversion fails, and a parsed list is converted in place instead of
  being copied first (`ReadValue<T>` of a small root −36 % allocations, −27 % time against 0.6.0).
- The primitive vocabulary is split into a compile-time catalog (`PrimitiveCatalog`: names, aliases, alignments,
  sizes, symbols, and a codec id per readable name) and a runtime delegate table (`CodecTable`, indexed by codec
  id). The compiled model carries ids instead of reader/writer delegates, so it can compile without any I/O - the
  prerequisite for hosting the layout compiler inside the source generator. Per-field reads index an array
  instead of holding a delegate; measured neutral (allocations identical, timings inside the noise floor).

### Documentation and tooling

- Documentation: a twelve-lesson [generated code series](docs/guides/generated/index.md) (what a source generator
  is and where the files live, the first generated layout, views and zero allocation, arrays/strings/enums,
  unions/bitfields/nested structs, pointers and budgets, conditionals, writing and updating, mapped classes, the
  diagnostics catalogue, how the generator works, and a runtime-or-generated decision table), each with a
  "check yourself" and an exercise; every code block runs in `docs/examples/GeneratedExamples.cs`. The
  diagnostics page's table is generated from `AnalyzerReleases.Unshipped.md` by
  `tools/documentation/validate-generator-diagnostics.mjs`, which the documentation gate runs with `--check`.
  [Typed values](docs/guides/typed-values.md) is written around `[CStructMapped]`,
  [trimming and Native AOT](docs/guides/trimming-and-native-aot.md) covers generated layouts, and the reading,
  choosing-an-api, install, performance, language, project (architecture, repository map, testing with the
  snapshot and parity workflow), and README pages point at the generated path. Three generated recipes
  (`generated-first-layout`, `generated-views`, `generated-mapped-classes`) join the exported set (36), and the
  starter gains `Generated.cs`.
- Documentation: every C# example, recipe, and guide snippet reads results as `StructValue` with `Get<T>` instead
  of `dynamic`; [Read values and paths](docs/guides/reading-values.md) gained a "Dynamic access" section that
  states what `dynamic` trades away, and a new guide, [Trimming and Native AOT](docs/guides/trimming-and-native-aot.md),
  covers a trimmed or AOT publish end to end: the package's claims per target, how mapped classes take part
  without reflection (and why they register from a module initializer), and the fact that `dynamic` is JIT-only (the C# runtime binder needs
  runtime code generation - `IL2026`/`IL3050` at publish, a binder failure if suppressed). The README and the
  typed-values guide link to it.
- The release workflow's npm publication check polls the registry (every 10 s, up to five minutes) instead of
  looking once: npm now processes an upload asynchronously, and the 0.6.0 release needed a recovery run because
  the version became visible about a minute after the publish.

## 0.6.0 — 2026-09-19

### Breaking changes

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
- **Breaking (API):** `ICustomCodec` is span-based. `OperationStatus Read(ReadOnlySpan<byte> source, out object?
  value, out int bytesConsumed)` sees the bytes from the value's start (the whole remaining input for memory
  sources; a window that grows on `NeedMoreData` for other streams, bounded by `MaxStringBytes`) and reports the
  encoded length; `OperationStatus Write(Span<byte> destination, object value, out int bytesWritten)` fills a
  window and answers `DestinationTooSmall` for a larger one. `InvalidData`, a thrown exception, or an impossible
  byte count become the operation's read or write error naming the codec and the field. The custom-codec recipe
  and the dissect migration guide show the new shape.
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
  compiled-layout cache key and is exposed to JavaScript as `bitfieldPacking`.
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
- **Breaking (repository layout):** the lesson app is the **Explorer** everywhere: `apps/workshop` is now
  `apps/explorer` (package `cstructsharp-explorer`; the inspector package is `cstructsharp-inspector`), its
  operation form is the "Operation panel" (`OperationPanel.vue`, "Operation settings"), and every workflow,
  script, contract, and guide points at the new path.

### Added and improved

- `StructValue.Get<T>(path)` / `TryGet<T>(path, out value)` (and the same on `UnionValue`) read one member - or a
  nested value through a dotted, indexed, or pointer path such as `"items[2].tag"` or `"next.value.id"` - with the
  checked conversion `ReadValue<T>` uses, so a parsed struct can stay typed without `dynamic`:
  `header.Get<ushort>("kind")`. A path that selects nothing throws `CStructPathException` naming the failing segment
  and the members that exist. `UnionValue.ToString()` now prints the union name, the selected member, every decoded
  member, and the raw storage length. The README and starters use `StructValue` with `Get<T>`.
- `ReadOptions.TrimFixedText` (default `false`) drops the trailing NUL padding from fixed-capacity text - `char[N]`,
  `wchar[N]`, bounded `utf8 name[N]` buffers, and string tables - so `61 62 00 00` reads as `"ab"`; embedded NULs
  stay and writing still zero-pads. `WriteOptions.UnknownMembers` (`UnknownMemberPolicy.Ignore`, the default, or
  `Reject`) makes a supplied member the struct does not declare fail the write before any byte is written, naming
  the member and the declared ones; it checks dictionaries, `StructValue`s, and .NET objects, nested structs
  included, and applies to `UpdateOptions`. The errors guide gained a "What is not an error" table listing the
  quiet behaviours (trailing bytes, unknown members, NUL padding, unnamed enum values, undereferenced pointers)
  and how to opt into strictness for each.
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
- Performance: `ReadValue`/`ReadValue<T>` of a runtime-sized root (a struct with a `count`-sized or otherwise
  data-dependent member) no longer throws and catches three layout exceptions per call while resolving the root's
  extent: the compiled type already knows it has none. The typed read into a POCO drops from about 8 µs to about
  1.3 µs and allocates 45 % less; the untyped `ReadValue(bytes, "root")` gains the same. The performance guide
  has a "Typical costs" section with measured medians and allocations for thirteen managed operations, the
  JavaScript fast path against a WebAssembly call, and the runtime size, rendered by
  `tools/quality/render-performance-table.mjs` from the benchmark summaries with machine, runtime, date, and
  revision in the caption.
- Compiler comparison fixture: `tools/compiler-fixtures/portable-host-facts.c` now records twenty-two layout shapes
  (the bitfield shapes where compiler families diverge, zero-width separators, a signed bitfield, `uint64`/`double`
  after a byte, native `long`, a large enum, `_Bool`, `#pragma pack(2)` with an array, nested-struct alignment, union
  size, and `#pragma pack(1)` bitfields). `node tools/quality/compiler-fixture.mjs record|validate|table` replaces
  the two PowerShell scripts and also drives `cl`/`clang-cl`; the new `compiler-fixtures` workflow (weekly, manual)
  records GCC and Clang on Linux (x64 and 32-bit), Clang on macOS, and MSVC and clang-cl on Windows. The checked-in
  baseline is GCC 15.2 on Linux x64; `CompilerDifferentialFixtureTests` verifies, for every baseline, that the
  library in the `BitfieldPacking` mode of the baseline's ABI family reproduces every shape byte for byte, and
  `differences-from-c.md` carries a generated "Portable versus real compilers" table.
- The browser large-data guide documents parsing many records in one call (`record records[EOF]` or a fixed
  count, which keeps the JavaScript fast path) with measured per-record costs against one call per record; the
  layout is the batch, so no separate batch API was added.
- npm package polish: the README lists every public function, states the 4 MiB in-memory limit of `update` and
  the raw byte-array exports (parse and resolveAddress page larger sources through the worker), and gives the
  package and runtime sizes with how a browser caches the runtime; both apps require Node 22.14 like the package.
- A [learning path](docs/guides/learning-path.md) page (nine steps, one page each, and the three examples to read
  first: `starter/Program.cs`, `starter/Next.cs`, the runtime-payload recipe, and `app.js` for JavaScript) linked
  from the README, the docs home, and the guides index; the guides TOC follows that order with the background
  primers and the memory-image series grouped at the end; the "common mistakes" checklist moved into the errors
  guide and "reuse layouts safely" into the performance guide (the two standalone pages are gone); the repository
  map lists every project of the current tree.
- Five inline byte-grid diagrams in the docs: packed versus aligned placement (layout page), union overlap, bitfield
  allocation (`LowBitFirst`/`HighBitFirst`) and packing (`SysV`/`Msvc` units), and pointer `Absolute` versus
  `Relative` with an `Origin`. They are plain SVG that follows the site theme (`currentColor` and the accent
  variables) and scales to narrow screens.
- Documentation correctness sweep: the README and AGENTS.md no longer call the project pre-publication (both
  packages are on their registries), the README links are absolute for nuget.org, memory images are a feature
  bullet, a "Why CStructSharp instead of …" table positions the library, and a "Versioning and support" section
  states the 0.x semantics, the browser contract policy, and the .NET/Node support windows; the language landing
  page describes the directives, header vocabulary, and function-pointer support 0.5 added instead of listing
  them as unsupported; every quick start leads with `parse`/`Parse(bytes, "header")` (the starter continuation
  and two guides drop `AsSpan()`); the npm README states the NaN/Infinity convention and the error location
  fields; `MUTATION_TESTING.md` is no longer packed into the NuGet package.

### Fixed

- Fix: a count, offset, or conditional selector that cannot be evaluated because of the *data* now fails as that
  operation - `CStructReadException` for reads (`CStructWriteException` for writes) - and names the value:
  `Cannot evaluate array length for data: 'n' is 4294967295, which is outside the 32-bit range that layout
  expressions support.` Previously a decoded `uint32` at or above 2^31, any wide `uint64`, or an overflowing
  `a * b` surfaced as `CStructLayoutException` with "Undefined expression identifier" or a bare overflow message.
  `CStructLayoutException` is now raised only for the layout text itself.
- Browser/WASM: a float that is NaN or infinite no longer fails the whole parse with `invalid-input`; it arrives
  as the string `"NaN"`, `"Infinity"`, or `"-Infinity"` (the convention already used for integers beyond
  `Number`'s exact range), and `serialize`/`update` accept those strings for float fields.
- Browser/WASM: the JavaScript static plan formats `float32` values with the same tie-to-even shortest round-trip
  rule as the managed projection, so the fast path and the WASM path return identical numbers for every input. A
  differential test in the npm package checks (benchmark fixtures plus seeded random inputs) now guards the two paths.

### Tooling, CI, and repository

- `Microsoft.SourceLink.GitHub` 8.0.0 → 10.0.401 (CI builds only): its `Microsoft.Build.Tasks.Git` 8.0.0 carries
  the CVE-2026-62900 advisory, which the warnings-as-errors restore rejects.
- **Tooling is Node only.** The 29 PowerShell scripts, the shared module, and the Python corpus extractor are
  replaced by Node scripts under `tools/` with the same checks, messages, and exit codes (`node tools/<area>/<name>.mjs
  --option value`; `--self-test` where a tool has fail-first fixtures): the quality validators (solution parity,
  README badges, coverage risk, fuzz corpus, mutation report, release budgets, artifact baseline, feature matrix,
  dissect corpus), the packaging checks (package validation, package/memory/onboarding consumers, browser
  onboarding), and the documentation family (build, API, language, canonical reference, quality, external links,
  workflow, Pages artifact, source snapshot, and the `validate-documentation.mjs` gate). Shared helpers live in
  `tools/lib/` (assertions, a logged `dotnet` runner, argument parsing, and small XML, ZIP, and NuGet readers), so
  the tools need no dependencies beyond Node. PowerShell 7 and `ripgrep` are no longer prerequisites; the workflows
  and guides run the Node commands. The managed API baseline tool builds the generator's scratch project outside
  the repository, so the repository's analyzers and warnings-as-errors do not apply to generated code. The historical `benchmarks/ConditionalComparison` harness is retired (its case
  definitions moved to `benchmarks/fixtures/conditional-cases.json`), and the fixture generator reads the inspector's
  format registry again.
- The npm package owns the JavaScript it ships: the adapter sources (`main.js`, `bootstrap.js`,
  `large-source.js`, `source-worker.js`, `cstructsharp-api.js`, the ZIP entry `cstructsharp-wasm.js`) and their
  unit tests live in `packages/cstructsharp/src/`, the standalone bundle's README, starter pages, and `serve.mjs`
  in `packages/cstructsharp/standalone/`, and a root `package.json` carries `build:wasm`, `pack:npm`, `pack:zip`,
  `test:npm`, `test:bootstrap`, `test:parity`, `bench:js`, and the other packaging checks (the explorer keeps only
  its own scripts). The packaging tools locate npm from PATH when not started through `npm run`, and a successful
  pack or tarball test removes its staging directory.
- Hygiene: `TreatWarningsAsErrors` in every project and a `dotnet format --verify-no-changes --severity warn` CI
  step (the `.editorconfig` now mirrors the library's documentation-rule exclusions so build and format agree);
  the dissect corpus sweep is an `OptIn` test category excluded by `tests/CStructSharpTests/default.runsettings`
  (run it with `opt-in.runsettings`), so the default run has no skipped tests; the stale performance contracts
  are re-baselined (`web-rc1.json` without its old package version and with the explorer's real dist size,
  `non-web-rc1.json` with the 0.5 package sizes); work-item codes are gone from source comments and guides
  (the contracts keep them, explained on the new "Traceability codes" project page) and project-history
  narrative in the library comments is trimmed to the invariant.
- CI runs the managed test suite on Windows and macOS as well as Linux (build and `dotnet test` only; Linux stays
  the full gate), and a weekly `dependency-check` workflow reports known vulnerabilities in the locked managed
  and Node dependency graphs (`dotnet list package --vulnerable`, `npm audit --audit-level=high`).
- The release workflow smokes the packaged starter page and one inspector flow in Firefox and WebKit
  (`CSTRUCT_BROWSERS` selects the Playwright engines); PR CI stays Chromium-only. Two inspector tests that drive
  Monaco through keyboard chords (`Ctrl+End`, `Ctrl+K Ctrl+I`) skip themselves in WebKit, where Playwright does
  not deliver the chords.

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
