import { ref, shallowRef, watch, type Ref } from "vue";

import type { OperationRequest } from "../components/OperationPanel.vue";
import { compareLessonResult, type Lesson, type LessonOperation } from "../lessons";
import { runOperation } from "../run-operation";
import { bytesToHex } from "@cstructsharp/app-shared/hex";
import {
  INTEROP_CONTRACT_VERSION,
  type InteropResult,
} from "@cstructsharp/app-shared/wasm/contract";

/**
 * Runs the selected example's operations and keeps their result: the envelope, the bytes the result panel shows,
 * whether the inputs changed since the run, and for a lesson whether the result matches its expected one. This is a
 * Vue composable: call it from a component's setup.
 * @param selection What is selected and whether the runtime can run it.
 * @param selection.example The selected lesson or test; changing it clears the result.
 * @param selection.lesson The selected lesson, whose expected results are compared, or null for a test.
 * @param selection.ready Whether the runtime has loaded.
 * @returns The result state, and the functions to run, reset, and take edited result bytes as the next input.
 */
export function useOperationRun(selection: {
  example: Ref<{ binaryHex?: string | null } | null>;
  lesson: Ref<Lesson | null>;
  ready: Ref<boolean>;
}) {
  const isProcessing = ref(false);
  const result = shallowRef<InteropResult | null>(null);
  const resultBytes = shallowRef<Uint8Array>(new Uint8Array());
  const binaryHexInput = ref("");
  const stale = ref(false);
  const expected = ref<LessonOperation["expected"] | null>(null);
  const expectationMatches = ref<boolean | null>(null);

  /** Clears the result and the lesson comparison. */
  function clearResult(): void {
    result.value = null;
    resultBytes.value = new Uint8Array();
    stale.value = false;
    expected.value = null;
    expectationMatches.value = null;
  }

  // A new selection starts from its own input bytes and no result.
  watch(
    selection.example,
    (example) => {
      binaryHexInput.value = example?.binaryHex ?? "";
      clearResult();
    },
    { immediate: true },
  );

  /** Restores the selected example's input bytes and clears the result. */
  function reset(): void {
    binaryHexInput.value = selection.example.value?.binaryHex ?? "";
    clearResult();
  }

  /**
   * Takes bytes edited in the result's hex view as the next input: the hex field shows them and the result is marked
   * stale until the operation runs again.
   * @param bytes The edited bytes.
   */
  function applyEditedBytes(bytes: Uint8Array): void {
    resultBytes.value = bytes;
    binaryHexInput.value = bytesToHex(bytes);
    stale.value = true;
  }

  /**
   * Runs one operation. Invalid input hex or JSON becomes a "browser-error" envelope, so the result panel reports
   * every failure the same way; nothing runs before the runtime is ready.
   * @param request The panel's request.
   */
  async function run(request: OperationRequest): Promise<void> {
    if (!selection.ready.value) {
      return;
    }

    isProcessing.value = true;
    clearResult();
    expected.value = selection.lesson.value?.operations[request.operation]?.expected ?? null;
    // Let the panel render its running state before the synchronous runtime call blocks the thread.
    await Promise.resolve();

    try {
      const outcome = runOperation(request);
      result.value = outcome.result;
      resultBytes.value = outcome.bytes;
    } catch (error) {
      result.value = browserFailure(request.operation, error);
    } finally {
      if (expected.value && result.value)
        expectationMatches.value = compareLessonResult(
          expected.value,
          result.value,
          resultBytes.value,
        );
      isProcessing.value = false;
    }
  }

  return {
    isProcessing,
    result,
    resultBytes,
    binaryHexInput,
    stale,
    expected,
    expectationMatches,
    run,
    reset,
    applyEditedBytes,
  };
}

/**
 * Wraps a failure that happened before the runtime ran (invalid hex or JSON) in an envelope.
 * @param operation The requested operation.
 * @param error The thrown value.
 * @returns A failed envelope with the "browser-error" code.
 */
function browserFailure(operation: OperationRequest["operation"], error: unknown): InteropResult {
  return {
    contractVersion: INTEROP_CONTRACT_VERSION,
    operation,
    success: false,
    root: null,
    data: null,
    debug: [],
    error: {
      code: "browser-error",
      message: error instanceof Error ? error.message : "The browser operation failed.",
      offset: null,
      path: null,
      member: null,
      memberType: null,
      line: null,
      column: null,
    },
  };
}
