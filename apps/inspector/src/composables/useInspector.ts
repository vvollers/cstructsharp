import { computed, onMounted, onScopeDispose, ref, shallowRef } from "vue";
import { detectFile } from "../detect-file";
import {
  rawFileSchema,
  schemaForFile,
  sampleExamples,
  type InspectorExample,
} from "../schema-catalog";
import { formatLayout } from "../format-layout";
import {
  getVersion,
  hexToBytes,
  initWasm,
  isLoaded,
  type ParseWithDebugOptions,
} from "../wasm/cstruct-wasm";
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

  const wasmStatus = ref<"loading" | "ready" | "error">("loading");
  const wasmVersion = ref("");
  const wasmError = ref("");
  const schemaDisabled = computed(
    () => wasmStatus.value !== "ready" || parse.isRunning.value || isLoadingFile.value,
  );
  const sourceLabel = computed(
    () =>
      loadedFileName.value ??
      `${selectedExample.value?.schemaOnly ? "Load a file" : "Sample data"} · ${selectedExample.value?.title ?? "New schema"}`,
  );

  function cancelFileLoad(): void {
    fileController?.abort();
    fileController = null;

    isLoadingFile.value = false;
    isDetecting.value = false;
    detectionMessage.value = "";
  }

  function invalidateDocument(): void {
    // Results belong to the old schema and bytes. Clear them and stop any work using that input.
    cancelFileLoad();
    parse.invalidate();
  }

  function applySchema(example: InspectorExample | null): void {
    selectedExample.value = example;
    definition.value = example?.definition ?? "struct root {\n    uint8 value;\n};";

    // SchemaPanel watches this counter and restores the chosen schema's parser settings.
    // The editor component stays mounted, so we do not have to create Monaco again.
    schemaRevision.value++;
  }

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

  function startNew(): void {
    invalidateDocument();

    fileSource.value = null;
    loadedFileName.value = null;
    bytes.value = new Uint8Array();
    applySchema(null);
  }

  function setDefinition(value: string): void {
    // Monaco can report text that we just supplied to it. Ignore that notification if nothing changed.
    if (value === definition.value) return;

    invalidateDocument();
    definition.value = value;
  }

  function editBytes(value: Uint8Array): void {
    invalidateDocument();
    bytes.value = value;
  }

  function editSource(value: Blob): void {
    invalidateDocument();
    fileSource.value = value;
  }

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

  function runParse(options: ParseWithDebugOptions): Promise<void> | undefined {
    // Loaded files use the full Blob. Built-in samples use their small in-memory byte array.
    if (!schemaDisabled.value)
      return parse.run(definition.value, fileSource.value ?? bytes.value, options);
  }

  onMounted(async () => {
    try {
      await initWasm();
      if (!isLoaded()) throw new Error("WASM finished loading without usable exports.");

      wasmVersion.value = getVersion();
      wasmStatus.value = "ready";
    } catch (error) {
      wasmStatus.value = "error";
      wasmError.value = error instanceof Error ? error.message : String(error);
    }
  });

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
