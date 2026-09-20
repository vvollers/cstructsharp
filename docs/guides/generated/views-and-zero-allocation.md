---
title: Views and zero allocation
description: What a Span<byte> is, what an allocation costs, what a ref struct is and why a view cannot escape a method, and when a view is the wrong tool.
---

# Views and zero allocation

`Wire.Parse(bytes)` creates a `Wire.Header` object: one allocation on the managed heap, a few dozen bytes, freed
later by the garbage collector. For one header that is nothing. For a million headers in a tight loop it is a
million objects the collector has to track and reclaim, and that shows up as time. A **view** reads the same bytes
without creating anything.

## Spans

A `ReadOnlySpan<byte>` is a pointer and a length: a window onto memory that already exists - an array, a stack
buffer, a slice of a larger buffer. Slicing a span (`bytes.Slice(2, 4)`) creates a new window, not a copy. The
generated readers take spans because that is what the bytes are; a `byte[]` converts to one implicitly.

## What an allocation is and why it costs

`new Header()` asks the runtime for heap memory, zeroes it, writes an object header, and returns a reference. The
memory stays in use until no reference remains and a garbage collection runs. The cost is not the `new` itself,
which is fast, but the collections: the more objects a program creates, the more often the collector pauses it and
the more memory it walks. A parser that allocates nothing per record never pays that.

BenchmarkDotNet reports the allocated bytes of each case; the [performance page](../performance.md#typical-costs)
lists the generated view at `0 B` next to `Parse`.

## A view

[!code-csharp[A view over the header](../../examples/GeneratedExamples.cs#generated-views)]

`Wire.HeaderView` is a `readonly ref struct` holding the span and the read options. `view.Kind` decodes two bytes
at offset 0 when it is read, and again if it is read again; nothing is stored. `view.Bytes` is the value's own
bytes. `ToObject()` is the escape hatch back to a `Header` object.

The constructor checks that the source holds the value's fixed size and otherwise fails with the runtime's short-read
message, so a view can be trusted from its first accessor on.

## What a ref struct is

A `ref struct` lives on the stack and may hold spans. The C# compiler enforces that it never leaves the stack: you
cannot store a view in a field of a class, put it in a `List<T>`, capture it in a lambda, box it, or return it from
a method that created it over a local buffer. Those rules are what make it safe for a view to point straight into
memory that may be gone a moment later. Write a method that takes the bytes, creates the view, reads what it needs,
and returns plain values.

## What a view exposes

A view has an accessor for every member whose position the compiler fixed at build time: scalars, bitfields,
enums, a pointer's stored address, a nested struct (as a nested view), a fixed array of numbers (`Values(int index)`
and `ValuesBytes`), and fixed text (`NameBytes`, and `Name` when a string is wanted - that one allocates the string).
Members it does not expose are the ones whose position depends on the data: a count-sized array, a terminated
string, a conditional member, and everything placed after one of those. For those, `ToObject()` runs the full
reader, or `Parse` does from the start.

## When a view is the wrong tool

- You need most of the members anyway and keep them around: parse once into an object.
- You need a string member: the accessor allocates the string, so the view saves nothing there.
- The struct is runtime-sized and the members you want sit after the variable part.
- The value must outlive the method or travel through an `async` boundary.

## Check yourself

1. Does slicing a span copy bytes?
2. Why can a `ref struct` not be stored in a `List<T>`?
3. A header has `uint8 n; uint8 data[n]; uint32 crc;`. Can a view read `Crc`?

<details>
<summary>Answers</summary>

1. No; it creates a new window onto the same memory.
2. A list lives on the heap and could outlive the memory the struct's span points to; the compiler forbids it.
3. No. `crc` is placed after a runtime-sized array, so its offset is not fixed at build time; `ToObject()` reads it.

</details>

## Exercise

Write a method `static long SumLengths(ReadOnlySpan<byte> input)` that walks a buffer of consecutive six-byte
headers with `Wire.HeaderView` and returns the sum of all `Length` values without allocating.

<details>
<summary>Solution</summary>

```csharp
static long SumLengths(ReadOnlySpan<byte> input)
{
    long sum = 0;
    for (int offset = 0; offset + Wire.Sizes.Header <= input.Length; offset += Wire.Sizes.Header)
    {
        sum += new Wire.HeaderView(input.Slice(offset)).Length;
    }

    return sum;
}
```

`Wire.Sizes.Header` is the build-time size; the view over each slice reads one `uint`.

</details>
