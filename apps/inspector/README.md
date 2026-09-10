# Binary inspector

`src/` contains the independent binary file inspection UI, format examples, editor, and byte/field panels. `wasm/` owns the app's TypeScript declarations. `scripts/copy-wasm.mjs` validates and copies `artifacts/wasm/`; it never builds the workshop. Run `node tools/packaging/publish-wasm.mjs` from the repository root first. Here run `npm ci`, `npm run build`, `npm run test:unit`, and `npm run test:e2e`. `dist/`, `public/wasm/`, and browser reports are ignored output.
