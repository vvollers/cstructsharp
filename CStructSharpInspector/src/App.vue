<script setup lang="ts">
import { onMounted, ref, shallowRef } from "vue";
import { useFileDialog } from "@vueuse/core";

import ExampleList from "./components/ExampleList.vue";
import SchemaPanel from "./components/SchemaPanel.vue";
import BinaryPanel from "./components/BinaryPanel.vue";
import ResultPanel from "./components/ResultPanel.vue";
import { formats, type FormatExample } from "./formats";
import { findDebugEntryByPath, tokenizePath } from "./debug-path";
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
const highlightRange = ref<{ start: number; end: number } | null>(null);
const focusPath = ref<string[] | null>(null);

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
  highlightRange.value = null;
  focusPath.value = null;
  resetCount.value += 1;
}

function startNew(): void {
  selectedExample.value = null;
  definition.value = "struct root {\n    uint8 value;\n};";
  loadedFileName.value = null;
  bytes.value = new Uint8Array();
  result.value = null;
  highlightRange.value = null;
  focusPath.value = null;
  resetCount.value += 1;
}

function loadFile(file: File): void {
  void file.arrayBuffer().then((buffer) => {
    bytes.value = new Uint8Array(buffer);
    loadedFileName.value = file.name;
    result.value = null;
    highlightRange.value = null;
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
  highlightRange.value = null;
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
  const debugData = result.value?.Success ? result.value.DebugData : [];
  const entry = debugData.find((item) => offset >= item.CurPos && offset < item.EndPos);
  focusPath.value = entry ? tokenizePath(entry.DebugStackString) : null;
}

function handleSelectPath(path: string[] | null): void {
  if (!path || !result.value?.Success) {
    highlightRange.value = null;
    return;
  }
  const entry = findDebugEntryByPath(result.value.DebugData, path);
  highlightRange.value = entry ? { start: entry.CurPos, end: entry.EndPos } : null;
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
    <SchemaPanel
      :key="`${selectedExample?.id ?? 'new'}-${resetCount}`"
      v-model:definition="definition"
      :disabled="wasmStatus !== 'ready' || isRunning"
      :running="isRunning"
      :initial-root-type="selectedExample?.rootType"
      :initial-aligned="selectedExample?.parserOptions.aligned"
      :initial-little-endian="selectedExample?.parserOptions.littleEndian"
      :initial-pointer-size="selectedExample?.parserOptions.pointerSize"
      :initial-addressing-mode="selectedExample?.parserOptions.addressingMode"
      @run="runParse"
    />
    <BinaryPanel
      :bytes="bytes"
      :highlight-range="highlightRange"
      @update:bytes="handleBytesEdited"
      @byte-click="handleByteClick"
      @file-dropped="loadFile"
    />
    <ResultPanel :result="result" :focus-path="focusPath" @select-path="handleSelectPath" />
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
  display: grid;
  grid-template-columns: minmax(220px, 260px) minmax(0, 1fr) minmax(0, 1fr) minmax(0, 1fr);
  flex: 1;
  min-height: 0;
}
</style>
