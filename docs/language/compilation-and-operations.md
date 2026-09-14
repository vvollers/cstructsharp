---
title: How layouts are prepared and reused
description: Understand which work happens once in the constructor and which state belongs to one operation.
---

# How layouts are prepared and reused

Creating a `CStruct` prepares the source for later operations. The documentation sometimes calls this *compiling the
layout*. It prepares binary-layout metadata; it does not run a C compiler.

## Constructor-time work

The constructor:

1. parses the supported source syntax;
2. resolves declarations, names, aliases, pointers, and expressions;
3. checks enum, array, bitfield, recursion, and finite-storage rules; and
4. records field order, codecs, alignment, fixed or runtime offset/size rules, array counts and strides, and bit
   slices in an immutable internal model.

A codec is the read/write rule for one primitive or enum backing type. A stride is the number of bytes from one array
element to the next, including any required tail padding.

If preparation fails, the constructor throws `CStructLayoutException` and no usable layout is returned. Parsed
declaration dictionaries exposed for diagnostics are read-only views; parse, size, address, write, and update
operations use the prepared model.

Reuse a completed `CStruct` for every record that follows the same format options. Reconstructing it per record
repeats parsing and name/layout work.

## Reuse and cached plans

Keep the prepared object when possible. `CStruct.GetOrCompile` also offers bounded shared reuse when your
application repeatedly starts from definition text; see [managed caching](../guides/performance.md#managed-layout-caching).
Compilation prepares layout metadata, not native C machine code. The .NET runtime can separately compile
implementation methods and object-access delegates as part of executing the library.

Fixed layouts can use cached read and write plans with known offsets. Eligible typed reads fill C# objects
directly. Variable-sized and conditional layouts use general traversal where their positions depend on data.
These choices preserve layout rules; they are not extra syntax the application needs to select.

## Per-operation work

Each read-like call creates private state for:

- copied runtime variables and options;
- the stream or memory region;
- the total physical-read count;
- current struct and pointer depth;
- active pointer targets used for cycle detection;
- bitfield position; and
- optional debug ranges.

Locating a selected path and then reading its value uses the same state, so earlier traversal counts toward the same
limits. A runtime-sized nested struct consumes its actual data extent and final alignment rather than an invented
fixed size.

Struct reads at a root, nested field, array element, or pointer target share one prepared traversal. Union members
share one overlapping storage region; a struct view inside a union begins at that union address and then advances
through its own fields.

## Concurrency

The completed `CStruct` is immutable and supports concurrent operations without one global instance lock.
Concurrency does not extend to mutable caller resources. Each operation needs its own stream, writer, mutable payload
graph, POCO, or collection, or the application must synchronize that resource for the complete call.

Variable dictionaries are copied when an operation starts and must not change during that copy. Returned dynamic and
debug objects belong to one operation and remain mutable application data. Init-only option objects can be shared
after construction.

The contributor [architecture page](../project/architecture.md) maps this flow to internal classes. Those names are
maintenance details, not public API promises.
