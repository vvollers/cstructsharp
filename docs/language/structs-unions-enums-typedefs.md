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

A field's type reference may optionally carry a leading `struct`, `union`, or `enum` keyword, matching how C itself
refers to a tagged type: `record { struct point location; };` compiles to the identical field as
`record { point location; };`. The keyword is checked against the referenced declaration's actual kind, so
`union point location;` is rejected when `point` is declared as a `struct` - see the `tag-keywords` fixture.

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

Its paths are `root.item.kind` and `root.item.value`. There is no separate type name for the inner declaration - that
is already true of every inline struct, named or not.

Inline structs may nest, and a struct may nest an inline union the same way (`union { ... } choice;`), which reads
as a `UnionValue` exactly like a field of a named union type. A union body may in turn hold inline structs and
unions. Apart from name reuse, an inline composite follows the same placement, read, write, and update rules as a
named child. An inline composite is always a single member; declare an array of a named type for repeated ones.

A tagged inline body (`struct gen { ... } gen;` or `union version_information { ... };` inside another body)
declares its tag as a global type, exactly as C and dissect do: the member-name form is then an ordinary field of
that type (with every declarator shape: `} gen[2];`, `} *p;`), and the form without a member name promotes the body
(the reading MSVC's anonymous-member extension and dissect give it) while still declaring the tag.

### Padding fields

A field named `_` is unnamed padding, the way an [anonymous bitfield](bitfields.md) is: it is read and skipped,
written as zeroes without a caller value, absent from every result, addressable by nobody, and free to repeat in
one body. It must be a fixed-size primitive or a fixed primitive array (`uint32 _; char _[3];`); a struct, pointer,
or runtime-sized `_` is rejected.

### Anonymous promoted members

The *member declarator* itself - not the inline struct's own type, which is already always unnamed - may also be
omitted. When it is, the inline struct's own fields are promoted directly into the containing struct's
own namespace instead of nesting under a name of their own:

```c
struct root {
    uint8 a;
    struct {
        uint8 x;
        uint8 y;
    };
    uint8 b;
};
```

Here `x` and `y` are addressable directly as `root.x` and `root.y`, not `root.<something>.x`. Placement, size, and
alignment are completely unaffected - promotion changes only which path/mapped-class/JSON name resolves to a field, not
where it lives in the stream. Parsing, serializing, writing, and updating all treat `x` and `y` as if they were
declared directly on `root`.

Promotion is transitive: an anonymous member's own anonymous members promote all the way up to the nearest named
container. A promoted member's own field names must not collide with the containing struct's own names, a sibling
promoted member's names, or a transitively deeper promoted member's names - any collision anywhere in that
flattened namespace is a construction-time error, naming the struct the collision becomes visible in. A *named*
nested struct keeps its own independent namespace, unaffected by any of this - reusing a name already used by a
sibling promoted member is not a collision.

The same promotion applies to an anonymous inline union, the shape Windows headers use everywhere:

```c
struct file_name {
    uint32 attributes;
    union {
        struct { uint16 ea_size; uint16 reserved; };
        uint32 reparse_tag;
    };
    uint8 name_length;
};
```

`ea_size`, `reserved`, and `reparse_tag` are all addressable directly on `file_name`; every one of them is decoded
from the same four bytes, because they are the union's overlapping views, and an anonymous struct inside the union
promotes its members through the union. Reading gives each promoted member its decoded value (the union's raw
storage is not published under a name of its own). Writing a promoted union chooses the member to encode from the
members the data supplies - the widest one first, so a value that came from a parse reproduces the whole storage,
and a new value needs only one member (`reparse_tag`, or `ea_size` with `reserved`) - and clears the rest of the
union extent, exactly as `UnionValue.FromMember` does; supplying none of them is a write error. Updating a promoted
member changes only that member's bytes.

The `anonymous-promoted-member` fixture checks `a=1`, `x=2`, `y=3`, `b=4`, size 4, and bytes `01020304` on both
frameworks; the `inline-unions` fixture checks the union shape above.

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

The optional backing type must resolve to a supported fixed integral type. Without one, storage follows the rule C
compilers apply: 32 bits, `uint32` unless a member is written as a negative number, which selects `int32`. That is
GCC's choice for an enum without negative members and the `uint32` dissect.cstruct assumes;
`CStructCompilationOptions.DefaultEnumStorage` names any other spelling (`"byte"` for the smallest storage,
`"int32"` for MSVC's fixed `int`).

Members are evaluated in order. The first omitted value is zero; each later omitted value is the previous value plus
one. The comma between members is optional, because a value can never be followed by a name: members separated by
line breaks alone (the Windows-header habit) are unambiguous, and a trailing comma is allowed as in C. A member name
may start with, or consist of, digits (`32BIT_MACHINE`, or `0 = 0x30` in an enum of character codes). Duplicate
names, values outside the backing range, circular/unknown expression dependencies, and unsupported backing types
produce `InvalidLayout`.

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

A member is referred to from an expression by its qualified name (`mode.Write`); the bare name stays local to the
enum. An enum or flag may be bitfield storage (`mode kind : 2;`): its bits live in the backing type's storage unit,
which it shares with adjacent bitfields of that type or of other enums on it, and the slice reads back as an
`EnumValueResult`. An enum with no name (`enum { A = 3, B };`) declares no type: its members become integer
constants, counting up from zero, exactly as C treats them.

### Flags

A `flag` is a bitmask enum. It is declared like an enum, but an omitted member value is the next unused bit rather than
the previous value plus one (dissect.cstruct's rule), and reading one decomposes the stored value:

```c
flag access : uint16 {
    READ,
    WRITE,
    EXEC,
    HIDDEN = 0x100
};
```

Reading returns `FlagValueResult`, an `EnumValueResult` with `Names` (every member whose bits are all set, in
declaration order, computed on first use), `Remainder` (the bits no member accounts for), and `Has(name)`; `Name`
is still the single member equal to the whole value, when there is one. Writers additionally accept `"READ|EXEC"`, a
sequence of member names, and a `[Flags]` CLR enum; typed reads map to a `[Flags]` enum by value. An anonymous
`flag { ... };` declares constants like an anonymous enum. A struct holding a flag field reads through the general
interpreter rather than the span fast path. The `flags` fixture checks the declaration above.

## Typedefs

A typedef gives another name to one existing type, an optional pointer depth, and optionally a fixed array shape:

```c
typedef uint16 word;
typedef int24< little_delta;
typedef fixed16_16> network_revision;
typedef word *word_pointer;
typedef unsigned long long ticks_t;
typedef uint8 byte_t, *pbyte_t;
typedef uint16 pair[2];
typedef struct packet {
    uint8 kind;
    word value;
}; packet_alias;
```

Following aliases does not change width, byte order, alignment, or pointer addressing. Alias cycles are rejected.
A field declared with an array typedef (`pair p;`) has the typedef's dimensions as its innermost ones (`pair rows[3]`
is `uint16 rows[3][2]`), and the typedef itself can be a root (`Parse(bytes, "pair")` reads two values). A pointer
to an array typedef and an unsized array typedef are rejected.

The typedef-struct form declares a struct and one or more aliases. The named-tag form declares the tag as a global
type as well, exactly as C does, so `_X`, `X`, and `PX` below all resolve, and a second `typedef struct _X` is a
duplicate. The trailing declarator list may carry pointer stars; a body with no alias at all
(`typedef struct NAME { ... };`) declares just the tag; and `typedef struct tag alias;` aliases a tag declared
elsewhere in the same layout (its `struct`/`union` keyword is checked against the tag's kind):

```c
typedef struct _X { uint8 a; } X, *PX;
typedef struct { uint8 x; uint8 y; } point_t;
typedef union { uint8 small; uint16 large; } choice_t;
typedef union tagged_choice { uint8 small; uint16 large; } tagged_choice_t;
typedef struct NAME { uint8 a; };
typedef struct _X x_alias;
typedef enum _KIND : uint8 { NONE, CODE } KIND, *PKIND;
typedef flag { READ, WRITE } access_t;
typedef enum _KIND kind_alias;
```

The `enum` and `flag` spellings follow the same forms: a tagged body declares the tag and aliases it, an anonymous
body is the alias's own enum, an alias equal to the tag never collides with itself, and `typedef enum Tag Alias;`
aliases an enum declared elsewhere.

The `typedefs` fixture checks that `word value` reads `34 12` as 4660 and that a packed root followed by one byte has
size/alignment `3/2`.

## Top-level declaration forms

Two more spellings appear in copied headers and dissect definitions. A body whose only name follows the closing
brace declares that name as the type (it is a typedef without the keyword), and a named body may be followed by an
object name, which declares nothing:

```c
struct { uint32 tv_sec; uint32 tv_usec; } timeval;
struct exit_status { uint16 termination; uint16 exit; } status;
```

`timeval` and `exit_status` are types; `status` is not a declaration and does not reserve its name. A forward
declaration (`struct node;`) is accepted and declares nothing: a self-referential pointer (`node *next;` inside
`struct node`) never needed one, and using a forward-declared tag by value before its body is still an unknown type.

## Compare the declarations

| Declaration | Storage | Direct result | Reusable name | Mistake to avoid |
| --- | --- | --- | --- | --- |
| Named struct | Sequential | Dynamic object or mapped class | Yes | Assuming host padding |
| Inline struct (named member) | Sequential, nested | Nested dynamic object | No | Assuming member promotion |
| Inline struct (anonymous member) | Sequential, promoted | Spliced into the parent object | No | Assuming a nested container still exists |
| Union | Overlapping | `UnionValue` | Yes | Guessing an active member |
| Enum | One backing integer | `EnumValueResult` | Yes | Assuming a compiler's signedness for values at bit 31 |
| Typedef | Same as its target | Same as its target | Alias | Expecting a new ABI/layout |

See [Names and scopes](names-and-scopes.md), [Layout and padding](layout-alignment-and-padding.md), and the
[complete grammar](grammar.md).
