---
title: Traceability codes
description: What the work-item codes in the repository contracts mean and where each family is used.
---

# Traceability codes

The machine-readable contracts under `contracts/` tag some entries with a short code such as `LANG-05` or `QA-07`.
The codes are traceability ids: the validators require one wherever a feature is blocked, a limit is known, or an
exclusion is deliberate (`tools/quality/feature-operation-matrix.mjs` accepts `^[A-Z]+-\d{2}$`), so a reader can see
that the state was decided rather than forgotten. They are not ticket numbers in an external tracker, and the
source code and guides describe behavior in plain words rather than by code.

## Families

| Family | Meaning | Where it appears |
| --- | --- | --- |
| `LANG-nn` | A language decision: syntax accepted, rejected, or limited by the Portable profile (for example `LANG-05` multidimensional arrays, `LANG-10` bitfield storage rules, `LANG-15` alignment and offset overrides, `LANG-16` compiler/ABI behavior). | `feature-operation-matrix.json` limitations and exclusions |
| `API-nn`, `IR-nn`, `PERF-nn`, `SAF-nn`, `COR-nn` | Managed API, compiled intermediate representation, performance, safety-limit, and core-behavior decisions that bound a feature or an operation. | `feature-operation-matrix.json` contracts and known limits |
| `QA-nn` | A quality gate: `QA-03` compiler-differential fixtures, `QA-04` the managed fuzz corpus, `QA-07` the frozen managed and browser API baselines, `QA-08` the non-web release budgets. | `feature-operation-matrix.json`, `contracts/api/*/manifest.json`, `contracts/performance/non-web-rc1.json` |
| `DOC-nn` | A documentation contract: `DOC-01` is the canonical Portable reference and its manual fixtures. | `feature-operation-matrix.json` |
| Performance-plan items (`E1.x`, `E2.x`, `E3.x`) and architecture-plan items (`AP-x.y`) | Steps of two completed improvement plans, kept only in the history entries of the API baselines and the browser contract, where they explain why a recorded change was made. | `contracts/api/managed-rc1/manifest.json`, `contracts/api/browser-rc1/contract.json` |

## Reading a tagged entry

Each tagged entry carries its own explanation next to the code (a `summary`, `rationale`, `syntax`, or `change`
text), so the code never has to be looked up to understand the entry. When a limitation is lifted, remove the
entry and its code together; when a new deliberate limit or exclusion is recorded, tag it with the family that
owns the decision and the next unused number.
