# Binary inspector

`src/` contains the independent binary file inspection UI, format examples, editor, and byte/field panels. `wasm/` owns the app's TypeScript declarations. `scripts/copy-wasm.mjs` validates and copies `artifacts/wasm/`; it never builds the workshop. Run `node tools/packaging/publish-wasm.mjs` from the repository root first. Here run `npm ci`, `npm run build`, `npm run test:unit`, and `npm run test:e2e`. `dist/`, `public/wasm/`, and browser reports are ignored output.

Loaded files remain Blob sources: worker parsing can seek across the full file, while the hex panel requests
visible byte windows. Full-file search scans bounded chunks, and edits compose immutable Blob ranges with
session undo/redo. Files are never silently truncated to 4 MiB or rewritten on disk. Cancel parse stops the worker;
changing the file or schema example also cancels outstanding work.

The ZIP example reads the local file header at offset zero, including filename and raw extra bytes. It is not an
archive extractor; empty, prefixed, and split archives need a suitable layout. See the
[large-data guide](../../docs/guides/browser/large-data.md) for the public API and its limits.
