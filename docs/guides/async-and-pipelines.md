---
title: Async reads, cancellation, and pipelines
description: What the awaitable forms do with a stream, how cancellation reaches a read, and how to frame records from a PipeReader with ParseMany and TryParse.
---

# Async reads, cancellation, and pipelines

Every stream operation has an awaitable twin - `ParseAsync`, `ReadValueAsync`, `WriteAsync`, `UpdateAsync`,
`ParseManyAsync`, and the generated `ParseAsync`/`WriteAsync`/`RecordsAsync`. This guide explains what those
forms do differently from the synchronous ones (less than you might think), how a `CancellationToken` reaches a
read, and how bytes that arrive in pieces - a socket, a `PipeReader` - become records.

[!code-csharp[Read a file asynchronously](../examples/AsyncExamples.cs#recipe-async-stream)]

## What `async` and `await` do here

A synchronous `layout.Parse(file, "header")` asks the file for bytes and *waits*: the thread that made the call
sits idle until the disk or the network answers. `await layout.ParseAsync(file, "header")` asks for the same
bytes but gives the thread back while they are on their way; when they arrive, the method continues where it left
off. The value it produces is the same `StructValue`, read by the same code with the same limits and messages.

The awaitable forms return a `ValueTask<T>`. A `ValueTask` is a lightweight promise of a result: `await` it once.
When the bytes are already in memory - a `MemoryStream` that exposes its buffer - the library reads them in
place and the `ValueTask` is *already complete* when it is returned, so `await` costs nothing and nothing is
allocated for the wait. A `FileStream` opened with `useAsync: true` or a network stream pays the real asynchronous
I/O, which is the point.

## The buffer-then-span rule

There is exactly one reader for a stream and one for a span; the awaitable forms add no third. Instead, each one
follows a single rule:

1. Read the stream with `ReadAsync` into a pooled buffer: a seekable stream up to its remaining length, any
   stream up to `MaxTotalBytesRead`, plus one byte.
2. Run the span reader over the buffer.
3. Set the stream's position and return the buffer to the pool.

The extra byte is what makes a value larger than the budget fail with the budget message rather than a short
read, exactly as the synchronous stream reader would. The stream position follows from the rule:

```text
seekable stream, success:            origin ─── value ───┤ position   (just after the value)
seekable stream, any failure:        origin ┤ position                (back where it started)
ResolveAddressAsync, GetArrayLength: origin ┤ position                (a query consumes nothing)
non-seekable stream:                 consumed by what was buffered, whatever the outcome
```

A stream that cannot seek (a socket, a pipe, a compressed stream) cannot be rewound, so the bytes the rule
buffered are gone whether the value used them or not. A value-sized budget does **not** frame consecutive records:
`ParseAsync` may consume the budget plus one byte. For example, with `struct record { uint16 value; };`, input
`01 00 02 00` and a two-byte budget, the first call returns 1 but consumes `01 00 02`. A second call has only `00`
left, not the two bytes needed for 2. The budget limits decoding; it does not define stream message boundaries.

For fixed-size records, use `ParseManyAsync` (or generated `RecordsAsync`). Each two-byte record below starts at
offset 0 within its own input slice: `01 00` gives 1 and `02 00` gives 2, in little-endian order. The record iterator
consumes whole records without single-value read-ahead. A trailing incomplete record is an error.

[!code-csharp[Two records from a forward-only stream](../examples/AsyncExamples.cs#async-two-records)]

When the application owns message framing, keep unconsumed bytes in a `PipeReader`, as shown below.

One consequence of the rule is easy to miss. The buffer starts at the stream's *current position*, so a stored
absolute pointer address counts from that origin - as it does for a span or memory input - while the synchronous
stream form counts from the stream's first byte. A file whose addresses are absolute file positions is read
asynchronously from position 0, or through the synchronous form.

## Cancellation

A `CancellationToken` is a signal an operation checks: "has someone asked me to stop?" You create it from a
`CancellationTokenSource`, which can be cancelled by a timer (`new CancellationTokenSource(TimeSpan.FromSeconds(10))`),
by a shutdown handler, or by a call to `Cancel()`. Every awaitable form takes one as its last parameter, and every
read and write option record has a `CancellationToken` property for the synchronous forms; when both are given they
are linked, and either one stops the operation.

The library checks the token where a check is cheap and a stop is safe: before the first byte, at every composite,
pointer, and array boundary, between the blocks of a large primitive array, between the chunks of a terminated
string, and between records of a sequence. It never checks in the middle of decoding one value, so a cancelled
read stops with a whole value read and nothing half-decoded.

Cancellation surfaces as `OperationCanceledException` and never as a read failure: the non-throwing forms
(`TryReadValue`, `TryParse`, `TryReadValueAsync`) let it through instead of returning `false`, because a cancelled
read says nothing about the bytes. Buffered runtime async reads and generated stream reads restore a seekable
stream's origin even if acquisition fails after reading some bytes. Synchronous runtime reads can leave the cursor
where the reader stopped. A forward-only source cannot restore consumed bytes.

Restoration assumes the stream's `Position` setter still works. If restoring the cursor also fails while handling
an earlier read/update failure, the original exception is preserved and the cursor is no longer guaranteed.

## Awaitable writes

[!code-csharp[Write and update asynchronously](../examples/Program.cs#api-guide-write-async)]

`WriteAsync` serializes the value first, into an array of exactly the value's size, then writes it with one
`WriteAsync` call. A validation failure therefore writes nothing: the destination is untouched. `UpdateAsync`
needs a readable, writable, seekable stream; it buffers the region from the origin, applies the update to a copy,
writes back only the runs of bytes that differ, and restores the origin. Its failure rule is the synchronous
one: a replacement that does not fit, or that would move a later field, changes nothing.

[!code-csharp[Update asynchronously](../examples/Program.cs#api-guide-update-async)]

## Records from a pipe

A socket delivers bytes in whatever pieces the network chose. A `PipeReader` (`System.IO.Pipelines`) manages
that: `ReadAsync` hands you everything that has arrived as a `ReadOnlySequence<byte>` (a chain of buffers), and
`AdvanceTo(consumed, examined)` tells it what you used and what you looked at, so the next `ReadAsync` returns
new bytes appended to the ones you did not consume.

This small example first receives `01`, keeps that incomplete record, then receives `00 02 00`. It parses exactly
two bytes at a time from the retained four-byte sequence, yielding 1 and 2 without losing the next record's bytes.

[!code-csharp[Retain incomplete records](../examples/AsyncExamples.cs#async-retained-record)]

Run both two-record examples from the repository root:

```sh
dotnet run --project docs/examples/CStructSharp.Docs.Examples.csproj -c Release -- forward-only-records retained-record
```

[!code-csharp[Frame records from a pipe](../examples/AsyncExamples.cs#recipe-pipe-reader)]

Two patterns cover most protocols:

- **Fixed-size records.** The number of whole records in the buffer is `buffer.Length / size`. `ParseMany` reads a
  `ReadOnlySequence<byte>` directly - one record per step, a single-segment sequence in place and a chain through
  one pooled copy - so hand it the whole records and advance past them; the partial record at the end stays in
  the pipe as *examined*, which makes `ReadAsync` wait for more bytes instead of returning the same ones.
- **Count-prefixed messages.** Read the count first with `TryReadValue` (it fails harmlessly while the header is
  incomplete), compute the message's length from it, and parse the message only when every byte of it is there.

Both loops end when `result.IsCompleted` says the writer has finished; bytes left over then are a truncated
record, which the application decides how to report.

## Options as records

`ReadOptions`, `WriteOptions`, and `UpdateOptions` are C# `record` types, so a variant of a shared options object
is a `with` expression: `var quick = defaults with { MaxTotalBytesRead = 4096, CancellationToken = token };`. Two
option records with the same values are equal, which makes them safe keys and easy to assert on.

[!code-csharp[Copy options with a change](../examples/Program.cs#api-guide-options-with)]

## Check yourself

1. Which reader decodes the bytes `ParseAsync` buffered?
2. A `ParseAsync` on a `FileStream` at position 100 fails. Where is the stream afterwards?
3. Why does `TryReadValueAsync` throw `OperationCanceledException` instead of returning `false`?
4. In the pipe loop, why is the partial frame passed as `examined` but not as `consumed`?

<details>
<summary>Answers</summary>

1. The span reader - the same one `Parse(ReadOnlySpan<byte>)` uses. There is no separate asynchronous decoder.
2. At position 100: a seekable stream returns to its origin on any failure.
3. A cancelled read says nothing about the bytes; `false` would claim the bytes were wrong.
4. Consumed bytes are gone; examined-but-not-consumed bytes stay in the pipe and are returned again with the next
   chunk, so the frame completes when the rest arrives.

</details>

## Exercise

Change the pipe producer to write the twenty bytes one at a time. The reader loop should still count five frames:
each `ReadAsync` returns a buffer that holds at most one new whole frame, and often none, and `AdvanceTo` keeps
the incomplete one waiting. Then remove the `examined` argument (pass `buffer.GetPosition(whole)` alone) and watch
the loop spin: without *examined*, the pipe reports the same unconsumed bytes as new data every time.
