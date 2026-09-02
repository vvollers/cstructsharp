---
title: Portable v1 rules
description: Understand the fixed cross-platform rules behind CStructSharp layouts and their versioned reference data.
---

# Portable v1 rules

Portable is the only layout behavior CStructSharp currently ships. The name means that the same source and the same
constructor options produce the same sizes, offsets, alignments, value ranges, and bytes on every supported .NET
runtime.

The library does not inspect the current operating system, CPU, process pointer width, native byte order, culture,
installed C compiler, or system headers when it prepares a layout.

## Format choices belong to the layout

The `CStruct` constructor defaults to:

- an eight-byte stored pointer;
- packed field placement (`aligned: false`); and
- little-endian order for neutral multi-byte values.

These defaults describe the binary data, not the machine running the application. Pass the three values explicitly
when code reads a persisted or externally defined format. That makes an accidental format change visible in a code
review.

There is no public API for selecting `MSVC`, `GCC`, `Clang`, `SysV`, `LP64`, `LLP64`, or “native” behavior.
“Portable” is the name used in documentation and reference files, not a profile string passed to the constructor.

## Why the rules are versioned

The files below let people, tests, and tools check the same details:

- [`portable-v1.json`](../contracts/language/portable-v1.json) records revision 1 of every primitive spelling,
  predicted layout example, and representative unsupported C form.
- [`manual-fixtures-v1.json`](../contracts/language/manual-fixtures-v1.json) supplies one valid and one invalid case
  for each row in the [feature table](operation-matrix.md).

`CanonicalPortableReferenceTests` executes the primitive and layout records on .NET 8 and .NET 10.
`ManualLanguageFixtureTests` checks fixed sizes, alignments, offsets, values, bytes, and error categories on both
frameworks. Repository validators make sure the JSON, feature table, test names, and linked manual headings still
agree.

The `v1` filenames version these data formats and their current rule set. They do not mean that a selectable native
compiler profile exists.

## Relationship to C compilers

Small compiler-comparison fixtures record exact Clang and GCC observations for selected C11 objects. Fixed-width
examples sometimes match Portable, while native `long` and bitfield examples deliberately show differences. Those
records are observations under named environments, not modes that CStructSharp can select.

If a binary format was produced from a native C structure, use the format specification and representative bytes as
the source for widths and offsets. Do not choose rules based only on the compiler installed on the reader's machine.

Start with the [tutorial](tutorial/index.md). Use the [grammar](grammar.md) for accepted source/path syntax, the
[feature table](operation-matrix.md) for operation support, and [Differences from C](differences-from-c.md) when
translating a header.
