import { computed, onScopeDispose, ref, shallowRef } from "vue";
import {
  debugEntryJsonPath,
  findDebugEntryIndexByOffset,
  findDebugEntryIndicesByPath,
} from "../debug-path";
import { INTEROP_CONTRACT_VERSION } from "../wasm/cstruct-contract";
import {
  parseSourceWithDebug,
  type InteropResult,
  type ParseWithDebugOptions,
} from "../wasm/cstruct-wasm";

/**
 * Runs the parser and keeps its result together with the selected fields.
 * Both the JSON tree and hex view use this selection, so clicking in either view updates the other.
 */
export function useParseSession() {
  // Each parse replaces the whole result; we do not edit its nested objects here.
  // shallowRef tells Vue to watch that replacement, without tracking every value in a large array.
  const result = shallowRef<InteropResult | null>(null);
  const isRunning = ref(false);
  const selectedDebugIndices = shallowRef<ReadonlySet<number>>(new Set());
  const focusPath = shallowRef<string[] | null>(null);
  let controller: AbortController | null = null;

  const debugData = computed(() => (result.value?.success ? result.value.debug : []));
  // The JSON tree keeps the selected root as its first step so debug paths ("root.values[2]") map onto it directly.
  const parsedResult = computed(() =>
    result.value?.success &&
    result.value.operation === "parse" &&
    !(result.value.data instanceof Uint8Array)
      ? { [result.value.root ?? "root"]: result.value.data }
      : undefined,
  );

  function clearSelection(): void {
    selectedDebugIndices.value = new Set();
    focusPath.value = null;
  }

  function stop(): void {
    controller?.abort();

    // Clear the current attempt immediately. Its promise may finish later, but its result is obsolete.
    controller = null;
    isRunning.value = false;
  }

  function invalidate(): void {
    stop();
    result.value = null;
    clearSelection();
  }

  function fail(message: string, code: string): void {
    invalidate();
    result.value = parseFailure(message, code);
  }

  async function run(
    definition: string,
    source: Blob | Uint8Array,
    options: ParseWithDebugOptions,
  ): Promise<void> {
    invalidate();

    // Remember which parse this is, so a previous parse cannot overwrite its result.
    const attempt = new AbortController();
    controller = attempt;
    isRunning.value = true;

    try {
      const parsed = await parseSourceWithDebug(definition, source, {
        ...options,
        signal: attempt.signal,
      });

      // Cancellation does not mean a promise stops immediately. Only show these results if this
      // is still the current parse; the user may have edited the schema or started another one.
      if (controller === attempt) result.value = parsed;
    } catch (error) {
      if (controller === attempt)
        result.value = parseFailure(
          error instanceof Error ? error.message : String(error),
          "operation-failed",
        );
    } finally {
      // Only this attempt can clear its own running indicator.
      if (controller === attempt) {
        controller = null;
        isRunning.value = false;
      }
    }
  }

  function cancel(): void {
    fail("Parsing was cancelled.", "cancelled");
  }

  function selectPath(path: string[] | null): void {
    // One JSON field can cover several byte ranges, such as the elements of an array.
    // Store all matching debug-entry indices so the hex view can highlight those ranges together.
    selectedDebugIndices.value = new Set(
      path ? findDebugEntryIndicesByPath(debugData.value, path, parsedResult.value) : [],
    );
  }

  function selectByte(offset: number): void {
    const index = findDebugEntryIndexByOffset(debugData.value, offset);
    if (index === -1) return clearSelection();

    const path = debugEntryJsonPath(debugData.value[index]!, parsedResult.value);

    // For an array such as uint16 values[4], the parser gives all elements the same field path.
    // Select those entries together, then ask the JSON panel to scroll to that field.
    selectPath(path);
    focusPath.value = path;
  }

  onScopeDispose(stop);

  return {
    result,
    isRunning,
    debugData,
    selectedDebugIndices,
    focusPath,
    run,
    cancel,
    invalidate,
    fail,
    selectByte,
    selectPath,
  };
}

/** Give UI errors the same result shape as errors returned by the parser. */
export function parseFailure(
  message: string,
  code = "invalid-input",
  offset: number | null = null,
): InteropResult {
  return {
    contractVersion: INTEROP_CONTRACT_VERSION,
    operation: "parse",
    success: false,
    root: null,
    data: null,
    debug: [],
    error: {
      code,
      message,
      offset,
      path: null,
      member: null,
      memberType: null,
      line: null,
      column: null,
    },
  };
}
