---
title: Runtime or generated?
description: A decision table for the runtime API and the generated path, and what the measured numbers mean.
---

# Runtime or generated?

Both paths read and write the same layouts with the same rules. They differ in *when* the layout is known and in
*what* a read produces.

| Question | Runtime API | Generated |
| --- | --- | --- |
| Is the layout known when the program is compiled? | Either | Required |
| Do users or files supply layouts at run time? | Yes | No - use `Wire.Layout` for the fixed ones and `new CStruct` for the rest |
| Is the read in a hot loop? | `StructValue` per read | Typed class per read, or a view with no allocation |
| Do you want IntelliSense on member names? | Strings and paths | Properties |
| Native AOT or trimming? | Works (mapped classes, no `dynamic`) | Works; the generated code has no reflection to trim |
| Reading a memory image with the memory-analysis API? | Yes | Through `Wire.Layout` |
| JSON, introspection, debug output? | `CStruct` members | `Wire.Layout` and `ParseWithDebug` |
| Writing? | Dictionaries or mapped classes | Typed classes, typed `Update` setters |
| Input that may be wrong? | `TryReadValue<T>`, `TryGet`/`GetOrDefault` on the value | `TryParse` with the failure it would have thrown |
| A file of records, a stream of frames? | `ParseMany` / `ParseManyAsync` (`StructValue` per record) | `Records` / `RecordsAsync` (a class per record), or the view enumerator (nothing allocated) |
| Streams that arrive while the program runs? | `ParseAsync`, `WriteAsync`, `UpdateAsync` | `ParseAsync`, `WriteAsync` - the same buffering, the generated reader |
| Exploring a format interactively? | `dynamic`, paths | Less convenient |

The table's short form: **if the layout is in your source, generate; if it arrives with the data, use the
runtime.** Mixed programs are normal - a generated class always carries its runtime `Layout`.

## What the numbers mean

The [performance page](../performance.md) has a "Generated" table measured with `GeneratedBenchmarks`. For the
reference record used throughout the benchmarks (the primitives record), it compares four ways of reading the
same bytes: the runtime `Parse`, the generated `Parse`, a generated view, and hand-written `BinaryPrimitives`
code. Reading the table:

- **Runtime `Parse`** allocates a `StructValue` and its members; it is the baseline every other row is compared
  against.
- **Generated `Parse`** allocates the typed class and nothing else, and does no dictionary lookups; expect it to
  be several times faster with a fraction of the allocations.
- **Generated view** allocates nothing; each member is decoded when read. It is the number to compare with the
  hand-written row.
- **Hand-written** is what a careful programmer writes with `BinaryPrimitives` for one layout; the generated view
  should sit close to it, because it is the same code with the offsets filled in.

The nested fixture (`Nested256`) shows the same four numbers for a struct with an array of 256 nested records,
where the generated `Parse` has 256 objects to create and the view has none.

Numbers are from one machine and one run; the point is the ratios, not the microseconds. The release gate
(`contracts/performance/non-web-rc1.json`) keeps the generated headline cases from regressing.

## Check yourself

1. A program reads PNG chunks; the chunk layout is fixed. Which path?
2. The same program lets users describe extra chunk types in a config file. Which path for those?
3. Which generated form allocates nothing?

<details>
<summary>Answers</summary>

1. Generated: the layout is known at build time.
2. Runtime: the layouts are only known at run time. Both can live in one program.
3. The `readonly ref struct` view.

</details>

## Exercise

Take the `Wire` header from the [first lesson](first-generated-layout.md) and read it three ways - runtime
`Parse`, generated `Parse`, and `Wire.HeaderView` - printing the `length` from each. Then decide which you
would use in a loop over a million packets.

<details>
<summary>Solution</summary>

All three print the same value; the view, because it allocates nothing per packet and the loop cannot let the
view escape anyway.

</details>
