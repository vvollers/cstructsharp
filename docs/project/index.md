---
title: Project documentation
description: Understand the CStructSharp repository, prepare a development machine, and choose the right change workflow.
---

# Project documentation

This section is for people working on CStructSharp itself. If you only want to use the library, begin with the
[library guides](../guides/index.md).

New contributors should read these pages in order:

1. [Project overview](overview.md) explains what belongs in the core library and what does not.
2. [Contributor setup](getting-started.md) prepares the SDK, restores local tools, and establishes a clean test
   baseline.
3. [Repository map](repository-map.md) shows which project owns each kind of change.
4. [Architecture](architecture.md) follows a layout from source text to a read, write, or update operation.
5. [Contributing workflow](contributing.md) explains the fail-first change loop and review expectations.

Keep these reference pages nearby:

- [Dependencies](dependencies.md) explains which packages reach library users and which stay in build/test tooling.
- [Building](building.md) gives core-only, non-Web, documentation, and package commands.
- [Testing](testing.md) explains the purpose of each test and quality layer.
- [Debugging](debugging.md) gives a repeatable path for SDK, layout, byte, fuzz, coverage, and mutation failures.
- [Release process](release-process.md) separates candidate validation from publishing.
- [Documentation deployment](documentation-deployment.md) and
  [documentation maintenance](maintenance.md) cover the site.

Routine development uses `CStructSharp.NonWeb.sln`. The WebAssembly adapter and browser workbench are optional and
are tested together only during final integration, because rebuilding them for every core or documentation change
adds substantial time without improving those focused checks.
