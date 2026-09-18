---
title: Handle errors and recovery
description: Distinguish layout, path, read, write, and configured-limit failures without parsing message text.
---

# Handle errors and recovery

CStructSharp separates expected data/layout failures from invalid method arguments and unexpected application
defects. Expected failures derive from `CStructException` and include a stable `Code`. The exception types and
`CStructErrorCode` live in the `CStructSharp.Diagnostics` namespace.

Use the exception type or code to decide what the application can do. Keep the message and inner exception for
diagnostics; do not parse message wording as a program protocol.

## Error categories

| Code | Exception | Typical cause |
| --- | --- | --- |
| `InvalidLayout` | `CStructLayoutException` | Unsupported syntax, duplicate or unknown type, invalid constant expression, recursive by-value storage. Never raised for a problem in the data. |
| `InvalidPath` | `CStructPathException` | Unknown root/member, bad array index, or invalid pointer accessor |
| `ReadFailed` | `CStructReadException` | Truncated bytes, malformed encoding, invalid pointer target, a decoded count or selector that cannot be evaluated, typed-mapping failure |
| `ReadLimitExceeded` | `CStructReadLimitException` | Read array, string, byte, nesting, or pointer limit reached |
| `WriteFailed` | `CStructWriteException` | Missing field, wrong shape, out-of-range value, a supplied count that cannot be evaluated, encoding, pointer, union, or physical output failure |
| `WriteLimitExceeded` | `CStructWriteLimitException` | Write array, string, byte, or nesting limit reached |

Null arguments, unsupported stream capabilities, and invalid option values remain ordinary argument exceptions.
Cancellation and unexpected runtime defects are not wrapped as malformed binary data.

## Choose throwing or non-throwing reads

Use `ReadValue<T>` when an invalid value should follow the application's exception path. Use `TryReadValue<T>` when
an expected CStructSharp failure should become `false`.

The [first-parse example](install-and-first-parse.md) shows both a successful typed read and a truncated
`TryReadValue` call. On the truncated input, it returns `false` and the output receives its default value. A stream
overload also restores the position captured before the attempt.

`TryReadValue<T>` catches categorized CStructSharp failures only. It will not turn a null argument or an unrelated
bug in application code into `false`.

## Know what can be recovered

| Operation | Expected failure behavior |
| --- | --- |
| Layout construction | No binary input has been touched. Fix or reject the layout. |
| `TryReadValue<T>` on a stream | Returns `false` and restores the starting position. |
| `ResolveAddress` / length lookup | Restores position after inspection. |
| `Serialize` to a new `byte[]` | No result array is returned. |
| Span / `IBufferWriter` serialization | An initialized or advanced prefix may remain. |
| `WriteStream` | Earlier fields may already be written. |
| `UpdateStream` validation failure | Content, length, and position remain unchanged. |
| `UpdateStream` physical commit failure | A destination-accepted prefix may remain; position restoration is best effort. |

This distinction is why the choice between owned output, direct output, and an update matters.

## Read a message

Every message names what failed first and then, in parentheses, every fact the library knows: the field being
read or written and its layout type, the requested path, and the position at which the operation stopped. The
same facts are available as properties, so an application can format them its own way:

```text
Not enough bytes: needed 4, available 1 (field 'length' (uint32), in 'header', offset 3).
Value 70000 does not fit: uint16 accepts 0 to 65535 (field 'kind' (uint16), in 'header', offset 0).
Unknown root 'Header'. Names are case-sensitive; did you mean 'header'?
Unknown field 'nope' in 'header' (path 'header.nope', offset 6).
Array length 2147483647 exceeds MaxArrayElements (1000000) (field 'data' (uint8), in 'p', offset 4).
```

| Property | Meaning |
| --- | --- |
| `Member`, `MemberType` | The innermost field the failure belongs to and its layout type spelling. |
| `Path` | The path the operation was asked for (`header`, `header.nope`, `packet.items[2]`). |
| `Offset` | Where the operation stopped: the absolute stream position, or the offset within the supplied region. It is at or after the failing item, not necessarily its start. |
| `Code` | The stable category (`ReadFailed`, `WriteFailed`, `InvalidPath`, ...) for `switch` statements and logs. |

A layout error carries `Line` and `Column` instead; see [Limits and diagnostics](../language/limits-and-diagnostics.md).

## Record useful diagnostic context

When reporting a failure, keep:

- the exception type and `Code`;
- its normalized `Path` and `Offset` when present;
- the layout options and operation options;
- runtime variables;
- the input's starting stream position; and
- a minimal byte sample that reproduces the problem.

Do not forward managed exception messages, inner exceptions, or `DebugData` unchanged to an untrusted client. They
may reveal data or implementation detail. Map the stable code to an application-safe error response instead.

Read [Limits and diagnostics](../language/limits-and-diagnostics.md) for the complete taxonomy and unknown-enum
behavior. Use [Debugging contributor failures](../project/debugging.md) when diagnosing the library itself.
