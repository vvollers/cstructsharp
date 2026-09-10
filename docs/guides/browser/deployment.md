---
title: Load and deploy the browser bundle
description: Keep browser runtime files together and diagnose missing files, media types, and deployment paths.
---

# Load and deploy the browser bundle

## npm consumers

With Vite, register `cstructsharp()` from `cstructsharp/vite`. It serves runtime files in development and copies
them to a content-specific directory in production. Set Vite's `base` to `/` or an absolute deployment path such
as `/tools/binary/`; relative `./` bases are rejected. Vite 8 is tested.

For other browser build tools:

```sh
npx --no-install cstructsharp-copy --out public/cstructsharp
```

```js
import { loadCStructSharpWasm } from "cstructsharp/browser";
await loadCStructSharpWasm({ runtimeUrl: "/cstructsharp/" });
```

Use your application's actual static-output directory and public URL. Copying refuses an existing destination
to protect application files. Copy upgrades into a new versioned directory, then update the URL. Serve WASM as
`application/wasm` and JS as JavaScript. A same-origin CSP can use `script-src 'self' 'wasm-unsafe-eval'` and
`connect-src 'self'`; this build does not need cross-origin isolation headers. The runtime is about 5.2 MB unpacked.

Node consumers use `import ... from "cstructsharp"` without copying or serving files. Keep the package external
in server bundles; the Vite plugin does this for SSR. `cstructsharp/node` is available for explicit host selection.

## Standalone ZIP consumers

Complete the [starter](index.md) before integrating the bundle into a larger application.
Copy the entire extracted bundle into your static assets and import its public JavaScript entry point using a
relative URL. A Vue, React, or other framework is not required. Building the bridge from C# source requires the
.NET WASM workload; consuming a release archive does not.

## Serve all runtime files

Use HTTP(S). Opening `index.html` with `file://` prevents normal module and runtime loading. Serve `.js` as JavaScript
and `.wasm` as `application/wasm`. The included `serve.mjs` provides those types for local development.

Keep `cstructsharp-api.js`, `main.js`, `bootstrap.js`, the runtime configuration, and `_framework` beside the public entry point. Publish
one complete release together; mixing cached files from different releases can prevent startup. When deploying
under a path such as `/tools/binary/`, keep relative imports inside that path instead of using domain-root URLs.

## Troubleshoot loading

| Symptom | Check | Action |
| --- | --- | --- |
| `node` cannot be found | Local server prerequisite | Install Node.js or use your application's existing static server |
| Port 8080 is already in use | Another local server | Stop that server or change the port in `serve.mjs` |
| Page says it could not load WASM | Browser Network tab | Find the first failed request and restore the missing file or correct its path |
| Runtime request returns HTML | Static host fallback | Serve runtime files directly; do not rewrite missing runtime paths to the application page |
| Module or MIME error | Response Content-Type | Serve JavaScript and WASM with their correct media types |
| Works at root but fails under a directory | Import URLs | Use paths relative to the bundle and deploy the complete directory |
| Works locally but fails after an update | Mixed assets or cache | Deploy a complete bundle into a versioned directory and update the application import |

Load the runtime once and reuse it. The public convenience calls are asynchronous, but parsing itself runs managed
work in the browser; a large operation can still delay the page. Keep input and traversal limits appropriate to the
format. For large interactive workloads, assess a worker integration separately rather than assuming `await` moves
work off the UI thread.
