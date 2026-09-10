# Independent browser applications

`workshop/` is the test/lesson workbench; `inspector/` is the binary file inspector. Each owns its components, boundary code, configuration, dependencies, and tests. They share the generated WASM publication, not frontend source. Install dependencies with `npm ci` in each app. Build WASM once using `node tools/packaging/publish-wasm.mjs`, then build either app. See each app README for commands.
