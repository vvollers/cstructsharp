# Browser applications

`explorer/` is the test and lesson explorer; `inspector/` is the binary file inspector. `shared/` holds the source both
apps compile, imported as `@cstructsharp/app-shared/...`:

- `wasm/`: the runtime loader and envelope validation, with the contract types taken from the npm package declarations;
- `language/`: the editor vocabulary built from the language contract, the document symbol scan, and the Monaco
  language registration;
- `composables/useWasmRuntime`, `components/SettingStatusItem.vue`, and the layout formatter, error hints, hex helpers
  and option defaults.

The three are members of the repository's npm workspace, with the npm package in `packages/cstructsharp`: one
lockfile (`package-lock.json` at the repository root), so both apps use the same versions of Vue, Vite and their tools.

Install everything once with `npm ci` at the repository root. From the root, `npm run lint`, `npm run format:check`
and `npm run test:unit` check every workspace. Publish the WASM runtime once with `npm run build:wasm` at the root,
then build either app from its directory: `npm run build` stages the publication, builds the app, and checks that
the build embeds exactly that publication (`shared/scripts`). See each app README for commands.
