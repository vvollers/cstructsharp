---
title: Contributing workflow
description: Make a focused CStructSharp change, prove the behavior on both frameworks, and update affected documentation.
---

# Contributing workflow

The root `CONTRIBUTING.md` is the short checklist. This page explains why the repository asks for a fail-first test
and how to choose the checks that follow.

## Work from a known baseline

Before editing:

1. Follow [Contributor setup](getting-started.md).
2. Run the smallest existing tests around the behavior.
3. Inspect `git status --short`.
4. Preserve tracked and untracked work that is not part of your change.

A dirty working tree is not permission to reset or delete someone else's files. Keep your patch limited to the
behavior you can explain.

## Add a test that fails for the intended reason

A *fail-first* test demonstrates the missing or incorrect behavior before the implementation changes. It prevents a
test from passing accidentally because it never reached the relevant path.

Write the smallest test that includes:

- a complete supported layout;
- exact input values or bytes;
- the specific API call;
- the expected result, position, bytes, or error code; and
- any limit, pointer, enum, union, or update condition involved.

Run the test on .NET 8 and .NET 10. Confirm the failure message proves the intended gap, not a setup mistake.

## Fix the shared path

Many public overloads adapt to the same prepared layout and operation code. Fix that shared reader, writer, path, or
layout stage when the behavior is common. Avoid copying fixes into stream, span, memory, debug, and typed wrappers
individually.

This project is still pre-release. Prefer one clear design over a compatibility shim for an obsolete internal path
unless a recorded public decision requires compatibility. Do not broaden a catch, suppress a real warning, lower a
quality threshold, change a replay seed, or replace a reviewed baseline merely to make a check pass.

## Widen verification according to the change

After the focused tests pass:

1. Run the full managed test project.
2. Run the affected language, API, regression, compiler, fuzz, package, or documentation validator.
3. Measure timing and allocation for a hot path.
4. Compare package or site artifacts when output contents can change.
5. Run the consolidated Web/WASM checks only at the designated final integration stage.

The [testing guide](testing.md) explains each layer and command.

## Update all affected reader material

| Change | Also review |
| --- | --- |
| Public method, type, default, exception, ownership, or limit | XML comments, generated API, relevant guide/example, API baseline, release notes/version |
| Layout syntax or behavior | Parser and operation tests, grammar, language page, Portable data, fixtures, feature matrix |
| Dynamic or typed value shape | Read/write guides, round-trip properties, mapping errors, browser representation impact |
| Performance-sensitive code | BenchmarkDotNet scenarios and allocation results |
| Build/package/dependency | Both framework assets, consumer test, metadata, symbols, audit, size |
| Browser adapter | Recorded browser format and accumulated final Web integration |
| Documentation structure or presentation | Markdown, spelling, links, search, browser/accessibility, Pages artifact |

Do not copy internal planning notes or generated logs into the public site. A product claim belongs in published
documentation only after source, an executable test, or maintained reference data supports it.

Documentation follows current supported preview/main behavior. Add a version selector only when two maintained
release lines need different instructions.

## Review your patch

Before handing off:

```powershell
git diff --check
git status --short
```

Read every changed file rather than relying only on generated output. A reviewer should be able to find the original
failing test, explain why the implementation point is shared, reproduce the commands, and understand any limitation.

Commit, push, pull-request, publication, deployment, and release actions are separate operations. Perform only the
ones that have been explicitly requested.

For documentation ownership and review cadence, see [Documentation maintenance](maintenance.md).
