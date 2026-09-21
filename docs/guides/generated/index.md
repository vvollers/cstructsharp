---
title: Generated code
description: What a source generator is, why a layout known at build time can become C# classes, where the generated files live, and when the runtime API is the better tool.
---

# Generated code

The runtime API reads a layout when the program runs: `new CStruct("...")` parses the text, compiles it into a
plan, and `Parse` follows that plan byte by byte. That is the right tool when the layout arrives with the data or
changes between runs. Many programs, though, know their formats before they are compiled - a file header, a
network packet, a save-game record. For those, CStructSharp ships a **source generator**: the same layout text, put
on a C# class, becomes ordinary C# code at build time.

This series teaches that path, one idea per page:

1. [Your first generated layout](first-generated-layout.md) - the six-byte header as a `[CStructLayout]` class.
2. [Views and zero allocation](views-and-zero-allocation.md) - reading without creating objects.
3. [Arrays, strings, and enums](arrays-strings-enums.md) - how each layout type becomes a C# type.
4. [Unions, bitfields, and nested structs](unions-bitfields-nested.md) - the composite shapes.
5. [Pointers and budgets](pointers-and-budgets.md) - stored addresses and the limits that protect a read.
6. [Conditional fields](conditionals.md) - `if` and `switch` in generated code.
7. [Writing and updating](writing-and-updating.md) - `Serialize`, `Write`, typed setters.
8. [Sequences and TryParse](sequences-and-try-parse.md) - `TryParse`, `Records`, the view enumerator, `ParseAsync`.
9. [Mapped classes](mapped-classes.md) - `[CStructMapped]` for your own types.
10. [Diagnostics](diagnostics.md) - every CSG message, its cause, and its fix.
11. [How it works](how-it-works.md) - the pipeline from attribute to code.
12. [Runtime or generated?](choosing-runtime-or-generated.md) - a decision table.

## Compile time and run time

A C# program has two lives. At **compile time** the compiler turns source files into an assembly; nothing of your
program executes yet. At **run time** the assembly is loaded and its code runs. `new CStruct("struct header {...}")`
does all of its work at run time: when that line executes, the text is parsed and compiled into a plan, and the
plan is followed for every `Parse`.

A **source generator** is a component that runs *inside the compiler*. The compiler hands it the syntax trees and
symbols of your project, and the generator hands back additional C# source files that the compiler then compiles
together with yours. Nothing of the generator's own code ends up in your program; only the C# it wrote does.

The compiler that hosts generators is **Roslyn**, the C# compiler platform. The same Roslyn runs in `dotnet build`,
in Visual Studio, in Rider, and in VS Code, so a generator's output is the same everywhere and appears in the IDE
with IntelliSense as if you had typed it.

## What the CStructSharp generator does

Put the layout text on a `static partial` class:

```csharp
[CStructLayout("struct header { uint16 kind; uint32 length; };")]
public static partial class Wire { }
```

The generator parses that text with the same parser the runtime uses, compiles it with the same rules (the same
offsets, the same alignment decisions, the same expression semantics), and writes:

- one C# class per struct or union (`Wire.Header`) with typed properties;
- `Wire.Parse(bytes)` and `Wire.ParseHeader(...)` readers that decode straight into those classes;
- `Wire.Serialize(header)` and `Wire.Write(stream, header)` writers;
- a `readonly ref struct` view (`Wire.HeaderView`) that decodes members on demand without allocating;
- `Wire.Sizes`, `Wire.Offsets`, and typed `Wire.Update` setters for the members whose position is fixed;
- `Wire.Layout`, the runtime `CStruct` for the same text, for anything the generated code does not do.

The generated reader and the runtime reader produce the same values and the same failures for the same bytes;
the parity tests in the repository check that on every fixture the runtime is tested with.

## Where the generated files are

Generated sources are compiled into your assembly but are not written into your source tree. To read them:

- In an IDE, expand the project's **Dependencies → Analyzers → CStructSharp.Generators** node.
- In any build, add `<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>` to the project file: the files
  appear under `obj/<Configuration>/<TargetFramework>/generated/CStructSharp.Generators/...`, one per attributed
  class, named `<Namespace>.<Class>.CStructLayout.g.cs`.

The next lesson reads one of these files line by line.

## When not to generate

Use the runtime API when:

- the layout is only known at run time (a format loaded from a file, typed by a user, or received over the wire);
- you want to explore data interactively or through `dynamic`;
- the same program must accept many formats it did not compile against;
- you are reading a memory image with the memory-analysis API, which works on `CStruct` layouts.

Use the generator when the layout is part of the program's source: it gives you typed classes with IntelliSense,
readers that do not build a `StructValue` in between, and views that do not allocate. The
[decision table](choosing-runtime-or-generated.md) puts the two side by side with measurements. Both paths can be
used in the same program, and a generated class always exposes its runtime `Layout`.

## Check yourself

1. Does `new CStruct("...")` do any work at compile time?
2. Where does the code a generator writes end up?
3. A tool must accept layouts its users write in a text box. Which path fits?

<details>
<summary>Answers</summary>

1. No. The text is a string literal; parsing and compiling happen when the line runs.
2. In your assembly, compiled with your sources; the generated `.g.cs` files are visible under the project's
   Analyzers node or under `obj/` when `EmitCompilerGeneratedFiles` is on.
3. The runtime API: the layout is not known when the tool is compiled.

</details>

## Exercise

Add `<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>` to a project that declares the `Wire` class
above, build it, and find the generated file. Count the `Parse` overloads it contains.

<details>
<summary>Solution</summary>

The file is `obj/Debug/net10.0/generated/CStructSharp.Generators/CStructSharp.Generators.CStructLayoutGenerator/<Namespace>.Wire.CStructLayout.g.cs`.
It contains `ParseHeader` for a `ReadOnlySpan<byte>`, a `byte[]`, a `ReadOnlyMemory<byte>`, and a `Stream`, and
the same four `Parse` overloads for the root declaration: eight in total.

</details>
