# ADR-004: Width-aware enum values

- Status: Accepted
- Reviewed: 2026-07-26

## Decision

Enums retain their declared signed or unsigned 8-, 16-, 32-, or 64-bit domain. `EnumValueResult` exposes the exact
`BigInteger` value, raw bits, width, signedness, storage type, enum name, and optional member name. Reads preserve
unknown values; expressions and writes are range-checked without an `Int32` compatibility bridge.

## Related files

- `CStructSharp/CStructEnumIntegers.cs` handles exact numeric ranges and raw-bit conversion.
- `CStructSharp/EnumValueResult.cs` defines the public immutable result.
- `CStructSharpTests/EnumIntegerDomainTests.cs` and `CStructSharpTests/ReviewRegressionTests.cs` cover wide,
  signed, unsigned, known, and unknown values.
