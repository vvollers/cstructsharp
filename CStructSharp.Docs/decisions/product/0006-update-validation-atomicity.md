# ADR-006: Update validation before mutation

- Status: Accepted
- Reviewed: 2026-07-26

## Decision

`UpdateStream` first prepares the change in a sparse copy-on-write view: a temporary view that records only modified
byte ranges. Path, traversal, shape, conversion, pointer, union, preservation, and size-limit failures that the
library can detect therefore happen before it writes to the destination. The original content, length, and caller
position remain unchanged.

After validation, adjacent changed ranges are combined and written in address order. A physical destination failure
may leave an earlier range written; a general-purpose stream cannot promise reliable rollback. Direct `WriteStream`
also writes progressively and does not roll back.

## Related files

- `CStructSharp/SparseUpdateStream.cs` handles staging and committing changed ranges.
- `CStructSharp/CStruct.cs` exposes the public update operation.
- `CStructSharpTests/UpdateAtomicityTests.cs` and `CStructSharpTests/SparseUpdateStreamTests.cs`.
