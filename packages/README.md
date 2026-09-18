# npm package inputs

`cstructsharp/` owns everything JavaScript that ships to consumers: the authored Node/browser loaders, the Vite
integration, the copy command, package metadata, the README, the canonical TypeScript declarations
(`index.d.ts`), the adapter sources the WASM publication and both bundles copy (`src/`: `main.js`, `bootstrap.js`,
`large-source.js`, `source-worker.js`, `cstructsharp-api.js`, the ZIP entry `cstructsharp-wasm.js`, and their
unit tests), and the standalone ZIP pieces (`standalone/`: README, starter pages, `serve.mjs`). It is a private
staging template, not a complete installable package. From the repository root, `npm run build:wasm` publishes
the managed bridge, `npm run pack:npm` assembles the tarball, `npm run pack:zip` the standalone bundle, and
`npm run test:npm` / `npm run test:bootstrap` verify them; the assembled tarball, runtime, and notices land under
ignored `artifacts/npm/`.
