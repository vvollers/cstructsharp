---
title: CStructSharp documentation
description: Describe binary data with a small C-like layout, then read, write, inspect, or update it from .NET, Node.js, or browsers.
---

# CStructSharp documentation

CStructSharp helps you work with binary data whose structure is known. You describe the bytes with a small C-like
layout, create a `CStruct` from that description, and then use the same object to read or write values.

For example, suppose a file starts with this six-byte header:

```text
02 00 06 00 00 00
```

The first two bytes contain a kind, and the next four contain a length. This layout gives those byte ranges names and
types:

```c
struct header {
    uint16 kind;
    uint32 length;
};
```

With the default little-endian byte order, CStructSharp reads `kind` as `2` and `length` as `6`. The same layout
works on .NET 8 and .NET 10 without depending on the operating system's C compiler or native pointer size.

The layout is used at run time - `new CStruct(text)` compiles it when the program runs, for formats that arrive
with the data - and at compile time: put the same text on a `[CStructLayout]` class and the source generator
turns it into typed C# classes, readers, writers, and allocation-free views while the program is built
([generated code](guides/generated/index.md)).

If terms such as *little-endian*, *offset*, or *padding* are new to you, start with
[Binary layout basics](guides/binary-layout-basics.md). It explains how declarations map to bytes before introducing
the library API.

## Where to start

- [Try a browser lesson](https://vvollers.github.io/cstructsharp/explorer/#lesson=header) with no installation.
- [Install the JavaScript/WASM npm package](guides/browser/index.md) with `npm install cstructsharp` for Node.js or a browser application.

- To learn in order, follow the [learning path](guides/learning-path.md): nine steps from what a layout is to the
  reference material, and the three examples to read first.
- To read your first value, follow [Install and make a first parse](guides/install-and-first-parse.md).
- To decide between a stream, a byte array, a typed C# object, or a `StructValue` result, see
  [Choose an API](guides/choosing-an-api.md); between the runtime and the generator,
  [runtime or generated?](guides/generated/choosing-runtime-or-generated.md).
- To learn the C-like layout syntax, work through the [layout-language tutorial](language/tutorial/index.md).
- To solve a specific task, browse the [library guides](guides/index.md) or
  [tested recipes](guides/recipes/index.md).
- To look up a method, option, return type, or exception, use the [API reference](api/index.md).
- To build or contribute to CStructSharp itself, use the [project documentation](project/index.md).

> [!IMPORTANT]
> CStructSharp reads its own Portable layout language. The syntax resembles C, but the library is not a C compiler
> and does not import arbitrary C headers. Widths, byte order, alignment, and pointer size follow the options and
> rules documented on this site.

These pages describe the repository source. Published versions are listed in the release assets. Read the
[release notes](https://github.com/vvollers/CStructSharp/blob/main/CHANGELOG.md) for changes, or
[report a documentation problem](https://github.com/vvollers/CStructSharp/issues/new?labels=documentation&title=Documentation%3A%20).
