<script setup lang="ts">
import { computed, nextTick, ref, watch } from "vue";
import { useDropZone } from "@vueuse/core";
import { DEFAULT_ASCII_CATEGORY_CELL_CLASS_RESOLVER, VueHex } from "vuehex";
import { useBinarySource } from "../composables/useBinarySource";

import { computeFieldGroups, findDebugEntryIndexByOffset } from "../debug-path";
import type { DebugDataItem } from "../wasm/cstruct-contract";

const props = defineProps<{
  bytes: Uint8Array;
  source: Blob | null;
  debugData: DebugDataItem[];
  selectedIndices: ReadonlySet<number>;
}>();

const emit = defineEmits<{
  "update:bytes": [bytes: Uint8Array];
  "update:source": [source: Blob];
  "byte-click": [offset: number];
  "file-dropped": [file: File];
  "load-file": [];
}>();

const dropZone = ref<HTMLElement | null>(null);
const hexEditor = ref<InstanceType<typeof VueHex> | null>(null);
const jumpOffset = ref("");
const byteCount = computed(() => props.source?.size ?? props.bytes.length);
const { windowBytes, windowOffset, windowError, loadWindow, handleEdit, searchSource } =
  useBinarySource(
    () => props.source,
    (source) => emit("update:source", source),
  );

// The hex editor only draws a portion of the file. Scrolling far away can change that portion
// and its scroll height. Wait for Vue to update the page, then apply the position again.
async function scrollToByte(offset: number, isCurrent = () => true): Promise<void> {
  if (!isCurrent()) return;

  hexEditor.value?.scrollToByte?.(offset);
  await nextTick();

  if (isCurrent()) hexEditor.value?.scrollToByte?.(offset);
}

async function jumpToByte(): Promise<void> {
  const text = jumpOffset.value.trim();
  const offset = Number(text);

  // Accept decimal or 0x-prefixed addresses, and reject values outside the available bytes.
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
  await scrollToByte(offset);
}

// A JSON selection may refer to bytes far away from the visible window, especially through a
// pointer. Find the earliest selected byte and bring it into view.
watch(
  () => props.selectedIndices,
  async (indices, _previous, onCleanup) => {
    // If the selection changes while we wait for Vue, the old selection must not scroll us back.
    let current = true;
    onCleanup(() => {
      current = false;
    });

    let offset = Number.POSITIVE_INFINITY;
    for (const index of indices) {
      const entry = props.debugData[index];
      if (entry) offset = Math.min(offset, entry.CurPos);
    }

    if (!Number.isFinite(offset)) return;

    await nextTick();
    await scrollToByte(offset, () => current);
  },
);

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
  // Sample data is edited as a whole array. Loaded files use the separate Blob edit/history path.
  if (!props.source) emit("update:bytes", next);
}

// VueHex asks these functions which CSS classes to apply to each byte. This computed value reads
// the result and selection, so Vue builds a new function list whenever either changes. VueHex then
// knows to refresh the colors. Cache the field groups separately so clicks do not rebuild them.
const fieldGroups = computed(() => computeFieldGroups(props.debugData));
const cellClassResolvers = computed(() => {
  const entries = props.debugData;
  const selected = props.selectedIndices;
  const groups = fieldGroups.value;

  const fieldClassForByte = ({ index }: { index: number }): string[] => {
    const entry = findDebugEntryIndexByOffset(entries, index);
    if (entry === -1) return selected.size ? ["field-dim"] : [];

    // Give each field a repeating background color, then dim fields outside the selection.
    const classes = [`field-range-${groups[entry]! % 6}`];
    if (selected.size) classes.push(selected.has(entry) ? "field-active" : "field-dim");
    return classes;
  };

  return [DEFAULT_ASCII_CATEGORY_CELL_CLASS_RESOLVER, fieldClassForByte];
});

function handleByteClick(event: { index: number }): void {
  emit("byte-click", event.index);
}
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
    <div class="hex-body" data-testid="binary-panel-hex">
      <!-- A scroll request starts a read. useBinarySource updates windowBytes and windowOffset
           together once that read finishes, so the displayed addresses match the displayed bytes. -->
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
        :cell-class-for-byte="cellClassResolvers"
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
