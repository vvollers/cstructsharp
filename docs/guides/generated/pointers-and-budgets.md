---
title: Pointers and budgets
description: What a stored address is, absolute and relative addressing with Origin, Pointer<T>, depth and byte budgets, and what a CStructReadLimitException protects against.
---

# Pointers and budgets

A pointer in a binary format is a number stored in the data that names another position in the same data. The
[pointers guide](../pointers.md) explains stored addresses, absolute and relative modes, and null; this lesson
shows the generated shape and the limits that keep a hostile input from running away with the reader.

[!code-csharp[A linked list](../../examples/GeneratedExamples.cs#generated-pointers-class)]

[!code-csharp[Following it](../../examples/GeneratedExamples.cs#generated-pointers)]

## `Pointer<T>`

A pointer member becomes `Pointer<T>` from `CStructSharp.Generated`, where `T` is the target's type: a generated
class (`Pointer<Lists.Node>`), a primitive (`Pointer<ushort>`), or another pointer for `T **p`. It carries:

- `Address`, the stored number (with `PointerSize` bytes in the layout's byte order);
- `IsNull`, true for a stored zero, which is never followed;
- `IsDereferenced` and `Value`, the target when the read followed the pointer.

With `ReadOptions.DereferencePointers = false` the reader stores the addresses and follows nothing; `Value` is then
the default and `IsDereferenced` false. `Pointer<T>` converts to and from the runtime's untyped `Pointer` with
`FromPointer`/`ToPointer`, so mapped classes can hold either.

## Absolute and relative addresses

By default an address is a position from the start of the input. With `ReadOptions.AddressingMode = Relative` and
an `Origin`, the target is `Origin + address` - the rule for formats whose pointers count from a base that is not
the buffer's first byte. The generated reader applies the same arithmetic as the runtime, with the same failure
texts when the sum overflows or lands outside the input.

## The budgets

Following pointers means trusting the data to say where to read next. Four limits bound that trust; each is a
`ReadOptions` member, and exceeding one raises `CStructReadLimitException`, a subtype of `CStructReadException`
that says the *input was rejected for size*, not that it was malformed:

| Limit | Default | What it stops |
| --- | --- | --- |
| `MaxPointerDepth` | 64 | A chain of pointers nested deeper than the format should ever need (or a stack overflow from a hostile file). |
| `MaxPointerTargetBytes` | none | A pointer to a huge target; a fixed-size target larger than the limit is refused before it is read. |
| `MaxTotalBytesRead` | 64 MiB | The total the whole operation may consume, pointer targets included. |
| `MaxNestingDepth` | 256 | Structs inside structs beyond what a format needs. |

A pointer that leads back to a target already being read is a **cycle**; the reader detects it by remembering the
targets on the active path and fails with `Cyclic pointer target detected at stream address N` instead of following
it until the depth limit.

## Check yourself

1. What does `list.Head.Value` hold when `DereferencePointers` is false?
2. Why is `CStructReadLimitException` a separate type?
3. How does the reader know `[1, 10, 1]` is a cycle rather than a long chain?

<details>
<summary>Answers</summary>

1. The default (`null` for a class target): nothing was read at the address.
2. So a caller can distinguish "this input is bigger than I allow" from "this input is broken" and react differently.
3. It keeps the set of pointer targets on the path being read; the second `next` names a target already on it.

</details>

## Exercise

Change the list so the first node's `next` points to position 4 in `[1, 10, 4, 0, 20, 0]`. Read it with
`PointerSize = 1` and confirm the second node's value; then set `AddressingMode = Relative, Origin = 2` and work out
which bytes the same stored addresses now select.

<details>
<summary>Solution</summary>

Absolute: `head → 1` (value 10, next → 4), then position 4 holds value 20 and next 0: the second value is 20. Relative with
`Origin = 2`: the stored `1` selects position 3 and the stored `4` selects position 6, which is past the end -
the read fails with `Pointer target is outside the readable stream range: 6`.

</details>
