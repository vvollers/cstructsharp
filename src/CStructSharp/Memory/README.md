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
MemoryAccessContext      one budget (bytes, requests, depth, cancellation) shared by every layer in one operation
```

## Follow one read

1. `MemorySession.Resolve` tokenizes the path and walks semantic field offsets from `MemorySchema`. Ordinary
   members and array indexes are arithmetic on the region; a `.value` step reads a `StoredPointer` and asks the
   resolver for another bounded `MemoryRegion`, possibly in another source.
2. `ReadCore` decodes only the selected storage. A `MemoryRegionStream` translates local stream positions into
   unsigned source addresses so the existing compiled scalar codecs can do the binary decoding.
3. `MappedMemorySource` splits a read across mappings and forwards the same `MemoryAccessContext`. Leaf sources
   acquire bytes; caches and overlays compose around them. No layer owns a caller's file or transport lifetime.
4. `Inspect` adds ordered backing ranges to the value by flattening known mapping layers. Semantic names and bit
   slices come from descriptors; the generated compiled storage names stay an implementation detail.

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
| Write planning and uncertain completion | `MemoryPatch`, `MemoryPatchFragment`, `MemoryPatchCommitException`, `MemoryUnionSelection` |
| Shared limits and diagnostics | `MemoryAccessContext`, `MemoryAccessException`, `MemoryFailure` |

## Invariants to preserve

- **An address belongs to a source object.** Two sources with the same `Id` are still two address spaces.
  Identity comparisons use `ReferenceEquals`, never the label.
- **Metadata supplies placement.** Code never fills a missing offset or size by guessing a host ABI; a missing
  fixed extent is a reason to reject a projection.
- **A hole is missing information, not zero.** Sources return the real count, and finite views turn an unexpected
  zero into `MissingBytes`.
- **Pointer bits are preserved.** Only an explicit `.value` step calls the resolver; serialization writes the bits
  that were read.
- **Work budgets cross boundaries.** Adapters and callbacks forward the context they received instead of creating
  a new one.
- **Generations detect reported changes.** They are a change counter, not a snapshot or a lock; callers arrange
  consistency when a source cannot report changes.
- **Validation precedes writes, but commit is not atomic.** `MemoryPatch` checks every fragment first and reports
  the uncertain fragment when a later write fails; it never promises rollback.

Use the existing core codecs for primitive and bitfield operations. Changing a source adapter should not require
another integer decoder. Conversely, importing a metadata format should produce descriptors rather than implement
its own memory reader. These boundaries let one layout serve file images, translated process spaces, and synthetic
fixtures with the same session behavior.
