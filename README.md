# CStructSharp

<p align="center">
  <a href="LICENSE.txt"><img alt="License" src="https://img.shields.io/github/license/vvollers/cstructsharp"></a>
  <a href="https://www.npmjs.com/package/cstructsharp"><img alt="npm version" src="https://img.shields.io/npm/v/cstructsharp"></a>
  <a href="https://www.npmjs.com/package/cstructsharp"><img alt="npm unpacked size, including WASM" src="https://img.shields.io/npm/unpacked-size/cstructsharp?label=npm%20unpacked"></a>
  <a href="https://www.nuget.org/packages/CStructSharp"><img alt="NuGet version" src="https://img.shields.io/nuget/v/CStructSharp"></a>
  <a href="https://github.com/vvollers/cstructsharp/releases/latest"><img alt="NuGet package download size" src="https://img.shields.io/endpoint?url=https%3A%2F%2Fvvollers.github.io%2Fcstructsharp%2Fbadges%2Fnuget-size.json"></a>
</p>
<p align="center">
  <a href="https://github.com/vvollers/cstructsharp/actions/workflows/ci.yml"><img alt="Managed CI" src="https://github.com/vvollers/cstructsharp/actions/workflows/ci.yml/badge.svg?branch=main&amp;event=push"></a>
  <a href="https://vvollers.github.io/cstructsharp/badges/"><img alt="C# line coverage on .NET 10" src="https://img.shields.io/endpoint?url=https%3A%2F%2Fvvollers.github.io%2Fcstructsharp%2Fbadges%2Fline-coverage.json"></a>
  <a href="https://vvollers.github.io/cstructsharp/badges/"><img alt="C# branch coverage on .NET 10" src="https://img.shields.io/endpoint?url=https%3A%2F%2Fvvollers.github.io%2Fcstructsharp%2Fbadges%2Fbranch-coverage.json"></a>
  <a href="https://vvollers.github.io/cstructsharp/badges/"><img alt="C# test results on .NET 10" src="https://img.shields.io/endpoint?url=https%3A%2F%2Fvvollers.github.io%2Fcstructsharp%2Fbadges%2Ftests.json"></a>
</p>

CStructSharp reads and writes binary data using a description that looks like a C struct. Give it a layout and
some bytes, and it gives you named values. Give it values, and it can create bytes or change a field in existing
data. Use it from C#, Node.js, or JavaScript in a browser.

**Zero runtime package dependencies.** The core .NET library uses only the .NET runtime, keeping integration
simple and your application's dependency tree small.

**Built for performance.** Serialization and deserialization speeds are competitive with hand-written implementations and other libraries. ([See the speed comparison](#speed-compared-with-other-net-serializers))

## Choose your starting point

- [Demo app: binary inspector](https://vvollers.github.io/cstructsharp/inspector/): use a cstruct layout to parse one of your own files in the browser.
- [Browser lessons](https://vvollers.github.io/cstructsharp/explorer/#lesson=header): follow hands on lessons explaining the features or explore the tests for this library.
- [Use C#](https://vvollers.github.io/cstructsharp/docs/guides/install-and-first-parse.html): create a console app.
- [Use JavaScript and WASM](https://vvollers.github.io/cstructsharp/docs/guides/browser/index.html): install the npm package for Node.js or browsers.

## C-Struct serialization language features

A binary format defines the byte layout of a file, packet, or record. CStructSharp uses C-style `struct` declarations to describe and serialize these layouts. Field order follows declaration order; native C structs may also include padding and alignment.

Layouts can depend on their contents: a length field determines the size of a following string, a tag selects a record variant, byte order controls numeric encoding, and bit fields pack flags into a byte. The annotated data-logger example below demonstrates each feature.

```c
/* A data-logger file: a header, a calibration table, and records of two kinds. */
#define MAGIC_SIZE 4                                        /* constants, as in C */

enum record_kind : uint8  { MEASUREMENT = 1, EVENT = 2 };   /* enums with an explicit storage type */
enum sensor_type : uint16 { TEMPERATURE = 0x10, PRESSURE = 0x20 };

typedef struct { uint8 major; uint8 minor; } version;       /* typedef aliases */

struct options {                                            /* bitfields: several values in one byte */
    uint8 compressed : 1;
    uint8 encrypted  : 1;
    uint8            : 2;                                   /* unnamed, reserved bits */
    uint8 priority   : 4;
};

struct header {
    char     magic[MAGIC_SIZE];                             /* fixed-size text: "LOG1" */
    version  ver @4;                                        /* offset assertion: must start at byte 4 */
    uint32>  created;                                       /* big-endian, unlike the rest of the file */
    options  options;
    uint8    name_length;
    utf8     device_name[name_length];                      /* length taken from an earlier field */
    uint8    padding[(4 - (name_length + 12) % 4) % 4];     /* arithmetic: pad to a multiple of 4 bytes */
};

union value32 { uint32 raw; float32 as_float; uint8 bytes[4]; };   /* one storage, three views */

struct record {
    record_kind kind;
    switch (kind) {                                         /* the tag decides which members follow */
        case record_kind.MEASUREMENT: {
            struct { sensor_type sensor; uint8 sample_count; float32 samples[sample_count]; } measurement;
        }
        case record_kind.EVENT: {
            struct { uint16 code; cstring message; } event;  /* cstring: text ending in a zero byte */
        }
    }
};

struct logfile {
    header   hdr;
    int16    calibration[2][3];                             /* two-dimensional array */
    value32  checksum;
    record  *latest;                                        /* pointer: a stored file offset, followed on read */
    uint16   record_count;
    record   records[record_count];                         /* array of records that differ in size */
    if (hdr.options.priority > 7) { uint32 alarm_code; }    /* optional member, chosen by a nested field */
    uint8    trailer[EOF];                                  /* every byte that remains */
};
```

An 81-byte file written with this layout contains, among others:

| Field             | Byte offsets | Bytes               | Value                                                          |
| ----------------- | ------------ | ------------------- | -------------------------------------------------------------- |
| `hdr.magic`       | 0–3          | `4C 4F 47 31`       | `"LOG1"`                                                       |
| `hdr.created`     | 6–9          | `6A B1 3B 80`       | 1,790,000,000 (big-endian)                                     |
| `hdr.options`     | 10           | `91`                | `compressed` = 1, `encrypted` = 0, `priority` = 9              |
| `hdr.device_name` | 12–17        | `70 72 6F 62 65 37` | `"probe7"` (length 6 from `name_length`)                       |
| `checksum`        | 32–35        | `EF BE AD DE`       | `raw` = 0xDEADBEEF, the same bytes also readable as `as_float` |
| `latest`          | 36–43        | `3E 00 … 00`        | offset 62, which is where `records[1]` starts                  |
| `records[0]`      | 46–61        | `01 10 00 03 …`     | a measurement with 3 samples: 21.5, 21.75, 22                  |
| `records[1]`      | 62–74        | `02 F7 01 64 6F …`  | an event: code 503, message `"door open"`                      |
| `alarm_code`      | 75–78        | `07 00 00 00`       | 7 (present because `priority` > 7)                             |
| `trailer`         | 79–80        | `AA BB`             | the remaining two bytes                                        |

Read, navigate, and change it from C#. The same layout text works unchanged in JavaScript and in the browser:

```csharp
var layout = new CStruct(File.ReadAllText("logger.h"));
byte[] file = File.ReadAllBytes("probe7.log");

StructValue log = layout.Parse(file, "logfile");
log.Get<string>("hdr.device_name");                  // "probe7"
log.Get<float>("records[0].measurement.samples[2]"); // 22
log.Get<string>("latest.value.event.message");       // "door open", reached through the pointer
layout.ResolveAddress(file, "logfile.alarm_code");   // 75: where a field lives, without reading it

// Change four bytes in place. An update that would move later fields (such as lowering priority, which removes
// alarm_code) is refused with an error instead of corrupting the file.
layout.Update(file, "logfile.records[0].measurement.samples[1]", 23.5f);
```

If the file is cut short or a length field is corrupt, the read stops with an exception that names the field, its
type, and the byte offset. Limits cap array sizes, string lengths, nesting, pointer chains, and the total bytes
read (by default, for example, one million array elements and 64 MiB per read; every limit is configurable), so a
hostile file cannot make the reader allocate or loop without bound.

### Why not a `[StructLayout]` struct?

.NET can already map bytes onto a struct: declare it with `[StructLayout(LayoutKind.Sequential, Pack = 1)]`
and copy the bytes in with `MemoryMarshal.Read` or `Marshal.PtrToStructure`. That copies memory as it is. It
works when every record has the same size, and every number uses the byte order and width of the computer that
runs the program. The layout above breaks those assumptions on almost every line:

| The format needs                                                                                      | A `[StructLayout]` struct                                                                                           | A CStructSharp layout                                                        |
| ----------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------- |
| Lengths that come from the data (`device_name[name_length]`, `records[record_count]`, `trailer[EOF]`) | Has one fixed size; every variable part is hand-written reading code                                                | Declares the length where the field is                                       |
| Mixed byte order (`uint32> created`)                                                                  | Uses the machine's byte order; each big-endian field is swapped by hand                                             | A `>` or `<` suffix per field                                                |
| Bitfields (`priority : 4`)                                                                            | C# has no bitfields; shift and mask by hand                                                                         | Declared as in C, with C's packing rules                                     |
| A tag that selects the members (`switch`, `if`)                                                       | `[FieldOffset]` can overlap fixed-size members, but nothing checks which one is valid                               | Only the selected members are read, written, and reported                    |
| Text (`char[4]`, `utf8[n]`, `cstring`)                                                                | Needs marshalling attributes or `unsafe` fixed buffers; text that ends in a zero byte cannot live inside the struct | Decoded to `string`; UTF-8 and UTF-16 text is checked strictly               |
| Pointers (`record *latest`)                                                                           | A C# pointer is an address in this process's memory, 4 or 8 bytes depending on the process                          | A stored offset of a configured width, followed with bounds and cycle checks |
| Untrusted input                                                                                       | Copies whatever bytes are there; a corrupt length is found later, or never                                          | Checks bounds and limits, and reports the failing field and offset           |
| A format known only at run time (user-supplied, from a plugin)                                        | The struct must be compiled into the program                                                                        | Load the layout text at run time, or generate C# at build time               |
| Inspecting and editing files                                                                          | No offsets, no way to change one field in place                                                                     | `ResolveAddress`, `ParseWithDebug` byte ranges for hex viewers, and `Update` |
| Other languages                                                                                       | C# only                                                                                                             | The same layout in Node.js and the browser                                   |

When a record really is fixed-size, stored in the machine's byte order, and free of text, `MemoryMarshal` is the
fastest possible reader, and [the comparison below](#speed-compared-with-other-net-serializers) shows it.
CStructSharp's generated code comes close to it on that record while keeping the checks, and it handles everything
in this example as well.

## Read your first value in C#

Install a stable .NET 10 SDK. These commands work in PowerShell or a Unix shell:

```sh
dotnet new console -n BinaryHeader -f net10.0
cd BinaryHeader
dotnet add package CStructSharp
```

Replace `Program.cs` with this complete program, then run `dotnet run`:

```csharp
using CStructSharp;
using CStructSharp.Values;

var layout = new CStruct("struct header { uint16 kind; uint32 length; };");
byte[] bytes = { 0x02, 0x00, 0x06, 0x00, 0x00, 0x00 };
StructValue header = layout.Parse(bytes, "header");

Console.WriteLine($"kind = {header.Get<ushort>("kind")}");
Console.WriteLine($"length = {header.Get<uint>("length")}");
```

Output:

```text
kind = 2
length = 6
```

The layout names the fields. The byte array supplies the data. The result is a `StructValue`: read a member typed
with `header.Get<ushort>("kind")`; `dynamic` field syntax (`header.kind`) also works on the JIT, at the cost of
compile-time checking. The values:

| Field    | Byte offsets | Input bytes   | Value |
| -------- | ------------ | ------------- | ----- |
| `kind`   | 0–1          | `02 00`       | 2     |
| `length` | 2–5          | `06 00 00 00` | 6     |

By default, fields are packed together, numbers use little-endian byte order, and pointers occupy eight bytes.
The [binary layout basics](https://vvollers.github.io/cstructsharp/docs/guides/binary-layout-basics.html) explain these choices.

The same package ships a source generator. Put the layout on a `static partial` class and the compiler produces
typed classes, `Parse`, `Serialize`, in-place setters, and zero-allocation views for it, with the same values and
the same failures as the runtime reader:

```csharp
[CStructLayout("struct header { uint16 kind; uint32 length; };")]
public static partial class Wire { }

Wire.Header header = Wire.Parse(bytes);   // header.Kind == 2, header.Length == 6
byte[] again = Wire.Serialize(header);
```

Every stream form has an awaitable twin - `await layout.ParseAsync(file, "header", cancellationToken: token)` reads
the bytes while the thread is free and decodes them with the same reader - and a file of records is `foreach`
over `Wire.Records(bytes)` or `layout.ParseMany(bytes, "header")`, one record per step.

A `[CStructMapped] partial class` maps a parsed `StructValue` to your own properties by name, and the analyzer
warns about a path string that does not match the layout it is used with. The
[generated code series](https://vvollers.github.io/cstructsharp/docs/guides/generated/index.html) teaches this
path from the first class to the decision between runtime and generated.
For the background, read [how C structs occupy memory](https://vvollers.github.io/cstructsharp/docs/guides/native-c-memory.html) and
[memory addresses and stored data](https://vvollers.github.io/cstructsharp/docs/guides/memory-and-stored-data.html).
See [reading values](https://vvollers.github.io/cstructsharp/docs/guides/reading-values.html) for managed result types and the
[JavaScript API](https://vvollers.github.io/cstructsharp/docs/guides/browser/api.html) for browser results.
Try changing `0x02` to `0x03`: `kind` becomes `3`.

## A portable C struct definition language

Turn a binary format into an executable specification. CStructSharp combines familiar C struct syntax with
portable layout rules, giving you one definition for decoding records, generating bytes, inspecting offsets, and
updating individual fields. Load definitions at runtime and use the same format description from C#, Node.js, or
a browser to build protocol tools, file inspectors, and binary editors.

- **Model rich binary data.** Compose nested structs, overlapping union views, enums with explicit integer storage,
  and reusable `typedef` aliases. Represent values with fixed-width integers, IEEE-754 floats, booleans, bitfields,
  fixed character buffers, and terminated ASCII, UTF-8, or UTF-16 strings.
- **Let the data determine the shape.** Use arithmetic and bitwise expressions, `#define` constants, earlier fields,
  and caller-supplied variables to size one-dimensional arrays. Select conditional fields with `if`/`else` or `switch`. Describe count-prefixed payloads, fixed multidimensional tables, and arrays of structured records directly in the definition.
- **Control the bytes precisely.** Mix little- and big-endian primitives in one record with `<` and `>` suffixes.
  Choose packed or aligned layout, refine alignment with `@align(N)`, reserve bits with unnamed bitfields, and
  assert expected field offsets with `@N`. Type widths follow portable rules, and pointer width is configured
  explicitly, so the format's interpretation stays independent of the host process.
- **Navigate beyond sequential records.** Describe stored pointers, pointer arrays, and multiple levels of
  indirection, and give a pointer its element count with `@count(len)`, even when `len` is stored after it. Read
  targets using absolute or relative addressing, or inspect stored addresses without following them. Select nested
  values with paths such as `packet.samples[2].value` or `root.ptr.value`.
- **Generate the code.** Put a layout on a `[CStructLayout]` class and the source generator in the same package
  writes typed classes, `Parse`/`Serialize`/`Write`, `readonly ref struct` views that allocate nothing, typed
  in-place setters, and size and offset constants at build time - the same parser, the same placement, and the
  same failure texts as the runtime, checked by a parity suite over every fixture. `[CStructMapped]` generates
  the mapping into your own classes, with no reflection, so trimmed and Native AOT publishes need no conventions.
- **Streams and pipelines.** `ParseAsync`, `WriteAsync`, and `UpdateAsync` read and write with `ReadAsync`/`WriteAsync`
  and a `CancellationToken` that is checked at every boundary; `ReadOnlySequence<byte>` input reads a `PipeReader`'s
  buffer in place; `ParseMany` and the generated `Records` walk one record after another lazily, and `TryParse`,
  `TryGet`, and `GetOrDefault` turn expected failures into values instead of exceptions - see
  [async reads, cancellation, and pipelines](https://vvollers.github.io/cstructsharp/docs/guides/async-and-pipelines.html).
- **Analyze memory images.** `CStructSharp.Memory` adds unsigned address spaces, mapped regions, BTF/ISF type
  import, bounded traversal, and offline patches, with the same zero-dependency runtime; see the
  [memory-analysis guide](https://vvollers.github.io/cstructsharp/docs/guides/memory-analysis.html) and the
  [runnable synthetic consumer](https://vvollers.github.io/cstructsharp/docs/examples/memory-analysis/index.html).

Prepare a layout once and reuse it to read `StructValue` results or C# classes, write new records, and update
selected fields in existing data. The definition keeps the format's structure and byte-level rules together as your
tools grow from a single header parser into a complete format explorer. The library is trim-safe and Native AOT
compatible; see [trimming and Native AOT](https://vvollers.github.io/cstructsharp/docs/guides/trimming-and-native-aot.html)
for what a published program contains (and why `dynamic` stays on the JIT).

Start with the [language tutorial](https://vvollers.github.io/cstructsharp/docs/language/tutorial/index.html),
explore the [language reference](https://vvollers.github.io/cstructsharp/docs/language/index.html), or consult
[differences from C](https://vvollers.github.io/cstructsharp/docs/language/differences-from-c.html) when adapting
an existing header.

## Why CStructSharp instead of …

| If you would otherwise use                              | CStructSharp instead                                                                                                                                                                                                                                                                                                       |
| ------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Manual offsets with `BinaryReader` / `BinaryPrimitives` | The layout text names every field, offset, width, and byte order once; reads, writes, updates, address lookups, and the debug byte map all come from that one description, and a change to the format is a change to the text.                                                                                             |
| `[StructLayout]` structs with `MemoryMarshal`           | Portable widths never depend on the host process; layouts load at run time, so a tool can accept formats it did not compile against, and variable-length arrays, conditional fields, pointers, and strings are part of the description rather than hand code.                                                              |
| A source generator or a serializer                      | The same layout text drives C#, Node.js, and the browser; on .NET you choose per layout between the run-time `CStruct` (no build step, layouts loaded at run time) and the `[CStructLayout]` generator (typed classes, views, and setters emitted at build time), and the two agree on every byte and every error.         |
| Kaitai Struct or another schema language                | The schema is C: an existing header or a `dissect.cstruct` definition is the input, with `#define`, `#ifdef`, and `#pragma pack` honored, so format knowledge that already exists as C stays C.                                                                                                                            |
| dissect.cstruct (Python)                                | The same definition language and habits on .NET and in JavaScript, with a compiled layout cache, bounded read budgets, trim-safe Native AOT support, and a [migration guide](https://vvollers.github.io/cstructsharp/docs/guides/migrating-from-dissect.html) for the few places the two libraries read bytes differently. |

## Speed compared with other .NET serializers

To _serialize_ is to turn an object in memory into bytes; to _deserialize_ is to turn the bytes back into values.
The tables below time both operations on one 79-byte sensor record. Its members are packed (no padding bytes
between them) and stored little-endian (least significant byte first):

```c
struct vec3 { float32 x; float32 y; float32 z; };
struct reading {
    uint32  id;          /* bytes 0-3   */
    int64   timestamp;   /* bytes 4-11  */
    vec3    position;    /* bytes 12-23 */
    vec3    velocity;    /* bytes 24-35 */
    uint16  flags;       /* bytes 36-37 */
    uint8   kind;        /* byte  38    */
    float64 value;       /* bytes 39-46 */
    int32   samples[8];  /* bytes 47-78 */
};
```

The first table compares ways to read and write exactly these bytes. That is CStructSharp's job: a file format,
a device, or a C program has already fixed the layout. The second table shows general-purpose serializers. Each
one defines its own byte format and cannot read the C layout, so the table lists the size of each format and
shows how fast each library handles its own.

The third table uses a record whose shape depends on its own data: the length of `samples` comes from `count`, the
length of `name` from `name_length`, `kind` selects one of two members, and `note` ends at a zero byte. No member
after `samples` has an offset that is known before the bytes are read:

```c
struct packet {
    uint32 id;
    uint16 count;
    int32  samples[count];
    uint8  name_length;
    char   name[name_length];
    uint8  kind;
    if (kind == 1) { float64 value; } else { uint32 code; }
    cstring note;
};
```

- **Deserialize** decodes one record and reads every member once. Reading every member makes lazy readers, such as
  the generated view and FlatSharp, do the same work as readers that build an object.
- **Serialize** writes one record from an object that already exists into a buffer the benchmark reuses.
- **Allocated** is the managed heap memory used per call, which the garbage collector must reclaim later.

CStructSharp appears in several rows because it offers several ways to use the same layout:

- **Generated** code comes from the source generator at build time (`[CStructLayout]`): a typed class with `Parse`
  and `Serialize`, and a _view_ that decodes each member from the bytes only when it is read.
- **Runtime** rows compile the layout text while the program runs (`new CStruct(text)`), once, before timing.
  `Parse` returns a `StructValue`, a dictionary-like object; members are read either with path strings such as
  `"samples[3]"` or with _accessors_, which resolve a path once and reuse it. `CreateView` reads members from the
  bytes without building a `StructValue`.
- **Mapped class** rows read into and write from an ordinary C# class marked `[CStructMapped]`. When the class is
  bound to a fixed-size layout (the first table), the generator emits a direct reader and writer that the runtime
  uses whenever the layout text matches, which is why it runs as fast as generated code.

[BenchmarkDotNet](https://benchmarkdotnet.org/) repeats each operation until the timing is stable (millions of
calls) and reports the median time for one record. Multiply by 100,000 to estimate the time for 100,000 records.
Each table is sorted with the fastest deserializer first. Before measuring, every case is checked. Deserializers
must return the same values, same-bytes serializers must write the same bytes, and own-format serializers must
write bytes their own library reads back.

<!-- comparison-benchmarks:start -->

Measured on AMD Ryzen 9 9950X, Windows 11, .NET 10.0.12, with BenchmarkDotNet 0.15.8 (`default` job) on 2026-09-30. Times are medians for one record; each table lists the fastest deserializer first.

**Same bytes: the 79-byte C layout**

| Approach                                                                      | Deserialize | Allocated | Serialize | Allocated |
| ----------------------------------------------------------------------------- | ----------: | --------: | --------: | --------: |
| CStructSharp generated view                                                   |      8.5 ns |       0 B |         — |         — |
| .NET `MemoryMarshal.Read` / `Write`                                           |      9.8 ns |       0 B |    0.3 ns |       0 B |
| Hand-written `BinaryPrimitives`                                               |     20.3 ns |     248 B |    8.2 ns |       0 B |
| CStructSharp generated `Parse` / `Serialize`                                  |     27.2 ns |     248 B |   28.6 ns |       0 B |
| CStructSharp runtime view (`CreateView` + accessors)                          |     30.4 ns |       0 B |         — |         — |
| CStructSharp runtime `ReadValue<T>` / `Serialize` (layout-bound mapped class) |     31.8 ns |     248 B |   21.7 ns |       0 B |
| .NET `BinaryReader` / `BinaryWriter`                                          |     36.8 ns |     248 B |   42.6 ns |       0 B |
| .NET `Marshal.PtrToStructure` / `StructureToPtr`                              |     53.3 ns |     128 B |   31.3 ns |      72 B |
| Kaitai Struct 0.11.0                                                          |      125 ns |   1,040 B |         — |         — |
| CStructSharp runtime `Parse` + accessors                                      |      139 ns |     672 B |         — |         — |
| CStructSharp runtime `Parse` / `Serialize` (`StructValue`, path strings)      |      380 ns |     672 B |   76.0 ns |       0 B |

**Same record, each library's own format**

| Library                                      | Format           |  Size | Deserialize | Allocated | Serialize | Allocated |
| -------------------------------------------- | ---------------- | ----: | ----------: | --------: | --------: | --------: |
| CStructSharp generated `Parse` / `Serialize` | C layout         |  79 B |     27.2 ns |     248 B |   28.6 ns |       0 B |
| FlatSharp 7.9.0 (lazy)                       | FlatBuffers      | 116 B |     31.5 ns |     240 B |   48.8 ns |       0 B |
| MemoryPack 1.21.4                            | MemoryPack       |  86 B |     32.5 ns |     248 B |   19.2 ns |       0 B |
| MessagePack-CSharp 3.1.10                    | MessagePack      |  74 B |      111 ns |     248 B |   59.0 ns |       0 B |
| protobuf-net 3.4.30                          | Protocol Buffers |  91 B |      242 ns |     184 B |    217 ns |       0 B |
| System.Text.Json (source-generated)          | JSON             | 207 B |      698 ns |     848 B |    391 ns |       0 B |

**A record whose shape depends on its data (the `packet` layout above)**

| Approach                                                               | Deserialize | Allocated | Serialize | Allocated |
| ---------------------------------------------------------------------- | ----------: | --------: | --------: | --------: |
| Hand-written `BinaryPrimitives`                                        |     27.1 ns |     208 B |   10.0 ns |       0 B |
| CStructSharp generated `Parse` / `Serialize`                           |     73.1 ns |     800 B |   36.2 ns |       0 B |
| CStructSharp runtime `Parse` + accessors / `Serialize` (`StructValue`) |      371 ns |     432 B |    247 ns |       0 B |
| CStructSharp runtime `Parse` from a `MemoryStream` + accessors         |      539 ns |     592 B |         — |         — |
| CStructSharp runtime `ReadValue<T>` / `Serialize` (mapped class)       |      556 ns |     552 B |    413 ns |     272 B |

<!-- comparison-benchmarks:end -->

Keep these limits in mind when reading the tables:

- `MemoryMarshal` copies raw memory. It matches this layout only because the C# struct is declared with
  `Pack = 1` and the test machine is little-endian. It cannot handle big-endian fields, variable-length arrays,
  strings, or pointers. `MemoryMarshal.Write` is a single 79-byte memory copy; its time is below what the
  benchmark can resolve.
- The generated view reads members straight from the bytes and never builds an object, so it has no serialize
  column. Write with the generated `Serialize` method or the typed `Update` setters instead.
- The Kaitai Struct C# runtime can read but not write.
- On the fixed record, the runtime reads most members through prepared plans. On the `packet` record it walks the
  layout field by field and evaluates each length and condition while reading, which costs more than ten times as
  much as generated code. Choose generated code when a hot loop reads a data-dependent layout; choose the runtime when
  layouts arrive while the program runs.
- The hand-written readers do only the bounds checks that `Span<T>` does. CStructSharp also enforces its configured
  limits and reports the failing field, so the rows are not doing identical work.
- The timings leave out one-time costs: compiling a layout with `new CStruct(text)`, the first call of each method
  (just-in-time compilation), and reading from disk or network.
- All numbers come from one machine. Compare rows with each other rather than treating the times as absolute. To
  measure on your own machine, run `node tools/quality/comparison-benchmarks.mjs`.
  [benchmarks/README.md](benchmarks/README.md#compare-with-other-serializers) explains in detail what the
  benchmark measures and what it does not.

## Use JavaScript in Node.js or a browser

Read large files, buffers, and streamed binary input with automatic paging and worker execution. The
[large-data guide](https://vvollers.github.io/cstructsharp/docs/guides/browser/large-data.html) shows how to pass
`File`, `Blob`, byte views, fetch responses, and Node streams directly to `parse` or `parseWithDebug`.

The npm package includes the prebuilt WebAssembly runtime and TypeScript declarations:

```sh
npm install cstructsharp
```

Save this as `example.mjs` and run `node example.mjs` with Node.js 22.14 or later:

```js
import { parse } from "cstructsharp";

const result = await parse(
  "struct header { uint16 kind; uint32 length; };",
  new Uint8Array([2, 0, 6, 0, 0, 0]),
  { root: "header" },
);
if (!result.success) throw new Error(result.error.message);
console.log(result.data.kind); // 2
```

`parse` returns the values; `parseWithDebug` additionally lists each field's byte range for a hex viewer. Node
loads the installed runtime from disk; no .NET SDK or server is needed. Browser applications use the same API
with the `cstructsharp/vite` plugin or an explicit static-asset directory. See the
[npm package README](https://www.npmjs.com/package/cstructsharp) for complete setup, write/update examples, and
supported hosts.

## Use the standalone browser bundle

Download `cstructsharp-wasm-v<VERSION>.zip` from [GitHub Releases](https://github.com/vvollers/cstructsharp/releases).
Extract the complete archive. With Node.js installed, run `node serve.mjs` in that directory and open
`http://127.0.0.1:8080/starter/`. The included page reads, writes, and updates the same header.

Browser users do not need .NET installed. Keep the runtime files together and serve them over HTTP(S).
The [browser guide](https://vvollers.github.io/cstructsharp/docs/guides/browser/index.html) explains the files,
JavaScript API, result conversion, and common loading errors.

## Continue learning

- [Follow the learning path](https://vvollers.github.io/cstructsharp/docs/guides/learning-path.html)
- [Find an executable recipe](https://vvollers.github.io/cstructsharp/docs/guides/recipes/index.html)
- [Learn the layout language](https://vvollers.github.io/cstructsharp/docs/language/tutorial/index.html)
- [Look up the C# API](https://vvollers.github.io/cstructsharp/docs/api/CStructSharp.html)
- [Read release notes](https://github.com/vvollers/CStructSharp/blob/main/CHANGELOG.md)

## Versioning and support

CStructSharp follows semantic versioning and is at major version 0: a minor release (0.5 → 0.6) may change the
public API, the layout language, or the JavaScript contract, and the [changelog](https://github.com/vvollers/cstructsharp/blob/main/CHANGELOG.md)
marks every such change **Breaking** with the migration; a patch release never does. Pin the minor version you tested (for example
`0.10.*`) in a project that must not absorb breaking changes. The managed API baseline (`contracts/api/managed`) and the browser contract
(`contracts/api/browser`, `contractVersion` 9) are reviewed together with each change; a breaking JavaScript
change increments the contract version.

The NuGet package targets .NET 8 (LTS) and .NET 10 (LTS); a target is dropped in the first minor release after
Microsoft ends its support. The npm package supports the Node.js releases that are active or in maintenance
(currently 22.14 and later) and evergreen Chromium, Firefox, and WebKit browsers. Release assets describe published
versions; the repository's `src/CStructSharp/CStructSharp.csproj` records the development version.

## Work on the project

Package consumers do not need to clone or build this repository. Contributors should start with the
[repository setup guide](https://vvollers.github.io/cstructsharp/docs/project/getting-started.html), then follow
[build instructions](https://vvollers.github.io/cstructsharp/docs/project/building.html),
[testing](https://vvollers.github.io/cstructsharp/docs/project/testing.html), and
[contribution guidance](https://github.com/vvollers/CStructSharp/blob/main/CONTRIBUTING.md).
The [repository map](https://vvollers.github.io/cstructsharp/docs/project/repository-map.html) explains the projects.

CStructSharp uses the [MIT License](https://github.com/vvollers/CStructSharp/blob/main/LICENSE.txt).
Report questions and bugs in the [issue tracker](https://github.com/vvollers/CStructSharp/issues).
