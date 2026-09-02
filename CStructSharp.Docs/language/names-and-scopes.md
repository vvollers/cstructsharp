---
title: Names and scopes
description: Understand where declaration, field, enum-member, and inline-struct names can be reused.
---

# Names and scopes

A *scope* is the region in which a name must be unique. Portable uses a small set of scopes and compares every name
case-sensitively.

## Global declarations

Named structs, unions, enums, typedef aliases, and `#define` constants share one global declaration scope. Two of
them cannot use the same spelling, even when they are different declaration kinds.

Built-in primitive and string names are reserved in that scope. For example, `struct byte { ... };` is rejected so
that type lookup cannot interpret `byte` differently in different operations.

Portable does not implement C's separate tag namespace. A completed declaration is referenced directly:

```c
struct child {
    uint8 value;
};

struct root {
    child item;
};
```

## Fields and union members

Field names are local to their containing struct or union. One container cannot define the same exact spelling
twice. A different struct may reuse it, and `value` remains different from `Value`.

An inline struct belongs to its containing field:

```c
struct root {
    struct {
        uint8 code;
    } item;
};
```

The public path is `root.item.code`. The anonymous inner declaration has no reusable global type name, and its member
is not promoted to `root.code`.

## Enum members

Enum member names are local to the enum. A later member in the same enum may refer to an earlier member in an
expression. The member is not exported as a global `#define`, so another enum or field may use the same spelling.

## Typedef struct names

In the supported form:

```c
typedef struct backing {
    uint8 code;
}; packet;
```

`packet` is the global type name applications use. `backing` is retained for diagnostics but is not a second
referenceable type. Another typedef-struct declaration may reuse that backing spelling.

## Type references and recursion

A field type must be a built-in type or an exported global declaration/alias. An inline declaration cannot be named
later as a type.

Recursive by-value storage would have no finite size and is rejected:

```c
struct node {
    node next;
};
```

Recursion through a pointer to a real named declaration can be finite in the stored layout. Following it remains
limited by `ReadOptions`.

Duplicate or unresolved names fail while constructing `CStruct`, before a stream or output value is touched.
