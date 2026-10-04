# Memory source map

The memory types compile into the main CStructSharp library. This directory separates *acquiring* bytes from
*interpreting* their layout: sources hand out bytes for unsigned addresses, schemas say where fields are, and a
session joins the two. Start with the [worked memory guides](../../../docs/guides/memory-analysis.md) for runnable
examples. The XML comments in each source file explain its contract and the algorithms it owns; this page is the
map that shows how the files fit together.

## The layers

```text
MemorySession            applies a MemorySchema to a MemoryRegion: Resolve, Read, Inspect, Serialize, PlanUpdate
   |  MemoryWalker       repeats session reads over lists and trees with node limits and identity tracking
   |  MemoryPatch        flattens a logical edit into physical fragments, validates, then commits
MemoryRegion             a source, an unsigned start, and a finite length; OpenRead() bridges to core streams
   |
IMemorySource            "which bytes are at this address?"
   |  MappedMemorySource translates logical ranges to backing regions (a sorted MemoryMapping table)
   |  CachedMemorySource remembers exact ranges until the backing generation changes
   |  OverlayMemorySource keeps changed bytes apart from an unchanged backing snapshot
   |  ByteArrayMemorySource / StreamMemorySource   leaf sources that own or borrow the actual bytes
MemoryAccessContext      one budget (bytes, requests, nesting, pointer steps, cancellation) shared by every layer
```

## Follow one read

1. `MemorySession.Resolve` tokenizes the path and walks semantic field offsets from `MemorySchema`. Ordinary
   members and array indexes are arithmetic on the region; only matching or promoted members need selections.
   All promoted branches still participate in ambiguity and depth checks. A `.value` step reads a `StoredPointer`
   and asks the resolver for another bounded `MemoryRegion`, possibly in another source.
2. `ReadCore` decodes only the selected storage through the existing compiled scalar codecs. A schema shares
   equivalent built-in scalar preparation within its own lifetime, while validating every definition's size and
   references. Custom codecs, declarations, preludes and caller-owned option collections keep independent preparation.
   Plain built-in numeric arrays use their final owned storage directly; each element still makes the same reads and
   budget charges. Enums, text and custom configurations use the general materialization path.
   `MemoryRegionStream` separately exposes a region to callers of the ordinary stream API.
3. `MappedMemorySource` splits a read across mappings and forwards the same `MemoryAccessContext`. Leaf sources
   acquire bytes; caches and overlays compose around them. No layer owns a caller's file or transport lifetime.
4. `Inspect` adds ordered backing ranges to the value by flattening known mapping layers. Semantic names and bit
   slices come from descriptors; the generated compiled storage names stay an implementation detail.

`BtfMetadata` keeps bounded caches for its own lifetime: the first 256 queried names retain unique, missing or
ambiguous outcomes, and imports share up to 256 successful core layouts within a one-million-character source budget.
Compilation keys include the complete declaration, pointer width, byte order and core settings. Every import still
walks its own reachable graph, validates every descriptor, and returns independent schemas and ordered diagnostics.
Failed compilations and exceptions are not cached. Base and split tables own separate caches. ISF imports and public
`MemorySchema` construction do not use this cross-root cache.

## Find the owner of a change

| Concern | Files |
| --- | --- |
| Byte acquisition and capabilities | `IMemorySource`, `IWritableMemorySource`, `ByteArrayMemorySource`, `StreamMemorySource` |
| Coordinate translation | `MemoryMapping`, `MappedMemorySource`, `MemoryRegion`, `MemoryRegionStream` |
| Repeated reads and isolated edits | `CachedMemorySource`, `OverlayMemorySource` |
| Explicit placement and codec compilation | `MemoryTypeDefinition`, `MemoryField`, `MemoryTypeKind`, `MemorySchema`, `PortableMemorySchema` |
| Metadata format parsing | `Metadata/BtfMetadata`, `Metadata/IsfMetadata`, `Metadata/MetadataImportResult` |
| Value selection and pointer interpretation | `MemorySession`, `MemorySelection`, `MemoryInspection`, `StoredPointer`, `PointerRequest` |
| Repeated graph work | `MemoryWalker`, `MemoryWalkResult`, `MemoryWalkStop` |
| Write planning and uncertain completion | `MemoryPatch`, `MemoryPatchFragment`, `MemoryPatchCommitException` |
| Shared limits and diagnostics | `MemoryAccessContext`, `MemoryAccessException`, `MemoryFailure` |

## Invariants to preserve

- **An address belongs to a source object.** Two sources with the same `Id` are still two address spaces.
  Identity comparisons use `ReferenceEquals`, never the label.
- **Metadata supplies placement.** Code never fills a missing offset or size by guessing a host ABI; a missing
  fixed extent is a reason to reject a projection.
- **A hole is missing information, not zero.** Sources return the real count, and finite views turn an unexpected
  zero into `MissingBytes`.
- **Stored pointer values are preserved.** Only an explicit `.value` step calls the resolver; serialization writes
  the `StoredPointer.Address` that was read.
- **Values use the core vocabulary.** Reads return `StructValue`, `UnionValue`, and `PrimitiveArray<T>` or
  `List<object?>` arrays, as the core reader does; a union is written from a `UnionValue` named after it.
- **Work budgets cross boundaries.** Adapters and callbacks forward the context they received instead of creating
  a new one. Composite values and stacked source layers count against `MaxNestingDepth`; `.value` steps count
  against `MaxPointerDepth`. Definition graphs are bounded separately by `MemorySchema.MaxDefinitionNestingDepth`,
  the core's default layout nesting limit.
- **"Opaque" means an opaque pointer.** A `Pointer` with a null `ElementTypeId` (like C's `void *`) keeps its
  address but cannot be followed. A type whose size is known but whose members are unusable is `RawBytes`, read
  and written as a `byte[]`.
- **Generations detect reported changes.** They are a change counter, not a snapshot or a lock; callers arrange
  consistency when a source cannot report changes.
- **Failures use the core exception hierarchy.** Unreadable memory is a `MemoryAccessException`, a
  `CStructReadException` whose code is `ReadLimitExceeded` for `BudgetExceeded`; paths throw
  `CStructPathException`, values `CStructWriteException`, and definitions or metadata `CStructLayoutException`.
  Only parameter-contract violations, read-only sources, and cancellation keep .NET exception types. Coordinates
  that do not exist, such as the source of a depth limit, stay null rather than taking a placeholder.
- **Validation precedes writes, but commit is not atomic.** `MemoryPatch` checks every fragment first and reports
  the uncertain fragment when a later write fails; it never promises rollback.

Use the existing core codecs for primitive and bitfield operations. Changing a source adapter should not require
another integer decoder. Conversely, importing a metadata format should produce descriptors rather than implement
its own memory reader. These boundaries let one layout serve file images, translated process spaces, and synthetic
fixtures with the same session behavior.
