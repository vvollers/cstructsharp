---
title: Portable language tutorial
description: Learn CStructSharp layouts from a fixed header through composite and runtime-sized data.
---

# Portable language tutorial

This tutorial is for developers who can read a simple C struct but have not had to calculate binary field positions
by hand. You need basic C# syntax and the ideas from [Binary layout basics](../../guides/binary-layout-basics.md).
You do not need assembly language, compiler implementation knowledge, or native-memory programming.

The lessons use exact byte arrays and C# examples from the documentation runner:

1. [Your first fixed layout](01-first-layout.md) maps a six-byte header, explains little-endian order, and performs a
   dynamic and typed read.
2. [Composites and overlapping storage](02-composites-and-layout.md) adds an enum, a fixed text field, and a union.
3. [Runtime data and safe traversal](03-runtime-data.md) supplies an array count, selects one element, follows a
   stored pointer, and sets resource limits.

Read them in order. Each lesson answers four questions:

- Which bytes does this field own?
- What C# value does reading return?
- What must the application supply?
- Which mistake would produce a different offset or unsafe amount of work?

After the lessons, use the [cookbook](../cookbook/index.md) to adapt a tested pattern or the
[primitive](../primitive-types.md), [layout](../layout-alignment-and-padding.md), and
[operation](../operation-matrix.md) pages as lookup references.
