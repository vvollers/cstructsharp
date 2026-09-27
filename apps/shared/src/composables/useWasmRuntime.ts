import { onMounted, ref } from "vue";

import { getVersion, initWasm, isLoaded } from "../wasm/adapter";

/** Whether the WebAssembly runtime is still loading, ready, or failed to load. */
export type WasmStatus = "loading" | "ready" | "error";

/**
 * Loads the WebAssembly runtime when the calling component mounts and reports its state. This is a Vue composable:
 * call it from a component's setup, and read the refs in the template.
 * @returns The load status, the managed library version once ready, and the failure message after an error.
 */
export function useWasmRuntime() {
  const status = ref<WasmStatus>("loading");
  const version = ref("");
  const error = ref("");

  // Loading starts after mount so the page renders first; a failure is shown instead of thrown.
  onMounted(async () => {
    try {
      await initWasm();
      if (!isLoaded()) throw new Error("The runtime finished loading without usable exports.");
      version.value = getVersion();
      status.value = "ready";
    } catch (cause) {
      status.value = "error";
      error.value = cause instanceof Error ? cause.message : String(cause);
    }
  });

  return { status, version, error };
}
