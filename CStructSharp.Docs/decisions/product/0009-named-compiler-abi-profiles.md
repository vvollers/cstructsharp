# ADR-009: Explicit named compiler ABI profiles

- Status: Accepted
- Reviewed: 2026-07-26

## Decision

Portable is the sole shipped behavior profile. CStructSharp never infers a compiler ABI from the current OS,
runtime, CPU, or installed compiler. Any future compiler ABI profile must have an explicit versioned identity,
one documented home for all of its rules, an explicit way to select it, repeatable comparisons with the named
compiler, and support across every applicable read and write operation.

No named-profile selection API currently ships. Compiler fixture baselines remain observation-only and do not imply
MSVC, SysV, GCC, or Clang compatibility.

## Related files

- The public managed baseline contains no profile-selection type or member.
- `CStructSharp.Docs/contracts/language/portable-v1.json` lists Portable as the sole shipped profile.
- Compiler fixture contracts label their claims as observation-only.
