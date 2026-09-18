# Working on CStructSharp

These instructions apply across the repository. Keep changes small, focused, and easy to explain.
Read [CONTRIBUTING.md](CONTRIBUTING.md) for the checks specific to the area you change.

The project is published on NuGet and npm at major version 0 and has one maintainer. A minor release may change
public APIs and project boundaries; record every such change as **Breaking** in the CHANGELOG with its migration,
and update the current contracts, tests, and documentation without retaining unused compatibility layers.

## Project boundaries

- `src/CStructSharp` owns the layout language and binary operations; `src/CStructSharp.Wasm` exposes the browser bridge.
- `apps/explorer` teaches the library; `apps/inspector` applies standalone schemas to binary files.
  Use each app's README and package scripts. Keep related helpers together; create modules for clear responsibilities.
- Inspector definitions belong in `apps/inspector/src/schema-catalog.ts`. Keep a format expressible as one CStruct;
  do not introduce file scans, generated offsets, or merged parse results to compensate for schema limitations.
- `docs/` contains DocFX sources and executable examples; `contracts/` records API and language promises.
  Update authored sources rather than generated API pages, app bundles, or files under `artifacts/`.

## Source code documentation

- Every authored class and function/method needs a documentation comment, including constructors, private helpers,
  test methods, and Vue composables. Add or repair documentation when adding or editing the declaration.
  For anonymous callbacks, put their purpose immediately above the callback or its registration.
- C#: use `/// <summary>` on types and methods. Document parameters, return values, type parameters, and expected
  exceptions with the appropriate XML tags when applicable. Use `<remarks>` for important constraints and examples,
  and `<inheritdoc/>` only when the inherited documentation accurately describes the implementation.
- JavaScript/TypeScript, including Vue scripts: use `/** ... */` JSDoc/TSDoc immediately above the declaration.
  Describe purpose, inputs, outputs, and relevant side effects; do not duplicate TypeScript type annotations in prose.
- Node tools under `tools/`: a header comment states purpose and usage; the shared helpers live in `tools/lib/`.
- Explain the contract: what the operation achieves, units such as bytes versus elements, offset origin, ownership,
  mutation, cancellation, and failure behavior where relevant. Simple helpers can have a brief summary.
- Add selective inline comments around difficult algorithms, state transitions, binary layouts, and non-obvious
  decisions. Explain why a step exists, its invariant, or the constraint behind a choice; avoid narrating assignments.
- Group related declarations and blocks of logic together, using blank lines between meaningful steps.
  Keep comments current when moving or changing code. Document generated code through its generator/template;
  do not hand-edit generated or third-party sources or reformat unrelated files to satisfy this rule.

## Project documentation and changelog

- Update documentation in the same task as changes to behavior, API, schema coverage, setup, build, or deployment.
  Check affected READMEs, `docs/` pages, XML API comments, executable examples, and contracts for consistency.
- Manuals, guides, and examples must describe the current API and specifications directly. Replace stale explanations
  and examples; do not append project-development narratives, old/new comparisons, or references to superseded APIs.
  Keep project release history and migration notes in [CHANGELOG.md](CHANGELOG.md).
- Review `CHANGELOG.md` for every task. Add concise, user-relevant entries under `Unreleased` for notable features,
  fixes, compatibility changes, and documentation/tooling improvements. Consolidate related entries before release;
  record the actual version and date when released. Do not invent release dates or log every formatting edit.
- Write for a second-year computer science student: define terms before using them, use short concrete sentences,
  and explain the principle before showing syntax. Assume basic programming knowledge, not binary-format expertise.
- Teach CStruct through small worked examples: a declaration, input bytes, field offsets/values, and the result.
  Explain C's `struct`, member order, arrays, unions, pointers, alignment, padding, and byte order where relevant.
- Include concise, sourced historical context about C structs when it helps explain their design or use.
  Distinguish C memory-layout/ABI rules from CStructSharp's supported layout language and explicit parser settings.
  Historical context about C is useful teaching material; the project's implementation timeline is changelog material.
- Verify examples against the current implementation and canonical contracts. Explain limitations honestly;
  do not imply that a header schema fully decodes a format or that CStructSharp implements all of C.

## Validation and delivery

- Use the SDK in `global.json`, Node 22.14 or later for `tools/`, and each package's declared Node/npm versions and lockfile.
  Run commands from the repository root unless an app directory is specified.
- Managed changes: `dotnet build CStructSharp.NonWeb.sln -c Release`, then
  `dotnet test tests/CStructSharpTests/CStructSharpTests.csproj -c Release --no-build` (both target frameworks).
  Add regression tests for behavior changes; use focused tests while iterating, then the required area checks.
- Vue changes: from the affected app, run `npm run lint`, `npm run format:check`, `npm run test:unit`,
  `npm run build`, and relevant `npm run test:e2e` checks. Follow its README for WASM build prerequisites.
- Documentation changes under `docs/`: run `node tools/documentation/validate-documentation.mjs`.
  Follow [docs/README.md](docs/README.md); routine documentation work builds the core library, not the full WASM solution.
  For root prose-only edits, check links and `git diff --check`; unrelated application tests are unnecessary.
- API/language changes: follow the contract, fixture, and baseline checks in `CONTRIBUTING.md`.
  Do not weaken tests or refresh snapshots merely to hide a failure; explain and verify intentional changes.
- Before finishing, review the entire diff and report what changed, the checks run, and any remaining limitations.
  Preserve unrelated user work and keep commits focused. Follow [release guidance](docs/project/release-process.md)
  for authorized publication; do not trigger a release or deployment as a side effect of ordinary edits.
- Keep this file under roughly 100 lines. Link to detailed procedures instead of duplicating them;
  add narrower instructions only when a subtree genuinely requires different rules.
