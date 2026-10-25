---
title: Field constraints
description: State which values a field may hold with @expect, @supports and @magic, and what a read does with a value outside them.
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

## Recognize a format with a magic

Many formats start with a **magic number**: a fixed value that says "these bytes are this format" (an ext superblock holds
`0xEF53` at offset 56; an ELF file starts with `7F 45 4C 46`). A magic is not like the other fields. If it is wrong, the other
fields mean nothing, and a complaint about them would be misleading. `@magic` marks such a field:

```c
struct superblock {
    uint32 inodes_count  @expect(inodes_count > 0, "ext.inodes");
    uint16 magic         @magic(magic == 0xEF53, "ext.magic", "Not an ext volume.");
    uint32 rev_level     @supports(rev_level <= 1, "ext.revision");
};
```

`@magic` takes the same arguments as `@expect` (a condition, an optional label and an optional message). It differs in three ways:

1. **It is checked first.** The conditions of the members in front of the magic are held back until the magic has been read and
   found right, so garbage in an earlier field cannot hide the fact that the bytes are not this format. The read still fails
   with a short-read error if the bytes end before the magic. When the magic holds, the held-back conditions run next, then the
   later ones, each in declaration order. If a magic sits inside an `if` or `switch` arm, the struct keeps declaration order.
2. **It fails as `ConstraintKind.Unsupported`.** The bytes are not malformed; they are simply not this format.
3. **`Matches` evaluates the magic alone.** A struct with at least one `@magic` is recognized by its magic conditions, and its
   `@expect` conditions are ignored by `Matches`. A struct without a magic is recognized by its `@expect` conditions as before.
   Several `@magic` conditions on one struct are all required.

The failure's `Offset` is the end of the member whose check ran, so it can differ from the position reported for the same
condition written with `@expect`; with collected violations (`ReadOptions.ConstraintViolations`) the magic is listed first.
The analyzer warns ([CSG020](../guides/generated/diagnostics.md#csg020---a-magic-follows-a-variable-size-member)) about a magic that follows a
variable-size member, because `Matches` cannot evaluate it there.

## Compare an array or text with a byte string

A signature is usually not a number but a short run of bytes: the text `NTFS` padded with four spaces in a boot sector, `\x7fELF` at the start of an ELF file.
A condition may compare such a field with a **byte string**, a quoted literal that stands for those bytes:

```c
struct boot {
    uint8 jump[3];
    uint8 oem[8]  @expect(oem == "NTFS    ", "ntfs.oem", "Not an NTFS volume.");
    uint32 sectors;
};
struct elf_ident {
    uint8 magic[4] @magic(magic == b"\x7fELF", "elf.magic");
};
```

- **Where.** The field must be the one the suffix is on, a fixed-size one-dimensional `uint8` or `char` array, and the string
  must have exactly as many bytes as the array has elements. `==` is true when every byte is equal, `!=` when at least one
  differs. The comparison is a condition on its own, so it combines with the other operators:
  `@expect(fs == "FAT" || (fs == "EXT" && kind == 2))`. Any other use of an array in a condition (`oem[0]`, `oem < "A"`,
  `oem + 1`, an array with a runtime size, a `uint16` array) is a layout error.
- **Two spellings.** `"NTFS    "` holds one byte per character, so its characters are U+0000 to U+00FF. `b"\x7fELF"` holds
  ASCII characters and escapes only. Both mean the same bytes. The escapes are `\\`, `\"`, `\0`, `\n`, `\r`, `\t` and `\xHH`
  (two hexadecimal digits). A string holds 1 to 256 bytes.
- **Bytes, not decoded text.** A `char` array compares its raw bytes, so `ReadOptions.TrimFixedText` (which drops trailing NUL
  characters from the value) does not change the answer, and neither does the byte order of the layout.
- **No copy.** A generated reader compares the array's bytes in place with a span comparison; it allocates nothing for the
  comparison. `Matches` can use a byte-string `@magic` (or `@expect`) like any other condition.

An older engine or npm version that does not know byte strings rejects a layout that uses them.

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

## Give a constraint a label

A condition is for the machine and its message is for a person, but a program that sorts failures (a report, a log filter,
a test) needs a name it can match. Write the name as a quoted string after the condition, inside the parentheses:

```c
struct superblock {
    uint16 magic @expect(magic == 0xEF53, "ext.superblock_magic");
    uint32 rev_level @supports(rev_level <= 1, "ext.revision");
};
```

The label is carried by `CStructConstraintException.Label` (`null` for a constraint without one) and the message ends
with it in brackets, `... holds. [ext.revision]`. It has no effect on the check. A label is not empty, stays on one line,
and writes a quote or a backslash as `\"` or `\\`; `ToDefinition()` prints it back, so a layout rebuilt from its text
reports the same labels.

## Give a failure its own message

The sentence a failure reports is generated from the condition: `Invalid value for 'magic': it must satisfy 'magic == 0xEF53'.`
A program that shows the failure to a person often needs its own wording (and the rule and its wording then live in one place:
the layout). Write the wording as a second quoted string, after the label:

```c
struct superblock {
    uint16 magic @expect(magic == 0xEF53, "ext.magic", "The superblock does not carry the ext magic number.");
    uint32 rev_level @supports(rev_level <= 1, "ext.revision", "Only ext revisions 0 and 1 are supported.");
};
```

The message is carried by `CStructConstraintException.Description` (`null` for a constraint without one). It replaces the
generated sentence in `Message`, which still ends with the label in brackets and the position facts:

```text
The superblock does not carry the ext magic number. [ext.magic] (field 'magic', in 'superblock', offset 2)
```

A message follows a label (a constraint cannot have a message without one), is not empty, stays on one line, and writes a
quote or a backslash as `\"` or `\\`, like a label. `ToDefinition()` prints it back.

## What a failure looks like

A value outside a condition throws `CStructConstraintException`, a `CStructReadException` with the code `ReadFailed`:

```text
Invalid value for 'magic': it must satisfy 'magic == 0xEF53'. (field 'magic', in 'superblock', offset 2)
Unsupported value for 'rev_level': this layout supports only values for which 'rev_level <= 1' holds. (...)
```

`Kind` says malformed or unsupported, `Constraint` holds the condition as written, and `Member`, `Path` and `Offset`
are those of every read failure. The runtime layout, the generated readers and a generated view's `Validate()` report
the same failure for the same bytes.

## Throw .NET format exceptions from a generated reader

An application that maps "the file is damaged" and "the file uses something this reader does not do" to the exceptions of
the .NET base class library can ask the generated reader to throw those directly. Set `ConstraintFailures` on the layout, or
the `CStructSharpConstraintFailures` build property for the whole project:

```csharp
[CStructLayout("struct superblock { uint16 magic @expect(magic == 0xEF53, \"ext.magic\", \"Not an ext superblock.\"); uint32 rev_level @supports(rev_level <= 1, \"ext.revision\"); };",
    Root = "superblock", ConstraintFailures = ConstraintFailureMode.FormatExceptions)]
public static partial class Ext { }
```

| `ConstraintFailureMode` | A failed `@expect` throws | A failed `@supports` throws |
| --- | --- | --- |
| `Constraint` (default) | `CStructConstraintException` | `CStructConstraintException` |
| `FormatExceptions` | `System.IO.InvalidDataException` | `System.NotSupportedException` |

The format exception has the constraint failure's message, and the `CStructConstraintException` is its `InnerException`, so the
label, kind, member, path and offset are still there:

```csharp
try
{
    Ext.Parse(bytes);
}
catch (InvalidDataException exception) when (exception.InnerException is CStructConstraintException failure)
{
    Console.WriteLine($"{failure.Label} at offset {failure.Offset}: {exception.Message}");
}
```

The mode changes only what the **throwing** generated entry points raise: `Parse` and `Parse<Name>` for every input (span, array,
memory, `ReadLimits`, sequence, stream and the awaitable stream form), the record enumerators (`Records`) and a view's `Validate`
and `ToObject`. It is applied once, where the failure leaves the generated method, so a failure inside nested reads is still the
constraint exception that the reader's own code sees. `TryParse`, `TryValidate` and `Matches` already return failures as
values and do not change (`TryParse` hands back the `CStructConstraintException`), and so do the violations a read collects.
Failures that are not constraints (a short read, a limit) keep their types.

A layout read through the runtime engine (`CStruct.Parse`) or in the browser always throws `CStructConstraintException`: the
mode belongs to the generated code, so the same layout text throws the same type on both paths only in the default mode.

## Collect every failure instead of the first

A read stops at the first value outside a condition (unless it [skips the constraints](#read-without-evaluating-the-constraints)). To list *every* inconsistent field of a header in one pass, give the
read a collection:

```csharp
var violations = new List<CStructConstraintException>();
StructValue header = layout.Parse(bytes, "superblock", null, new ReadOptions { ConstraintViolations = violations });
foreach (CStructConstraintException violation in violations)
{
    Console.WriteLine($"{violation.Label ?? violation.Member}: {violation.Message}");
}
```

Each broken condition is added to the collection, in field order, with the member, path, offset and label a thrown failure
has, and the read continues with the value as read. The collection stays empty when every condition holds. Only constraints
are collected: a short read, a limit or a condition that cannot be evaluated still ends the read with its exception, and a
value that breaks a condition may break what depends on it (a count that is too large fails the array limit next), so the
collection then holds the failures found up to that point. The same option is read by the generated readers and by a
generated view's `Validate(violations)`. The collection is added to in place and is not thread-safe: use one per read.

## Read without evaluating the constraints

A layout that states its header rules is sometimes read at a second place that does not want them: a scan that lists what is
on a damaged disk, a lenient pass over data another reader already validated, a tool that shows the bytes of an invalid
header. Pass the policy with the read instead of keeping a second copy of the layout:

```csharp
// Strict: the first broken condition throws. Lenient: no condition is evaluated and nothing is collected.
StructValue strict = layout.Parse(bytes, "superblock");
StructValue lenient = layout.Parse(bytes, "superblock", null, new ReadOptions { Constraints = ConstraintPolicy.Skip });

Superblock quick = Ext.Parse(bytes, new ReadLimits { Constraints = ConstraintPolicy.Skip });   // generated, no options object
```

`ConstraintPolicy.Skip` is a setting of one read on `ReadOptions` and `ReadLimits` (the default is `Enforce`). The runtime layout,
every generated `Parse`, the sequence, stream and record readers, `TryParse` and a view's `Validate`, `TryValidate` and
`ToObject` honor it. A skipped condition is not evaluated at all, so one that names a caller variable the read does not supply
(or divides by zero) does not fail either. Three things to know:

- It skips every condition of the read, including one that later code relies on. A bound such as `n @expect(n <= 16)` that
  guards a shift or an index is not checked, so the reader meets the bad value itself and fails (or not) as it would for a layout
  without constraints.
- It cannot be combined with `ConstraintViolations`, which asks for the conditions to be evaluated and collected. A read
  that sets both fails with `ArgumentException` when it starts.
- `Matches` takes no read settings and always evaluates its conditions: it answers "is this the format?", which is a different
  question from "read it anyway".

## Mistakes the compiler can find

The analyzer reads the constraints in a `[CStructLayout]` attribute or a `new CStruct("...")` literal when you build. It warns
([CSG018](../guides/generated/diagnostics.md#csg018---the-conditions-on-a-member-can-never-hold)) when the conditions on one
integer member cannot be true together, for example `uint8 kind @expect(kind == 1) @expect(kind == 2)` or a bound above the type's
maximum, because every read would fail there. It also notes
([CSG019](../guides/generated/diagnostics.md#csg019---a-constraint-label-names-different-conditions)) when one label names two
different conditions, since a report could not tell them apart. It only reports what it can prove.

## Where a constraint is checked

- **Reads.** `Parse`, `ParseMany`, `ReadValue`, `TryReadValue`, stream and asynchronous reads, the debug read and the
  generated `Parse` and `TryParse` check every constraint of every struct they read, including the elements of an array
  of structs, a pointer's target and the fields of a selected `if`/`switch` arm. A field that is never read (an unselected
  arm) is not checked, and a selected read of one field (`ReadValue<int>(bytes, "root.field")`) reads only that field.
- **Fast paths.** The runtime decodes a fixed struct whose own fields, or whose anonymous struct members' fields, carry
  constraints through its fixed-offset plan and evaluates each field's conditions right after that field, in field
  order, with the same failure and offset as the member-by-member reader. A whole root read from memory
  (`Parse(bytes, "root")`) is decoded and checked directly; when a condition fails, the root is read again through the
  engine, which raises the failure. A struct that contains a struct with a constraint, or whose anonymous union member
  has one, is read member by member, and so is a read whose limits or input the plan does not cover. The generated reader
  keeps its fixed-offset reader for such a struct (when everything else about it is fixed) and checks the conditions in
  field order right after decoding, with the same failure and offset; a struct that contains a struct with a constraint,
  or whose anonymous struct or union member has one, is read member by member there too. A layout without constraints
  pays nothing, and a write of a struct with constraints keeps its fixed-offset writer because it checks nothing.
- **Generated views.** A view decodes lazily and never checks on its own. `Validate()` checks the conditions of the
  view's struct that the view can evaluate (it reads the members they name through the view's properties), including those
  of the members of an anonymous struct or union and of the members after it, and throws the reader's failure with the
  reader's `Offset`, the end of the failing field; a condition that names a member the view does not expose is left to
  `Parse`.
- **Without throwing.** An exception costs far more than the check it reports (it captures a stack trace and unwinds), so
  code that probes many inputs for a format should not use one as its answer. The generated `TryParse` of a fixed-size struct
  with constraints, the constraint pass behind it, and a generated view's `TryValidate(out failure)` return a broken condition
  as a failure value and throw nothing. `Matches(bytes)` goes further: it decodes only the members the `@expect` conditions
  name, straight from their offsets, builds no value, and answers yes or no. A source that is too short for the struct, or a
  limit, still fails with an exception that `TryParse` catches. See
  [TryParse](../guides/generated/sequences-and-try-parse.md#trying-without-an-exception).
- **Writes.** A constraint describes what a read accepts, so a write does not check it: `Serialize` and `Update` write
  any value the field's type holds. Check the value before writing, or read it back.

## Where a constraint is not allowed

A union member, padding (`_`) and an unnamed bitfield cannot carry a constraint (`@expect`, `@supports` or `@magic`): the members of a union overlap and the
other two have no name to quote. A condition that does not parse or names an unknown identifier at layout time is a layout
error, reported when the layout is built.

```c
union u { uint8 a @expect(a == 1); uint16 b; };   // layout error: a constraint on a union member
struct s { uint8 _ @expect(1); };                  // layout error: padding has no name
```

## In JavaScript and the browser

The layout text, including its constraints, works unchanged in Node.js and the browser. A value that breaks a constraint
fails the read with the code `read-failed`, and the message names the field and the condition (or is the layout's own message
when the constraint has one, followed by the label and the position). `ConstraintFailures` is a generated-code option and has
no effect there. Collecting every broken
condition (`ReadOptions.ConstraintViolations`), the offset base and `Matches` are managed C# API; the browser bridge does
not expose them.

## Check yourself

1. A header says `uint8 version @supports(version == 1)` and the bytes hold `2`. Is the file malformed?
2. Why can the generated reader still use its fixed-offset reader for a struct with constraints?
3. Does `Serialize` reject a value that violates a constraint?
4. With `ConstraintFailures = FormatExceptions`, what does `TryParse` return for a value that breaks an `@expect`?
5. A read sets `Constraints = ConstraintPolicy.Skip` and a `ConstraintViolations` list. What happens?

<details>
<summary>Answers</summary>

1. Not necessarily: `@supports` reports `ConstraintKind.Unsupported`. The same value under `@expect` would be malformed.
2. It decodes the whole struct first and then checks the conditions in field order, so an invalid value never leaves the
   reader, and the failure is the one a member-by-member read would raise.
3. No. A constraint checks what a read accepts; a write encodes the value as given.
4. `false` and the `CStructConstraintException` itself: only the throwing entry points use the format exceptions.
5. The read fails with `ArgumentException` before it reads a byte: skipping evaluates nothing, so there is nothing to collect.

</details>
