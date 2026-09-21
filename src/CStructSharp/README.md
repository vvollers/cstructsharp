# Core library source map

Every folder in this project is a namespace: a file in `Reading/` declares `namespace CStructSharp.Reading;`.
The root folder holds the `CStruct` facade, the option types that appear on its methods, and the generator's
public attributes and interfaces, and nothing else. When you look for the code behind a behavior, start from the
stage of the pipeline it belongs to.

The part of the library that is known before any byte is read - the parser, the syntax tree, the expression
evaluator, the compiled model and placement, the codec descriptors, the diagnostics texts, introspection, the
path grammar, the options - lives in [`src/CStructSharp.Core/`](../CStructSharp.Core/README.md), a source folder
this project and the source generator both compile (`<Compile Include="../CStructSharp.Core/**/*.cs" />`). The
folders below with the same names hold the runtime halves of those namespaces.

## Folders and namespaces

| Folder | Namespace | What lives there | Public? |
| --- | --- | --- | --- |
| `/` | `CStructSharp` | `CStruct` (one `partial class` across the `CStruct*.cs` files, one file per concern: reading, writing, address resolution, introspection, memory I/O, synthetic roots; `CStructOperations.Sequences.cs` holds the `ReadOnlySequence<byte>` overloads and `ParseMany`, `CStructOperations.Async.cs`/`.AsyncWrite.cs` the awaitable forms) plus `CStructCompilationOptions`, `ReadOptions`, `WriteOptions`, `UpdateOptions`, `BitfieldAllocation`, and `StaticHelpers`. The operation files execute over the compiled model only: after construction, `Syntax` nodes are consulted solely to resolve a root name. | Yes |
| `Syntax/` | `CStructSharp.Syntax` | The syntax tree the parser produces: `Struct`, `Field`, `Enum`, `Typedef`, `Defines`, and the expression nodes (`Expr`, `Literal`, `BinaryOp`, ...) | No |
| `Parsing/` | `CStructSharp.Parsing` | `LayoutParser`, the hand-written parser for the layout language; `CStructDefinitionParser`, its entry point; `LayoutSourceValidator`, the size and nesting guard that runs before parsing | No |
| `Expressions/` | `CStructSharp.Expressions` | `ExpressionEvaluator` and the layout-variable machinery that turns `count`-style expressions into bounded `Int32` values at construction and operation time | No |
| `Compilation/` | `CStructSharp.Compilation` | The compiled model built once per layout: `Compiled*` types and fields, array shapes, size queries, symbol validation, and the process-wide `CStructLayoutCache` | No |
| `Codecs/` | `CStructSharp.Codecs` | How individual primitives become bytes and back: integer, float, fixed-point, LEB128, text, identifier, enum, and bitfield codecs, plus the `PrimitiveSpellings` alias table and the `ICustomCodec` extension point | `ICustomCodec` |
| `Streams/` | `CStructSharp.Streams` | Stream adapters used by operations: pinned-buffer and buffer-writer streams, read and write budget streams, the sparse update stream, and `AsyncStreamBuffer` (the one buffer-then-span rule the awaitable forms and the generated `Parse(Stream)` follow, plus the token linking) | No |
| `Addressing/` | `CStructSharp.Addressing` | Public path syntax (`a.b[2].c`) parsing, target resolution, and pointer arithmetic | No |
| `Reading/` | `CStructSharp.Reading` | Read-operation state, conditional field selection, data-sized array extents, the static and typed read plans that decode fixed composites quickly, and `RecordParser` (the runtime's one-record reader and stream form behind `ParseMany`) | No |
| `Writing/` | `CStructSharp.Writing` | Write-operation state, value materialization, and projection of written values into the variable domain | No |
| `Values/` | `CStructSharp.Values` | What reads return and writes accept: `StructValue`, `UnionValue`, `EnumValueResult`, `FlagValueResult`, `Pointer`, `PrimitiveArray<T>`, and the typed conversion (`TypedValueConverter`) behind `Get<T>` and mapped classes | Yes |
| `Generated/` | `CStructSharp.Generated` | What generated code calls at run time: `ReadCursor`, `WriteCursor`, `CompositeCursor` (position, limits, budgets, path context, the runtime's failure texts), `Codec` (text and bitfield decoding), `Expressions` (the layout operators), `Pointer<T>`, and `RecordSequence` over a `RecordReader<T>` (the record-sequence rules the generated `Records` forms and the runtime's `ParseMany` share); the root also holds `CStructLayoutAttribute`, `CStructMappedAttribute`, `CStructMemberAttribute`, `ICStructGenerated<T>`, `ICStructMapped<T>`, and `MappedTypes` | Yes |
| `Introspection/` | `CStructSharp.Introspection` | `LayoutInfo` and the `Layout*Info` records that describe a compiled layout's declarations, fields, offsets, and constants | Yes |
| `Diagnostics/` | `CStructSharp.Diagnostics` | The exception family, `CStructErrorCode`, `DebugData`, and the helpers that attach path and stream context to failures | Yes |
| `Memory/` | `CStructSharp.Memory`, `.Memory.Metadata` | Address spaces, mappings, metadata import, sessions, traversal, and offline patches for memory images; see its own [README](Memory/README.md) | Yes |

## Follow one read

1. `new CStruct(text)`: `Parsing` validates and parses the text into `Syntax` nodes; `Compilation` resolves every
   type, offset, and size (evaluating constant `Expressions`) into an immutable compiled model, which
   `CStructLayoutCache` may already hold for identical inputs.
2. `layout.Parse(bytes, "root")` or `ReadValue(stream, "root.field")`: `Addressing` turns the path into a resolved
   target; `Reading` walks the compiled model (`CompiledCompositeType.Fields`, each `CompiledField` carrying its
   name, width, pointer depth, codec, and direct references to its nested composite or enum) over a `Streams`
   adapter, asking `Codecs` to decode each primitive.
3. The result is assembled from `Values` types; a failure is raised from `Diagnostics` with the path and position.

Writes mirror this with `Writing` in place of `Reading`, and updates stage their bytes through
`Streams/SparseUpdateStream` so unchanged surroundings are preserved. An awaitable form adds no reader: it buffers
the stream through `Streams/AsyncStreamBuffer` and runs step 2 over the buffer; a record sequence
(`Generated/RecordSequence`) runs step 2 once per record from the end of the previous one.

## Follow a generated read

1. `CStructLayoutGenerator` recognizes the attributed partial type and captures its layout text and options.
   `CStructSharp.Core` supplies the same parser and compiled model used by the runtime; no input bytes are read
   while generating source.
2. `LayoutEmitter.Readers` emits the entry points and per-composite reader methods. Start with `EmitReaders`,
   then follow `EmitParseOverloads` and `EmitCompositeReader`. The generated source is output, not an editing target.
3. A generated `Parse` call constructs a `Generated/ReadCursor` over borrowed bytes. Its position is relative to
   that input, while origin information supplies diagnostic offsets. Nested `CompositeCursor` values track placement;
   they do not own another copy of the input.
4. Stream entry points rent temporary storage through `AsyncStreamBuffer`. Acquisition and decoding share the
   failure-restoration scope; the buffer is returned even if decoding throws. A usable seekable stream returns to
   its starting position on failure. If restoration also throws, the original failure wins.

For a behavioral comparison, start with `tests/CStructSharp.Generated.Parity/StreamFailureTests.cs` and the
generated parity layouts. `ReadCursor` borrows bytes; a growing `WriteCursor` can own rented storage and must be
disposed. A reserved writable span must not outlive a growth operation or cursor disposal.

## Rules that keep the map honest

- A file's namespace is its folder. Moving a file means changing its namespace and the `using` directives of the
  files that referenced it; nothing may reach a type through a stale namespace.
- The root folder stays small: only the facade, its option types, and the generator's public attributes and
  interfaces. New behavior belongs in the stage that owns it, or in a new folder with a matching namespace when
  no stage fits.
- A type that needs no I/O belongs in `src/CStructSharp.Core/` so the generator sees it too; a type that reads or
  writes bytes stays here.
- Public types outside the root live in `Values`, `Introspection`, `Diagnostics`, `Codecs`, and `Memory`. Adding a
  public type elsewhere changes the API baseline in `contracts/api/managed-rc1` and needs a review entry there.
