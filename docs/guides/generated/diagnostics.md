---
title: Generator and analyzer diagnostics
description: Every CSG diagnostic the generator and the analyzer report - what causes it, an example, and the fix.
---

# Generator and analyzer diagnostics

The generator reports problems as build diagnostics with ids `CSG001` to `CSG300`, each pointing at the attribute
or expression that caused it. The table is generated from the analyzer's release files by
`tools/documentation/validate-generator-diagnostics.mjs`, which the documentation gate runs with `--check` so the
list cannot drift from what ships.

<!-- generator-diagnostics:start -->
| Id | Severity | Title |
| --- | --- | --- |
| [CSG001](#csg001---layout-does-not-compile) | Error | Layout does not compile |
| [CSG002](#csg002---layout-file-not-found) | Error | Layout file not found |
| [CSG003](#csg003---generated-name-collision) | Error | Generated name collision |
| [CSG004](#csg004---unknown-root-declaration) | Warning | Unknown root declaration |
| [CSG005](#csg005---attributed-class-must-be-partial) | Error | Attributed class must be partial |
| [CSG006](#csg006---custom-codec-declaration-is-invalid) | Error | Custom codec declaration is invalid |
| [CSG010](#csg010---c-12-or-later-is-required) | Error | C# 12 or later is required |
| [CSG011](#csg011---the-views-surface-needs-views) | Error | The Views surface needs views |
| [CSG012](#csg012---a-cstructsharp-project-property-is-not-valid) | Warning | A CStructSharp project property is not valid |
| [CSG013](#csg013---runtimebyteorder-is-not-supported-here) | Error | RuntimeByteOrder is not supported here |
| [CSG014](#csg014---a-value-type-shape-is-not-supported-here) | Error | A value-type shape is not supported here |
| [CSG015](#csg015---a-scalar-shape-is-not-supported-here) | Error | A scalar shape is not supported here |
| [CSG016](#csg016---a-generated-layout-is-never-used) | Info | A generated layout is never used |
| [CSG017](#csg017---a-generated-layout-generates-more-than-its-uses-need) | Info | A generated layout generates more than its uses need |
| [CSG018](#csg018---the-conditions-on-a-member-can-never-hold) | Warning | The conditions on a member can never hold |
| [CSG019](#csg019---a-constraint-label-names-different-conditions) | Info | A constraint label names different conditions |
| [CSG020](#csg020---a-magic-follows-a-variable-size-member) | Warning | A magic follows a variable-size member |
| [CSG021](#csg021---a-layout-qualifies-for-the-scalar-shape) | Info | A layout qualifies for the scalar shape |
| [CSG022](#csg022---a-layout-qualifies-for-value-type-shapes) | Info | A layout qualifies for value-type shapes |
| [CSG023](#csg023---members-of-a-generated-layout-are-never-read) | Info | Members of a generated layout are never read |
| [CSG100](#csg100---mapped-type-must-be-partial-with-a-parameterless-constructor) | Error | Mapped type must be partial with a parameterless constructor |
| [CSG101](#csg101---mapped-member-type-is-not-mapped) | Error | Mapped member type is not mapped |
| [CSG102](#csg102---mapped-member-has-no-layout-counterpart) | Warning | Mapped member has no layout counterpart |
| [CSG200](#csg200---path-does-not-resolve-against-the-layout) | Warning | Path does not resolve against the layout |
| [CSG201](#csg201---parse-selects-a-root-that-is-not-a-struct) | Info | Parse selects a root that is not a struct |
| [CSG300](#csg300---dynamic-over-a-parsed-value-in-a-trimmed-or-aot-published-project) | Warning | dynamic over a parsed value in a trimmed or AOT-published project |
<!-- generator-diagnostics:end -->

The `CSG0xx` ids come from `[CStructLayout]`, `CSG1xx` from `[CStructMapped]`, and `CSG2xx`/`CSG3xx` from the
analyzer that looks at how a project uses the runtime API.

## CSG001 - Layout does not compile

**Cause.** The layout text (inline or in a `.cstruct` file) fails to parse or compile. This is an error, and the
message is the runtime's own - the same text `new CStruct(text)` would throw at run time.

```csharp
[CStructLayout("struct header { uint32 length; uint8 tail[length + ]; }")]
public static partial class Broken { }
// CSG001: Unexpected token '}' in array length expression ...
```

**Fix.** Correct the layout. The message names the line and column; the
[language manual](../../language/index.md) covers every construct.

## CSG002 - Layout file not found

**Cause.** `[CStructLayout(File = "...")]` names a file that is not among the project's `AdditionalFiles`. An error.

**Fix.** The package's build props include every `.cstruct` file under the project automatically; a file with
another extension, or one outside the project, needs `<AdditionalFiles Include="path" />` in the project file.

## CSG003 - Generated name collision

**Cause.** Two layout identifiers become the same C# name after PascalCase conversion (`chunk_type` and
`ChunkType`), a generated type is named like the containing class, the class itself is named like a generated
member (`Layout`, `Parse`, `Update`, ...), a conditional member's `Has<Member>` flag or a fixed array's view slice
`<Member>Bytes` is spelled by another member, or a member the view exposes is named `bytes` or `to_object` (the
view's own `Bytes` and `ToObject`), or an integer `#define` (which becomes a constant of the class under its own name) is named like a
generated member or type. An error, because C# cannot compile the result.

**Fix.** Rename the identifier in the layout, rename the class, use `[CStructLayout(KeepNames = true)]` to keep
the C spellings, or `Views = false` when only a view member collides.

## CSG004 - Unknown root declaration

**Cause.** `Root = "name"` names a declaration the layout does not have. A warning: the class still generates, and
the plain `Parse`/`Serialize` use the compiler's default root (the last struct declared) instead.

**Fix.** Spell the root as the layout declares it, or drop `Root` to accept the default.

## CSG005 - Attributed class must be partial

**Cause.** The attributed class is not `static partial`, or a type that contains it is not `partial`, so the
generator has nowhere to add members. An error.

**Fix.** Declare the class `public static partial class Name` and mark every containing type `partial`.

## CSG006 - Custom codec declaration is invalid

**Cause.** An entry of `Codecs = [...]` is not `"name"`, `"name:size"`, `"name:size:alignment"`, or
`"name:*:alignment"`, repeats a built-in type, or has an alignment that is not a power of two. An error.

**Fix.** Declare each codec as the [custom codec recipe](../../examples/recipes/custom-codec.md) shows and implement
`CreateCodecs()` to return one instance per declaration, in order.

## CSG010 - C# 12 or later is required

**Cause.** The project's language version is older than C# 12, which the generated code needs (collection
expressions, static abstract interface members). An error.

**Fix.** Add `<LangVersion>12</LangVersion>` (or newer) to the project file; .NET 8 and later default to it.

## CSG011 - The Views surface needs views

**Cause.** `Surface = GeneratedSurface.Views` generates only views, and `Views = false` turns views off, so together they
would generate nothing. An error. Either value may come from the attribute or from the project's `CStructSharpSurface`
and `CStructSharpViews` properties.

**Fix.** Remove `Views = false`, or choose `GeneratedSurface.Reader` or `GeneratedSurface.Full`.

## CSG012 - A CStructSharp project property is not valid

**Cause.** One of the `CStructSharp*` build properties that set a project-wide default (see
[Project defaults](choosing-runtime-or-generated.md#project-defaults)) has a value the generator does not understand,
for example `<CStructSharpSurface>Tiny</CStructSharpSurface>`. A warning: the property is ignored and the library's
default applies.

**Fix.** Use a valid value: `Full`, `Reader`, `Parser` or `Views` for `CStructSharpSurface`; `SysV` or `Msvc` for
`CStructSharpBitfieldPacking`; `LowBitFirst` or `HighBitFirst` for `CStructSharpBitfieldAllocation`; `1`, `2`, `4` or
`8` for `CStructSharpPointerSize`; `32` or `64` for `CStructSharpCLongWidth`; `Class`, `Struct` or `Auto` for
`CStructSharpShape`; `Constraint` or `FormatExceptions` for `CStructSharpConstraintFailures`; a whole number from 1 to 65536 for `CStructSharpMaxValueBytes`; `true` or `false` for the flag properties.

## CSG013 - RuntimeByteOrder is not supported here

**Cause.** `RuntimeByteOrder = true` (or the project's `CStructSharpRuntimeByteOrder`) cannot be generated: the layout uses
the full surface, which writes and so encodes one byte order, or a bitfield sits in a different place when the layout is
compiled big-endian than when it is compiled little-endian, or the layout does not compile in the opposite order. An error;
the message names the reason and, for a bitfield, the field.

**Fix.** Use `GeneratedSurface.Reader`, `Parser` or `Views`, and give the bitfield members an order that does not move them
(declare them with the same storage type, or separate runs with an ordinary member), or keep two layouts, one per order.

## CSG014 - A value-type shape is not supported here

**Cause.** `Shape = GeneratedShape.Struct` (or the project's `CStructSharpShape`) cannot be generated for this layout: the
surface is `Full` (its writers, `Update` setters and `Records` need classes) or `Views` (which generates no type), a struct is
bigger than `MaxValueBytes` (1,024 unless raised), or a struct holds something a value type cannot: a union, a conditional
member, a pointer, a runtime-sized or multidimensional array, a custom codec or a terminated string. An error; the message
names the member and the reason.

**Fix.** Use `GeneratedSurface.Reader` or `Parser`, change the layout so every struct is fixed in size with the shapes listed
above, raise `MaxValueBytes`, keep `Shape = GeneratedShape.Class` for that layout, or use `Shape = GeneratedShape.Auto` to take
the value type only where it fits.

## CSG015 - A scalar shape is not supported here

**Cause.** `Shape = GeneratedShape.Scalar` cannot be generated for this layout: the surface is `Full` or `Views`, or the layout
is not one struct whose only member is a number, an enum or an identifier at offset 0 that fills the struct (a second member,
an array, a bitfield, a nested struct, a pointer, a conditional member, a custom codec or an alignment that pads it). An error;
the message names the reason.

**Fix.** Use `GeneratedSurface.Reader` or `Parser` and a one-member layout, or choose another shape.

## CSG016 - A generated layout is never used

**Cause.** A `[CStructLayout]` class that no other assembly can see is not referenced by any code in this one, so everything
generated for it is dead weight. Reported as info; once per class and only when the analysis is certain (no friend assembly,
no `[CStructMapped]` class).

**Fix.** Delete the layout, or use it. Raise the severity in an `.editorconfig` to make a new unused layout a build error.

## CSG017 - A generated layout generates more than its uses need

**Cause.** Every member of the layout that the code uses is also generated by a smaller `Surface`, or the layout generates views
that no code names. Reported as info; the message lists the members used and the options to write.

```text
The layout class 'Header' is used only through Parse; generate less with Surface = GeneratedSurface.Parser, Views = false
```

**Fix.** Write the suggested options on the attribute, or set them as [project defaults](choosing-runtime-or-generated.md#project-defaults).
A call with a `byte[]` argument needs the `Reader` surface (the array overload); passing a `ReadOnlySpan<byte>` needs only `Parser`.

## CSG018 - The conditions on a member can never hold

**Cause.** The `@expect` and `@supports` conditions of one member exclude every value its type can hold, so every read of the
layout fails at that member. The analyzer compares the member with constants and `#define`d integers and combines the
comparisons with `&&`, `||` and `!`; a bound outside the type is the simplest case. It reports only what it can prove: a condition
that uses another member, a mask or arithmetic on the member is not reduced, and a layout that does not compile is left to CSG001.
Reported as a warning at the layout text, for a `[CStructLayout]` attribute and for a literal passed to `new CStruct` or
`CStruct.GetOrCompile`.

```text
The conditions on 'a.big' can never hold: 'big > LIMIT' holds for no value of uint8 (0 to 255), so every read of this layout fails
```

**Fix.** Correct the bound (a `uint8` is at most 255), or merge conditions that were meant as alternatives into one `||` condition.

## CSG019 - A constraint label names different conditions

**Cause.** Two constraints of one layout carry the same label but test different conditions, for example the magic numbers of two
structs in one file under `"xfs.magic"`. A label is what a report shows to say which constraint failed, so a repeated label makes
two failures look alike. Reported as info; repeating the same condition under one label is not reported.

**Fix.** Give each condition its own label (`"xfs.sb.magic"`, `"xfs.agf.magic"`), or keep one label only where the condition is the same.

## CSG020 - A magic follows a variable-size member

**Cause.** A member with an `@magic` condition comes after a member whose size depends on the data (a runtime-sized array,
a string), so its offset is not known when the layout is compiled. The magic still works: it is checked as soon as its member has
been read, with the conditions of the members in front of it held back until then. But `Matches` cannot evaluate it (it decodes
only members at known offsets), and the members before it have to be read before the format is recognized. Reported as a warning.

**Fix.** Move the magic member to the front of the struct, or before the first variable-size member.

## CSG021 - A layout qualifies for the scalar shape

**Cause.** A layout generated as classes (the default `Shape`) on the `Reader` or `Parser` surface is one struct with one number, enum
or identifier at offset 0 that fills the struct, such as a magic number. A scalar `Parse` returns that value directly, so no class
exists to allocate. Reported as info, once per class; a layout that sets `Shape` is not second-guessed.

```text
The layout class 'Magic' qualifies for Shape = GeneratedShape.Scalar: Parse can return the ushort itself and no type needs to be generated
```

**Fix.** Set `Shape = GeneratedShape.Scalar` (the caller then uses the value, not a `.A` member), or ignore the suggestion when you want the
struct type. See [value types](choosing-runtime-or-generated.md#value-types-instead-of-classes) and [just the value](choosing-runtime-or-generated.md#just-the-value-of-a-one-field-layout).

## CSG022 - A layout qualifies for value-type shapes

**Cause.** Every struct of the layout, on the `Reader` or `Parser` surface, is fixed in size and holds only numbers, enums,
bitfields, nested structs, text and fixed one-dimensional arrays, and is no larger than `MaxValueBytes`. Generating them as `struct`
values means a read allocates no object. Reported as info, once per class, and not when `CSG021` applies.

```text
The layout class 'Pair' qualifies for Shape = GeneratedShape.Struct: its structs are fixed in size and can be value types, so a read allocates no object
```

**Fix.** Set `Shape = GeneratedShape.Struct`, or `GeneratedShape.Auto` to take value types wherever a layout qualifies. A value type is
copied when it is assigned or returned, so keep a large struct a class; ignore the suggestion when the code needs reference semantics.

## CSG023 - Members of a generated layout are never read

**Cause.** A layout on the `Reader` or `Parser` surface generates a property for every named member, but no code in the project reads some
of them: reserved fields, unknown fields, flags nobody checks. Reported as info, once per class, with the first eight names as
`Type.Member`. A write to a property is not a read, and a layout that also writes (the `Full` surface serializes every member) is not
checked. Like [CSG016](#csg016---a-generated-layout-is-never-used) it needs a class that no other assembly can see and stays quiet when a
friend assembly or a `[CStructMapped]` class may use it.

```text
The layout class 'Header' generates members that no code reads: Root.Reserved, Root.Present; declare bytes you do not need as '_' padding in the layout
```

**Fix.** For bytes the code will never need, rename the member to `_` in the layout: it is then [padding](../../language/structs-unions-enums-typedefs.md#padding-fields),
read and skipped, with no property. Leave a member alone when the layout documents a format you want to keep complete, when a condition
in the layout names it (a name used by an `@expect` or an array length is still needed by the reader even if no code reads the property),
or when it is read only by code the analyzer cannot see (reflection, another assembly).

## CSG100 - Mapped type must be partial with a parameterless constructor

**Cause.** A `[CStructMapped]` class is not `partial`, or has no parameterless constructor for the generated
`ReadFrom` to create an instance. An error.

**Fix.** Declare the class `partial` and give it a parameterless constructor (an implicit one is enough).

## CSG101 - Mapped member type is not mapped

**Cause.** A property of a mapped class has a class type that is neither `[CStructMapped]` nor implements
`ICStructMapped<T>`, so the generator cannot map a nested struct into it. An error.

**Fix.** Mark the nested class `[CStructMapped]`, or declare the property as `StructValue` to keep the value
untyped.

## CSG102 - Mapped member has no layout counterpart

**Cause.** With `Layout` given, a property matches no member of that layout by exact name, case-insensitively, or
ignoring underscores. A warning: the property is left alone by `ReadFrom` and `WriteTo`.

**Fix.** Add `[CStructMember("member_name")]` or rename the property. A property that is not meant to be mapped
needs no public setter: the mapper only considers public properties that can be both read and written.

## CSG200 - Path does not resolve against the layout

**Cause.** A constant path - the `path` argument of any `CStruct` method, such as `ReadValue`, `TryReadValue`,
`GetAccessor`, `ResolveAddress`, `GetArrayLength` or `Update`, or the declaration name of `GetStructSizeInBytes` -
names a declaration or member that the layout does not have. The analyzer checks it when the `CStruct`'s layout is
visible at build time: `new CStruct("...")` or `CStruct.GetOrCompile("...")` with constant arguments and no
compilation options, or a generated class's `Layout`. It compiles the layout with the same pointer size, alignment,
byte order and attribute settings as the runtime. A warning, because the runtime would throw `CStructPathException`.

```csharp
Wire.Layout.ReadValue(bytes, "header.lenght"); // CSG200: no member 'lenght' in 'header'
```

**Fix.** Correct the path; the message lists the members that exist.

## CSG201 - Parse selects a root that is not a struct

**Cause.** `CStruct.Parse` is called with a root that the visible layout declares as a union, an enum, or a
scalar. Reported as info: `Parse` returns structs only, so the call fails at run time.

**Fix.** Use `ReadValue` for a union or a scalar root.

## CSG300 - dynamic over a parsed value in a trimmed or AOT-published project

**Cause.** A `StructValue` (or `UnionValue`) is bound as `dynamic` in a project that sets `PublishAot` or
`PublishTrimmed`; the runtime binder is not available there. A warning.

**Fix.** Read members through the indexer, `Get<T>`, or a [mapped class](mapped-classes.md), which need no
runtime binding.
