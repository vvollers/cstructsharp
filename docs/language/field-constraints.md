---
title: Field constraints
description: State which values a field may hold with @expect and @supports, and what a read does with a value outside them.
---

# Field constraints

A layout says where the bytes of a field are and how to decode them. It does not say which decoded values make sense:
a `uint16 magic` can hold any of 65,536 values, but only one of them means "this is an ext superblock". A *constraint*
writes that rule next to the field, so the reader checks it and every failure says what was wrong.

```c
struct superblock {
    uint16 magic        @expect(magic == 0xEF53);
    uint32 log_block_size @expect(log_block_size <= 6);
    uint32 rev_level    @supports(rev_level <= 1);
    uint32 inodes_per_group @expect(inodes_per_group > 0);
};
```

A constraint is a suffix of the declarator, like `@align(N)` and `@count(N)`. Its argument is the same integer
[expression](expressions-defines-and-variables.md) an array count uses, and the read fails when the expression evaluates
to zero. There are two kinds, because two different things can be wrong with a value:

| Suffix | A value outside the condition means | `CStructConstraintException.Kind` |
| --- | --- | --- |
| `@expect(condition)` | the data is **malformed**: no valid file holds this value | `ConstraintKind.Malformed` |
| `@supports(condition)` | the data may be valid, but this layout does not **support** it (an unknown version, an unimplemented feature) | `ConstraintKind.Unsupported` |

A forensic tool needs that difference: a malformed header is evidence of damage or tampering, while an unsupported
version is a limit of the reader.

## What a condition can name

A condition is evaluated right after its field is read, so it can name:

- the field itself and the fields before it in the same struct (`m @expect(m <= n)`), nested fields through their
  struct field (`hdr.count`), exactly as an array count can;
- `#define` constants, qualified enum members (`kind.Bad`), `sizeof` and `offsetof`;
- a caller variable (`v @expect(v == EXPECTED)`), supplied with the operation's variables.

It can use every operator of an expression, including `&&`, `||` and `?:`, and it fails as an ordinary expression does
(a division by zero is "Cannot evaluate constraint ...", not a constraint failure). A condition that names a text, array,
struct or floating-point field is a layout error, as in any expression: only integers, characters, `bool`, enums and
pointer addresses have a single integer value.

A field may carry several constraints; they are checked in the order written and the first that fails is reported:

```c
uint8 v @expect(v > 1) @supports(v < 5) @expect(v != 3);
```

## What a failure looks like

A value outside a condition throws `CStructConstraintException`, a `CStructReadException` with the code `ReadFailed`:

```text
Invalid value for 'magic': it must satisfy 'magic == 0xEF53'. (field 'magic', in 'superblock', offset 2)
Unsupported value for 'rev_level': this layout supports only values for which 'rev_level <= 1' holds. (...)
```

`Kind` says malformed or unsupported, `Constraint` holds the condition as written, and `Member`, `Path` and `Offset`
are those of every read failure. The runtime layout, the generated readers and a generated view's `Validate()` report
the same failure for the same bytes.

## Where a constraint is checked

- **Reads.** `Parse`, `ParseMany`, `ReadValue`, `TryReadValue`, stream and asynchronous reads, the debug read and the
  generated `Parse` and `TryParse` check every constraint of every struct they read, including the elements of an array
  of structs, a pointer's target and the fields of a selected `if`/`switch` arm. A field that is never read (an unselected
  arm) is not checked, and a selected read of one field (`ReadValue<int>(bytes, "root.field")`) reads only that field.
- **Fast paths.** The runtime reads a struct with a constraint member by member, so no fast path can skip the check. The
  generated reader keeps its fixed-offset reader for such a struct (when everything else about it is fixed) and checks the
  conditions in field order right after decoding, with the same failure and offset; a struct that contains a struct with
  a constraint is read member by member. A layout without constraints pays nothing.
- **Generated views.** A view decodes lazily and never checks on its own. `Validate()` checks the conditions of the
  view's struct that the view can evaluate (it reads the members they name through the view's properties) and throws the
  reader's failure; a condition that names a member the view does not expose is left to `Parse`.
- **Writes.** A constraint describes what a read accepts, so a write does not check it: `Serialize` and `Update` write
  any value the field's type holds. Check the value before writing, or read it back.

## Where a constraint is not allowed

A union member, padding (`_`) and an unnamed bitfield cannot carry a constraint: the members of a union overlap and the
other two have no name to quote. A condition that does not parse or names an unknown identifier at layout time is a layout
error, reported when the layout is built.

```c
union u { uint8 a @expect(a == 1); uint16 b; };   // layout error: a constraint on a union member
struct s { uint8 _ @expect(1); };                  // layout error: padding has no name
```

## Check yourself

1. A header says `uint8 version @supports(version == 1)` and the bytes hold `2`. Is the file malformed?
2. Why can the generated reader still use its fixed-offset reader for a struct with constraints?
3. Does `Serialize` reject a value that violates a constraint?

<details>
<summary>Answers</summary>

1. Not necessarily: `@supports` reports `ConstraintKind.Unsupported`. The same value under `@expect` would be malformed.
2. It decodes the whole struct first and then checks the conditions in field order, so an invalid value never leaves the
   reader, and the failure is the one a member-by-member read would raise.
3. No. A constraint checks what a read accepts; a write encodes the value as given.

</details>
