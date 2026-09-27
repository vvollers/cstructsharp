# Browser applications

`explorer/` is the test and lesson explorer; `inspector/` is the binary file inspector. `shared/` holds the source both
apps compile, imported as `@cstructsharp/app-shared/...`:

- `wasm/`: the runtime loader and envelope validation, with the contract types taken from the npm package declarations;
- `language/`: the editor vocabulary built from the language contract, the document symbol scan, and the Monaco
  language registration;
- `composables/useWasmRuntime`, `components/SettingStatusItem.vue`, and the layout formatter, error hints, hex helpers
  and option defaults.

The three form one npm workspace with one lockfile (`apps/package-lock.json`), so both apps use the same versions of
Vue, Vite and their tools.

Install everything once with `npm ci` in `apps/`. From `apps/`, `npm run lint`, `npm run format:check` and
`npm run test:unit` check every workspace. Build WASM once using `node tools/packaging/publish-wasm.mjs`, then build
either app from its directory. See each app README for commands.
