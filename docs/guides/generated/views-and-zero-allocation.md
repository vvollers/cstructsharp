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

A view has an accessor for every member it can place: scalars, bitfields, enums, a pointer's stored address, a nested
struct (as a nested view), an array of numbers (`Values(int index)`, `ValuesBytes`, and `CopyValuesTo(Span<uint>)`
to decode the array's elements in one pass into the start of a destination at least that long), text (`NameBytes`, and `Name` when a string is wanted - that one allocates the
string), and a custom codec's value (`ColourBytes`, and `Colour`, which the codec boxes).

An array of fixed-size structs (or unions) gives one nested view per element: `view.Entries(i)` is the view of element `i`
at a stride of the element's size (the padded size in an aligned layout), and `view.EntriesBytes` holds the elements one
after the other. An element struct with bitfields works the same way, so `chunk entries[count]` with
`struct chunk { uint32 offset:31; uint32 compressed:1; }` reads `view.Entries(i).Compressed` without building any object;
a runtime-sized one also has `CountOfEntries()`, and an element past the source fails with the reader's short-read
message naming the array.

An array whose length comes from an earlier member (`uint32 entries[count]`) is exposed the same way, with
`CountOfEntries()` for its length. The view evaluates the count from the members before it with the reader's own limits
and failures, so a hostile count fails exactly as it does in `Parse`. Members placed after such an array are exposed
too: the view works out where they start from the counts before them, rounds up to the member's alignment in an aligned
layout, and fails with the reader's short-read message when the source ends first.

```c
struct table { uint32 count; uint64 origin; uint32 entries[count]; uint32 checksum; };
```

`view.Checksum` finds `checksum` after the `count` entries; `view.CopyEntriesTo(destination)` copies the entries
without the array a `Table` object would allocate for them.

When a count uses a name the layout does not define (`uint8 data[N]`), or a `#define` the caller may override, the layout's
views take the values as a third constructor argument: `new TableView(bytes, options, new Dictionary<string, int> { ["N"] = 3 })`.
The view evaluates the count with them exactly as `Parse` does (a name without a value fails with the reader's message),
passes them to the views nested in it and to its enumerator, and `ToObject()` reads with them unless it is given others.
Only a layout with such a count has the extra argument.

Every call that needs the length evaluates the count again, because a view keeps no state. That is cheap for one
read (`Checksum`, `CopyEntriesTo`), but `Entries(i)` in a loop pays it for every element. To visit all elements, read
`EntriesBytes` once, or call `CopyEntriesTo` into a span you own, and loop over that.

Members it does not expose are the ones whose position the view cannot compute: a terminated string, a conditional
member, a value that is not read yet, or a member of a nested struct whose
view has no property for it (a `char`, an `int48`, an `int128` or a pointer), a pointer's target, a struct
with a variable size, a bitfield whose storage the compiler placed after the data, and everything placed after one of
those. For those, `ToObject()` runs the full reader, or `Parse` does from the start. A member after a runtime-sized
array that is named like one of the view's own members (`bytes`, `to_object`) stays hidden rather than breaking the build,
and a derived member (`DataBytes` for `data`, `PtrAddress` for a pointer `ptr`) that would take the name of one of the
layout's own members gets a trailing underscore instead (`DataBytes_`).

## When a view is the wrong tool

- You need most of the members anyway and keep them around: parse once into an object.
- You need a string member: the accessor allocates the string, so the view saves nothing there.
- The members you want sit after a terminated string, a conditional member, or a variable part the view cannot measure.
- The value must outlive the method or travel through an `async` boundary.

## Check yourself

1. Does slicing a span copy bytes?
2. Why can a `ref struct` not be stored in a `List<T>`?
3. A header has `uint8 n; uint8 data[n]; uint32 crc;`. Can a view read `Crc`, and where does it look for it?

<details>
<summary>Answers</summary>

1. No; it creates a new window onto the same memory.
2. A list lives on the heap and could outlive the memory the struct's span points to; the compiler forbids it.
3. Yes. The view reads `n`, evaluates the array length with the reader's limits, and reads `crc` at offset `1 + n`.

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
