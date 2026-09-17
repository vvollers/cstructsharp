---
title: Migrating from dissect.cstruct
description: Paste a dissect.cstruct definition, keep its vocabulary and API habits, and know the few places the two libraries read bytes differently.
---

# Migrating from dissect.cstruct

[dissect.cstruct](https://github.com/fox-it/dissect.cstruct) is the Python library many forensic parsers were written
with. A definition written for it compiles in CStructSharp as it is - Windows SDK, Linux kernel, IDA, and C99 type
spellings, `flag` declarations, `[EOF]` and `[]` arrays, `#include` and `#pragma pack` lines, `typedef struct _X {...}
X, *PX;` lists - and the API calls its users make have direct equivalents. This page is the checklist.

## Definitions

| dissect.cstruct | CStructSharp | Notes |
| --- | --- | --- |
| `DWORD`, `BYTE`, `WCHAR`, `__u32`, `u8`, `wchar_t`, `unsigned __int64`, `uleb128`, ... | built in | See the [alias table](../language/primitive-types.md#alias-spellings). Aliases resolve to canonical codecs, so debug output says `uint32` for a `DWORD`. |
| `long` = 32 bits | `long` = 64 bits (LP64) | Use `CStructCompilationOptions { CLongWidth = 32 }` for the dissect reading. Windows `LONG`/`ULONG` are always 32 bits in both. |
| `int24` aligned to 4 | aligned to 1 | Only aligned placement is affected. |
| `flag F : uint32 { A, B, C };` | same | Reads as `FlagValueResult` with `Names`; writes accept `"A\|C"`. |
| `enum { A, B };` (anonymous) | same | Members become constants. |
| `enum E { A = 0x80000000 };` (no backing type) | same | 32 bits, `uint32` unless a member is written negative (the GCC rule; dissect always uses `uint32`); `CStructCompilationOptions { DefaultEnumStorage = "uint32" }` pins dissect's reading. |
| `E.A` in an expression | same | The bare name `A` stays local to the enum in CStructSharp. |
| Members separated by line breaks, names like `0` | same | The comma is optional; a member name may be all digits. Duplicate member names are an error (dissect keeps the last). |
| `typedef enum _E : DWORD { ... } E;` | same | Every struct typedef form has an enum/flag counterpart. |
| `uint32 _;` repeated | same | `_` is padding: skipped on read, zeroed on write, never in the result (dissect shows the last one). |
| `struct gen { ... } gen;`, `union tag { ... };` inside a struct | same | The tag becomes a global type; the unnamed form promotes the body. |
| `#define X SOMENAME`, `#define M (1 << 63)` | same | A non-expression value is a text constant; a 64-bit value is published exactly; both fail only when a count uses them. |
| `char data[EOF];`, `uint16 v[];` | same | Read-to-end and zero-terminated arrays; both need a fixed element size. |
| `union { ... };` inside a struct | same | Anonymous members are promoted; a promoted union writes back through the widest member supplied. |
| `struct { ... } timeval;` | same | The trailing name is the type. |
| `#define MAGIC b"CD001"` | same | Published on `CStruct.Constants` as a `byte[]`; a `"text"` define is a `string`. |
| `#include`, `#ifdef`, `#undef`, `#pragma pack` | same | `#include` is recorded on `CStruct.Includes`; `#pragma pack` clamps alignment (dissect ignores it); `#ifdef` names come from the source or `CStructCompilationOptions.Defined`. |
| `sizeof(T)`, `offsetof(T, f)` | same | Folded at construction; `T` must be complete and fixed-size. |
| `hdr.count` (a nested field) in an expression | same | The head must be a scalar struct field; `items[0].count` is not accepted (use the bare name after the element is read). |
| `uint8 *a, b;` (both pointers in dissect) | C semantics | Only `a` is a pointer. |
| Big-endian bitfields allocated from the high bit | low bit first | `CStructCompilationOptions { BitfieldAllocation = BitfieldAllocation.HighBitFirst }` selects the dissect/RFC reading. |
| `char x[4]` → `bytes` | `string` of code units | Declare `uint8 x[4]` for a byte array. |
| `int48`, `int128`, `float16`, `void *` | same | `Int128`/`UInt128`/`Half` results; `void *` is an opaque address. |
| `PWSTR`, `LPSTR`, `HANDLE`, `time_t` | built in | `wchar *`/`char *` pointers, an opaque address, and the `long` family. |
| `flag F : USHORT { X = 0x10000 }`, duplicate member names | rejected | dissect accepts a value that does not fit the backing type and a name declared twice; both are defects in a header, and CStructSharp reports them. |
| A struct-typed field registered from Python (`cs.add_custom_type`) | `ICustomCodec` | See [a custom codec](#a-custom-codec). |

## API

| dissect.cstruct | CStructSharp |
| --- | --- |
| `cstruct().load(definition)` | `new CStruct(definition)` or `CStruct.GetOrCompile(definition)` |
| `cs.load(a); cs.load(b)` | one string, or `CStructCompilationOptions { Prelude = a }` with `b` |
| `cs.copy().load(more)` (ELF 32/64) | the same prelude with two bodies |
| `cs.endian = ">"` after reading a header | `layout.WithEndianness(false)` |
| `cs.header(fh)` | `layout.ParseStream(stream, "header")` - the stream advances |
| `cs.uint32(fh)`, `cs.uint64[n] (fh)`, `cs.char[None] (fh)` | `layout.ParseStream(stream, "uint32")`, `"uint64[N]"` with `variables`, `"char[]"` |
| `obj.dumps()` | `layout.Serialize("header", obj)` |
| `cs.header(a=1, b=2).dumps()` | `layout.Serialize("header", new Dictionary<string, object?> { ... })` |
| `len(cs.header)` | `layout.GetStructSizeInBytes("header")` |
| `cs.add_custom_type("varint", MyType)` | `CStructCompilationOptions { Codecs = [new MyVarint()] }` implementing `ICustomCodec` |
| `cs.typedefs`, `struct.fields` | `layout.Layout.Declarations` |
| `cs.cdef()` | `layout.ToDefinition()` |
| `dumpstruct(obj)` | `ParseStreamWithDebug` byte ranges |
| `obj.field.dereference()` | `Pointer.Value` (dereferenced during the read by default) |

## A custom codec

The protobuf varint that `dissect.target` registers with `add_custom_type` is a fifteen-line class in CStructSharp
(`ICustomCodec` lives in `CStructSharp.Codecs`):

```csharp
sealed class Varint : ICustomCodec
{
    public string Name => "varint";
    public int? FixedSize => null;   // variable length
    public int Alignment => 1;

    public object Read(Stream stream)
    {
        ulong result = 0;
        for (int shift = 0; ; shift += 7)
        {
            int next = stream.ReadByte();
            if (next < 0) throw new EndOfStreamException();
            result |= (ulong)(next & 0x7F) << shift;
            if ((next & 0x80) == 0) return result;
        }
    }

    public void Write(Stream stream, object value)
    {
        ulong remaining = Convert.ToUInt64(value);
        do
        {
            byte chunk = (byte)(remaining & 0x7F);
            remaining >>= 7;
            stream.WriteByte(remaining == 0 ? chunk : (byte)(chunk | 0x80));
        }
        while (remaining != 0);
    }
}

var options = new CStructCompilationOptions { Codecs = [new Varint()] };
var layout = new CStruct("struct entry { varint length; uint8 data[length]; };", compilationOptions: options);
```

A custom codec is read through its delegate on every operation (never a span fast path), may be an array element or
a pointer target, is measured by reading when it has no fixed size, and is neither bitfield storage nor an enum
backing type. Keep one instance per codec: the list is compared by reference in the compiled-layout cache.

## What stays different on purpose

CStructSharp prefers C where dissect deviates from it, and keeps dissect's behavior reachable through an option:
the `long` width, the default enum storage, bitfield bit order, and `uint8 *a, b;`. It does not substitute macro
text, follow `#include`, or evaluate `#if` expressions, and it reports the header defects dissect glosses over
(duplicate names, values outside a declared backing type). Dynamic unions (a union with a runtime-sized member)
are not supported; they are rare in real definitions.
