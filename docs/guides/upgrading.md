---
title: Upgrade to the current API
description: Migrate older managed and JavaScript consumers to the 0.4 series and browser contract version 7.
---

# Upgrade to the current API

The current release is **0.4.1**. It republishes the tested library and browser artifacts after the repository
history reset and includes the cleaned changelog; it introduces no new library behavior beyond 0.4.0.
The managed package version and browser contract version are different identifiers: browser contract **7**
describes the result format, not package version 7. See the
[release history](https://github.com/vvollers/cstructsharp/blob/main/CHANGELOG.md) for earlier releases.

## Managed results use StructValue and PrimitiveArray

Parsed structs are now `StructValue` objects. Existing `dynamic` access such as `header.length` continues to work.
For explicit types, use `StructValue`, `IDictionary<string, object?>`, or `IReadOnlyDictionary<string, object?>`
instead of casting to `ExpandoObject`. Parsed field names follow the layout, not C# property naming conventions.

One-dimensional arrays of fixed-width numeric types and `bool` now use `PrimitiveArray<T>`. They still support
`IList<object?>` access, but have fixed length: replacing an element is supported, while adding or removing one
is not. Replace explicit `List<object?>` casts. `Span` gives access to the typed elements without boxing;
`ToArray()` makes an independent typed copy. Changing a parsed value does not change the original input bytes;
serialize it or explicitly update the destination. See [reading values](reading-values.md).

These types reduce repeated member metadata and boxed numeric objects. They do not change the byte format or
turn parsed values into native C memory views. Multidimensional arrays retain their nested collection shape,
and text buffers still return strings.

## Browser Data is already decoded

Older contract versions stored JSON text in a successful parse envelope. Contract 7 stores the object itself:

```js
// Before contract 7:
const previousHeader = JSON.parse(result.Data).header;

// Contract 7:
const header = result.Data.header;
```

Check `result.Success` first. This example only illustrates the migration; use the complete
[quick start](browser/index.md) for imports, bytes, and error handling.
The selected root wrapper remains: a browser parse of `header` gives `result.Data.header`, while a managed
`Parse` returns the selected struct directly. For browser serialization, pass the root's fields rather than
that wrapper. Serialize/update `Data` is a `Uint8Array`; remove older Base64 decoding with `atob`.

Debug records use `CurPos` and `EndPos` as an end-exclusive byte range. The old browser `DebugData.Buffer`
field was removed in contract 6. Slice the original input to display bytes; for a file, read the matching file
range. The managed `DebugData` API is separate and still has its own buffer. See the
[JavaScript API](browser/api.md) and [debug guide](debug-data-and-addresses.md).

Update the JavaScript wrapper, TypeScript declarations, and runtime assets together. Mixing versions can produce
plausible-looking but incompatible results. Use one npm package version or one complete release ZIP.

## Earlier managed API changes

When upgrading from before 0.3.0:

- Rename `UpdateOptions.AllowPointerDereference` to `DereferencePointers`.
- Use `UpdateOptions.MaxTraversalArrayElements` for the array work needed to locate an update target.
  `MaxArrayElements` controls arrays being written; the two budgets are independent.
- `WriteOptions` and `UpdateOptions` are records, supporting `with` copies.
- Expected buffer-boundary and insufficient-destination failures use `CStructReadException` or
  `CStructWriteException`, rather than the older buffer implementation exceptions.

Application constructors, getters, and setters can still throw their own exceptions. Compiled accessors call
application code directly, so do not rely on reflection's `TargetInvocationException` wrapper or assume that
`TryReadValue<T>` hides every application error.

## Reuse preparation deliberately

Keep one `CStruct` per known format. If your call site repeatedly receives definition text, `CStruct.GetOrCompile`
provides a bounded shared cache. It stores prepared layouts, not input bytes or parsed results. Clearing the cache
does not invalidate layouts already returned. See [performance](performance.md#managed-layout-caching).

In JavaScript, ordinary calls benefit from bridge caching; `compile` additionally provides a retained handle for
repeated reads. Dispose the handle when finished because its worker owns a runtime. Small eligible inputs can use
the calling thread instead. See [compiled JavaScript layouts](browser/api.md#reuse-a-compiled-layout).

## Check an upgrade

1. Read a known fixture and check named values, not only whether parsing succeeds.
2. Check explicit casts, array resizing, root wrappers, and debug byte extraction.
3. Serialize or update representative data and compare expected bytes, including padding and union storage rules.
4. Exercise malformed input, limits, cancellation, and application callback failures.
5. Deploy matching runtime assets and confirm `getVersion()` reports the expected package version.
