---
title: Architecture and execution flow
description: Follow a layout from source text through validation and preparation to a read, write, or update.
---

# Architecture and execution flow

CStructSharp separates work that depends only on the layout from work that depends on one input. This is why a
completed `CStruct` can be reused for many records.

## From layout text to a reusable object

Constructing [`CStruct`](xref:CStructSharp.CStruct) has four stages:

1. **Parse the source.** `CStructDefinitionParser` uses Pidgin to recognize the supported declarations and
   expressions. Pidgin is a parser-combinator library: it lets the project build a parser from small C# parsing
   functions.
2. **Check meaning.** The constructor resolves names and aliases, checks expression dependencies and value ranges,
   rejects recursive by-value storage, and confirms that each declaration has supported behavior.
3. **Prepare the layout.** `CStructCompiledModel` records field order, direct value codecs, array counts and strides,
   pointer depth, alignment, offsets, sizes, and bit slices. A *codec* is the small reader/writer rule that converts
   a declared primitive between bytes and a CLR value.
4. **Publish the completed object.** Only after every check passes does the constructor make the reusable layout
   available.

An expected failure during these stages becomes
[`CStructLayoutException`](xref:CStructSharp.CStructLayoutException). No stream or payload has been accepted yet, so
an invalid layout cannot partially read or write binary data.

Parsed syntax objects help with diagnostics, but operations use the prepared model. This prevents the size, address,
debug, read, and write paths from each interpreting the source in a different way.

## What happens during an operation

A public read-like call:

1. copies the supplied integer variables and option values;
2. creates one per-call state object for the stream, limits, nested depth, pointer traversal, and optional debug
   ranges;
3. resolves the requested root or path with its array, union, bitfield, alignment, and pointer context; and
4. executes the prepared field readers.

Root reads, selected nested reads, array elements, and pointer targets share the same composite traversal code.
Debug capture observes that read rather than running a separate interpretation of the layout.

Writers use the same prepared field shapes in reverse. `Serialize` stages through owned memory when returning an
array. Span, writer, and stream overloads write to caller-provided destinations with the partial-output rules
documented in the API guides.

## Why updates use staging

`UpdateStream` must inspect existing bytes to find a path, but it should not change the destination before it knows
the replacement is valid. It therefore runs the writer against `SparseUpdateStream`, a bounded copy-on-write view.
That view records changed ranges while reading unchanged bytes from the original stream.

After path, range, shape, pointer, union, and limit checks pass, the method combines adjacent changed ranges and
commits them in address order. A physical destination can still fail partway through that final commit; generic
streams do not provide reliable rollback.

## Ownership and concurrency

The completed layout is immutable and safe for concurrent use. Per-call state is separate. Mutable resources supplied
by the application are not made thread-safe:

- streams and output writers need exclusive use for the complete call;
- dictionaries must not change while they are being copied;
- POCOs, dynamic objects, and collections being written must not change during the write; and
- returned dynamic and debug values belong to that call.

Internal class names on this page help contributors navigate the source; they are not public APIs. Public behavior is
defined by the documented operations, generated API signatures, and executable tests.

Use [Testing](testing.md) to see how the shared paths are checked, or [Debugging](debugging.md) to trace a failure
through these stages.
