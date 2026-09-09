<script setup lang="ts">
import { computed, ref } from "vue";
import { useDropZone } from "@vueuse/core";
import { DEFAULT_ASCII_CATEGORY_CELL_CLASS_RESOLVER, VueHex } from "vuehex";

import { findDebugEntryIndexByOffset } from "../debug-path";
import type { DebugDataItem } from "../wasm/cstruct-contract";

const props = defineProps<{
  bytes: Uint8Array;
  debugData: DebugDataItem[];
  selectedIndex: number | null;
}>();

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
 * only needs to return the background/outline classes, not reimplement byte-category coloring itself.
 */
function fieldClassForByte(payload: { index: number }): string[] {
  const fieldIndex = findDebugEntryIndexByOffset(props.debugData, payload.index);
  if (fieldIndex === -1) {
    return props.selectedIndex === null ? [] : ["field-dim"];
  }
  const classes = [`field-range-${fieldIndex % 6}`];
  if (props.selectedIndex !== null) {
    classes.push(props.selectedIndex === fieldIndex ? "field-active" : "field-dim");
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
      <h2>Binary data</h2>
      <span class="byte-count">{{ byteCount.toLocaleString() }} bytes</span>
    </div>
    <!-- data-debug-count/-selected are unused styling hooks; reading debugData/selectedIndex here (not
         just inside fieldClassForByte's closure) makes this template re-render when either changes, so
         the cell-class-for-byte array literal below is rebuilt with a fresh identity and VueHex
         recomputes classes (a stable array reference would otherwise never be re-evaluated). -->
    <div
      class="hex-body"
      data-testid="binary-panel-hex"
      :data-debug-count="debugData.length"
      :data-debug-selected="selectedIndex"
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
  border-right: 1px solid rgba(255, 255, 255, 0.08);
  position: relative;
}
.binary-panel.drop-active {
  outline: 2px dashed var(--color-accent);
  outline-offset: -2px;
}
.panel-topbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 10px 14px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.08);
  background: var(--color-bg-secondary);
}
.panel-topbar h2 {
  margin: 0;
  font-size: 13px;
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
.hex-body :deep(.field-range-0) {
  background-color: rgba(0, 212, 255, 0.22) !important;
  outline: 1px solid #00d4ff;
}
.hex-body :deep(.field-range-1) {
  background-color: rgba(0, 255, 136, 0.2) !important;
  outline: 1px solid #00ff88;
}
.hex-body :deep(.field-range-2) {
  background-color: rgba(255, 184, 0, 0.22) !important;
  outline: 1px solid #ffb800;
}
.hex-body :deep(.field-range-3) {
  background-color: rgba(186, 104, 255, 0.22) !important;
  outline: 1px solid #ba68ff;
}
.hex-body :deep(.field-range-4) {
  background-color: rgba(255, 105, 180, 0.22) !important;
  outline: 1px solid #ff69b4;
}
.hex-body :deep(.field-range-5) {
  background-color: rgba(64, 224, 208, 0.22) !important;
  outline: 1px solid #40e0d0;
}
.hex-body :deep(.field-active) {
  outline-width: 2px !important;
  filter: brightness(1.35);
}
.hex-body :deep(.field-dim) {
  opacity: 0.32;
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
