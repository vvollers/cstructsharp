---
title: Stored pointers and bounded traversal
description: Separate pointer encoding from address interpretation, then walk lists and trees without unbounded recursion.
---

# Stored pointers and bounded traversal

A pointer field contains bits. Interpreting those bits as an address is a separate step, and it is a step the
library cannot take on its own. The bits might be an absolute virtual address, an offset from the containing
object, or an address whose low bits carry flags. They might belong to a different process than the record that
holds them. Reading the bits tells you none of this; only the format's documentation does.

This guide shows how the memory APIs keep the two steps apart, how to supply the interpretation, and how to
walk linked structures without trusting the image to be well-formed.

## Pointer bits stay bits until you ask

`StoredPointer` holds the unsigned `Bits` exactly as they were stored, together with their `Width` in bytes
(1, 2, 4, or 8). Zero is null; this is a deliberate rule of the API, the same rule Portable stream pointers use,
and it applies even when the address space has readable bytes at address zero. Reading a whole struct returns its
pointer fields as `StoredPointer` values and
stops there; it does not read the targets. That rule is what makes it safe to read a node from a circular list:
parsing the node cannot start an endless chain of reads.

Given `struct Node { uint32 value; Node *next; };`, a path has one of these meanings:

| Path | Result |
| --- | --- |
| `next` | The stored pointer |
| `next.address` | The same `StoredPointer`, not a signed integer or a resolved target |
| `next.value` | The pointed-to `Node` value |
| `next.value.value` | The target node's member named `value` |

The first `value` after `next` is an accessor that means "follow the pointer"; the second is the member name.
`address` must end a path. Arrays use zero-based indexes such as `nodes[2].next.value.value`. An empty path selects
the root. Null pointers, opaque targets (`void *`), and incomplete targets cannot be dereferenced; the stored bits
of such a pointer are still readable and often still useful.

## Interpret a relative pointer

When a path uses `.value`, the session hands the pointer to a **resolver**: a function from `PointerRequest` to
`MemoryRegion`. The default resolver treats nonzero bits as an absolute address in the same source as the pointer
storage. Supply your own when the format specifies a different rule.

A `PointerRequest` carries everything a resolver might need:

| Member | Meaning |
| --- | --- |
| `Pointer` | The stored bits and width |
| `Storage` | The region holding the pointer bytes; its `Source` is the space the pointer was read from |
| `Container` | The region of the record that contains the pointer, for relative encodings |
| `TargetTypeId`, `TargetSize` | What the session expects to find at the target and how many bytes it needs |
| `Path`, `Depth` | Which path step is being resolved, for diagnostics and limits |

The resolver returns a region of at least `TargetSize` bytes in whichever source is appropriate. The session
slices that extent and continues the path. Ownership of the returned source stays with your application.

The example below uses a format whose `next` field stores a byte displacement from the start of the containing
node, a common choice in position-independent data.

[!code-csharp[Resolve a displacement from the containing node](../examples/memory-analysis/MemoryTutorialExamples.cs#memory-relative-pointer)]

The first record starts at 8 and stores displacement 16. The resolver adds them with checked arithmetic, returning
address 24. The stored value remains 16, so serializing the node later preserves the format's encoding. The default
packed layout puts the four-byte value at offset 0 and the eight-byte pointer at offset 4, for a twelve-byte
record; the fixture spaces records sixteen bytes apart, which is the application's placement choice.

A resolver is also the place to switch to another process's source, or to remove a documented tag such as the
low bit in `Bits & ~1UL` when the format reserves it. Do not clear bits merely because an address looks unusual.
Validate the format's rules, and keep the original `StoredPointer` when writing the record back.

## Lists in memory: heads, sentinels, and embedded links

A linked list in a C program is a chain of records, each holding the address of the next. Two design choices
common in systems code affect how you walk one.

First, many lists are **circular with a sentinel**: a special head node that holds no data, whose `next` points at
the first item, and whose address is stored in the last item's `next`. There is no null terminator. The walk ends
when the link returns to the head's address. Returning to a *data* node before reaching the head is not normal
termination; it means the list is corrupt.

Second, many lists are **intrusive**: the link field lives inside the data record rather than in a separate list
cell. A walk therefore visits link addresses, and the containing record starts some fixed number of bytes earlier.
`MemoryWalker.ContainingRecord(memberAddress, memberOffset)` performs that checked subtraction. If a link is at
`0x1028` and metadata says the member offset is `0x28`, the record starts at `0x1000`. The helper does arithmetic
only; your application chooses the containing type and confirms that its bytes are available.

## Walk a sentinel list

`MemoryWalker.SentinelList` follows caller-defined links until it returns to the sentinel, revisits a data node,
reaches a node limit, or runs out of available memory. You supply the callback that reads one link; the walker
supplies the stopping rules and the identity tracking.

[!code-csharp[Walk a finite circular list](../examples/memory-analysis/MemoryTutorialExamples.cs#memory-sentinel)]

The stored links are `8 -> 16 -> 24 -> 8`. The result contains addresses 16 and 24, in that order, and reports
`Sentinel`. The sentinel itself is not in `Nodes`, because it is not a data item.

Note how the callback receives a `MemoryAccessContext` and passes it to `session.Read`. The walk and every link
read then count against one budget. Creating a fresh context inside the callback would make the node limit the
only bound, and a corrupt list of long nodes could still read an unbounded number of bytes.

## Interpret traversal outcomes

| Stop | Meaning |
| --- | --- |
| `Sentinel` | The list returned to its original head |
| `Complete` | The tree's pending work is empty |
| `RepeatedNode` | The list reached a previously returned data node |
| `NodeLimit` | Another distinct node would exceed `maxNodes` |
| `Unavailable` | A callback encountered `Unmapped` or `MissingBytes`; inspect `Failure` |

`Nodes` lists the regions visited before stopping. It is not a promise that all fields of each region were read;
the callback decides what it reads. In a tree, a node is recorded before its children are requested, so if that
callback fails the node is still in the result. Always check `Stop` before treating the list as complete: a partial
list is useful evidence, but it must not silently stand in for an inventory.

Node limits do not replace byte and request budgets. Deciding whether a next node exists may itself require another
link read, so `maxNodes` bounds the returned collection, not the exact number of reads. Cancellation, exhausted
budgets, invalid values, and other source failures propagate as exceptions rather than being reported as an ordinary
end of list; an incomplete operation should never look like a successful one.

## Trees, shared children, and tagged entries

`MemoryWalker.Tree(root, children, maxNodes, context)` asks a callback for each node's child regions and visits
every distinct node once. It uses an explicit stack rather than recursion, so a deep tree cannot overflow the call
stack, and children are visited in reverse of their enumeration order. Two nodes are the same when they have the
same source object and address; length is not part of the key. A child shared by two parents is visited once, and
a cycle terminates through the same rule.

The callback owns the meaning of each entry. Systems code often stores a **tagged pointer**: because records are
aligned to 4 or 8 bytes, the low address bits are always zero, so a format may use them as flags. Your callback
decides whether an entry is a child address, a leaf value, or a tag, and strips documented tag bits before
returning a region. The walker does not impose an operating system's node layout.

The [synthetic consumer](../examples/memory-analysis/index.md) includes a two-node tagged tree, a corrupt list
cycle, and a missing-page list, showing how each is classified. Child enumeration must itself terminate or honor
cancellation; the synchronous walker cannot interrupt a callback that blocks before returning its next child.

## Check your understanding

1. You read a node with `session.Read(node, "Node")` and get a `StructValue`. Is the `next` member a `Node`
   dictionary, a `StoredPointer`, or a `ulong`?
2. A list's links are `8 -> 16 -> 24 -> 16`. What does `SentinelList` return, and how many nodes are in the result?
3. A format stores `next` as an absolute address in the *process* space, but the record was read from a *kernel*
   mapping of the same physical page. What must the resolver change, and what must it leave unchanged?

Answers: **a `StoredPointer`**; reading a struct never follows its pointers. **`RepeatedNode` with two nodes**
(16 and 24): the third link revisits 16, which was already returned, so the walker reports a corrupt cycle rather
than looping. **The resolver returns a region whose `Source` is the process source** with `Address` equal to
`Pointer.Bits`; it leaves the bits themselves unchanged so a later serialization writes the same value.

Continue with [budgets, caching, and source contracts](memory-reliability.md).
