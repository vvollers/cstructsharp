# Binary inspector

The inspector demonstrates CStructSharp directly. Every example is a standalone, formatted CStruct definition.
Native `if` and `switch` statements select variants; runtime array counts, bitfields, explicit byte order and typed
pointers describe the stored data. The editor text is the complete layout passed to the library.

**Load & detect** uses `file-type` to identify a format and selects its fixed definition. Detection does not generate
fields, discover records for the schema, change its pointer width from file bytes, or merge multiple parse results.
**Load file** preserves the editor text and settings. Sample buttons load their small teaching fixtures;
**Schema · load your file** entries keep the currently loaded file. All examples open pretty-printed.

Files stay as Blob sources. Parsing uses the full source in a worker; the hex panel reads visible windows.
Pointers can target offsets beyond 4 GiB without reading the intervening bytes. Search scans bounded chunks,
and edits compose immutable Blob ranges with undo/redo. Cancel stops parsing; changing the file or example also
cancels outstanding work. No edits are written to disk until the user downloads a result.

The settings dialog controls decoded array, string and total-read budgets. These are independent of file size
and pointer distance. Larger payload budgets can be selected explicitly; results still have to fit available memory.
See the [schema coverage and catalog audit](SCHEMA-REVIEW.md),
[detection behavior](DETECTION.md), and the [large-file API guide](../../docs/guides/browser/large-data.md).

## Development

`src/` owns the UI and definitions; `wasm/` holds declarations copied from the runtime package.
From the repository root run `node tools/packaging/publish-wasm.mjs`. Then, in this directory, run
`npm ci`, `npm run build`, `npm run test:unit`, and `npm run test:e2e`.
`npm run copy:wasm` copies and validates existing runtime artifacts without rebuilding them.
`dist/`, `public/wasm/`, and browser reports are ignored output.
