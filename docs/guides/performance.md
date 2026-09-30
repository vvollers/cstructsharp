---
title: Use CStructSharp efficiently
description: Avoid repeated layout preparation, unnecessary decoding, stream adapters, and output allocation.
---

# Use CStructSharp efficiently

Start with the API that makes ownership and failure handling clear. Measure the real workload before replacing it
with a lower-allocation overload.

The highest-value choices are usually:

1. Construct a `CStruct` once and reuse it for records with the same format.
2. Read one path with `ReadValue` when later fields are irrelevant.
3. Use span or memory input when bytes are already in memory.
4. Use the `byte[]` serialization overload unless an allocation measurement justifies caller-provided output.
5. Request debug ranges only in diagnostic paths.
6. Map to a class only when typed application code needs it. `ReadValue<T>` parses the value (through the static
   read plan when the layout is fully fixed) and hands it to the class's own `ReadFrom`; the mapping itself is the
   code the generator or you wrote, with no reflection to pay for.
7. Resolve paths you read repeatedly once, with `GetAccessor<T>`, and read members without parsing through
   `CreateView` ([read the same members many times](reading-values.md#read-the-same-members-many-times)). <!-- facts:accessor-costs:start -->In the repository's serializer comparison (a 79-byte record, every member read), `Parse` followed by path-string reads took 350 ns, `Parse` followed by accessor reads took 130 ns, and `CreateView` with accessors took 28.5 ns without allocating.<!-- facts:accessor-costs:end -->
8. Generate the layout when it is part of the program: a `[CStructLayout]` class parses straight into typed
   properties, and its view reads members without allocating (the "Generated" table below and
   [runtime or generated?](generated/choosing-runtime-or-generated.md)).

Selected reads can avoid decoding unrelated later siblings, but they still perform the work needed to locate the
target. Runtime arrays, alignment, terminated strings, and pointers before the selected field must still be read
to find where that field starts.

Span output avoids creating the final result array but requires enough capacity. `IBufferWriter<byte>` can append
through pooled windows. Both have partial-output behavior on late failure, so allocation is not the only tradeoff.

Use `benchmarks/CStructSharp.Benchmarks` and BenchmarkDotNet for changes to a hot path. Compare time and allocation with the same
layout, payload, target framework, build configuration, and operation. A small benchmark that removes validation or
changes who owns the output is measuring a different workload, so its numbers are not a fair comparison.

Do not add an application cache of mutable streams or results around a reusable layout. Reuse the immutable
`CStruct`; keep per-operation data owned by the caller.

See [Spans, memory, and buffer writers](spans-and-memory.md) for ownership details and the
[project testing guide](../project/testing.md#performance-packages-and-release-checks) for repository benchmark
expectations.

## Typical costs

<!-- typical-costs:start -->
The medians below come from the repository's BenchmarkDotNet cases (`benchmarks/CStructSharp.Benchmarks`,
`Short` job, Release build), recorded in `benchmarks/CStructSharp.Benchmarks/typical-costs.json`; they show
the order of magnitude of each operation, not a guarantee. Most managed operations allocate their result plus
some per-call state (options snapshot, variable slots, budget stream); the Allocated column shows the total. A
whole fixed-layout struct read from or written to memory skips that state and allocates only its result.

| Operation | Median | Allocated |
| --- | ---: | ---: |
| Compile a two-field struct (`struct root { uint8 kind; uint32 value; };`) | 3.30 µs | 15,848 B |
| Compile the PNG header fixture (an enum and two structs) | 19.1 µs | 47,776 B |
| `GetOrCompile` hit for the same source (cache lookup) | 254 ns | 0 B |
| `Parse` a five-byte record with a `count`-sized array from memory | 115 ns | 392 B |
| `ReadValue<T>` of the same record into a mapped class | 274 ns | 560 B |
| `ReadValue<ushort>` of one selected field | 153 ns | 208 B |
| `Parse` a 1 KiB `uint8[1024]` (one `PrimitiveArray`) | 116 ns | 1,264 B |
| `ResolveAddress` of `items[127]` in a fixed nested array | 131 ns | 144 B |
| `ParseWithDebug` of the PNG fixture (byte ranges for every value) | 895 ns | 5,200 B |
| Truncated input: `Parse` throws and the caller catches | 5.38 µs | 1,976 B |
| `Serialize` a mapped class into a caller-provided span | 122 ns | 304 B |
| `Update` one value behind a pointer in place | 313 ns | 424 B |
| `Parse` a 16 MiB record from a `MemoryStream` | 1.06 ms | 16.0 MiB |

Measured 2026-09-30 on AMD Ryzen 9 9950X, .NET 10.0.12 (10.0.12, 10.0.1226.42308), Windows 11 (10.0.26200.9550/25H2/2025Update/HudsonValley2).

A layout on a `[CStructLayout]` class is read by generated code instead ([generated code](generated/index.md)).
The same bytes four ways - the runtime `Parse`, the generated `Parse`, a generated view, and hand-written
`BinaryPrimitives` code - then the runtime/generated pairs for a write, an update, and a debug read
(`GeneratedBenchmarks`):

| Operation | Median | Allocated |
| --- | ---: | ---: |
| Runtime `Parse` of the 28-byte primitives record (`StructValue`) | 36.4 ns | 256 B |
| Generated `Parse` of the same record (the typed class) | 7.14 ns | 48 B |
| Generated view of the same record (every member read, nothing allocated) | 0.67 ns | 0 B |
| Hand-written `BinaryPrimitives` reader of the same record | 1.41 ns | 0 B |
| Runtime `Parse` of 256 nested records (6,400 bytes) | 29.9 µs | 212 KiB |
| Generated `Parse` of the 256 nested records | 3.81 µs | 57,392 B |
| Generated view over the 256 nested records (one view per element by offset) | 199 ns | 0 B |
| Hand-written reader of the 256 nested records | 209 ns | 0 B |
| Runtime `Serialize` of the record from a `StructValue` | 45.2 ns | 56 B |
| Generated `Serialize` of the record from the typed class | 5.50 ns | 56 B |
| Runtime `Update` of one field by path | 234 ns | 480 B |
| Generated typed setter for the same field (`Update.C`) | 3.18 ns | 0 B |
| Runtime `ParseWithDebug` of the record | 336 ns | 1,552 B |
| Generated `ParseWithDebug` (the generated value plus the runtime's ranges) | 336 ns | 1,600 B |

The generated `Parse` allocates the typed class and nothing else; the view allocates nothing and sits next to
the hand-written reader because it is the same code with the offsets filled in. `ParseWithDebug` costs a
runtime read on top of the generated one (the ranges come from the runtime). Use the generated path when the
layout is in the program's source and the read is hot; [runtime or generated?](generated/choosing-runtime-or-generated.md)
has the full decision table.

The awaitable forms read the stream with `ReadAsync` into a pooled buffer and run the same reader over it, so
their cost is the synchronous read plus the buffering and the state machine; a record sequence parses one
record per step (`AsyncBenchmarks`, `SequenceBenchmarks`; [async and pipelines](async-and-pipelines.md),
[sequences and TryParse](generated/sequences-and-try-parse.md)):

| Operation | Median | Allocated |
| --- | ---: | ---: |
| Runtime `Parse(Stream)` of the 28-byte record from a `MemoryStream` | 94.1 ns | 360 B |
| Runtime `ParseAsync` of the same stream (read in place, the task already complete) | 116 ns | 416 B |
| Runtime `ParseAsync` of a stream that hides its buffer (one pooled copy) | 113 ns | 416 B |
| Runtime `ParseAsync` of a `FileStream` opened for asynchronous I/O | 12.6 µs | 974 B |
| Runtime `Write(Stream)` of the record | 85.0 ns | 64 B |
| Runtime `WriteAsync` of the record (serialized first, one `WriteAsync`) | 56.9 ns | 56 B |
| Runtime `Update(Stream)` of one field | 222 ns | 424 B |
| Runtime `UpdateAsync` of the same field (the region buffered, the changed run written back) | 353 ns | 568 B |
| Generated `Parse(Stream)` of the record | 19.2 ns | 48 B |
| Generated `ParseAsync` of the same stream | 33.6 ns | 48 B |
| Runtime `Parse` in a loop over 256 consecutive records (7,168 bytes) | 9.05 µs | 65,536 B |
| Runtime `ParseMany` over the same 256 records | 19.4 µs | 78 KiB |
| Generated `Parse` in a loop over the 256 records | 2.02 µs | 12,288 B |
| Generated `Records` over the same 256 records | 2.42 µs | 12,464 B |
| Hand-written offset loop over 256 views (two members read each) | 110 ns | 0 B |
| Generated view enumerator (`RootView.Enumerate`) over the same 256 records | 116 ns | 0 B |

A `MemoryStream` that exposes its buffer is read in place and the `ValueTask` is already complete when it is
returned; a file pays the real asynchronous I/O. `ParseMany` and the generated `Records` run the loop a caller
would otherwise write; compare each with the plain loop above it to see what the convenience costs. The view
enumerator allocates nothing, like the hand-written offset loop.

The JavaScript package pays a WebAssembly crossing per call unless the layout is fully fixed and no option
is set, in which case `parse` reads it in JavaScript (see [many records in one call](browser/large-data.md#many-records-in-one-call)):

| Operation | Median |
| --- | ---: |
| `parse` of a fixed 28-byte record (JavaScript fast path, no WebAssembly call) | 2.29 µs |
| `parse` of the PNG header fixture, 33 bytes (fast path) | 1.95 µs |
| `parse` of 256 nested records, 6,400 bytes (fast path) | 88.2 µs |
| `parse` of a record with four terminated strings, 6,592 bytes (one WebAssembly call) | 132 µs |
| `parseWithDebug` of the 28-byte record (WebAssembly) | 46.0 µs |
| `serialize` of the 28-byte record (WebAssembly) | 39.5 µs |
| `update` of one scalar in the 28-byte record (WebAssembly) | 33.4 µs |

Measured 2026-09-30 in Node 26.8.1 with `benchmarks/js` (`npm run bench:node`).

The browser runtime (the WASM publication the npm package and the standalone bundle ship) is 4.2 MiB across 27 files, 1.6 MiB gzip-compressed; it is downloaded once and cached by the browser.
<!-- typical-costs:end -->

## How a layout is run: the compiled engine

A layout describes a record, but the description itself says nothing about speed. The slow way to read a record is
to walk the declarations for every call: look at the next member, decide whether it is a number, an array or a nested
struct, work out where it starts, find its name among the variables, read it, and repeat. Almost none of those
decisions depend on the bytes being read, so making them again on every call is wasted work.

CStructSharp makes them once. The first time a root is read, written or updated, each struct it reaches is
*compiled* into a **program**: a flat list of small **steps**, each of which does exactly one thing - read a
little-endian `uint32` into member `id`, store a value in a named variable, evaluate an array count, check the
element limit, skip to the next aligned offset. Placement that does not depend on the data is worked out while the
program is built, so a member at a fixed offset needs no placement step at all. Names that expressions use (such as
`count` in `values[count]`) are numbered when the layout is compiled, so the running program finds a variable by
its number instead of looking up a string. The programs are cached with the layout, and every later call runs them.

Take this aligned layout:

```c
struct sample { uint32 id; uint16 count; int16 values[count]; uint8 flags; };
```

Its program has seven steps:

| Step | Member | What it does |
| --- | --- | --- |
| Read a little-endian `uint32` | `id` | 4 bytes at offset 0 |
| Read a little-endian `uint16` | `count` | 2 bytes at offset 4 |
| Capture an integer | `count` | stores the value in the variable `count` for the next step |
| Evaluate a count | `values` | computes `count`, rejects a negative value, checks `MaxArrayElements` |
| Read a numeric array | `values` | reads all the elements as one block (a `PrimitiveArray<short>`) |
| Read a `uint8` | `flags` | 1 byte, right after the array |
| Finish the struct | - | pads the end to a multiple of 4, the struct's alignment |

Reading the 12 bytes `07 00 00 00 02 00 E8 03 FE FF 01 00` runs the steps in order and produces
`{ id = 7, count = 2, values = [1000, -2], flags = 1 }`. Nothing was decided about `id` or `count` during the read:
the program already knew that both come first and at which offsets. Only `values` depends on the data, so only its
count is evaluated while reading.

The same program runs over every source. Memory (a span, an array, `ReadOnlyMemory<byte>`, a sequence, a
`MemoryStream` that exposes its buffer) is read in place; any other seekable stream is read through a bounded stream
reader with the same byte budget and failure positions. Writing works the same way with write programs, and an update
reads with one program and writes with another.

Two faster paths stay in front of the engine, and the engine only runs when neither applies:

- A **whole fixed-layout struct** read from memory or written to memory, with no variables and no debug records, is
  decoded or encoded straight between the span and the result, without the per-call state the engine sets up.
- A **fixed struct** met inside a larger read or write (a record, an array element, a nested member) runs a
  *static plan*: all its members at their compile-time offsets in one pass over one block of bytes.

The engine runs its programs one step at a time; it does not generate machine code at run time, which keeps the
library usable with Native AOT, trimming and WebAssembly. Code the source generator writes for a `[CStructLayout]` class is still faster,
because every step is written out as ordinary C# that the compiler optimizes
([runtime or generated?](generated/choosing-runtime-or-generated.md)).

## Reuse layouts safely

Construct a `CStruct` once and share it: it is immutable, safe for concurrent operations, and saves parsing and
preparing the layout for every record, while each operation still needs exclusive use of its own stream, output, and
mutable values ([concurrency and ownership](../language/compilation-and-operations.md#concurrency-and-ownership)).

## Many records of one shape

Most operations set up their own bounded context (budget stream, variables, result container) before they decode a
byte. That fixed cost is small in absolute terms - a few hundred nanoseconds and well under a kilobyte - but it
dominates when the record itself is tiny. A whole fixed-layout struct read from memory without variables is the
exception: `Parse(bytes, "record")` skips the context and decodes the record straight from the span. For other
records - read from a stream, given variables, or holding a runtime-sized array or a string - let the layout express
the repetition instead of calling `Parse` per record:

```c
struct record { uint16 kind; uint32 length; uint8 flags; };
struct file { record records[EOF]; };
```

`layout.Parse(bytes, "file")` reads every whole record to the end of the input in one operation, and a fixed
record shape takes the span-based array path. Measured on the repository benchmark machine for 1,000 of the
seven-byte records above: 21 ns and 161 bytes per record through `records[EOF]`, and 23 ns and 152 bytes per record
for one `Parse(bytes.AsSpan(offset, 7), "record")` call each, because that record is fixed and in memory. Use
`records[count]` when a header supplies the count, and `ReadValue(bytes, "file.records[7]")` when only one record is
needed.

When the records must be handled one at a time - a file too large to hold as one value, a loop that stops early,
a stream that is still arriving - `ParseMany` and the generated `Records` parse one record per step and cost what
the per-record `Parse` loop costs (each record is its own operation, with its own context). The generated view
enumerator (`RootView.Enumerate(bytes)`) is the exception: it allocates nothing and costs what a hand-written
offset loop costs, so a scan that reads a field or two from each of a million records should use it. The async
and sequences table above has the measured rows; the [async guide](async-and-pipelines.md) and the
[sequences lesson](generated/sequences-and-try-parse.md) explain the forms.

## CPU features and vector instructions

A *vector* (SIMD, single instruction, multiple data) instruction processes several numbers at once: one
AVX2 instruction can reverse the byte order of eight 32-bit integers. CStructSharp has no processor-specific code of
its own. It relies on .NET routines that the JIT compiler (which turns .NET code into machine code while the program
runs) specializes for the processor it is running on: SSE and AVX2 or AVX-512 on x64, and AdvSimd on Arm64.

- An array of 2-, 4- or 8-byte numbers in the machine's own byte order is copied as one block of memory. In the other
  byte order, it is copied and then reversed with the vectorized `BinaryPrimitives.ReverseEndianness`, both when
  reading and when writing.
- A single big-endian number on a little-endian machine compiles to one byte-swap instruction (`bswap` or `movbe`
  on x64, `rev` on Arm64).
- A record of individual fields gains little from vectors: most of its cost is creating the result objects. The
  direct paths for whole fixed-layout structs and the generated fixed readers are what make those cheap.

Native AOT compiles the program before it runs, so it cannot see the target processor. By default it targets a
conservative instruction set that runs everywhere. If every machine the program will run on supports a newer set, set
`<IlcInstructionSet>` in the project file (for example `x86-x64-v3` for AVX2-class x64 machines) so that the same
routines can use wider vectors; see [Optimize AOT deployments](https://learn.microsoft.com/dotnet/core/deploying/native-aot/optimizing).

## Managed layout caching

When you already retain a `CStruct`, keep using it. When a call site repeatedly receives the same layout text,
`CStruct.GetOrCompile(definition, pointerSize: 8, aligned: false, isLittleEndian: true)` reuses a prepared
instance instead of constructing one every time.

The current cache holds at most 64 entries and a total source-text budget of 8,388,608 characters. Its key
includes the exact source, pointer width, alignment, byte order, and compilation limits. This is a retention
budget, not an exact managed-memory cap. Failed compilations are not cached. Entries can be evicted, so a cache
hit is an optimization rather than an identity guarantee.

`CStruct.ClearCompiledCache()` releases the cache's references. Existing returned instances remain usable.
The cache does not retain input bytes, stream positions, or parsed results. Layout reuse is safe across calls;
mutable streams and result objects still need separate ownership. The browser bridge uses the same kind of
bounded cache within each runtime.

## JavaScript compiled reuse

The record idiom above applies to JavaScript with larger stakes, because a WebAssembly crossing costs tens of
microseconds: 1,000 seven-byte records parse in one `records[1000]` call at 0.06 µs per record on the JavaScript
fast path, or 2.6 µs per record as `records[EOF]`, against 2.4-37 µs per record when each record is its own call
(see [many records in one call](browser/large-data.md#many-records-in-one-call)).

For repeated reads of the same schema, use `compile` from the npm package or standalone browser bundle:

```js
const layout = await compile(definition, { root: "root" });
try {
  for (const bytes of records) {
    const result = await layout.parse(bytes);
    if (!result.success) throw new Error(result.error.message);
    consume(result.data);
  }
} finally {
  await layout.dispose();
}
```

Compilation owns a dedicated worker and runtime. Reads reuse the compiled layout; `parseWithDebug` adds byte mappings.
One handle queues its worker reads and keeps operation state separate. Small byte reads without a signal can use
the shared calling-thread runtime. Layout options are fixed, while each read
can choose limits and an abort signal. Cancellation stops an active worker; a later read recreates it and compiles the layout again.
Dispose unused handles promptly: each retains a runtime. Ordinary source parsing also reuses a worker for 30 seconds
of idle time. Managed bridge calls use a bounded compiled-layout cache, so repeated ordinary calls can also reuse
preparation while their entries remain cached. A retained handle explicitly owns a layout for its worker reads;
it is not the only way to benefit from caching.

Compare cold startup separately from warmed reads. Worker messaging, source staging, result materialization and JSON
projection remain real costs even with a retained layout. Conditional groups are selected once on entry; their cost
also depends on the number of fields, nested scopes and active payload, so compare equivalent data and layouts.
