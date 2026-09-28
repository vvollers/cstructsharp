# Managed memory contract

`v1.json` records address rules, defaults, the operation matrix, and the importer subset for memory analysis.
Behavioral checks are the named managed test classes, run on both supported frameworks. Public signatures
are recorded in `contracts/api/managed` with the rest of the library.

Use `MemorySession` for unsigned or mapped sources, preserve stored pointers as `StoredPointer`, and follow
targets explicitly with `.value`. Reads return the core value types: `StructValue`, `UnionValue`, and
`PrimitiveArray<T>` or `List<object?>` arrays. A union is written from a `UnionValue` named after it: exact raw
storage (`UnionValue.FromRaw`, or a value that was read) or one selected member (`UnionValue.FromMember`); a
dictionary of overlapping members is not an unambiguous creation value.

The `defaults` are the `MemoryAccessContext` limits (`maxTotalBytes`, `maxRequests`, `maxNestingDepth`,
`maxPointerDepth`; nesting and pointer depth match the core `ReadOptions`) and the cache, overlay, and importer
budgets. Definition graphs may nest 256 levels, the core's default layout nesting limit. A `RawBytes` type has a
known size but no usable members and is read and written as a raw `byte[]`; an opaque pointer is a `Pointer`
with no target type (like C's `void *`) and cannot be followed.

Failures follow `failureRules`: memory analysis uses the core `CStructException` hierarchy, so a memory access
failure is a `CStructReadException`, and paths, values, and definitions fail with `CStructPathException`,
`CStructWriteException`, and `CStructLayoutException`.

Read the memory guide for importer coverage, generation and ownership requirements, source budgets, and the
non-atomic patch commit contract. The browser bridge exposes standalone schemas and binary operations.
