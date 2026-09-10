// Share the runtime per asset URL, without installing a global application API.
const runtimes = new Map();

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
