<script setup lang="ts">
import { computed, onMounted, onBeforeUnmount, ref, shallowRef } from "vue";
import { useFileDialog } from "@vueuse/core";
import {
  DockviewVue,
  themeVisualStudio,
  type DockviewReadyEvent,
  type VueComponent,
} from "dockview-vue";

import ExampleList from "./components/ExampleList.vue";
import SchemaPanelHost from "./components/SchemaPanelHost.vue";
import BinaryPanelHost from "./components/BinaryPanelHost.vue";
import ResultPanelHost from "./components/ResultPanelHost.vue";
import { formats, type FormatExample } from "./formats";
import { detectFile } from "./detect-file";
import { rawFileSchema, schemaForFile, schemaProfiles, schemaCatalog } from "./detected-schemas";
import { parseFailure, validateZipHeader } from "./parse-diagnostics";
import {
  findDebugEntryIndexByOffset,
  findDebugEntryIndicesByPath,
  tokenizePath,
} from "./debug-path";
import {
  initWasm,
  isLoaded,
  getVersion,
  parseSourceWithDebug,
  hexToBytes,
  type InteropResult,
  type ParseWithDebugOptions,
} from "./wasm/cstruct-wasm";

const wasmStatus = ref<"loading" | "ready" | "error">("loading");
const wasmVersion = ref("");
const wasmError = ref("");

const selectedExample = shallowRef<FormatExample | null>(formats[0] ?? null);
const definition = ref(selectedExample.value?.definition ?? "");
const bytes = shallowRef<Uint8Array>(hexToBytesSafe(selectedExample.value?.binaryHex ?? ""));
const loadedFileName = ref<string | null>(null);
const fileSource = shallowRef<Blob | null>(null);
let parseController: AbortController | null = null;
onBeforeUnmount(() => parseController?.abort());
let fileLoadVersion = 0;
const isDetecting = ref(false);
const detectionMessage = ref("");
let detectionController: AbortController | null = null;
onBeforeUnmount(() => detectionController?.abort());

function cancelDetection(): void {
  detectionController?.abort();
  isDetecting.value = false;
  detectionMessage.value = "";
}
const resetCount = ref(0);
const result = ref<InteropResult | null>(null);
const isRunning = ref(false);
const selectedDebugIndices = ref<ReadonlySet<number>>(new Set());
const focusPath = ref<string[] | null>(null);
const debugData = computed(() => (result.value?.Success ? result.value.DebugData : []));
const schemaDisabled = computed(() => wasmStatus.value !== "ready" || isRunning.value);

// dockview-vue's `components` map wants every entry to be the loosely-typed `VueComponent` (props: any);
// each host below deliberately requires a specific `params` shape instead, so an explicit cast is needed
// here - the actual shape passed via `addPanel({ params })` below is still checked against each host's
// own `defineProps`.
const dockComponents: Record<string, VueComponent> = {
  schema: SchemaPanelHost as unknown as VueComponent,
  binary: BinaryPanelHost as unknown as VueComponent,
  result: ResultPanelHost as unknown as VueComponent,
};

function hexToBytesSafe(hex: string): Uint8Array {
  try {
    return hexToBytes(hex);
  } catch {
    return new Uint8Array();
  }
}

function selectExample(example: FormatExample): void {
  cancelDetection();
  parseController?.abort();
  if (example.schemaOnly && example.extension) {
    fileLoadVersion += 1;
    if (!loadedFileName.value) bytes.value = new Uint8Array();
    const schema = {
      ...schemaForFile(example.extension, bytes.value),
      id: example.id,
      schemaOnly: true,
    };
    selectedExample.value = schema;
    definition.value = schema.definition;
    result.value = null;
    selectedDebugIndices.value = new Set();
    focusPath.value = null;
    resetCount.value += 1;
    return;
  }
  fileSource.value = null;
  fileLoadVersion += 1;
  selectedExample.value = example;
  definition.value = example.definition;
  loadedFileName.value = null;
  bytes.value = hexToBytesSafe(example.binaryHex);
  result.value = null;
  selectedDebugIndices.value = new Set();
  focusPath.value = null;
  resetCount.value += 1;
}

function startNew(): void {
  cancelDetection();
  parseController?.abort();
  fileSource.value = null;
  fileLoadVersion += 1;
  selectedExample.value = null;
  definition.value = "struct root {\n    uint8 value;\n};";
  loadedFileName.value = null;
  bytes.value = new Uint8Array();
  result.value = null;
  selectedDebugIndices.value = new Set();
  focusPath.value = null;
  resetCount.value += 1;
}

async function loadFile(file: File, autoDetect = false): Promise<void> {
  cancelDetection();
  parseController?.abort();
  const version = ++fileLoadVersion;
  const controller = new AbortController();
  detectionController = controller;
  isDetecting.value = autoDetect;
  try {
    const buffer = await file.slice(0, 65536).arrayBuffer();
    if (version !== fileLoadVersion) return;
    const preview = new Uint8Array(buffer);
    if (
      !autoDetect &&
      selectedExample.value?.schemaOnly &&
      selectedExample.value.extension &&
      definition.value === selectedExample.value.definition
    ) {
      const schema = {
        ...schemaForFile(selectedExample.value.extension, preview),
        id: selectedExample.value.id,
        schemaOnly: true,
      };
      selectedExample.value = schema;
      definition.value = schema.definition;
      resetCount.value += 1;
    }
    if (autoDetect) {
      let schema = rawFileSchema(preview);
      try {
        const detected = await detectFile(file, controller.signal);
        if (version !== fileLoadVersion) return;
        if (detected) {
          schema = schemaForFile(detected.ext, preview);
          const coverage = schemaProfiles[detected.ext]!.coverage;
          detectionMessage.value = `${detected.ext.toUpperCase()} detected · ${coverage === "prefix" ? "Prefix only" : "Schema loaded"}`;
        } else {
          detectionMessage.value = "Type not recognized. Choose a schema or inspect the raw bytes.";
        }
      } catch (error) {
        if (controller.signal.aborted || version !== fileLoadVersion) return;
        detectionMessage.value =
          error instanceof Error ? error.message : "Detection failed. Raw bytes are available.";
      }
      selectedExample.value = schema;
      definition.value = schema.definition;
      resetCount.value += 1;
    }
    // A parse may have started while detection was reading the new file.
    parseController?.abort();
    bytes.value = new Uint8Array(buffer);
    loadedFileName.value = file.name;
    fileSource.value = file;
    result.value = null;
    selectedDebugIndices.value = new Set();
    focusPath.value = null;
  } catch {
    if (version !== fileLoadVersion) return;
    result.value = parseFailure(
      "The selected file could not be read. Check that it is still available and try loading it again.",
      "file-read-failed",
    );
  } finally {
    if (version === fileLoadVersion) isDetecting.value = false;
  }
}

const { open: openFileDialog, onChange: onFileDialogChange } = useFileDialog({
  multiple: false,
  accept: "*",
});
onFileDialogChange((files) => {
  const file = files?.item(0);
  if (file) {
    loadFile(file);
  }
});

const { open: openDetectionDialog, onChange: onDetectionDialogChange } = useFileDialog({
  multiple: false,
  accept: "*",
});
onDetectionDialogChange((files) => {
  const file = files?.item(0);
  if (file) void loadFile(file, true);
});

async function runParse(options: ParseWithDebugOptions): Promise<void> {
  parseController?.abort();
  const controller = new AbortController();
  parseController = controller;
  isRunning.value = true;
  selectedDebugIndices.value = new Set();
  focusPath.value = null;
  try {
    if (
      selectedExample.value?.id === "zip" &&
      definition.value === selectedExample.value.definition
    ) {
      const header = fileSource.value
        ? new Uint8Array(await fileSource.value.slice(0, 131100).arrayBuffer())
        : bytes.value;
      if (controller.signal.aborted) return;
      const failure = validateZipHeader(header);
      if (failure) {
        result.value = failure;
        return;
      }
    }
    const parsed = await parseSourceWithDebug(definition.value, fileSource.value ?? bytes.value, {
      ...options,
      signal: controller.signal,
    });
    if (!controller.signal.aborted) result.value = parsed;
  } catch (error) {
    if (controller.signal.aborted) return;
    result.value = parseFailure(
      error instanceof Error ? error.message : String(error),
      "operation-failed",
    );
  } finally {
    if (parseController === controller) isRunning.value = false;
  }
}

function cancelParse(): void {
  parseController?.abort();
  result.value = parseFailure("Parsing was cancelled.", "cancelled");
}

function handleBytesEdited(next: Uint8Array): void {
  parseController?.abort();
  bytes.value = next;
  result.value = null;
  selectedDebugIndices.value = new Set();
  focusPath.value = null;
}

function handleSourceEdited(next: Blob): void {
  parseController?.abort();
  fileSource.value = next;
  result.value = null;
  selectedDebugIndices.value = new Set();
  focusPath.value = null;
}

function handleByteClick(offset: number): void {
  const index = findDebugEntryIndexByOffset(debugData.value, offset);
  if (index === -1) {
    selectedDebugIndices.value = new Set();
    focusPath.value = null;
    return;
  }
  const path = tokenizePath(debugData.value[index]!.DebugStackString);
  // Select every entry sharing this leaf's path too, not just the clicked one: a scalar array (e.g.
  // uint16 e_res[4]) records every element under the exact same un-indexed path, so one clicked byte
  // should still activate the whole array, matching what a JSON-side click on it already does.
  selectedDebugIndices.value = new Set(findDebugEntryIndicesByPath(debugData.value, path));
  focusPath.value = path;
}

function handleSelectPath(path: string[] | null): void {
  selectedDebugIndices.value = path
    ? new Set(findDebugEntryIndicesByPath(debugData.value, path))
    : new Set();
}

/**
 * Lays out the three panels side by side by default (matching the previous fixed 3-column look), but as
 * real dockview groups: the user can resize, retab (drag one onto another to combine), or rearrange them
 * freely from here on - dockview owns the layout once created, this only sets its starting shape.
 *
 * Each panel's `params` carries the actual refs/computeds/callbacks by reference (not their current
 * values), so the host components (SchemaPanelHost etc.) stay reactive indefinitely from this one call -
 * no dockview `updateParameters()` calls are needed as App.vue's own state changes later.
 */
function onDockviewReady(event: DockviewReadyEvent): void {
  event.api.addPanel({
    id: "schema",
    component: "schema",
    title: "Schema",
    params: {
      definition,
      disabled: schemaDisabled,
      running: isRunning,
      selectedExample,
      resetCount,
      onRun: runParse,
    },
  });
  event.api.addPanel({
    id: "binary",
    component: "binary",
    title: "Binary Data",
    position: { direction: "right", referencePanel: "schema" },
    params: {
      bytes,
      source: fileSource,
      debugData,
      selectedIndices: selectedDebugIndices,
      onBytesEdited: handleBytesEdited,
      onSourceEdited: handleSourceEdited,
      onByteClick: handleByteClick,
      onFileDropped: loadFile,
      onLoadFile: () => openFileDialog(),
    },
  });
  event.api.addPanel({
    id: "result",
    component: "result",
    title: "Result",
    position: { direction: "right", referencePanel: "binary" },
    params: {
      result,
      focusPath,
      onSelectPath: handleSelectPath,
    },
  });
}

onMounted(async () => {
  try {
    await initWasm();
    if (!isLoaded()) {
      throw new Error("WASM finished loading without usable exports.");
    }
    wasmVersion.value = getVersion();
    wasmStatus.value = "ready";
  } catch (error) {
    wasmStatus.value = "error";
    wasmError.value = error instanceof Error ? error.message : String(error);
  }
});
</script>

<template>
  <header class="top-bar">
    <div class="top-bar-left">
      <h1>CStructSharp Binary Inspector</h1>
      <nav class="header-links" aria-label="Project links">
        <a href="https://vvollers.github.io/cstructsharp/">
          <svg
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="1.8"
            aria-hidden="true"
          >
            <path d="m3 10 9-7 9 7M5 9v12h5v-7h4v7h5V9" />
          </svg>
          Home
        </a>
        <a
          href="https://github.com/vvollers/cstructsharp"
          target="_blank"
          rel="noopener noreferrer"
        >
          <svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
            <path
              d="M12 .75a11.25 11.25 0 0 0-3.56 21.92c.56.1.77-.24.77-.54v-2.1c-3.13.68-3.79-1.33-3.79-1.33-.51-1.3-1.25-1.65-1.25-1.65-1.02-.7.08-.69.08-.69 1.13.08 1.72 1.16 1.72 1.16 1 1.72 2.63 1.22 3.27.93.1-.73.39-1.22.71-1.5-2.5-.28-5.13-1.25-5.13-5.56 0-1.23.44-2.23 1.16-3.02-.12-.28-.5-1.43.11-2.98 0 0 .95-.3 3.1 1.15A10.8 10.8 0 0 1 12 6.16c.96 0 1.92.13 2.82.38 2.15-1.45 3.1-1.15 3.1-1.15.61 1.55.23 2.7.11 2.98.72.79 1.16 1.79 1.16 3.02 0 4.32-2.63 5.28-5.14 5.56.4.35.76 1.04.76 2.1v3.08c0 .3.2.65.78.54A11.25 11.25 0 0 0 12 .75Z"
            />
          </svg>
          GitHub
        </a>
        <a
          href="https://vvollers.github.io/cstructsharp/docs/"
          target="_blank"
          rel="noopener noreferrer"
        >
          <svg
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="1.8"
            aria-hidden="true"
          >
            <path d="M12 5v16M12 5C9 3 5 3 2 4v15c3-1 7-1 10 2 3-3 7-3 10-2V4c-3-1-7-1-10 1Z" />
          </svg>
          Documentation
        </a>
      </nav>
      <button v-if="isRunning" class="btn cancel-parse-button" type="button" @click="cancelParse">
        Cancel parse
      </button>
    </div>
    <span class="file-name">
      {{
        loadedFileName ??
        `${selectedExample?.schemaOnly ? "Load a file" : "Sample data"} · ${selectedExample?.title ?? "New schema"}`
      }}
    </span>
    <div
      class="status-badge"
      :class="{ ready: wasmStatus === 'ready', error: wasmStatus === 'error' }"
    >
      <span v-if="wasmStatus === 'loading'">Loading WebAssembly…</span>
      <span v-else-if="wasmStatus === 'ready'">Ready · {{ wasmVersion }}</span>
      <span v-else>Unavailable · {{ wasmError }}</span>
    </div>
  </header>
  <main class="workspace">
    <ExampleList
      :examples="schemaCatalog"
      :selected-id="selectedExample?.id ?? null"
      :selected-extension="selectedExample?.extension"
      :detecting="isDetecting"
      :detection-message="detectionMessage"
      @detect="openDetectionDialog()"
      @select="selectExample"
      @new="startNew"
    />
    <div class="dock-area">
      <DockviewVue
        class="dock"
        style="width: 100%; height: 100%"
        :theme="themeVisualStudio"
        :components="dockComponents"
        @ready="onDockviewReady"
      />
    </div>
  </main>
</template>

<style scoped>
.top-bar {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1fr) minmax(0, 1fr);
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  padding: 10px 20px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.08);
  background: var(--color-bg-secondary);
  flex-shrink: 0;
}
.top-bar-left {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 14px;
  min-width: 0;
}
.top-bar h1 {
  margin: 0;
  font-size: 15px;
  white-space: nowrap;
}
.header-links {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px;
  font-size: 12px;
}
.header-links a {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 5px 9px;
  border: 1px solid #4b6173;
  border-radius: var(--radius-sm);
  background: var(--color-bg-tertiary);
  color: #9cdcfe;
  font-weight: 500;
  text-decoration: none;
}
.header-links a:hover,
.header-links a:focus-visible {
  border-color: #75beff;
  background: #193b54;
  color: #ffffff;
}
.header-links svg {
  width: 16px;
  height: 16px;
  flex-shrink: 0;
}
.cancel-parse-button {
  padding: 7px 16px;
  font-size: 12px;
  border-radius: var(--radius-sm);
  background: var(--color-bg-tertiary);
  color: var(--color-text);
  border: 1px solid rgba(255, 255, 255, 0.12);
  cursor: pointer;
  text-transform: none;
  letter-spacing: normal;
}
.cancel-parse-button:hover {
  border-color: var(--color-accent);
}
.file-name {
  min-width: 0;
  text-align: center;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  color: var(--color-text-muted);
  font-size: 12px;
  font-family: var(--font-mono);
}
.status-badge {
  justify-self: end;
  flex-shrink: 0;
  padding: 6px 14px;
  border-radius: 999px;
  font-size: 12px;
  font-weight: 600;
  background: var(--color-bg-tertiary);
  color: var(--color-text-muted);
}
.status-badge.ready {
  color: var(--color-success);
}
.status-badge.error {
  color: var(--color-error);
}
@media (max-width: 1100px) {
  .top-bar {
    grid-template-columns: minmax(0, 1fr) auto;
  }
  .file-name {
    grid-column: 1 / -1;
    grid-row: 2;
  }
  .status-badge {
    grid-column: 2;
    grid-row: 1;
  }
}
.workspace {
  display: flex;
  flex: 1;
  min-height: 0;
}
.dock-area {
  flex: 1;
  min-width: 0;
  min-height: 0;
}
</style>
