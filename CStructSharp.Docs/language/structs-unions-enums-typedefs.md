---
title: Structs, unions, enums, and typedefs
description: Define sequential fields, overlapping storage, named integer values, and type aliases.
---

# Structs, unions, enums, and typedefs

These declarations combine primitive values into reusable shapes. Names are case-sensitive, every referenced type
must exist, and by-value storage must have a finite size or a runtime size the operation can determine safely.

## Named structs

A struct places fields in declaration order:

```c
struct point {
    int16 x;
    int16 y;
};

struct record {
    uint8 tag;
    point location;
};
```

In packed mode, each field starts where the previous one ended. In aligned mode, the start is rounded to the field's
alignment and the final struct size is rounded to the largest field alignment. A nested struct uses its complete
size, including tail padding.

For packed bytes `A1 FE FF 05 00`:

| Path | Offset | Value |
| --- | ---: | ---: |
| `record.tag` | 0 | `161` |
| `record.location.x` | 1 | `-2` |
| `record.location.y` | 3 | `5` |

The `nested-structs` fixture checks size 2 for a one-byte child/root case, exact offsets and values, and exact output
bytes on both frameworks.

Duplicate top-level names, duplicate fields in one struct, unknown types, and recursive by-value fields produce
`InvalidLayout`. A recursive pointer can be valid because the pointer itself has finite width; following it is
limited during reading.

## Inline structs

An inline struct gives one field a nested shape without creating a reusable global type:

```c
struct root {
    struct {
        uint8 kind;
        uint16 value;
    } item;
};
```

Its paths are `root.item.kind` and `root.item.value`. There is no separate type name for the inner declaration, and
its fields are not promoted to `root.kind` or `root.value`.

Inline structs may nest. Inline unions and unnamed containing fields are not supported. Apart from name reuse, an
inline struct follows the same placement, read, write, and update rules as a named child struct.

## Unions

A union overlays every member at one address:

```c
union choice {
    uint8 small;
    uint32 large;
};
```

Its storage is large enough for the largest complete member. Its alignment is the largest member alignment; aligned
mode rounds the final union size to that value.

```text
aligned union choice, bytes A5 00 00 00
offset   0    1    2    3
storage A5   00   00   00
small   A5
large   A5   00   00   00  → 165 (little-endian)
```

Reading returns `UnionValue` with a copy of the complete raw storage and every decoded member. CStructSharp cannot
infer an active member because an untagged union does not store one.

Writing an unchanged parsed union reproduces its raw storage. For new output, choose one member with
`UnionValue.FromMember` or `WithSelectedMember`. The writer clears the complete union first, then writes that member,
so bytes outside a smaller member become zero.

Arrays of unions use the complete union stride. A struct member inside a union begins at the union address, then its
own fields proceed normally. Debug and address operations observe the same overlapping region. See the
[union guide](../guides/unions.md).

## Enums

An enum stores an integer and associates names with selected values:

```c
enum mode : uint16 {
    Unknown = 0,
    Read = 1,
    Write = Read << 1
};

struct root {
    mode value;
};
```

The optional backing type must resolve to a supported fixed integral type. Without one, storage is unsigned `byte`,
not a compiler-selected C `int`.

Members are evaluated in order. The first omitted value is zero; each later omitted value is the previous value plus
one. Duplicate names, values outside the backing range, circular/unknown expression dependencies, and unsupported
backing types produce `InvalidLayout`.

Reading returns `EnumValueResult`:

| Property | Meaning |
| --- | --- |
| `Enum` | Declared enum name |
| `Name` | First matching member, or `null` when the number is not declared |
| `Value` | Exact signed or unsigned mathematical value |
| `RawBits` | Backing bits represented as an unsigned number |
| `StorageType` / `BitWidth` / `IsSigned` | Backing identity, width, and signedness |

Unknown numbers remain valid and can be written back without narrowing. Writers accept a compatible
`EnumValueResult`, a declared member name, or an in-range numeric value. See the [enum guide](../guides/enums.md).

## Typedefs

A typedef gives another name to one existing type and optional pointer depth:

```c
typedef uint16 word;
typedef word *word_pointer;
typedef struct packet {
    uint8 kind;
    word value;
}; packet_alias;
```

Following aliases does not change width, byte order, alignment, array count, or pointer addressing. Alias cycles are
rejected.

The special typedef-struct form declares a named Portable struct and then exports an alias. Common C forms such as
`typedef struct { ... } Name;`, tag-plus-alias variants, typedef arrays, and typedef unions are not supported.

The `typedefs` fixture checks that `word value` reads `34 12` as 4660 and that a packed root followed by one byte has
size/alignment `3/2`.

## Compare the declarations

| Declaration | Storage | Direct result | Reusable name | Mistake to avoid |
| --- | --- | --- | --- | --- |
| Named struct | Sequential | Dynamic object or mapped POCO | Yes | Assuming host padding |
| Inline struct | Sequential, nested | Nested dynamic object | No | Assuming member promotion |
| Union | Overlapping | `UnionValue` | Yes | Guessing an active member |
| Enum | One backing integer | `EnumValueResult` | Yes | Assuming C `int` backing |
| Typedef | Same as its target | Same as its target | Alias | Expecting a new ABI/layout |

See [Names and scopes](names-and-scopes.md), [Layout and padding](layout-alignment-and-padding.md), and the
[complete grammar](grammar.md).
