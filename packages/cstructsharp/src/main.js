import { dotnet } from "./_framework/dotnet.js";
import { createCStructSharpWasm } from "./bootstrap.js";

try {
  const { getAssemblyExports } = await dotnet.create();
  const assemblyExports = await getAssemblyExports("CStructSharpWeb.Wasm");
  window.CStructSharpWasm = createCStructSharpWasm(assemblyExports);
  window.dispatchEvent(new Event("cstructsharp-wasm-ready"));
} catch (cause) {
  const error = cause instanceof Error ? cause : new Error(String(cause));
  window.CStructSharpWasm = {
    exports: null,
    ready: false,
    error: error.message,
  };
  window.dispatchEvent(
    new CustomEvent("cstructsharp-wasm-error", {
      detail: error.message,
    }),
  );
  console.error("CStructSharp WASM initialization failed.", error);
}
