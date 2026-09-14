---
title: Reuse layouts safely
description: Share a completed CStruct while keeping streams, buffers, values, and other mutable resources isolated.
---

# Reuse layouts safely

A successfully constructed `CStruct` is immutable and can be used by concurrent operations. Reusing it also avoids
parsing and preparing the same layout for every record.

That thread-safety applies to the layout object, not to mutable objects supplied by your application. Each operation
must have exclusive use of its:

- stream;
- writable span or `IBufferWriter<byte>`;
- mutable dictionary while CStructSharp is copying it;
- dynamic object, POCO, collection, or enumerable being written; and
- returned mutable dynamic or debug result.

Two tasks may share one `CStruct` and separate streams. They must not seek or read the same stream at the same time
unless the application holds a lock for the complete CStructSharp call. Locking only an individual stream read is not
enough because one operation may seek, read, and revisit several ranges.

Initialized option objects are safe to share. Variable dictionaries are copied at operation entry, but the caller
must not modify a dictionary while that copy is taking place.

A common mistake is placing both the layout and one `MemoryStream` in a singleton service. Keep the reusable layout
in the service; create or obtain an independent stream for each request.

For performance choices after establishing correct ownership, continue with
[Use CStructSharp efficiently](performance.md).
