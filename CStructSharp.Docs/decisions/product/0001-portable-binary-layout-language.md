# ADR-001: Portable binary-layout language

- Status: Accepted
- Reviewed: 2026-07-26

## Decision

The core accepts a self-contained C-like binary-layout language. It does not parse arbitrary C
translation units, infer a host ABI, discover headers, or implement general preprocessing. Pointer width, byte order,
and alignment behavior are explicit constructor inputs; primitive sizes and value domains are defined by the
Portable language rules.

Optional header frontends may normalize external input, but core compilation and execution remain
independent of those frontends. A language addition is complete only when every applicable read, inspect, address,
length, write, update, compatibility, and documentation path handles it.

## Related files

- `CStructSharp/CStruct.cs` captures pointer size, alignment mode, byte order, and compilation limits.
- `CStructSharp.Docs/contracts/language/portable-v1.json` stores the machine-readable language rules.
- `CStructSharpTests/CanonicalPortableReferenceTests.cs` validates primitive tables, layouts, and rejected forms.
