---
title: Memory budgets, caching, and source contracts
description: Bound work across adapters, diagnose unavailable data, and supply sources with honest ownership and consistency rules.
---

# Memory budgets, caching, and source contracts

A capture may be incomplete or corrupt. A live source may change between reads. Even a correct image can describe
a graph so large that an application should not traverse all of it. Code that reads untrusted bytes has to plan
for these cases, because the alternative is an analyzer that hangs, exhausts memory, or reports a plausible value
that was never in the image. This guide explains the tools the memory APIs provide for that planning, and the
contract you must honor when you write a source of your own.

## Budget an operation, not each individual read

A `MemoryAccessContext` is a small mutable object with three positive limits and a cancellation token:

| Limit | Default | What it bounds |
| --- | --- | --- |
| `maxBytes` | 64 MiB | Bytes requested from leaf sources plus bytes staged for output |
| `maxRequests` | 100,000 | Every unit of work: leaf reads, mapping lookups, path steps, traversal steps |
| `maxDepth` | 128 | Nesting of composites, pointer path steps, and stacked source layers |

The important idea is *one context per logical operation*. If a list walk reads twenty links through three mapping
layers, all of that work should count against one budget. Pass the same instance through callbacks and adapters;
the library does this for you inside a session and a walker. Create a fresh instance for each independent
operation. Contexts are counters, not thread-safe objects, so never share one between concurrent operations.

The counters measure library work, not unique addresses or hardware I/O calls. Reading the same physical byte
twice charges it twice. A mapping lookup or traversal step charges one request with zero bytes; a leaf read charges
the bytes it requested. Serialization and patch staging also charge work. A cache hit avoids the backing-byte charge
but still costs a request, because cheap operations in an unbounded loop are still an unbounded loop.

This is why `maxBytes: 4` is not enough to plan and commit a four-byte patch: planning reads the expected bytes,
commit validates them again, and the write itself is charged. Choose limits from the size of the task, then treat
`BudgetExceeded` as "the operation was incomplete", never as "the result was empty".

## Cache stable, repeated selections

An analyzer often reads the same few fields many times: the `next` link of every node, the header of every page.
`CachedMemorySource` sits in front of another source and remembers the bytes of exact requests it has already
answered.

[!code-csharp[Observe cold and warm reads and invalidation](../examples/memory-analysis/MemoryTutorialExamples.cs#memory-cache)]

Both reads return 42. The cold read requests four backing bytes; the warm read requests zero backing bytes and
still spends requests. Writing to the underlying byte array advances its generation, so the next cached read
returns 7 instead of a stale value.

The cache is deliberately simple so that its behavior is predictable:

- Entries are keyed by the exact `(address, requested length)` pair. Reading four bytes and then two bytes at the
  same address is not a hit on the four-byte entry. There is no page prefetch and no merging of overlapping ranges,
  so the cache never reads neighboring bytes that might be unavailable.
- Capacity is a retained byte count. Eviction is first-in, first-out, and a hit does not move an entry to the end.
  A request larger than the capacity is answered but not stored. A short read is not stored, because it does not
  describe the complete range.
- It is read-only. Plan writes through the writable source beneath it; that source's generation change then
  invalidates the cache before its next read.

A **generation** is the source's own change counter. The cache compares it on every read and empties itself when
it differs. That works only if the source reports every change. `ByteArrayMemorySource` and `OverlayMemorySource`
do; `StreamMemorySource` cannot see external file changes and keeps the constant generation you gave it. Caching a
live source whose changes are not reported can return obsolete data indefinitely, so use a stable snapshot or a
source that advances its generation on every write.

## Distinguish absent data from cancellation

[!code-csharp[Handle a missing mapping and cancellation separately](../examples/memory-analysis/MemoryTutorialExamples.cs#memory-failure)]

This snippet continues with the session and region from the cache example. A `MemoryAccessException` carries a
stable `Failure` category plus the failing source label, address, and byte length. When the failure crosses a
session call, the exception also records the requested `Path` and the logical root `LogicalRegion`. The failing
address may be a backing-file offset while the logical root is a process address; display both with their source
labels rather than comparing them as if they were the same coordinate system.

| Failure | Typical interpretation |
| --- | --- |
| `Unmapped` | No mapping covers the address, or a write is outside its source |
| `MissingBytes` | A requested backing range cannot supply bytes, for example a truncated file |
| `BudgetExceeded` | The operation limit or an overlay's capacity was reached |
| `StaleSource` | The source's reported state differs from the expected snapshot |
| `SourceFailure` | A backing read failed or an adapter returned an invalid count |
| `InvalidValue` | An addressed operation cannot use the value, such as following a null pointer |

Not every error is a `MemoryAccessException`. Bad schema arguments and malformed paths throw argument or lookup
exceptions; codec validation throws core exceptions. Cancellation uses `OperationCanceledException` before writes
and may be wrapped by a patch commit failure once writing has begun. Walkers convert only `Unmapped` and
`MissingBytes` into an `Unavailable` result, because those describe the image; everything else describes the
operation and propagates. See [patch failure semantics](memory-updates.md).

Cancellation is cooperative. The context checks its token between synchronous work requests. A source blocked in
an operating-system call, or a user callback that never returns, cannot be interrupted by this API. A custom
transport needs its own timeout or cancellation behavior in addition to the shared context checks.

## Implement a custom source

Implement `IMemorySource` when bytes come from a debugger, a remote reader, a decompressor, or any transport the
library does not know. The interface is small: one synchronous method,
`Read(ulong address, Span<byte> destination, MemoryAccessContext context)`, and two properties, `Id` and
`Generation`. The contract that makes the rest of the library work is larger than the signature, so keep this list
beside your implementation:

1. **Define the coordinate system.** State whether addresses are process virtual addresses, physical addresses, or
   file offsets. Never narrow an unsigned address to a signed value without checking that it fits.
2. **Return the real count.** Copy at most `destination.Length` bytes and return how many you copied. A positive
   short read is allowed; callers continue at the next byte. Zero means no bytes are available for the request.
   Finite views treat an unexpected zero as `MissingBytes`, not as a normal end of file.
3. **Never fake data.** Do not retain the caller's span after returning, and do not turn a transport error or an
   unmapped hole into zero-filled success. Wrap the underlying cause in a `MemoryAccessException` with
   `SourceFailure` where appropriate, so the caller sees a structured failure.
4. **Charge before doing work.** Call `context.Charge(Id, address, requestedBytes)` before a leaf read.
   Translation-only adapters charge zero-byte requests and forward the same context to their backing source. Check
   cancellation in longer loops.
5. **Be honest about change.** Advance a monotonic `Generation` for every observable change, or document that the
   caller must hold a stable snapshot and report a constant value. Never advertise a generation that can miss
   changes; a wrong generation silently poisons caches and patches.

`IWritableMemorySource` adds `Write`. A successful call must write the complete span and advance the generation.
It returns no byte count. A failing implementation may have partly written, which is why patch commit reports the
failing fragment as uncertain. Keep ownership of handles and transports explicit: sessions, regions, mapping
sources, and stream views never dispose caller-owned resources.

Individual byte-array and overlay operations are synchronized, and the stream adapter synchronizes access through
itself while restoring the caller's stream position. These locks cover single operations only. They do not cover
direct access to the stream from other code, or a whole multi-source analysis. For a consistent report, arrange a
snapshot or application-level synchronization.

## Check your understanding

1. A walk uses `maxNodes: 10` and a context with `maxRequests: 12`. Each node requires one traversal step and one
   link read through one mapping layer. Does the walk finish, and if not, how does it fail?
2. You wrap a `FileStream` in a `StreamMemorySource` and then in a `CachedMemorySource`. Another program appends
   to the file and changes bytes you already read. What does the cache return, and what should you have done?
3. Your custom source reads from a network service that returns "not found" for unmapped pages. Should `Read`
   return 0, return zero-filled bytes, or throw?

Answers: **it does not finish**; each node costs four requests (the walk step, the session's field selection,
the mapping lookup, and the leaf read), so the thirteenth request, at the start of the fourth step, throws
`MemoryAccessException` with `BudgetExceeded`, and the exception propagates out of the walk instead of becoming an
`Unavailable` result. **The old bytes**; the stream adapter's generation is
constant, so the cache has no way to notice; take a stable snapshot or copy of the file before analysis, or advance
the generation yourself by constructing a new adapter. **Throw `MemoryAccessException` with `Unmapped`**, or
return 0 if the page is genuinely absent from the capture; returning zero-filled bytes would present a hole as data.

Run all examples with the commands on the [synthetic memory example page](../examples/memory-analysis/index.md).
