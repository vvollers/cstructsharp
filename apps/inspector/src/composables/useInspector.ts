import { computed, onScopeDispose, ref, shallowRef } from "vue";
import { detectFile } from "../detect-file";
import {
  rawFileSchema,
  schemaForFile,
  sampleExamples,
  type InspectorExample,
} from "../schema-catalog";
import { formatLayout } from "@cstructsharp/app-shared/format-layout";
import { useWasmRuntime } from "@cstructsharp/app-shared/composables/useWasmRuntime";
import { hexToBytes } from "@cstructsharp/app-shared/hex";
import type { ParseWithDebugOptions } from "@cstructsharp/app-shared/wasm/contract";
import { useParseSession } from "./useParseSession";

/**
 * Keeps track of the schema and binary data currently being inspected.
 * This is a Vue composable: a function that groups related state and the functions that change it.
 * Panels display these refs and call the functions below when the user edits or loads something.
 */
export function useInspector() {
  const parse = useParseSession();
  const selectedExample = shallowRef<InspectorExample | null>(sampleExamples[0] ?? null);
  const definition = ref(formatLayout(selectedExample.value?.definition ?? ""));
  const bytes = shallowRef(hexToBytes(selectedExample.value?.binaryHex ?? ""));
  const fileSource = shallowRef<Blob | null>(null);
  const loadedFileName = ref<string | null>(null);
  const schemaRevision = ref(0);

  // Reading a file and detecting its type take time. Keep their progress separate from parsing.
  const isLoadingFile = ref(false);
  const isDetecting = ref(false);
  const detectionMessage = ref("");
  let fileController: AbortController | null = null;

  const { status: wasmStatus, version: wasmVersion, error: wasmError } = useWasmRuntime();
  const schemaDisabled = computed(
    () => wasmStatus.value !== "ready" || parse.isRunning.value || isLoadingFile.value,
  );
  const sourceLabel = computed(
    () =>
      loadedFileName.value ??
      `${selectedExample.value?.schemaOnly ? "Load a file" : "Sample data"} · ${selectedExample.value?.title ?? "New schema"}`,
  );

  /** Aborts any file read or detection in progress and clears its loading indicators. */
  function cancelFileLoad(): void {
    fileController?.abort();
    fileController = null;

    isLoadingFile.value = false;
    isDetecting.value = false;
    detectionMessage.value = "";
  }

  /** Discards the current parse result and cancels file and parse work that uses the old input. */
  function invalidateDocument(): void {
    // Results belong to the old schema and bytes. Clear them and stop any work using that input.
    cancelFileLoad();
    parse.invalidate();
  }

  /**
   * Shows a schema in the editor and asks the schema panel to restore its parser settings.
   * @param example Schema to show, or null for a blank starter layout.
   */
  function applySchema(example: InspectorExample | null): void {
    selectedExample.value = example;
    definition.value = example?.definition ?? "struct root {\n    uint8 value;\n};";

    // SchemaPanel watches this counter and restores the chosen schema's parser settings.
    // The editor component stays mounted, so we do not have to create Monaco again.
    schemaRevision.value++;
  }

  /**
   * Selects a catalog entry. A teaching example replaces the bytes with its sample; a schema-only
   * entry keeps the loaded file.
   * @param example Catalog entry to select.
   */
  function selectExample(example: InspectorExample): void {
    invalidateDocument();

    if (example.schemaOnly) {
      // A schema-only entry describes a format but has no sample bytes. Keep the user's file.
      if (!fileSource.value) bytes.value = new Uint8Array();
      applySchema(example);
    } else {
      // A teaching example includes its own bytes, so it replaces the loaded file as well.
      fileSource.value = null;
      loadedFileName.value = null;
      bytes.value = hexToBytes(example.binaryHex);
      applySchema({ ...example, definition: formatLayout(example.definition) });
    }
  }

  /** Clears the loaded file and schema so the user can write a new layout from scratch. */
  function startNew(): void {
    invalidateDocument();

    fileSource.value = null;
    loadedFileName.value = null;
    bytes.value = new Uint8Array();
    applySchema(null);
  }

  /**
   * Replaces the layout text after an edit and invalidates the result; unchanged text is ignored.
   * @param value New layout text from the editor.
   */
  function setDefinition(value: string): void {
    // Monaco can report text that we just supplied to it. Ignore that notification if nothing changed.
    if (value === definition.value) return;

    invalidateDocument();
    definition.value = value;
  }

  /**
   * Replaces the in-memory bytes after a hex edit and invalidates the result.
   * @param value Edited bytes.
   */
  function editBytes(value: Uint8Array): void {
    invalidateDocument();
    bytes.value = value;
  }

  /**
   * Replaces the file that parsing reads after an edit and invalidates the result.
   * @param value Edited file contents.
   */
  function editSource(value: Blob): void {
    invalidateDocument();
    fileSource.value = value;
  }

  /**
   * Loads a file: reads its first 64 KiB for the preview and, when requested, detects its type and
   * selects a matching schema. A later edit or load cancels this one; read failures are reported
   * through the parse session.
   * @param file File chosen by the user.
   * @param autoDetect Whether to detect the file type and choose a schema.
   * @returns A promise that settles when the load finishes or is cancelled.
   */
  async function loadFile(file: File, autoDetect = false): Promise<void> {
    invalidateDocument();

    // Give this load its own cancellation signal. A later edit or load cancels this attempt.
    const attempt = new AbortController();
    fileController = attempt;
    isLoadingFile.value = true;
    isDetecting.value = autoDetect;

    try {
      // A File is also a Blob: it lets us read selected ranges without copying the whole file.
      // Read just the first 64 KiB for the preview; parsing will still use the complete file.
      const preview = new Uint8Array(await file.slice(0, 65536).arrayBuffer());

      // The user may have changed the document while we were waiting for the read.
      if (attempt.signal.aborted) return;

      let schema: InspectorExample | undefined;
      let message = "";
      if (autoDetect) {
        schema = rawFileSchema();

        try {
          const detected = await detectFile(file, attempt.signal);
          if (attempt.signal.aborted) return;

          // Detection chooses an existing schema. It does not build fields from the file contents.
          if (detected) {
            schema = schemaForFile(detected.ext);
            message = `${detected.ext.toUpperCase()} detected · ${schema.coverage === "prefix" ? "Prefix only" : "Schema loaded"}`;
          } else {
            message = "Type not recognized. Choose a schema or inspect the raw bytes.";
          }
        } catch (error) {
          if (attempt.signal.aborted) return;

          // A detection error still leaves us with a readable file and a simple fallback schema.
          message =
            error instanceof Error ? error.message : "Detection failed. Raw bytes are available.";
        }
      }

      // All reads are finished. Update the file, preview and optional schema in one step so the
      // panels do not briefly show a new schema together with bytes from the previous file.
      bytes.value = preview;
      fileSource.value = file;
      loadedFileName.value = file.name;
      detectionMessage.value = message;
      if (schema) applySchema(schema);
    } catch {
      if (!attempt.signal.aborted)
        parse.fail(
          "The selected file could not be read. Check that it is still available and try loading it again.",
          "file-read-failed",
        );
    } finally {
      // An older load must not turn off the loading indicator for a newer load.
      if (fileController === attempt) {
        fileController = null;
        isLoadingFile.value = false;
        isDetecting.value = false;
      }
    }
  }

  /**
   * Parses the loaded file (or the sample bytes) with the current layout.
   * @param options Parser and debug settings from the schema panel.
   * @returns The parse promise, or undefined when parsing is unavailable (runtime not ready, busy,
   *   or loading).
   */
  function runParse(options: ParseWithDebugOptions): Promise<void> | undefined {
    // Loaded files use the full Blob. Built-in samples use their small in-memory byte array.
    if (!schemaDisabled.value)
      return parse.run(definition.value, fileSource.value ?? bytes.value, options);
  }

  // Stop outstanding file work if the app that created this composable is removed.
  onScopeDispose(cancelFileLoad);

  return {
    selectedExample,
    definition,
    bytes,
    fileSource,
    loadedFileName,
    sourceLabel,
    schemaRevision,
    isLoadingFile,
    isDetecting,
    detectionMessage,
    wasmStatus,
    wasmVersion,
    wasmError,
    schemaDisabled,
    result: parse.result,
    isRunning: parse.isRunning,
    debugData: parse.debugData,
    selectedDebugIndices: parse.selectedDebugIndices,
    focusPath: parse.focusPath,
    selectExample,
    startNew,
    setDefinition,
    editBytes,
    editSource,
    loadFile,
    runParse,
    invalidateResult: parse.invalidate,
    cancelParse: parse.cancel,
    selectByte: parse.selectByte,
    selectPath: parse.selectPath,
  };
}

export type Inspector = ReturnType<typeof useInspector>;
