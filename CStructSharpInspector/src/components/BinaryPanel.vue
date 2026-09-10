<script setup lang="ts">
import { computed, ref } from "vue";
import { useDropZone } from "@vueuse/core";
import { DEFAULT_ASCII_CATEGORY_CELL_CLASS_RESOLVER, VueHex } from "vuehex";

import { computeFieldGroups, findDebugEntryIndexByOffset } from "../debug-path";
import type { DebugDataItem } from "../wasm/cstruct-contract";

const props = defineProps<{
  bytes: Uint8Array;
  debugData: DebugDataItem[];
  selectedIndices: ReadonlySet<number>;
}>();

const fieldGroups = computed(() => computeFieldGroups(props.debugData));

const emit = defineEmits<{
  "update:bytes": [bytes: Uint8Array];
  "byte-click": [offset: number];
  "file-dropped": [file: File];
}>();

const dropZone = ref<HTMLElement | null>(null);
const hexEditor = ref<InstanceType<typeof VueHex> | null>(null);

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
  emit("update:bytes", next);
}

/**
 * Colors every parsed field's byte range with one of 6 cycling background colors as soon as a parse
 * succeeds - a structure map, not just a single highlight - and dims everything except the selected
 * field once one is selected (matching the range-N/active/dim pattern CStructSharpWeb's own
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

const byteCount = computed(() => props.bytes.length);

defineExpose({
  scrollToByte(offset: number): void {
    hexEditor.value?.scrollToByte?.(offset);
  },
});
</script>

<template>
  <section ref="dropZone" class="binary-panel" :class="{ 'drop-active': isOverDropZone }">
    <div class="panel-topbar">
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
      <VueHex
        ref="hexEditor"
        :model-value="bytes"
        data-mode="buffer"
        theme="dark"
        :editable="true"
        :cursor="true"
        :search="true"
        statusbar="bottom"
        :bytes-per-row="16"
        :cell-class-for-byte="[DEFAULT_ASCII_CATEGORY_CELL_CLASS_RESOLVER, fieldClassForByte]"
        aria-label="Binary data editor"
        @update:model-value="handleModelUpdate"
        @byte-click="handleByteClick"
      />
    </div>
    <p v-if="isOverDropZone" class="drop-hint">Drop to load this file</p>
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
  align-items: center;
  justify-content: flex-end;
  padding: 8px 12px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.08);
  background: var(--color-bg-secondary);
}
.byte-count {
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
