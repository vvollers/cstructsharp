<script setup lang="ts">
import { computed, ref } from "vue";
import { useDropZone } from "@vueuse/core";
import { VueHex } from "vuehex";

const props = defineProps<{
  bytes: Uint8Array;
  highlightRange: { start: number; end: number } | null;
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

function cellClassForByte(payload: { index: number }): string[] {
  const range = props.highlightRange;
  return range && payload.index >= range.start && payload.index < range.end
    ? ["field-highlight"]
    : [];
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
    <!-- data-highlight-start/-end are unused styling hooks; reading highlightRange here (not just
         inside cellClassForByte's closure) makes this template re-render when it changes, so the
         inline cell-class-for-byte arrow below gets a fresh identity and VueHex recomputes classes. -->
    <div
      class="hex-body"
      data-testid="binary-panel-hex"
      :data-highlight-start="highlightRange?.start"
      :data-highlight-end="highlightRange?.end"
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
        :cell-class-for-byte="(payload) => cellClassForByte(payload)"
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
.hex-body :deep(.field-highlight) {
  background: var(--color-accent-glow);
  outline: 1px solid var(--color-accent);
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
