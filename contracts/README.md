# Reviewed contracts and fixtures

API snapshots, language examples, compiler facts, performance budgets, and documentation policies are authoritative
reviewed inputs, not disposable build output. Managed tests and tools read them. See
[testing](../docs/project/testing.md) for the validation commands.

The documentation site publishes the contracts readers use under `/docs/contracts/`: the ones the pages link to, the
API baselines, and the memory contract. `published-files.json` lists them, `docs/docfx.json` copies exactly that list,
and the documentation validator checks that the site holds those files unchanged and nothing else. Measurement
baselines and review data (performance baselines, mutation proofs, coverage and compiler fixtures) stay in the
repository.
