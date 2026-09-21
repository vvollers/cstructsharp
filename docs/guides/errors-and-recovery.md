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
bug in application code into `false`, and it lets an `OperationCanceledException` through: cancellation through
`ReadOptions.CancellationToken` ([cancel a long operation](variables-options-and-limits.md#cancel-a-long-operation))
is the caller's decision to stop, not a property of the input.

## Know what can be recovered

| Operation | Expected failure behavior |
| --- | --- |
| Layout construction | No binary input has been touched. Fix or reject the layout. |
| `TryReadValue<T>` on a stream | Returns `false` and restores the starting position. |
| `ResolveAddress` / length lookup | Restores position after inspection. |
| `Serialize` to a new `byte[]` | No result array is returned. |
| Span / `IBufferWriter` serialization | An initialized or advanced prefix may remain. |
| `Write` | Earlier fields may already be written. |
| `Update` validation failure | Content, length, and position remain unchanged. |
| `Update` physical commit failure | A destination-accepted prefix may remain; position restoration is best effort. |

This distinction is why the choice between owned output, direct output, and an update matters.

## What is not an error

Some situations look like problems but are deliberate, documented behavior. The library stays quiet about them,
and each has an opt-in where strictness makes sense:

| Situation | What happens | Opt in to strictness |
| --- | --- | --- |
| Trailing bytes after the selected struct | They are left unread; a stream stays positioned right after the struct so a following record can be read. | Compare the position or `GetStructSizeInBytes` against the input length yourself. |
| A supplied value has members the layout does not declare | `Serialize`, `Write`, and `Update` skip them. | `WriteOptions.UnknownMembers = UnknownMemberPolicy.Reject`. |
| NUL padding in fixed text (`char name[8]`, bounded `utf8[N]`) | The padding stays in the string: `"ab\0\0"`. | `ReadOptions.TrimFixedText = true`, or `.TrimEnd('\0')`. |
| An enum field holds a value with no declared name | `EnumValueResult.Name` is `null` and `Value` keeps the number; flags decompose into the named bits plus the remainder. | Check `Name is null` (or `FlagValueResult.Remainder != 0`) in application code; the layout cannot declare "closed" enums. |
| A pointer that was not dereferenced | `Pointer.Value` is `null` and `IsDereferenced` is `false`; nothing was read at the target. | Leave `ReadOptions.DereferencePointers` at its default of `true`. |

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

## Common mistakes

Use this checklist when a new integration almost works but produces the wrong values or positions.

| Mistake | Why it causes trouble | Better approach |
| --- | --- | --- |
| Constructing `CStruct` for every record | Repeats layout parsing and preparation | Build it once per layout configuration and reuse it |
| Copying a C struct without checking its ABI | Native widths, padding, bitfields, and pointers may differ | Translate the documented file format to explicit Portable types |
| Relying on constructor defaults for a persisted format | The intended byte order, placement, or pointer width is hidden | Pass all three format choices explicitly |
| Reading the whole root for one early field | Decodes values the caller does not need | Use `ReadValue` with a path |
| Sharing one stream across concurrent calls | Seeks and reads interfere | Give each operation a separate stream or lock the complete call |
| Treating a stored pointer as process memory | File coordinates are not safe native addresses | Use `Pointer`, addressing options, and traversal limits |
| Discarding `EnumValueResult` or union raw storage | Unknown numbers or overlapping bytes may be lost | Keep the rich result until faithful round trip is no longer needed |
| Treating `char[N]` as terminated text | Fixed capacity and scanning have different extents | Choose fixed or terminated syntax from the format specification |
| Expecting span, writer, or stream output to roll back | A late failure can leave a prefix | Stage through an owned array when all-or-nothing output matters |
| Raising every safety limit after a failure | Can hide a wrong count, offset, or byte order | Verify the format and change only the justified limit |
| Feeding arbitrary headers to the core | Include paths, function-like macros, and compiler ABI rules are not interpreted | Keep the directives Portable understands and write the rest as Portable syntax |

Before shipping a reader or writer, keep at least one known byte fixture and verify offsets, decoded values, output
bytes, failure categories, and starting/ending stream positions. A successful round trip by itself can reproduce the
same wrong assumption in both directions.
