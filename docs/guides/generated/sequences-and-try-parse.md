---
title: Sequences and TryParse
description: TryParse and the failure it hands over, ReadValue<T> on the layout class, Records and RecordsAsync over one record after another, the allocation-free view enumerator, and the awaitable Parse and Write.
---

# Sequences and TryParse

The earlier lessons read one value from bytes you already had. Real inputs are messier: a buffer may or may not
hold a whole value, a file holds thousands of records in a row, and a socket delivers them while the program
does other work. This lesson covers the generated members for those cases. Each one runs the reader you already
know; what changes is how it is called and what it does when the bytes run out.

## TryParse: failure as a value

[!code-csharp[TryParse in both forms](../../examples/GeneratedExamples.cs#recipe-try-parse)]

`Parse` throws when the bytes are wrong. That is right when wrong bytes are a bug, and wrong when they are
expected - a truncated network read, a file of unknown format, a user's upload. Catching an exception for an
expected case is slow (an exception captures a stack trace) and noisy (every caller writes the same `try`).

`TryParse` returns a `bool` and an `out` value: `true` and the object, or `false` and `null`. The second overload
adds `out CStructException? failure`, the exception the throwing form *would* have raised, already built but never
thrown. A caller that only needs yes-or-no ignores it; a caller that logs why gets the same text `Parse` would
have shown - the field, the path, the offset.

What `TryParse` catches is precise: a read failure (short input, a bad value), a path failure, and a limit failure -
the `CStructException` family. What it lets through is just as precise: an `OperationCanceledException` (cancelled
work says nothing about the bytes) and argument errors (a null stream is a bug in the caller). A stream is left at
its origin after a failure, so another layout can be tried at the same place.

Every composite has the five input kinds - span, array, memory, `ReadOnlySequence<byte>`, stream - as
`TryParse<Name>`, and the root has them as `TryParse`. The interface `ICStructGenerated<TSelf>` exposes the span
form, so generic code can parse any generated root without knowing its class.

## ReadValue\<T\> on the layout class

`Wire.Layout.ReadValue<HeaderRecord>(bytes, "header")` reads the root into a
[mapped class](mapped-classes.md). The layout class shortens it to `Wire.ReadValue<HeaderRecord>(bytes)` - the same
call with the root's name filled in - and `Wire.TryReadValue<HeaderRecord>(bytes, out record)` is its non-throwing
form. They are forwarding members: a runtime read, not generated code, so use them where a mapped class is the
right result type and `Parse` where the generated class is.

## Records: one after another

[!code-csharp[Records, the view enumerator, and trailing bytes](../../examples/SequenceExamples.cs#recipe-record-sequence)]

A file of log entries or a message body of frames is one struct after another with nothing in between.
`Wire.Records(bytes)` returns an `IEnumerable<Header>` over such a sequence, and `foreach` walks it.

An `IEnumerable<T>` is a promise to produce values one at a time; `foreach` asks for the next one at each step.
`Records` is an *iterator*: it parses a record when the loop reaches it, not before. Ten million records cost one
object at a time, and `break` after the third parses three. The rules it follows:

- **Stride.** A composite whose size the layout fixes (`Sizes.Header` exists) advances by that size. A
  runtime-sized composite - a count-prefixed payload - advances by the bytes the previous record consumed, so its
  records are read in order and each one's length comes from its own contents.
- **Trailing bytes.** Bytes left over that are shorter than one record are not skipped. The step that meets them
  throws, with the text a `T v[EOF]` array uses for a partial element ("The remaining 2 bytes are not a whole
  number of 6-byte elements") when the size is fixed, or the short read itself when it is not. A file with a
  trailer slices it off first.
- **The record's index.** A failure names the record before the path: `[3].header.length` is the `length` of the
  fourth record. `failure.Path` carries the same text.
- **Each record is its own region.** The read limits apply per record, and a stored absolute pointer address counts
  from the record's first byte - so a pointer's target must lie inside its own record's bytes, which is what a
  record-per-message format guarantees and a shared-table format does not.

The inputs are memory, a `ReadOnlySequence<byte>` (one segment in place, a chain through one pooled copy), and a
stream: a fixed-size composite is read from any readable stream exactly one record at a time, byte for byte, and a
runtime-sized one through a pooled window that refills from the start of a record it could not hold, which needs
a seekable stream. `RecordsAsync(stream)` is the same over `await foreach`, with a cancellation token checked
between records. Every composite has `Records<Name>`/`Records<Name>Async`; the runtime's `ParseMany` reads the same
sequences as `StructValue`s, with the same rules and the same failure texts.

## The view enumerator

A loop that reads one field from each of a million records should not create a million objects.
`Wire.HeaderView.Enumerate(bytes)` returns a `foreach`-able enumeration whose `Current` is a `HeaderView` over the
next record - a [view](views-and-zero-allocation.md), so nothing is allocated for it either.

`foreach` does not require `IEnumerable<T>`. It requires a `GetEnumerator()` method whose result has `MoveNext()`
and `Current`, and it is happy for both to be `ref struct`s - types that live on the stack and cannot escape the
method. That is what makes the enumeration allocation-free: the enumerable, the enumerator, and every view are
stack values that vanish when the loop ends. It also means they cannot be stored in a field, captured by a lambda,
or handed to LINQ; for those, `Records` returns objects.

The enumerator exists for composites with a static size only (a view needs one), advances by that size, and fails
on trailing bytes exactly as `Records` does. On the reference machine, walking 256 records through the enumerator
costs what the hand-written offset loop costs and allocates 0 bytes; `Records` over the same bytes allocates one
48-byte class per record, the same as a `Parse` loop. The [performance page](../performance.md) has the measured
table.

## ParseAsync and WriteAsync on the class

[!code-csharp[ParseAsync and WriteAsync](../../examples/GeneratedExamples.cs#generated-async)]

`Wire.ParseAsync(stream)` reads the stream with `ReadAsync` into a pooled buffer - a seekable stream up to its
remaining length, any stream up to `MaxTotalBytesRead` plus one byte - and runs the generated reader over it:
the same value, limits, and messages as `Parse(stream)`, with the thread free while the bytes arrive. A seekable
stream ends after the value and returns to its origin on a failure. `Wire.WriteAsync(stream, header)` serializes
first and writes once, so a validation failure writes nothing. The [async guide](../async-and-pipelines.md)
explains the rule, the position, the pointer coordinates, and cancellation in full.

## Check yourself

1. `Wire.TryParse(bytes, out var header, out var failure)` returns `false`. What is `header`, and what is `failure`?
2. Which of these can `Records` do that the view enumerator cannot: read a runtime-sized composite, read from a
   stream, be passed to `.Select(...)`, avoid allocating?
3. Two bytes follow the last whole record. Which step of the `foreach` throws, and what does its path say?

<details>
<summary>Answers</summary>

1. `header` is `null`; `failure` is the `CStructReadException` (or path or limit exception) that `Parse` would have thrown.
2. The first three. Only the view enumerator avoids allocating.
3. The step after the last whole record - the fourth `MoveNext` for three records - with the path `[3].header`.

</details>

## Exercise

Add a `uint8 flags;` member to the header layout, making it seven bytes, and enumerate the same eighteen bytes.
Predict what happens before you run it: the stride is now 7, so the third step fails with "The remaining 4 bytes
are not a whole number of 7-byte elements" at `[2].header`. Then fix the bytes.
