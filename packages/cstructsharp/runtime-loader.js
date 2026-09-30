// Share the runtime per asset URL, without installing a global application API.
const runtimes = new Map();

/**
 * Starts the .NET runtime found under `base` and binds its managed exports to the raw adapter. Each asset URL
 * starts once: later calls, and concurrent ones, share the same promise, including a rejected one.
 * @param {URL | string} base The runtime directory URL, ending in `/`, that holds `_framework/` and `bootstrap.js`.
 * @returns {Promise<object>} The raw adapter (see RawWasmAdapter in index.d.ts).
 * @throws {Error} Through the promise, when the runtime cannot be imported or started, or an export is missing.
 */
export function loadRuntime(base) {
  const url = new URL("_framework/dotnet.js", base).href;
  if (!runtimes.has(url)) {
    runtimes.set(
      url,
      (async () => {
        const { dotnet } = await import(/* @vite-ignore */ url);
        const bootstrapUrl = new URL("bootstrap.js", base).href;
        const { createCStructSharpWasm } = await import(
          /* @vite-ignore */ bootstrapUrl
        );
        const runtime = await dotnet.create();
        return createCStructSharpWasm(
          await runtime.getAssemblyExports("CStructSharpWeb.Wasm"),
        );
      })(),
    );
  }
  return runtimes.get(url);
}
