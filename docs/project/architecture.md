---
title: Architecture and execution flow
description: Follow a layout from source text through validation and preparation to a read, write, or update.
---

# Architecture and execution flow

CStructSharp separates work that depends only on the layout from work that depends on one input. This is why a
completed `CStruct` can be reused for many records.

## From layout text to a reusable object

Constructing [`CStruct`](xref:CStructSharp.CStruct) has four stages:

1. **Parse the source.** `CStructDefinitionParser` hands the text to `LayoutParser`, a hand-written
   recursive-descent parser: one cursor over the source, direct character tests, and one method per grammar
   production. It builds the small model classes (`Struct`, `Field`, `Enum`, `Typedef`, `Defines`, and the `Expr`
   tree) the later stages consume, and reports every syntax error as a `CStructLayoutException` with a line and
   column. The test project keeps a parser-combinator grammar as a frozen reference and parses the whole
   fixture corpus through both to check accepted syntax and resulting trees. Explicitly documented exceptions
   cover intentional differences; the Portable contract defines the supported language.
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
debug, read, and write paths from each interpreting the source in a different way.

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

1. copies the supplied integer variables and option values;
2. creates one per-call state object for the stream, limits, nested depth, pointer traversal, and optional debug
   ranges;
3. resolves the requested root or path with its array, union, bitfield, alignment, and pointer context; and
4. executes the prepared field readers.

All reads use the same compiled layout facts. Eligible fixed composites execute cached read plans; dynamic
layouts and debug reads use general traversal. Numeric arrays can decode in blocks into `PrimitiveArray<T>`;
struct results use a shared member shape with per-result values in `StructValue`. A typed read can fill its
C# destination directly when the fixed layout and target type support that plan.
Debug capture uses the general reader to record the fields and ranges it visits.

Writers use the same prepared field shapes in reverse. `Serialize` stages through owned memory when returning an
array. Fixed-struct write plans encode into one block before writing; other shapes use field traversal.
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

The completed layout is immutable and safe for concurrent use. Per-call state is separate. Mutable resources supplied
by the application are not made thread-safe:

- streams and output writers need exclusive use for the complete call;
- dictionaries must not change while they are being copied;
- mapped-class instances, dynamic objects, and collections being written must not change during the write; and
- returned dynamic and debug values belong to that call.

Internal class names on this page help contributors navigate the source; they are not public APIs. Public behavior is
defined by the documented operations, generated API signatures, and executable tests.

Use [Testing](testing.md) to see how the shared paths are checked, or [Debugging](debugging.md) to trace a failure
through these stages.
