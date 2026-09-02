---
title: Debugging contributor failures
description: Reduce SDK, layout, path, byte, update, fuzz, coverage, mutation, and documentation failures to one cause.
---

# Debugging contributor failures

Begin with the smallest repeatable failure. Save the exact command, target framework, layout, options, variables,
input bytes, starting stream position, exception type/code/path/offset, and current Git revision. Changing several of
those at once makes it hard to know which assumption was wrong.

## First identify the stage

| Failure appears during | Inspect first |
| --- | --- |
| `CStruct` construction | Layout exception code/message, unsupported syntax, names/types, expressions, compilation limits |
| Path selection | Root/member case, array index, pointer `.address`/`.value` depth |
| Read or typed mapping | Starting position, byte order, placement, exact payload length, direct result before POCO conversion |
| Configured limit | The specific option and which array/string/byte/nesting/pointer work consumed it |
| Pointer traversal | Stored address, pointer width, absolute/relative mode, origin, target range, cycle/depth limits |
| Write | Input shape, missing/null member, numeric range, string/array size, union selection, destination capability |
| Update | Path-location read versus replacement write, validation failure versus physical commit failure |
| Documentation | Wrapper output, DocFX log, broken source/link, stale core assembly, Node/browser prerequisite |

Run only the focused reproducer until you understand the cause. Then rerun the broader check that originally found
the problem.

## SDK and restore problems

From the repository root:

```powershell
dotnet --info
dotnet --version
```

`dotnet --info` lists installed SDKs and the selected environment. `dotnet --version` should be a stable .NET 10 SDK
selected by `global.json`. The resolver message “there was no version specified” usually means no installed stable
.NET 10 SDK satisfies that file. Install the SDK rather than changing target frameworks to fit the machine.

To find the NuGet package cache:

```powershell
dotnet nuget locals global-packages --list
```

This prints the cache location for inspection. Do not copy cache contents into the repository or commit them as a
restore fix.

## Layout and byte failures

For a layout error:

1. Reduce the source to the smallest declaration that still fails.
2. Compare it with [Differences from C](../language/differences-from-c.md).
3. Check duplicate names, unknown types, by-value recursion, enum backing, array/bitfield expressions, and limits.
4. Keep the `InvalidLayout` code in a focused regression.

For a wrong value or offset:

1. Record the exact input in hexadecimal.
2. Draw the expected offset and width of each field.
3. Confirm little/big byte order and packed/aligned placement.
4. Check the stream's position before the operation.
5. Use `ParseStreamWithDebug` or `ResolveAddress` only after the simple calculation is explicit.

For a typed-mapping failure, read the path without `<T>` first. If the direct value is wrong, debug binary decoding.
If it is right, inspect the POCO constructor, member names, writable members, nullability, and numeric ranges.

## Update failures

Compare the complete destination bytes and stream position before and after the call. A path, traversal, conversion,
shape, pointer, union, or configured-limit failure should occur during staging and leave both unchanged.

A custom stream can fail during the final physical commit after accepting some bytes. Preserve the original inner
exception and distinguish that storage failure from a validation bug. Do not claim generic rollback that the stream
does not provide.

## Replay fuzz and property failures

The harness prints the target, seed, iteration, and minimized input. Use those exact values to reproduce the failure.
Turn the minimized input into a named focused test before changing the implementation. After the fix, confirm both
the named regression and the original seed.

If a property test fails, write down which equality it expects. Meaningful round-trip equality may allow normalized
padding while byte-for-byte union preservation has a stricter rule.

## Coverage and mutation failures

Coverage and mutation reports are generated from a particular DLL/PDB and target framework. Confirm they match the
source you are inspecting before interpreting a line number.

For a surviving mutation:

1. Read the changed condition or value.
2. Identify the public behavior that should differ.
3. Add an assertion that fails with the mutation.
4. Rerun the focused mutation set.

Separate a real survivor from “not covered,” compile failure, or an instrumentation limitation. Record a narrow
reviewed limitation when tooling genuinely cannot instrument the code; do not use a broad exclusion.

## Documentation failures

Run the documented wrapper rather than a partial DocFX command:

```powershell
.\tools\Validate-Documentation.ps1
```

The wrapper prints each subcommand and stops at the first failed stage. A stale-core message means the docs were
started with `-NoBuild` after a relevant source change; run the full validator once. Browser failures include the
page, assertion, console output, and accessibility finding.

After fixing the focused cause, return to [Testing and quality checks](testing.md) and rerun the affected wider gate.
