---
title: Executable examples
description: Find the tested recipe catalog, the complete recipe programs, and the runnable memory-analysis project.
---

# Executable examples

The examples in this section are programs that the documentation build runs and checks. The
[tested recipe catalog](../guides/recipes/index.md) is the one place to choose one of the <!-- facts:recipe-count:start -->40<!-- facts:recipe-count:end -->
recipes: it lists each task with its difficulty and the result it checks, and shows how to run one recipe or all of
them from the repository root; a run of all of them ends with <!-- facts:scenario-count:start -->`PASS all 58 scenarios`<!-- facts:scenario-count:end -->. Each
recipe links to its complete program, with its imports, helper methods, and types, ready to copy into a console
project.

The [synthetic memory analysis](memory-analysis/index.md) project is a separate runnable consumer for the memory
guides.

The complete programs are generated from the methods the runner executes (`Program.cs`, `MoreExamples.cs`,
`BinaryTypeExamples.cs`, and the other example files in `docs/examples/`), so copied code and tested code stay the
same. To change a recipe, edit its source method or its teaching text in
`tools/documentation/export-documentation-examples.mjs`, then run that script or build the documentation.
