# Managed memory contract

`v1.json` records address rules, defaults, the operation matrix, and the importer subset for memory analysis.
Behavioral checks are the named managed test classes, run on both supported frameworks. Public signatures
are recorded in `contracts/api/managed-rc1` with the rest of the library.

Use `MemorySession` for unsigned or mapped sources, preserve stored pointers as `StoredPointer`, and follow
targets explicitly with `.value`. Union reads expose all interpretations, so creation requires raw bytes or
an explicit member selection; a parsed union dictionary is not an unambiguous creation value.

Read the memory guide for importer coverage, generation and ownership requirements, source budgets, and the
non-atomic patch commit contract. The browser bridge exposes standalone schemas and binary operations.
