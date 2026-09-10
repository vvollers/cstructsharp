---
uid: CStructSharp.CStruct
example:
- *content
---
Create one `CStruct`, reuse it for dynamic and typed reads, and use `TryReadValue` when a short input is an expected
failure rather than an exceptional event:

The complete `DecodeHeader` method is compiled and executed by the documentation example runner:

[!code-csharp[Compile once, parse dynamically, and handle expected failure](../examples/Program.cs#api-reference-cstruct)]

---
uid: CStructSharp.DebugData
example:
- *content
---
Parse with byte-range information when you need to connect a result to its original bytes. `ResolveAddress` finds
the start of one field without changing the caller's stream position:

This `InspectRanges` check is compiled and executed with the other site examples:

[!code-csharp[Inspect byte ranges and resolve a field address](../examples/Program.cs#api-reference-debug-data)]

---
uid: CStructSharp.EnumValueResult
example:
- *content
---
An enum can contain a numeric value that has no declared name. `EnumValueResult` keeps that value, its width, and
its signedness instead of discarding information:

The unknown-value case in `PreserveEnum` is compiled and executed during documentation validation:

[!code-csharp[Preserve an enum payload with no declared member](../examples/Program.cs#api-reference-enum)]

---
uid: CStructSharp.Pointer
example:
- *content
---
The parsed `Pointer` keeps the stored address and, when dereferencing is enabled, the value read from its target:

`FollowPointer` is compiled and executed as part of the example program:

[!code-csharp[Follow a pointer and inspect its address and target](../examples/Program.cs#api-reference-pointer-read-options)]

---
uid: CStructSharp.ReadOptions
example:
- *content
---
Default read options follow this one-byte pointer while applying the library's normal pointer and traversal limits:

The same `FollowPointer` method is compiled and executed to check this default:

[!code-csharp[Read through a pointer with the bounded default policy](../examples/Program.cs#api-reference-pointer-read-options)]

---
uid: CStructSharp.UnionValue
example:
- *content
---
`UnionValue` preserves the complete union storage. You can write those original bytes back or explicitly choose one
member as the source for new output:

Both branches in `PreserveUnion` are compiled and executed by the example runner:

[!code-csharp[Round-trip raw union storage and select a member](../examples/Program.cs#api-reference-union)]

---
uid: CStructSharp.WriteOptions
example:
- *content
---
Serialize to caller-owned memory when you want to control allocation. The span overload reports how many bytes it
used; the buffer-writer overload appends exactly the produced bytes:

The `RoundTrip` scenario is compiled and executed with sentinel bytes that detect an overrun:

[!code-csharp[Serialize to a span and buffer writer](../examples/Program.cs#api-reference-write-options)]

---
uid: CStructSharp.UpdateOptions
example:
- *content
---
An update changes one selected field while preserving the stream's starting position. The failed update also shows
that invalid replacement data does not leave a partial change:

`PatchField` is compiled and executed for both the successful and failed update:

[!code-csharp[Patch one field while preserving position and failure atomicity](../examples/Program.cs#api-reference-update-options)]
