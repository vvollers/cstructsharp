---
title: Use CStructSharp efficiently
description: Avoid repeated layout preparation, unnecessary decoding, stream adapters, and output allocation.
---

# Use CStructSharp efficiently

Start with the API that makes ownership and failure handling clear. Measure the real workload before replacing it
with a lower-allocation overload.

The highest-value choices are usually:

1. Construct a `CStruct` once and reuse it for records with the same format.
2. Read one path with `ReadValue` when later fields are irrelevant.
3. Use span or memory input when bytes are already in memory.
4. Use the `byte[]` serialization overload unless an allocation measurement justifies caller-provided output.
5. Request debug ranges only in diagnostic paths.
6. Map to a POCO only when typed application code needs it; direct values avoid the additional mapping step.

Selected reads can avoid decoding unrelated later siblings, but they still perform the work needed to locate the
target. Runtime arrays, alignment, terminated strings, and pointers before the selected field may need traversal.

Span output avoids creating the final result array but requires enough capacity. `IBufferWriter<byte>` can append
through pooled windows. Both have partial-output behavior on late failure, so allocation is not the only tradeoff.

Use `CStructSharp.Benchmarks` and BenchmarkDotNet for changes to a hot path. Compare time and allocation with the same
layout, payload, target framework, build configuration, and operation. A small benchmark that removes validation or
changes who owns the output is measuring a different workload, so its numbers are not a fair comparison.

Do not add an application cache of mutable streams or results around a reusable layout. Reuse the immutable
`CStruct`; keep per-operation data owned by the caller.

See [Spans, memory, and buffer writers](spans-and-memory.md) for ownership details and the
[project testing guide](../project/testing.md#performance-packages-and-release-checks) for repository benchmark
expectations.
