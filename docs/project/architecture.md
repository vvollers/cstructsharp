---
title: Architecture and execution flow
description: Follow a layout from source text through validation and preparation to a read, write, or update.
---

# Architecture and execution flow

CStructSharp separates work that depends only on the layout from work that depends on one input. This is why a
completed `CStruct` can be reused for many records.

## From layout text to a reusable object

Constructing [`CStruct`](xref:CStructSharp.CStruct) has four stages:

1. **Parse the source.** `LayoutParser` reads the text. It is a hand-written
   recursive-descent parser: one cursor over the source, direct character tests, and one method per grammar
   production. It builds the small model classes (`Struct`, `Field`, `Enum`, `Typedef`, `Defines`, and the `Expr`
   tree) the later stages consume, and reports every syntax error as a `CStructLayoutException` with a line and
   column. The Portable contract defines the supported language. The test project parses a corpus of every layout
   in the repository, and thousands of mutated copies of it, to check that each input either parses or fails with
   a syntax diagnostic; recorded syntax trees pin the accepted token spellings.
2. **Check meaning.** The constructor resolves names and aliases, checks expression dependencies and value ranges,
   rejects recursive by-value storage, and confirms that each declaration has supported behavior.
3. **Prepare the layout.** `CStructCompiledModel` records field order, direct value codecs, array counts and strides,
   pointer depth, alignment, offsets, sizes, and bit slices. A *codec* is the small reader/writer rule that converts
   a declared primitive between bytes and a CLR value.
4. **Publish the completed object.** Only after every check passes does the constructor make the reusable layout
   available.

An expected failure during these stages becomes
[`CStructLayoutException`](xref:CStructSharp.Diagnostics.CStructLayoutException). No stream or payload has been accepted yet, so
an invalid layout cannot partially read or write binary data.

Parsed syntax objects help with diagnostics, but operations use the prepared model. This prevents the size, address,
debug, read, and write paths from each reading the source in a different way.

The source tree mirrors these stages: each folder under `src/CStructSharp` is a namespace (`Parsing`, `Syntax`,
`Expressions`, `Compilation`, `Codecs`, `Streams`, `Addressing`, `Reading`, `Writing`, `Values`, `Introspection`,
`Diagnostics`, `Memory`, `Generated`), and `src/CStructSharp/README.md` maps each folder to its role.

## One core, two hosts

Stages 1 to 3 need no bytes, so their code lives in `src/CStructSharp.Core/`: a source folder, not a project,
that two assemblies compile. The runtime library includes it and adds the I/O halves (streams, codec delegates,
readers, writers, values). The source generator (`src/CStructSharp.Generators`, `netstandard2.0`, loaded into the
C# compiler where the runtime library is not available) includes the same files and adds an emitter: for every
`[CStructLayout]` class it runs stages 1 to 3 on the layout text at build time, then writes C# readers, writers,
views, and typed setters from the compiled model - the same offsets, the same placement rules, and the same
failure texts (`ReadFailures`/`WriteFailures` are Core). At run time the generated code goes through the small
`CStructSharp.Generated` cursors so limits and diagnostics stay the runtime's.

`tests/CStructSharp.Generated.Parity` holds the generator to the runtime: every layout fixture is generated into
one project, and each generated `Parse` and `Serialize` is compared with the runtime's on the same bytes, including
a truncation sweep that expects the same exception text at every cut. [How the generator works](../guides/generated/how-it-works.md)
describes the pipeline for users; [Testing](testing.md) describes the snapshot and parity workflow.

## What happens during an operation

A public read-like call:

1. copies the supplied integer variables and option values into the layout's numbered variable slots;
2. creates one per-call state for the source (a memory or stream cursor), limits, nested depth, pointer traversal,
   and optional debug ranges;
3. resolves the requested root or path with its array, union, bitfield, alignment, and pointer context; and
4. runs the compiled engine: the program of each struct it reaches, built once per layout from the compiled model
   and cached with it (`src/CStructSharp/Engine/`, programs in `src/CStructSharp.Core/Compilation/Programs/`).

All reads use the same compiled layout facts. A whole fixed-layout struct in memory is decoded directly from the span
without the per-call state; inside other reads, fixed composites execute cached static read plans. Numeric arrays
can decode in blocks into `PrimitiveArray<T>`; struct results use a shared member shape with per-result values in
`StructValue`. A typed read can fill its C# destination directly when the fixed layout and target type support that
plan. A debug read runs each struct's debug program, which records the fields and ranges it visits. Writes run write
programs the same way, and an update resolves its path and captures its layout with read programs, then stages its
replacement with a write program.

The awaitable forms and the record sequences add no reader. An `*Async` read links the call's token with the
options' token, reads the stream with `ReadAsync` into a pooled buffer (`Streams/AsyncStreamBuffer`: first a
seekable stream up to its remaining length, any stream up to the budget plus one byte), runs the engine over the
pinned buffer, and sets the stream's position from where the read ended. The budget charges consumed bytes, not
positions, so a buffered read can need bytes past its copy; `Streams/BufferedInput` marks such a copy as part of a
longer input (`ReadOptions.ContinuedInputLength`), the memory cursor and the generated `ReadCursor` raise an internal
signal on any access past it, and the buffered form - a segmented sequence, an async read, a generated stream read,
a record window - grows the copy and runs the read again until it equals the span read. A record
sequence (`Generated/RecordSequence`, shared by `ParseMany` and the generated `Records`) drives a one-record
reader from the end of the previous record - the runtime's reader pins a slice and runs the stream core over it,
the generated one runs its cursor over it - checks a fixed-size root against the bytes left before reading it, and
reads a stream either one fixed-size record at a time or through a pooled window that refills from the record it
could not hold.

Writers use the same prepared field shapes in reverse. `Serialize` stages through owned memory when returning an
array. Fixed-struct write plans encode into one block before writing; other shapes run each struct's cached write
program, step by step, into the destination.
Span, writer, and stream overloads still have the partial-output limits documented in the API guides: a validated
block does not make a physical stream transactional.

## Why updates use staging

`Update` must inspect existing bytes to find a path, but it should not change the destination before it knows
the replacement is valid. It therefore runs the writer against `SparseUpdateStream`, a bounded copy-on-write view.
That view records changed ranges while reading unchanged bytes from the original stream. A contiguous replacement
uses one pooled buffer; separated ranges fall back to chunk staging.

After path, range, shape, pointer, union, and limit checks pass, the method combines adjacent changed ranges and
commits them in address order. A physical destination can still fail partway through that final commit; generic
streams do not provide reliable rollback.

## Ownership and concurrency

The completed layout is immutable and shared; everything an operation changes lives in its per-call state or in the
caller's own resources, which the library does not make thread-safe
([concurrency and ownership](../language/compilation-and-operations.md#concurrency-and-ownership)).

Internal class names on this page help contributors navigate the source; they are not public APIs. Public behavior is
defined by the documented operations, generated API signatures, and executable tests.

Use [Testing](testing.md) to see how the shared paths are checked, or [Debugging](debugging.md) to trace a failure
through these stages.
