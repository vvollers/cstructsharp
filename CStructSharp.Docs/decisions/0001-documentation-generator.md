# Decision 0001: Use DocFX for the CStructSharp documentation site

Status: accepted  
Date: 2026-07-26  
Tool version: DocFX `2.78.5`

## Context

CStructSharp needs one searchable static site containing:

- project and contributor documentation;
- task-oriented library guides and executable examples;
- a normative manual for the Portable C-like binary-layout language; and
- generated reference documentation for every public .NET API.

The site must build locally and on GitHub Pages without requiring routine restoration, build, publication, or tests
of `CStructSharpWeb` or `CStructSharpWeb.Wasm`.

## Decision

Use the repository-local DocFX `2.78.5` tool with the built-in `default` and `modern` templates.

Generate managed-reference metadata from the prebuilt
`CStructSharp/bin/Release/net10.0/CStructSharp.dll` plus its side-by-side XML/PDB files. Do not point DocFX at a
solution or project. The separate managed-compatibility gate remains responsible for proving identical public
surfaces on .NET 8 and .NET 10.

Enable DocFX’s local static search and treat all DocFX warnings as errors. A clean validation/Pages build must first
remove only the ignored generated `_site` directory because incremental DocFX builds do not remove stale HTML for
deleted pages.

## What we tested

All commands ran from `C:\projects\CStructSharp` on PowerShell 7.6.3 with .NET SDK 10.0.204.

| Criterion | Result |
| --- | --- |
| Pinned local tool | `dotnet tool restore` restored DocFX 2.78.5 with `rollForward: false`. |
| Core-only input | Explicit restore/build of `CStructSharp.csproj` for `Release/net10.0`; the project has zero `ProjectReference` items. |
| API generation | 22 API YAML files: one TOC plus 21 managed-reference files. |
| Public reference coverage | 163 primary generated UIDs: one namespace, 20 public types, and 142 members/enum values/explicit-interface members. |
| Conceptual content | Home, Project, Portable language, and API landing pages built. |
| Cross-references | Type, property, and method xrefs resolved with zero warnings. |
| Search | 25 HTML pages indexed; browser interaction returned the expected Project, Language, and `CStruct` API pages for three seeded terms. |
| Warnings gate | An intentional missing local target produced `InvalidFileLink` and a nonzero warnings-as-errors exit; the restored site rebuilt with zero warnings/errors. |
| Local preview | Home, conceptual pages, API page, CSS, JavaScript, and search index returned HTTP 200; missing page returned 404. |
| GitHub project subpath | Home, nested conceptual/API pages, and relative CSS/JavaScript returned HTTP 200 beneath `/CStructSharp.Docs/_site/`; generated HTML contained zero root-absolute asset/content URLs. |
| Presentation | Headless Edge screenshots at 1440×1000 and 390×844 showed readable dark-mode desktop and responsive mobile layouts, navigation, callouts, code, and API signatures. |
| Keyboard | Desktop Tab order reached the logo/home link, enabled search control with accessible label, and primary navigation using native keyboard focus. |
| Basic semantics | Generated pages use `lang="en"`, one `main`, one `h1`, standard links/buttons/forms, labeled search/navigation controls, and theme-aware rendering. |
| Web/WASM isolation | DocFX logs contained zero Web/WASM references; the core project has no project references; no Web/WASM command ran. |

Measured local observations:

| Measurement | Result |
| --- | ---: |
| Local tool restore | 5.140 s |
| Up-to-date core restore | 0.692 s |
| Core `Release/net10.0` build | 0.577 s |
| Full DocFX metadata + render | 2.428 s |
| Content-only DocFX render | 1.190 s |
| Static artifact | 261 files / 26,653,741 bytes |
| Optimally compressed artifact | 6,858,517 bytes |

Initial non-regression budgets, pending CI calibration:

- full DocFX metadata/render after the core build: at most 10 seconds;
- content-only render: at most 5 seconds;
- uncompressed static site: at most 32 MiB; and
- compressed Pages artifact: at most 8 MiB.

Network-dependent tool restore time is recorded but not budgeted. If a build exceeds a budget, record the
measurements and either explain the exception or improve the artifact or tooling; do not hide it by excluding
documentation.

## Alternatives considered

### VitePress

VitePress is attractive because the repository already uses Vue/Vite, and its conceptual-documentation and local
search experience is strong. It has no native .NET XML/API generator, so it would require a second tool or a custom
conversion layer. That would split navigation/search/theming or create transformation code the project must own.

### Docusaurus

Docusaurus has the largest adoption signal and a mature ecosystem, but adds React/MDX, has no native .NET API
generation, and commonly relies on a hosted or community search integration.

### Material for MkDocs

Material provides excellent content presentation and local search, but adds Python, has no native .NET API
generation, and is now in maintenance mode while feature development moves elsewhere.

### Astro Starlight

Starlight is modern, accessible, and search-oriented, but adds Astro, has no native .NET API generation, and has a
younger 0.x ecosystem.

### A DocFX plus frontend-generator hybrid

Rejected for the first site because it creates two generators, two templates, cross-site routing, and either
fragmented or custom unified search without improving the core documentation outcome.

## Risks and required follow-up

- The modern template contributes most of the 26.7 MB uncompressed artifact through source maps and optional
  rendering libraries. Keep the initial artifact budgets visible and optimize only with a measured, maintainable
  change.
- Incremental builds retain stale removed pages. The clean validation/Pages wrapper must safely delete only
  `CStructSharp.Docs/_site` before rebuilding.
- The built-in theme selector follows system dark/light preference and renders correctly, but its dynamically inserted
  anchor is skipped by native Tab order in DocFX 2.78.5. Primary navigation and search are keyboard accessible.
  `DOCSITE-08` must either add a small maintainable accessibility correction or record this as a release blocker after
  the full automated accessibility audit.
- Local PDBs do not contain the CI-conditioned Source Link package. CI-like source-link verification remains a
  `DOCSITE-06` gate.
- Generated member UIDs must be compared with the saved managed signature list after that list has a stable tracked
  location.

## Fallback trigger

Reopen this decision before full authoring if DocFX cannot maintain:

- complete public API rendering;
- one search index across conceptual and API content;
- repository-subpath-safe static links;
- accessible primary navigation and content; or
- the measured local/CI build and artifact budgets without fragile output rewriting.

If reopened, run an explicit VitePress plus API-generation spike and obtain maintainer approval before introducing a
hybrid or changing generators.
