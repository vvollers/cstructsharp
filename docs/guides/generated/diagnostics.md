---
title: Generator and analyzer diagnostics
description: Every CSG diagnostic the generator and the analyzer report - what causes it, an example, and the fix.
---

# Generator and analyzer diagnostics

The generator reports problems as build diagnostics with ids `CSG001` to `CSG300`, each pointing at the attribute
or expression that caused it. The table is generated from the analyzer's release file by
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
view's own `Bytes` and `ToObject`). An error, because C# cannot compile the result.

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

**Cause.** A constant path passed to `ReadValue`, `ResolveAddress`, `GetArrayLength`, `Update`, or `UpdatePath` on
a `CStruct` whose layout is visible at build time (`new CStruct("...")`, `CStruct.GetOrCompile("...")`, or a
generated class's `Layout`) names a declaration or member the layout does not have. A warning, because the
runtime would throw `CStructPathException`.

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
