<script setup lang="ts">
import { computed, ref, watch } from "vue";

import { VueHex } from "vuehex";

import type { DebugDataItem, InteropResult } from "../wasm/cstruct-contract";

interface DebugRange {
  end: number;
  index: number;
  start: number;
}

const props = defineProps<{
  bytes: Uint8Array;
  result: InteropResult | null;
}>();

const emit = defineEmits<{
  "bytes-edited": [bytes: Uint8Array];
}>();

const selectedRange = ref<number | null>(null);
const editorBytes = ref<Uint8Array<ArrayBufferLike>>(new Uint8Array());
watch(
  () => props.bytes,
  (bytes) => {
    editorBytes.value = bytes.slice();
  },
  { immediate: true },
);
const ranges = computed<DebugRange[]>(() =>
  (props.result?.DebugData ?? []).map((item, index) => ({
    index,
    start: Math.max(0, item.CurPos),
    end: Math.max(item.CurPos + 1, item.EndPos),
  })),
);
const parsedData = computed(() => {
  if (!props.result?.Data) {
    return null;
  }
  if (props.result.Operation !== "parse") {
    return props.result.Data;
  }
  try {
    return JSON.parse(props.result.Data) as unknown;
  } catch {
    return props.result.Data;
  }
});
function rangeFor(index: number): DebugRange | undefined {
  return ranges.value.find((range) => index >= range.start && index < range.end);
}

function byteClass(index: number): string[] {
  const range = rangeFor(index);
  if (!range) {
    return selectedRange.value === null ? [] : ["dim"];
  }
  const classes = [`range-${range.index % 6}`];
  if (selectedRange.value !== null) {
    classes.push(selectedRange.value === range.index ? "active" : "dim");
  }
  return classes;
}

function formatDebug(item: DebugDataItem): string {
  return `${item.DebugStackString || "value"} · ${item.Type} · ${item.Value ?? "null"} · bytes ${item.CurPos}–${Math.max(item.CurPos, item.EndPos - 1)}`;
}

function handleBytesEdited(bytes: Uint8Array): void {
  editorBytes.value = bytes;
  emit("bytes-edited", bytes);
}
</script>

<template>
  <section class="card result-panel" aria-live="polite">
    <h2>Result</h2>
    <p v-if="!result" class="placeholder">Run an operation to inspect its output.</p>
    <template v-else>
      <div class="result-status" :class="result.Success ? 'success' : 'error'">
        {{ result.Success ? `${result.Operation} completed` : result.Error?.Message }}
      </div>

      <dl v-if="!result.Success && result.Error" class="error-details">
        <div>
          <dt>Code</dt>
          <dd>{{ result.Error.Code }}</dd>
        </div>
        <div v-if="result.Error.Path">
          <dt>Path</dt>
          <dd>{{ result.Error.Path }}</dd>
        </div>
        <div v-if="result.Error.Offset !== null">
          <dt>Offset</dt>
          <dd>{{ result.Error.Offset }}</dd>
        </div>
      </dl>

      <template v-if="result.Success">
        <h3>{{ result.Operation === "parse" ? "Input bytes" : "Output bytes" }}</h3>
        <div v-if="editorBytes.length" class="binary-editor" data-testid="binary-editor">
          <VueHex
            v-model="editorBytes"
            data-mode="buffer"
            theme="dark"
            :editable="true"
            :cursor="true"
            :search="true"
            statusbar="bottom"
            :bytes-per-row="16"
            :cell-class-for-byte="(payload) => byteClass(payload.index)"
            @byte-click="selectedRange = rangeFor($event.index)?.index ?? null"
            @update:model-value="handleBytesEdited"
          />
          <p class="editor-hint">Click a byte to edit. Use Ctrl/Cmd+F to search.</p>
        </div>
        <p v-else class="placeholder">The operation produced no bytes.</p>

        <template v-if="result.DebugData.length">
          <h3>Field map</h3>
          <div class="debug-list">
            <button
              v-for="(item, index) in result.DebugData"
              :key="`${item.CurPos}-${index}`"
              type="button"
              :class="[`range-${index % 6}`, { active: selectedRange === index }]"
              @click="selectedRange = selectedRange === index ? null : index"
            >
              {{ formatDebug(item) }}
            </button>
          </div>
        </template>

        <h3>{{ result.Operation === "parse" ? "Parsed JSON" : "Base64" }}</h3>
        <pre>{{
          typeof parsedData === "string" ? parsedData : JSON.stringify(parsedData, null, 2)
        }}</pre>
      </template>
    </template>
  </section>
</template>

<style scoped>
.result-panel {
  display: grid;
  gap: 14px;
}

h2 {
  font-size: 20px;
}

h3 {
  color: var(--color-text-muted);
  font-size: 12px;
  letter-spacing: 0.04em;
  margin-top: 4px;
  text-transform: uppercase;
}

.placeholder {
  color: var(--color-text-muted);
}

.result-status {
  border-radius: var(--radius-sm);
  font-weight: 700;
  padding: 10px 12px;
}

.result-status.success {
  background: rgba(0, 255, 136, 0.08);
  color: var(--color-success);
}

.result-status.error {
  background: rgba(255, 71, 87, 0.08);
  color: var(--color-error);
}

.error-details {
  display: grid;
  gap: 6px;
}

.error-details div {
  display: flex;
  gap: 10px;
}

dt {
  color: var(--color-text-muted);
  width: 70px;
}

dd {
  font-family: var(--font-mono);
  overflow-wrap: anywhere;
}

pre {
  border: 1px solid rgba(255, 255, 255, 0.08);
  border-radius: var(--radius-sm);
  background: var(--color-bg-primary);
  overflow: auto;
  padding: 12px;
}

.binary-editor {
  display: grid;
  gap: 8px;
}

.binary-editor :deep(.vuehex) {
  height: 360px;
  border: 1px solid rgba(255, 255, 255, 0.08);
  border-radius: var(--radius-sm);
}

.editor-hint {
  color: var(--color-text-muted);
  font-size: 12px;
}

.debug-list {
  display: grid;
  gap: 5px;
}

.debug-list button {
  border: 0;
  border-left: 3px solid transparent;
  background: var(--color-bg-primary);
  color: var(--color-text);
  cursor: pointer;
  font-family: var(--font-mono);
  padding: 8px 10px;
  text-align: left;
}

:deep(.range-0) {
  background-color: rgba(0, 212, 255, 0.2) !important;
  border-color: #00d4ff !important;
}
:deep(.range-1) {
  background-color: rgba(0, 255, 136, 0.18) !important;
  border-color: #00ff88 !important;
}
:deep(.range-2) {
  background-color: rgba(255, 184, 0, 0.2) !important;
  border-color: #ffb800 !important;
}
:deep(.range-3) {
  background-color: rgba(186, 104, 255, 0.2) !important;
  border-color: #ba68ff !important;
}
:deep(.range-4) {
  background-color: rgba(255, 105, 180, 0.2) !important;
  border-color: #ff69b4 !important;
}
:deep(.range-5) {
  background-color: rgba(64, 224, 208, 0.2) !important;
  border-color: #40e0d0 !important;
}
:deep(.active) {
  outline: 2px solid white;
}
:deep(.dim) {
  opacity: 0.28;
}

pre {
  color: var(--color-text);
  font-size: 12px;
  max-height: 480px;
  white-space: pre-wrap;
  word-break: break-word;
}
</style>
