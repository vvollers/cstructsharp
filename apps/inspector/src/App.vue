<script setup lang="ts">
import { computed, onMounted, ref, shallowRef } from "vue";
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
import {
  findDebugEntryIndexByOffset,
  findDebugEntryIndicesByPath,
  tokenizePath,
} from "./debug-path";
import {
  initWasm,
  isLoaded,
  getVersion,
  parseWithDebug,
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
  selectedExample.value = null;
  definition.value = "struct root {\n    uint8 value;\n};";
  loadedFileName.value = null;
  bytes.value = new Uint8Array();
  result.value = null;
  selectedDebugIndices.value = new Set();
  focusPath.value = null;
  resetCount.value += 1;
}

function loadFile(file: File): void {
  void file.arrayBuffer().then((buffer) => {
    bytes.value = new Uint8Array(buffer);
    loadedFileName.value = file.name;
    result.value = null;
    selectedDebugIndices.value = new Set();
    focusPath.value = null;
  });
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

async function runParse(options: ParseWithDebugOptions): Promise<void> {
  isRunning.value = true;
  selectedDebugIndices.value = new Set();
  focusPath.value = null;
  try {
    result.value = parseWithDebug(definition.value, bytes.value, options);
  } finally {
    isRunning.value = false;
  }
}

function handleBytesEdited(next: Uint8Array): void {
  bytes.value = next;
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
      debugData,
      selectedIndices: selectedDebugIndices,
      onBytesEdited: handleBytesEdited,
      onByteClick: handleByteClick,
      onFileDropped: loadFile,
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
      <button class="btn load-file-button" type="button" @click="openFileDialog()">
        Load file
      </button>
      <span class="file-name">
        {{ loadedFileName ?? `Sample data · ${selectedExample?.title ?? "New schema"}` }}
      </span>
    </div>
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
      :examples="formats"
      :selected-id="selectedExample?.id ?? null"
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
  display: flex;
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
  align-items: center;
  gap: 14px;
  min-width: 0;
}
.top-bar h1 {
  margin: 0;
  font-size: 15px;
  white-space: nowrap;
}
.load-file-button {
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
.load-file-button:hover {
  border-color: var(--color-accent);
}
.file-name {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  color: var(--color-text-muted);
  font-size: 12px;
  font-family: var(--font-mono);
}
.status-badge {
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
