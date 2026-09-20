---
title: How the generator works
description: The pipeline from attribute to generated code, the Core sources the compiler and the runtime share, the cursor the generated code reads through, how parity is tested, and how to debug generated code.
---

# How the generator works

You do not need this page to use the generator. Read it when you want to know why the generated code agrees
with the runtime, or when you are about to change either.

## The pipeline

1. **Attribute.** Roslyn finds every class with `[CStructLayout]` (`ForAttributeWithMetadataName`, so classes
   without the attribute cost nothing) and the generator builds a small equatable *request*: the layout text or
   file name, the options, the class name, its namespace and containing types, `KeepNames`, `Root`.
2. **Parse and compile.** The request's text goes through `LayoutParser` and `CStructCompiledModel` - the same
   classes `new CStruct(text)` calls. The result is the compiled model: declarations, member types, fixed offsets
   where the layout makes them fixed, folded `#define`s, the bitfield allocation.
3. **Model.** `GeneratedModel` walks the compiled model once and decides every C# name (PascalCase, collisions,
   reserved names) and every member's C# type. Name collisions become `CSG003` here, before any code exists.
4. **Emit.** `LayoutEmitter` writes the file in the order you see it: the frame (`Definition`, `Layout`,
   `RootName`), the types, the readers, the writers, the operations (`Sizes`, `Offsets`, `Update`,
   `ParseWithDebug`, the bridge), and the views. `ExpressionEmitter` turns a layout expression into a call chain
   over `Expressions` so an array length or a selector is evaluated with the runtime's rules.
5. **Compile.** Roslyn adds the file to your compilation. Whatever it contains is checked like your own code.

`[CStructMapped]` has a smaller pipeline of its own: it inspects the class's properties, resolves names against
the project's layouts when `Layout` is given, and emits `ReadFrom`/`WriteTo` over `StructValue.Get<T>`.

## One Core, two hosts

The generator is a `netstandard2.0` assembly loaded into the compiler, where the runtime library is not
available. It therefore compiles the runtime's *Core* sources into itself: `src/CStructSharp.Core/` is a source
folder, not a project, that both `CStructSharp.csproj` and `CStructSharp.Generators.csproj` include. Core holds
the parser, the expression evaluator, the compiled model with its placement rules, the introspection model, the
codec descriptors, the option types, and the exception family with every failure text - and nothing that does
I/O: no streams, no spans, no delegates, no reflection.

That is why the generated offsets are the runtime's offsets: they are computed by the same function. And it is
why a failure text is the same in both paths: `ReadFailures` and `WriteFailures` are Core, and both the runtime
reader and the generated reader build their messages from them.

## The cursor

Generated code is straight-line C#, but the limits and the diagnostics must stay identical to the runtime's, so
the generated readers work through a small `ref struct` in `CStructSharp.Generated`:

- `ReadCursor` holds the position, the remaining bytes, the `ReadOptions` snapshot, the byte budget, the pointer
  depth, and the path prefix. `TakeUInt16()` and friends read a primitive and advance; `Seek` moves within the
  region; `Complete` attaches the offset to an exception at the operation boundary, the way the runtime does.
- `WriteCursor` is the writing counterpart, over a fixed span or a growable buffer.
- `CompositeCursor` tracks where a struct started, for alignment and for the bitfield unit rules.
- `Codec` decodes text and bitfields; `Expressions` implements the operators.

Everything the runtime reader checks (short reads, array limits, string budgets, pointer depth, total bytes) is
checked by the cursor with the same texts, and the parity tests hold it to that.

## How parity is tested

- **Snapshots** (`tests/CStructSharp.Generators.Tests/Snapshots/*.g.cs`): the generated file for a fixture,
  compared byte for byte; `UPDATE_SNAPSHOTS=1` rewrites them, and a rewrite is reviewed as a diff.
- **Parity tests** (`tests/CStructSharp.Generated.Parity/`): every layout fixture the runtime is tested with is
  generated into one project by `tools/quality/generate-parity-layouts.mjs`, and for each one the generated
  `Parse` is compared with `Layout.Parse` on the same bytes, member by member, including pointers, unions, and
  conditional arms. A truncation sweep cuts the bytes at every length and requires the same exception type and
  text from both paths. Writers are checked by round trip: serialize the runtime's value with the generated
  writer and the generated value with the runtime writer, then compare the bytes.
- **Differential fuzzing** (`tests/CStructSharp.Fuzz`, target `generated-differential`): random inputs through
  both readers and both writers, any disagreement is a finding.
- **Benchmarks** (`GeneratedBenchmarks`) keep the generated path measurably ahead of the runtime; the numbers are
  on the [performance page](../performance.md).

## Debugging generated code

- Turn on `<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>` and read the file; it is ordinary C#
  with the layout member named in a comment above each read.
- `Wire.ParseWithDebug(bytes)` returns the generated value together with the runtime's debug information - every
  member's offset and size - for the same bytes, so a wrong value can be placed.
- `Wire.Layout` is the runtime `CStruct` for the same text: `Wire.Layout.Parse(bytes)` is the oracle when you
  suspect the generated reader. If the two disagree, that is a bug; the parity project shows how to turn the
  layout into a fixture.
- `dotnet build -v:d` lists the generator's diagnostics with their locations when the build fails before code
  exists.

## Check yourself

1. Why can the generator not reference the runtime assembly?
2. Where does the text of `Array length mismatch for values: expected 4, got 3.` live?
3. What is the oracle a parity test compares the generated reader against?

<details>
<summary>Answers</summary>

1. It runs inside the compiler as a `netstandard2.0` analyzer; the runtime targets .NET 8 and 10 and is not loaded there. It compiles the Core sources instead.
2. In `WriteFailures`, part of Core, used by both writers.
3. The runtime `CStruct` built from the same definition (`Wire.Layout`).

</details>

## Exercise

Write a layout with a member whose offset depends on data (`uint8 count; uint8 items[count]; uint16 tail;`),
generate it, and look for `Offsets.Tail` and `Update.Tail` in the generated file.

<details>
<summary>Solution</summary>

Neither exists: `Sizes`/`Offsets` and the typed setters cover members whose position the compiler fixed;
`tail` moves with `count`, so it is reached through `Parse` or through `UpdatePath(bytes, "tail", value)`.

</details>
