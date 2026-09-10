---
title: Errors, limits, and unknown values
description: Distinguish invalid layouts, malformed data, configured ceilings, write failures, and unknown enums.
---

# Errors, limits, and unknown values

CStructSharp gives expected layout and binary-operation failures stable categories. Application code can branch on
the category while logs retain a more detailed message, path, offset, and inner exception.

## Invalid layouts

Constructing `CStruct` checks syntax, names, types, finite storage, expressions, and preparation limits before any
binary input or output is accepted.

Examples that produce `CStructLayoutException` with `InvalidLayout` include:

- unsupported syntax or type forms;
- unknown or duplicate names;
- alias or definition cycles;
- recursive by-value storage;
- invalid enum backing/range;
- bitfield widths outside their storage;
- negative/overflowing array counts; and
- source, nesting, dependency, or expression work above compilation limits.

The [Differences from C](differences-from-c.md) page lists 17 representative rejected C forms executed on both target
frameworks.

Invalid method arguments are not layout errors. A null source, unsupported pointer width, or non-positive option
limit remains an argument exception. Do not catch every `Exception` and relabel it as malformed data.

## Bounded failures

For a valid layout, operation options prevent untrusted data from requesting unlimited work:

| Limit area | Read result | Write result |
| --- | --- | --- |
| Array count | `ReadLimitExceeded` | `WriteLimitExceeded` |
| Encoded terminated string | `ReadLimitExceeded` | `WriteLimitExceeded` |
| Total physical bytes | `ReadLimitExceeded` | `WriteLimitExceeded` |
| Composite nesting | `ReadLimitExceeded` | `WriteLimitExceeded` |
| Pointer depth/fixed target | `ReadLimitExceeded` | Pointer range/shape uses `WriteFailed` |
| Malformed/truncated encoded bytes | `ReadFailed` | Not applicable |

The `bounded-failures` fixture reads `41 42 00` as `"AB"` under normal settings, then repeats with
`MaxStringBytes = 2` and requires `ReadLimitExceeded`. No partial value is returned.

Update path traversal has its own `MaxTraversal*` read limits. Those checks happen before staged replacement output
is committed.

## Stable error categories

Every expected layout-operation failure derives from `CStructException`:

| Code | Exception | Meaning |
| --- | --- | --- |
| `InvalidLayout` (`1`) | `CStructLayoutException` | Source cannot become a valid finite Portable layout |
| `InvalidPath` (`2`) | `CStructPathException` | Root, member, index, accessor, or checked path arithmetic is invalid |
| `ReadFailed` (`3`) | `CStructReadException` | Input is truncated/malformed, a pointer is invalid/cyclic, or typed mapping fails |
| `ReadLimitExceeded` (`4`) | `CStructReadLimitException` | A read/traversal ceiling was reached |
| `WriteFailed` (`5`) | `CStructWriteException` | Payload, conversion, shape, encoding, pointer, union, or physical output failed |
| `WriteLimitExceeded` (`6`) | `CStructWriteLimitException` | A write/output ceiling was reached |

Domain exceptions include a normalized `Path` and stream `Offset` when known. Their message and inner exception may
contain managed diagnostic detail. Invalid arguments, unsupported stream capabilities, cancellation, and unexpected
defects are not converted into these categories.

Branch on `Code`, not exact message text. Do not forward managed messages, inner exceptions, or `DebugData` unchanged
to an untrusted client; they may expose values and implementation details.

## Unknown enum values

An enum number may be valid even when no declared member has that number. `EnumValueResult` keeps:

- `Value` as an exact `BigInteger`;
- `RawBits`, `BitWidth`, `IsSigned`, and `StorageType`;
- the enum declaration name; and
- `Name`, which is null for an unknown number.

Writers accept declared names, the eight CLR integral types, `BigInteger`, invariant decimal integer strings,
compatible parsed results, or consistent objects containing enum/name/value metadata. Boolean, fractional,
contradictory, and out-of-range values produce `CStructWriteException`.

See [Handle errors and recovery](../guides/errors-and-recovery.md) for operation-specific recovery behavior and
[Preserve exact enum values](../guides/enums.md) for a runnable unknown-value example.
