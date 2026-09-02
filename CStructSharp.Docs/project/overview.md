---
title: Project overview
description: Understand what CStructSharp does, what the repository is preparing to release, and where its limits are.
---

# Project overview

CStructSharp is a .NET library for reading and writing binary formats described by a small C-like language. A layout
names the fields in a file or message and gives each field an exact width and placement rule. The library prepares
that layout once, then applies it to bytes supplied by the application.

The core supports:

- parsing a complete struct or union into runtime values;
- reading one selected value, either directly or mapped to a C# type;
- reporting the byte range or stream address of a value;
- serializing new data to an array, span, buffer writer, or stream; and
- replacing an existing value without moving the surrounding data.

The repository currently declares release candidate `0.2.0-preview` for .NET 8 and .NET 10. This is the version being
validated by the release files; it does not by itself prove that the package has been published. The managed public
surface contains 20 types and is checked against a reviewed `managed-rc1` baseline so accidental signature changes
are caught.

## The Portable layout language

The supported layout rules are called *Portable*. The same layout and constructor options produce the same widths,
offsets, alignments, and value ranges on every supported .NET runtime.

Portable syntax resembles C, but it does not ask the operating system or a C compiler how to lay out data. The
library does not process arbitrary translation units, system headers, includes, general macros, compiler attributes,
or platform-native ABI rules.

ABI means *application binary interface*: among other things, it defines how a particular compiler and target place
native fields in memory. CStructSharp avoids guessing an ABI. The binary format must supply its actual integer
widths, byte order, alignment behavior, and pointer width.

## When the library is a good fit

Use CStructSharp when the data can be described by the [Portable language](../language/index.md) and you need one or
more of these:

- fixed cross-platform field placement;
- integers, arrays, strings, enums, unions, bitfields, or stored pointers;
- bounded processing of untrusted input;
- runtime inspection or mapping to ordinary C# objects;
- byte positions for a format viewer or diagnostic tool; or
- an in-place update whose storage size cannot change.

CStructSharp is not a drop-in C compiler, native-memory marshaller, schema registry, or database transaction system.
Direct stream writes can leave a prefix after a late failure. `UpdateStream` validates errors the library can detect
before changing the destination, but a physical stream can still accept part of the final commit and then fail.

## Optional browser workbench

`CStructSharpWeb.Wasm` adapts the managed library to WebAssembly, and `CStructSharpWeb` provides a Vue/Vite workbench.
They are separate from the NuGet library and are not needed to build, test, or document the core. Their JSON-facing
behavior has its own versioned browser format because JavaScript callers do not consume the managed .NET API
directly.

Next, follow [Contributor setup](getting-started.md) to build the routine non-Web workspace.
