---
title: Tested recipes
description: Find a short, executable CStructSharp example for a common binary-data task.
---

# Tested recipes

Use this page when you know the result you need and want to find the closest complete example. Every scenario listed
here is compiled and executed by the documentation example project; the result column is an assertion, not sample
output copied by hand.

Run all scenarios from the repository root:

```powershell
dotnet run --project .\CStructSharp.Docs\examples\CStructSharp.Docs.Examples.csproj -c Release
```

The command builds the example project in Release mode and runs eleven scenarios. A successful run prints one `PASS`
line for each scenario followed by `PASS all 11 scenarios`. If the command fails, the first thrown comparison names
the result that differed.

| You need to | Scenario | Result checked by the runner | Read next |
| --- | --- | --- | --- |
| Decode a fixed binary header | `decode-header` | kind `2`, length `6`, typed mapping, and truncated `TryReadValue=false` | [First parse](../install-and-first-parse.md) |
| Combine enum, fixed text, and union storage | `composite-record` | `Text`, `"AB\0"`, both union views, and exact round trip | [Language tutorial 2](../../language/tutorial/02-composites-and-layout.md) |
| Decode an externally sized payload | `runtime-payload` | three items, selected item `0x20`, and a position-preserving length query | [Variables and limits](../variables-options-and-limits.md) |
| Map binary fields to a POCO | `map-poco` | `Point { X=-2, Y=5 }` | [Typed values](../typed-values.md) |
| Display byte ranges and locate a field | `inspect-ranges` | `uint16` range `[1,3)` and absolute address `1` | [Debug data](../debug-data-and-addresses.md) |
| Follow a stored pointer | `follow-pointer` | one-byte address `1` and target value `0x2A` | [Pointers](../pointers.md) |
| Preserve or select union storage | `preserve-union` | raw bytes `34 12`; selected `small` writes `A5 00` | [Unions](../unions.md) |
| Preserve an enum value with no known name | `preserve-enum` | unsigned 32-bit `4294967295` with `Name=null` | [Enums](../enums.md) |
| Read and write fixed-capacity text | `fixed-text` | `"ABC\0"`; writing `"XY"` produces `58 59 00 00` | [Strings](../strings-and-encodings.md) |
| Serialize to owned and provided storage | `round-trip` | array, span, and `IBufferWriter` all produce `34 12 A5` | [Writing](../writing-and-serialization.md) |
| Patch one nested field | `patch-field` | prefix preserved, field becomes `A5`, and invalid input changes nothing | [Updating](../updating-existing-data.md) |

The [executable examples page](../../examples/index.md) contains the complete source. When adapting a recipe, change
the layout widths, byte order, alignment, pointer size, and limits only when the real binary format requires it.
