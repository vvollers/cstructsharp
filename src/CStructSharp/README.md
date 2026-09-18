# Core library source map

Every folder in this project is a namespace: a file in `Reading/` declares `namespace CStructSharp.Reading;`.
The root folder holds the `CStruct` facade and the option types that appear on its methods, and nothing else.
When you look for the code behind a behavior, start from the stage of the pipeline it belongs to.

## Folders and namespaces

| Folder | Namespace | What lives there | Public? |
| --- | --- | --- | --- |
| `/` | `CStructSharp` | `CStruct` (one `partial class` across the `CStruct*.cs` files, one file per concern: reading, writing, address resolution, introspection, memory I/O, synthetic roots) plus `CStructCompilationOptions`, `ReadOptions`, `WriteOptions`, `UpdateOptions`, `BitfieldAllocation`, and `StaticHelpers`. The operation files execute over the compiled model only: after construction, `Syntax` nodes are consulted solely to resolve a root name. | Yes |
| `Syntax/` | `CStructSharp.Syntax` | The syntax tree the parser produces: `Struct`, `Field`, `Enum`, `Typedef`, `Defines`, and the expression nodes (`Expr`, `Literal`, `BinaryOp`, ...) | No |
| `Parsing/` | `CStructSharp.Parsing` | `LayoutParser`, the hand-written parser for the layout language; `CStructDefinitionParser`, its entry point; `LayoutSourceValidator`, the size and nesting guard that runs before parsing | No |
| `Expressions/` | `CStructSharp.Expressions` | `ExpressionEvaluator` and the layout-variable machinery that turns `count`-style expressions into bounded `Int32` values at construction and operation time | No |
| `Compilation/` | `CStructSharp.Compilation` | The compiled model built once per layout: `Compiled*` types and fields, array shapes, size queries, symbol validation, and the process-wide `CStructLayoutCache` | No |
| `Codecs/` | `CStructSharp.Codecs` | How individual primitives become bytes and back: integer, float, fixed-point, LEB128, text, identifier, enum, and bitfield codecs, plus the `PrimitiveSpellings` alias table and the `ICustomCodec` extension point | `ICustomCodec` |
| `Streams/` | `CStructSharp.Streams` | Stream adapters used by operations: pinned-buffer and buffer-writer streams, read and write budget streams, and the sparse update stream | No |
| `Addressing/` | `CStructSharp.Addressing` | Public path syntax (`a.b[2].c`) parsing, target resolution, and pointer arithmetic | No |
| `Reading/` | `CStructSharp.Reading` | Read-operation state, conditional field selection, data-sized array extents, and the static and typed read plans that decode fixed composites quickly | No |
| `Writing/` | `CStructSharp.Writing` | Write-operation state, value materialization, and projection of written values into the variable domain | No |
| `Values/` | `CStructSharp.Values` | What reads return and writes accept: `StructValue`, `UnionValue`, `EnumValueResult`, `FlagValueResult`, `Pointer`, `PrimitiveArray<T>`, and the POCO binding and typed conversion behind them | Yes |
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
`Streams/SparseUpdateStream` so unchanged surroundings are preserved.

## Rules that keep the map honest

- A file's namespace is its folder. Moving a file means changing its namespace and the `using` directives of the
  files that referenced it; nothing may reach a type through a stale namespace.
- The root folder stays small: only the facade and its option types. New behavior belongs in the stage that owns
  it, or in a new folder with a matching namespace when no stage fits.
- Public types outside the root live in `Values`, `Introspection`, `Diagnostics`, `Codecs`, and `Memory`. Adding a
  public type elsewhere changes the API baseline in `contracts/api/managed-rc1` and needs a review entry there.
