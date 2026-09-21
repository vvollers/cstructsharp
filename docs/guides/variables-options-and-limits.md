---
title: Configure variables, options, and limits
description: Supply runtime layout values and keep parsing, writing, and updating within explicit resource limits.
---

# Configure variables, options, and limits

Some layout choices are fixed when you construct `CStruct`. Others change for each piece of data. CStructSharp keeps
those two groups separate:

- constructor and compilation options describe the format and bound layout preparation;
- operation variables provide integer values such as an externally known array count; and
- read, write, or update options limit work performed on one input.

## Supply a runtime variable

This layout cannot know the payload count until the caller supplies `COUNT`:

```c
struct packet {
    uint8 kind;
    uint8 payload[COUNT];
};
```

Pass a read-only integer dictionary to every operation that needs the count:

[!code-csharp[Read and measure a runtime-sized payload](../examples/Program.cs#language-tutorial-runtime-payload)]

The operation copies the entries before it evaluates the layout. A caller value overrides a layout `#define` with
the same name for that operation, but CStructSharp does not change the dictionary.

Use the same variables for related read, address, length, write, and update calls. Omitting or changing `COUNT` can
make a later path refer to a different byte position than the initial parse.

## Choose the right option type

| Option type | When it applies | Examples of work it limits |
| --- | --- | --- |
| `CStructCompilationOptions` | Constructing `CStruct` | Source length, layout/expression depth, expression work |
| `ReadOptions` | Parse, read, debug, address, length, pointer traversal | Arrays, strings, total bytes, nesting, pointers |
| `WriteOptions` | Serialize and direct stream writes | Arrays, strings, total output, nesting, pointer encoding |
| `UpdateOptions` | Locating and replacing existing storage | Separate traversal-read limits plus inherited write limits |

Properties are init-only, so configure a complete object with an initializer:

```csharp
var options = new ReadOptions
{
    MaxArrayElements = 10_000,
    MaxStringBytes = 1_024 * 1_024,
    DereferencePointers = false,
};
```

The public operation reads the supplied values at its outer entry. Reuse an initialized options object when several
calls use the same policy; create another object for a different policy.

The four option types are C# *records*: a `with` expression makes a copy that changes only the members you name,
and two option objects with the same members are equal. That is how a shared policy gets one variation without
repeating every setting:

[!code-csharp[Copy options with a change](../examples/Program.cs#api-guide-options-with)]

The `Codecs` and `Defined` members of `CStructCompilationOptions` hold collections, which compare by reference: two
compilation options that carry different list instances are not equal even when the lists have the same contents.

## Understand the defaults

Default limits are intentionally finite:

- layout source: 128 KiB;
- layout or expression/dependency depth: 256;
- expression work: 100,000 steps/nodes;
- one read or write array: 1,000,000 elements;
- one encoded read or write string: 16 MiB;
- total bytes read or written: 64 MiB;
- read pointer depth: 64; and
- read or write nesting depth: 256.

`MaxTotalBytesRead` counts every byte read from the stream, including rereads. It is not a limit on the input's
file size. Debug parsing spends the same budget as a plain parse: a packed header with a `uint16` and a `uint32`
needs a budget of 6 either way, because debug records carry byte ranges rather than copies. Layouts with unions,
pointers, or selected reads may reread bytes for traversal or overlapping fields, so the input size is a lower
bound, not the exact cost.

`UpdateOptions` has separate `MaxTraversal*` values for bytes read while finding the destination. After the target is
found, its inherited write limits apply to the replacement.

These are safety ceilings, not a promise that every value below them is appropriate for your application. For a
network message expected to contain at most 100 items, set a limit near 100 rather than relying on one million.

## Choose a policy for the quiet cases

Two options change behavior rather than a limit. Both default to the permissive choice:

- `ReadOptions.TrimFixedText` (default `false`): fixed-capacity text such as `char name[8]`, `wchar[N]`, or a
  bounded `utf8 name[N]` buffer keeps its NUL padding when read, so `61 62 00 00` is `"ab\0\0"`. Set the option to
  read `"ab"`; only trailing NULs are removed and writing still zero-pads to the declared capacity.
- `WriteOptions.UnknownMembers` (default `Ignore`): a supplied value may carry members the struct does not declare
  and they are skipped. `UnknownMemberPolicy.Reject` fails the write before any byte is written -
  `'bogus' is not a member of 'root' (WriteOptions.UnknownMembers is Reject). The layout declares: kind, tail.` -
  for dictionaries, `StructValue`s, and .NET objects alike, nested structs included. It also applies to
  `UpdateOptions`.

The [what is not an error](errors-and-recovery.md#what-is-not-an-error) list explains the other quiet cases.

## Handle limit failures

Exceeding a read limit throws `CStructReadLimitException` with code `ReadLimitExceeded`. Exceeding a write limit
throws `CStructWriteLimitException` with code `WriteLimitExceeded`. Invalid non-positive option values are argument
errors and are rejected before work begins.

Do not respond to a limit failure by raising every limit globally. Confirm the real format maximum, distinguish
trusted from untrusted data, and change only the relevant policy. A count that is unexpectedly huge may indicate
wrong byte order, a wrong starting position, or an incorrect runtime variable rather than a legitimate large value.

## Cancel a long operation

Limits bound how much *work* an operation may do; they do not bound how long a caller is willing to wait. The
three option records carry a `CancellationToken` for that. A token is a small handle that another part of the
program - a timeout, a request that was abandoned, a user pressing Stop - can switch to "cancelled"; code that
holds the token checks it at sensible points and stops. The library checks it where a read or write reaches a
boundary: when it enters a struct or union, when it follows a pointer, between the 64 KiB blocks of a numeric
array, between the elements of an array of structs, and between the 256-byte chunks of a terminated string. It
never checks per primitive, so a small read costs nothing for it.

[!code-csharp[Cancel a read](../examples/Program.cs#api-guide-cancellation)]

Cancellation is not a read failure: the operation ends with the runtime's `OperationCanceledException`, which
`TryReadValue<T>` lets through (it restores the stream position first) rather than turning into `false`. An update
stages its bytes before it commits, so a cancelled update leaves the destination unchanged; a direct stream write
may have written a prefix, as any late failure may. Generated classes observe the same token through their
`ReadOptions`/`WriteOptions` arguments.

Continue with [Handle errors and recovery](errors-and-recovery.md), or use the
[exact language limits](../language/limits-and-diagnostics.md) when defining an input policy.
