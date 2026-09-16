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
| `E.A` in an expression | same | The bare name `A` stays local to the enum in CStructSharp. |
| `char data[EOF];`, `uint16 v[];` | same | Read-to-end and zero-terminated arrays; both need a fixed element size. |
| `union { ... };` inside a struct | same | Anonymous members are promoted; a promoted union writes back through the widest member supplied. |
| `struct { ... } timeval;` | same | The trailing name is the type. |
| `#define MAGIC b"CD001"` | same | Published on `CStruct.Constants` as a `byte[]`; a `"text"` define is a `string`. |
| `#include`, `#ifdef`, `#undef`, `#pragma pack` | same | `#include` is recorded on `CStruct.Includes`; `#pragma pack` clamps alignment (dissect ignores it); `#ifdef` names come from the source or `CStructCompilationOptions.Defined`. |
| `sizeof(T)`, `offsetof(T, f)` | same | Folded at construction; `T` must be complete and fixed-size. |
| `hdr.count` (a nested field) in an expression | not supported | Capture the value into a field of the enclosing struct, or supply it as a variable. |
| `uint8 *a, b;` (both pointers in dissect) | C semantics | Only `a` is a pointer. |
| Big-endian bitfields allocated from the high bit | low bit first | `CStructCompilationOptions { BitfieldAllocation = BitfieldAllocation.HighBitFirst }` selects the dissect/RFC reading. |
| `char x[4]` → `bytes` | `string` of code units | Declare `uint8 x[4]` for a byte array. |
| `int48`, `int128`, `float16`, `void *` | same | `Int128`/`UInt128`/`Half` results; `void *` is an opaque address. |

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

The protobuf varint that `dissect.target` registers with `add_custom_type` is a fifteen-line class in CStructSharp:

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
the `long` width, bitfield bit order, and `uint8 *a, b;`. It does not substitute macro text, follow `#include`, or
evaluate `#if` expressions. Dynamic unions (a union with a runtime-sized member) and a nested field in an expression
(`hdr.count`) are not supported; both are rare in real definitions.
