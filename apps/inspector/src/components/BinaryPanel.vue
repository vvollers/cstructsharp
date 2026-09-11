<script setup lang="ts">
import { computed, nextTick, ref, shallowRef, watch } from "vue";
import { useDropZone } from "@vueuse/core";
import {
  DEFAULT_ASCII_CATEGORY_CELL_CLASS_RESOLVER,
  VueHex,
  type VueHexWindowRequest,
  type VueHexEditIntent,
  type VueHexSearchRequest,
} from "vuehex";
import { editBlob, searchBlob } from "../blob-hex";

import { computeFieldGroups, findDebugEntryIndexByOffset } from "../debug-path";
import type { DebugDataItem } from "../wasm/cstruct-contract";

const props = defineProps<{
  bytes: Uint8Array;
  source: Blob | null;
  debugData: DebugDataItem[];
  selectedIndices: ReadonlySet<number>;
}>();

const fieldGroups = computed(() => computeFieldGroups(props.debugData));

const emit = defineEmits<{
  "update:bytes": [bytes: Uint8Array];
  "update:source": [source: Blob];
  "byte-click": [offset: number];
  "file-dropped": [file: File];
  "load-file": [];
}>();

const dropZone = ref<HTMLElement | null>(null);
const hexEditor = ref<InstanceType<typeof VueHex> | null>(null);
const windowBytes = shallowRef(new Uint8Array());
const windowOffset = ref(0);
const windowError = ref("");
const jumpOffset = ref("");
let windowVersion = 0;
const undo: Blob[] = [];
const redo: Blob[] = [];
let editedSource: Blob | null = null;

async function loadWindow({ offset, length }: VueHexWindowRequest): Promise<void> {
  const source = props.source;
  const version = ++windowVersion;
  if (!source) return;
  try {
    const start = Math.max(0, Math.min(offset, source.size));
    const bytes = new Uint8Array(
      await source
        .slice(start, start + Math.min(Math.max(length, 65536), 1024 * 1024))
        .arrayBuffer(),
    );
    if (version !== windowVersion || source !== props.source) return;
    windowOffset.value = start;
    windowBytes.value = bytes;
    windowError.value = "";
  } catch {
    if (version === windowVersion)
      windowError.value = "Could not read this file range. Reload the file and try again.";
  }
}

watch(
  () => props.source,
  (source) => {
    if (source !== editedSource) {
      undo.length = 0;
      redo.length = 0;
      windowOffset.value = 0;
    }
    if (source) void loadWindow({ offset: windowOffset.value, length: 65536 });
    else windowVersion++;
  },
  { immediate: true },
);

function handleEdit(intent: VueHexEditIntent): void {
  const source = props.source;
  if (!source) return;
  let next: Blob | undefined;
  if (intent.kind === "undo") {
    next = undo.pop();
    if (next) redo.push(source);
  } else if (intent.kind === "redo") {
    next = redo.pop();
    if (next) undo.push(source);
  } else {
    next = editBlob(source, intent);
    undo.push(source);
    if (undo.length > 100) undo.shift();
    redo.length = 0;
  }
  if (next) {
    editedSource = next;
    emit("update:source", next);
  }
}

function searchSource(request: VueHexSearchRequest) {
  return searchBlob(props.source!, request);
}

async function jumpToByte(): Promise<void> {
  const text = jumpOffset.value.trim();
  const offset = Number(text);
  if (
    !/^(?:[0-9]+|0x[0-9a-f]+)$/i.test(text) ||
    !Number.isSafeInteger(offset) ||
    offset < 0 ||
    offset >= byteCount.value
  ) {
    windowError.value = "Enter a decimal or 0x hexadecimal byte offset within the file.";
    return;
  }
  windowError.value = "";
  hexEditor.value?.scrollToByte?.(offset);
  // Crossing a virtual chunk changes its scroll height; apply the position after Vue renders it.
  await nextTick();
  hexEditor.value?.scrollToByte?.(offset);
}

const { isOverDropZone } = useDropZone(dropZone, {
  multiple: false,
  onDrop(files) {
    const file = files?.[0];
    if (file) {
      emit("file-dropped", file);
    }
  },
});

function handleModelUpdate(next: Uint8Array): void {
  if (!props.source) emit("update:bytes", next);
}

/**
 * Colors every parsed field's byte range with one of 6 cycling background colors as soon as a parse
 * succeeds - a structure map, not just a single highlight - and dims everything except the selected
 * field once one is selected (matching the range-N/active/dim pattern apps/workshop's own
 * ResultPanel.vue already uses for its field-map/hex cross-highlight). Combined below with vuehex's own
 * built-in DEFAULT_ASCII_CATEGORY_CELL_CLASS_RESOLVER (foreground byte-value coloring - digits,
 * upper/lowercase letters, control/high-bit/null bytes) via its multi-resolver array support, so this
 * only needs to return the field background classes, not reimplement byte-category coloring itself.
 *
 * The background color is keyed by field GROUP (see computeFieldGroups), not by the raw DebugData entry
 * index, so a whole array highlights as one block instead of every element getting its own color.
 * Selection (active/dim) targets a *set* of entry indices, not just one: a JSON click on a struct array's
 * container needs every descendant leaf active, and a scalar array needs every entry sharing its
 * duplicated un-indexed path active (see findDebugEntryIndicesByPath) - a single index could never
 * represent either case.
 */
function fieldClassForByte(payload: { index: number }): string[] {
  const entryIndex = findDebugEntryIndexByOffset(props.debugData, payload.index);
  if (entryIndex === -1) {
    return props.selectedIndices.size === 0 ? [] : ["field-dim"];
  }
  const groupIndex = fieldGroups.value[entryIndex]!;
  const classes = [`field-range-${groupIndex % 6}`];
  if (props.selectedIndices.size > 0) {
    classes.push(props.selectedIndices.has(entryIndex) ? "field-active" : "field-dim");
  }
  return classes;
}

function handleByteClick(event: { index: number }): void {
  emit("byte-click", event.index);
}

const byteCount = computed(() => props.source?.size ?? props.bytes.length);

defineExpose({
  scrollToByte(offset: number): void {
    hexEditor.value?.scrollToByte?.(offset);
  },
});
</script>

<template>
  <section ref="dropZone" class="binary-panel" :class="{ 'drop-active': isOverDropZone }">
    <div class="panel-topbar">
      <button class="btn btn-primary load-file-button" type="button" @click="emit('load-file')">
        <svg
          class="button-icon"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="1.8"
          stroke-linejoin="round"
          aria-hidden="true"
        >
          <path d="M3 10V5h6l2 2h9v3M3 10h19l-3 10H5L3 10Z" />
        </svg>
        Load file
      </button>
      <form v-if="source" class="byte-jump" @submit.prevent="jumpToByte">
        <input v-model="jumpOffset" aria-label="Go to byte" placeholder="Byte offset / 0x…" />
        <button type="submit">Go</button>
      </form>
      <span class="byte-count">{{ byteCount.toLocaleString() }} bytes</span>
    </div>
    <!-- data-debug-count/-selected are unused styling hooks; reading debugData/selectedIndices here (not
         just inside fieldClassForByte's closure) makes this template re-render when either changes, so
         the cell-class-for-byte array literal below is rebuilt with a fresh identity and VueHex
         recomputes classes (a stable array reference would otherwise never be re-evaluated). -->
    <div
      class="hex-body"
      data-testid="binary-panel-hex"
      :data-debug-count="debugData.length"
      :data-debug-selected="selectedIndices.size"
    >
      <!-- Commit the window offset with its bytes after the asynchronous read, not on the request event. -->
      <VueHex
        ref="hexEditor"
        :model-value="source ? windowBytes : bytes"
        :data-mode="source ? 'window' : 'buffer'"
        :total-size="byteCount"
        :window-offset="source ? windowOffset : 0"
        @update:window-offset="() => {}"
        :search-provider="source ? searchSource : undefined"
        theme="dark"
        :editable="true"
        :cursor="true"
        :search="true"
        statusbar="bottom"
        :bytes-per-row="16"
        :cell-class-for-byte="[DEFAULT_ASCII_CATEGORY_CELL_CLASS_RESOLVER, fieldClassForByte]"
        aria-label="Binary data editor"
        @update:model-value="handleModelUpdate"
        @update-virtual-data="loadWindow"
        @edit="handleEdit"
        @byte-click="handleByteClick"
      />
    </div>
    <p v-if="isOverDropZone" class="drop-hint">Drop to load this file</p>
    <p v-if="windowError" role="alert">{{ windowError }}</p>
  </section>
</template>

<style scoped>
.binary-panel {
  display: grid;
  grid-template-rows: auto minmax(0, 1fr);
  height: 100%;
  min-height: 0;
  min-width: 0;
  position: relative;
}
.binary-panel.drop-active {
  outline: 2px dashed var(--color-accent);
  outline-offset: -2px;
}
.panel-topbar {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
  justify-content: flex-end;
  padding: 8px 12px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.08);
  background: var(--color-bg-secondary);
}
.load-file-button {
  padding: 5px 12px;
  font-size: 12px;
}
.byte-jump {
  display: flex;
  gap: 4px;
  margin-right: auto;
}
.byte-jump input {
  width: 140px;
  min-width: 0;
}
.byte-jump input,
.byte-jump button {
  padding: 4px;
  color: var(--color-text);
  background: var(--color-bg-tertiary);
  border: 1px solid rgba(255, 255, 255, 0.15);
  border-radius: 3px;
}
.byte-count {
  margin-left: auto;
  font-size: 11px;
  color: var(--color-text-muted);
}
.hex-body {
  min-height: 0;
  padding: 10px;
  overflow: hidden;
}
.hex-body :deep(.vuehex) {
  height: 100%;
  border: 1px solid rgba(255, 255, 255, 0.12);
  border-radius: var(--radius-sm);
}
/* Fixed whole-pixel rows keep virtual chunk coordinates stable when a new window arrives. */
.hex-body :deep(.vuehex-table) {
  border-collapse: separate;
  border-spacing: 0;
}
.hex-body :deep(.vuehex-table td),
.hex-body :deep(.vuehex-table th) {
  line-height: 20px;
  padding-top: 7px;
  padding-bottom: 7px;
  border-top: 0;
  border-bottom: 0;
}
/* Fill the spacing between hex cells so a field reads as a band, not a row of boxes.
   Preserve the editor's wider middle-column gutter and its original cell widths. */
.hex-body :deep(.vuehex-byte) {
  min-width: 2.5ch;
  margin-inline: 0;
  padding-inline: 0.25ch;
}
.hex-body :deep(.vuehex-byte--column-start) {
  margin-left: var(--vuehex-mid-column-gutter);
}
.hex-body :deep(.field-range-0) {
  --field-color: 0, 212, 255;
}
.hex-body :deep(.field-range-1) {
  --field-color: 0, 255, 136;
}
.hex-body :deep(.field-range-2) {
  --field-color: 255, 184, 0;
}
.hex-body :deep(.field-range-3) {
  --field-color: 186, 104, 255;
}
.hex-body :deep(.field-range-4) {
  --field-color: 255, 105, 180;
}
.hex-body :deep(.field-range-5) {
  --field-color: 64, 224, 208;
}
.hex-body :deep([class*="field-range-"]) {
  background-color: rgba(var(--field-color), 0.14);
}
.hex-body :deep(.field-active) {
  background-color: rgba(var(--field-color), 0.28);
  box-shadow: inset 0 -2px rgba(var(--field-color), 0.85);
  opacity: 1;
}
.hex-body :deep(.field-dim) {
  opacity: 0.5;
}
/* Byte selection remains separate from the parsed-field highlight. Only the editing
   cursor gets a full outline; a multi-byte selection stays a continuous soft band. */
.hex-body :deep(.vuehex-selected) {
  background-color: #365c82;
  color: #f4f8ff;
  box-shadow: inset 0 -2px #b8d8ff;
  outline: none;
  opacity: 1;
}
.hex-body :deep(.vuehex-cursor) {
  outline: 2px solid #dcecff;
  outline-offset: -2px;
  opacity: 1;
}
.drop-hint {
  position: absolute;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  margin: 0;
  background: #0009;
  color: var(--color-accent);
  font-weight: 600;
  pointer-events: none;
}
</style>
