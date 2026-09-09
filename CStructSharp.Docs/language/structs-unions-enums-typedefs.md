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

Inline structs may nest. Inline unions are not supported - a struct cannot nest a union member, named or anonymous.
Apart from name reuse, an inline struct follows the same placement, read, write, and update rules as a named child
struct.

### Anonymous promoted members

The *member declarator* itself - not the inline struct's own type, which is already always unnamed - may also be
omitted (LANG-14). When it is, the inline struct's own fields are promoted directly into the containing struct's
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
alignment are completely unaffected - promotion changes only which path/POCO/JSON name resolves to a field, not
where it lives in the stream. Parsing, serializing, writing, and updating all treat `x` and `y` as if they were
declared directly on `root`.

Promotion is transitive: an anonymous member's own anonymous members promote all the way up to the nearest named
container. A promoted member's own field names must not collide with the containing struct's own names, a sibling
promoted member's names, or a transitively deeper promoted member's names - any collision anywhere in that
flattened namespace is a construction-time error, naming the struct the collision becomes visible in. A *named*
nested struct keeps its own independent namespace, unaffected by any of this - reusing a name already used by a
sibling promoted member is not a collision.

**Anonymous inline unions remain out of scope.** Promoting an inline union member would first require inline union
member support to exist at all, which is separate, unimplemented work, not a same-shape extension of struct
promotion - a struct still cannot nest a union member of any kind, named or anonymous, today.

The `anonymous-promoted-member` fixture checks `a=1`, `x=2`, `y=3`, `b=4`, size 4, and bytes `01020304` on both
frameworks.

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

The special typedef-struct form declares a Portable struct and then exports an alias. Both the named-tag form shown
above and the anonymous inline form are accepted, along with the equivalent forms for unions:

```c
typedef struct { uint8 x; uint8 y; } point_t;
typedef union { uint8 small; uint16 large; } choice_t;
typedef union tagged_choice { uint8 small; uint16 large; } tagged_choice_t;
```

A typedef alias of an already-declared tag without repeating its body (`typedef struct ExistingTag alias;`, no
braces), and typedef arrays, remain unsupported — see [differences from C](differences-from-c.md).

The `typedefs` fixture checks that `word value` reads `34 12` as 4660 and that a packed root followed by one byte has
size/alignment `3/2`.

## Compare the declarations

| Declaration | Storage | Direct result | Reusable name | Mistake to avoid |
| --- | --- | --- | --- | --- |
| Named struct | Sequential | Dynamic object or mapped POCO | Yes | Assuming host padding |
| Inline struct (named member) | Sequential, nested | Nested dynamic object | No | Assuming member promotion |
| Inline struct (anonymous member) | Sequential, promoted | Spliced into the parent object | No | Assuming a nested container still exists |
| Union | Overlapping | `UnionValue` | Yes | Guessing an active member |
| Enum | One backing integer | `EnumValueResult` | Yes | Assuming C `int` backing |
| Typedef | Same as its target | Same as its target | Alias | Expecting a new ABI/layout |

See [Names and scopes](names-and-scopes.md), [Layout and padding](layout-alignment-and-padding.md), and the
[complete grammar](grammar.md).
