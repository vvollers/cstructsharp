# CStructSharp.Core (shared sources)

This folder is not a project. Its sources compile into two assemblies:

- `src/CStructSharp/CStructSharp.csproj` (net8.0, net10.0) - the runtime library, through
  `<Compile Include="../CStructSharp.Core/**/*.cs" />`;
- `src/CStructSharp.Generators/` (netstandard2.0) - the source generator, which hosts the same parser and layout
  compiler inside the C# compiler.

Everything here is the part of the library that is known *before any byte is read*: the layout parser and syntax
tree (`Parsing/`, `Syntax/`), layout expressions (`Expressions/`), the compiled model and member placement
(`Compilation/`, with `LayoutCompilation` as the entry point), the primitive catalog and codec descriptors
(`Codecs/`), the exception types and error codes (`Diagnostics/`), introspection (`Introspection/`), the path
grammar (`Addressing/`), and the compilation options.

## Rules for code in this folder

It has to compile on netstandard2.0 without `System.Memory`, so:

- no `Stream`, no `Span<byte>`/`ReadOnlySpan<char>` reading, no `ArrayPool`/`IBufferWriter`;
- no reader/writer delegates - the compiled model carries codec ids (`PrimitiveCatalog`), the runtime maps them
  to delegates (`CodecTable`);
- no reflection, no `Half`/`Int128` arithmetic (those decode at run time);
- `ImmutableDictionary`/`ImmutableArray` instead of `FrozenDictionary`;
- language features that need runtime support (`Index`/`Range`, `init`, `required`, `ArgumentNullException.ThrowIfNull`)
  are fine: the generator project polyfills them with PolySharp.

A file that needs a runtime-only API is split: the Core half stays here as a `partial` type and the runtime half
lives under `src/CStructSharp/` with the same type name.

Namespaces are unchanged (`CStructSharp.Parsing`, `CStructSharp.Compilation`, ...); the folder only says where a
file compiles.
