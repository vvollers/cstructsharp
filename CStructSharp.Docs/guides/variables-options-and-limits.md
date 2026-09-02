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

`UpdateOptions` has separate `MaxTraversal*` values for bytes read while finding the destination. After the target is
found, its inherited write limits apply to the replacement.

These are safety ceilings, not a promise that every value below them is appropriate for your application. For a
network message expected to contain at most 100 items, set a limit near 100 rather than relying on one million.

## Handle limit failures

Exceeding a read limit throws `CStructReadLimitException` with code `ReadLimitExceeded`. Exceeding a write limit
throws `CStructWriteLimitException` with code `WriteLimitExceeded`. Invalid non-positive option values are argument
errors and are rejected before work begins.

Do not respond to a limit failure by raising every limit globally. Confirm the real format maximum, distinguish
trusted from untrusted data, and change only the relevant policy. A count that is unexpectedly huge may indicate
wrong byte order, a wrong starting position, or an incorrect runtime variable rather than a legitimate large value.

Continue with [Handle errors and recovery](errors-and-recovery.md), or use the
[exact language limits](../language/limits-and-diagnostics.md) when defining an input policy.
