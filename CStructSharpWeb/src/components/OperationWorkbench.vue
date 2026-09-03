<script setup lang="ts">
import { ref, watch } from "vue";

import { VueHex } from "vuehex";

import type {
  ParseWithDebugOptions,
  SerializeOptions,
  UpdateOptions,
} from "../wasm/cstruct-contract";
import { hexToBytes } from "../wasm/cstruct-wasm";

export type WorkbenchOperation = "parse" | "serialize" | "update";

export interface WorkbenchRequest {
  operation: WorkbenchOperation;
  definition: string;
  binaryHex: string;
  jsonValue: string;
  path: string;
  options: ParseWithDebugOptions & SerializeOptions & UpdateOptions;
}

const props = defineProps<{
  binaryHex: string;
  definition: string;
  disabled: boolean;
  initialAligned?: boolean;
  initialLittleEndian?: boolean;
  initialPointerSize?: number;
  initialRootType?: string | null;
}>();

const emit = defineEmits<{
  run: [request: WorkbenchRequest];
}>();

const operation = ref<WorkbenchOperation>("parse");
const definition = ref(props.definition);
const binaryHex = ref(props.binaryHex);
const binaryEditorBytes = ref<Uint8Array<ArrayBufferLike>>(parseBinaryHex(props.binaryHex));
const jsonValue = ref('{\n  "value": 42\n}');
const path = ref(props.initialRootType ? `${props.initialRootType}.value` : "root.value");
const rootTypeName = ref(props.initialRootType ?? "");
const aligned = ref(props.initialAligned ?? false);
const pointerSize = ref(props.initialPointerSize ?? 8);
const endian = ref<"little" | "big">(props.initialLittleEndian === false ? "big" : "little");
const addressingMode = ref<"Absolute" | "Relative">("Absolute");
const origin = ref("0");
const dereferencePointers = ref(true);
const maxArrayElements = ref(1_000_000);
const maxStringBytes = ref(16 * 1024 * 1024);
const maxTotalBytes = ref(64 * 1024 * 1024);
const maxNestingDepth = ref(256);

watch(
  () => props.binaryHex,
  (value) => {
    binaryHex.value = value;
    binaryEditorBytes.value = parseBinaryHex(value);
  },
);

function parseBinaryHex(value: string): Uint8Array {
  try {
    return hexToBytes(value);
  } catch {
    return new Uint8Array();
  }
}

function bytesToHex(bytes: Uint8Array): string {
  return Array.from(bytes, (byte) => byte.toString(16).padStart(2, "0")).join(" ");
}

function handleBinaryEdited(bytes: Uint8Array): void {
  binaryEditorBytes.value = bytes;
  binaryHex.value = bytesToHex(bytes);
}

function submit(): void {
  emit("run", {
    operation: operation.value,
    definition: definition.value,
    binaryHex: binaryHex.value,
    jsonValue: jsonValue.value,
    path: path.value,
    options: {
      rootTypeName: rootTypeName.value.trim() || null,
      aligned: aligned.value,
      pointerSize: pointerSize.value,
      littleEndian: endian.value === "little",
      addressingMode: addressingMode.value,
      origin: origin.value,
      dereferencePointers: dereferencePointers.value,
      allowPointerDereference: dereferencePointers.value,
      maxArrayElements: maxArrayElements.value,
      maxStringBytes: maxStringBytes.value,
      maxTotalBytesRead: maxTotalBytes.value,
      maxTotalBytesWritten: maxTotalBytes.value,
      maxTraversalBytesRead: maxTotalBytes.value,
      maxNestingDepth: maxNestingDepth.value,
      maxTraversalNestingDepth: maxNestingDepth.value,
    },
  });
}
</script>

<template>
  <form class="workbench" @submit.prevent="submit">
    <div class="field operation-field">
      <label for="operation">Operation</label>
      <select id="operation" v-model="operation" data-testid="operation-select">
        <option value="parse">Parse bytes</option>
        <option value="serialize">Serialize JSON</option>
        <option value="update">Update existing bytes</option>
      </select>
    </div>

    <div class="field">
      <label for="definition">CStruct definition</label>
      <textarea
        id="definition"
        v-model="definition"
        data-testid="definition-input"
        rows="10"
        spellcheck="false"
      ></textarea>
    </div>

    <div v-if="operation !== 'serialize'" class="field">
      <label for="binary">Binary data (hex; whitespace is allowed)</label>
      <div id="binary" class="binary-input-editor" data-testid="binary-input">
        <VueHex
          v-model="binaryEditorBytes"
          data-mode="buffer"
          theme="dark"
          :editable="true"
          :cursor="true"
          :search="true"
          statusbar="bottom"
          :bytes-per-row="16"
          aria-label="Binary data editor"
          @update:model-value="handleBinaryEdited"
        />
      </div>
      <p class="field-hint">Edit bytes directly or paste hexadecimal data into the hex column.</p>
    </div>

    <div v-if="operation !== 'parse'" class="field">
      <label for="json-value"
        >{{ operation === "serialize" ? "Value" : "Replacement" }} (JSON)</label
      >
      <textarea
        id="json-value"
        v-model="jsonValue"
        data-testid="json-input"
        rows="7"
        spellcheck="false"
      ></textarea>
    </div>

    <div v-if="operation === 'update'" class="field">
      <label for="path">Update path</label>
      <input id="path" v-model="path" data-testid="path-input" spellcheck="false" />
    </div>

    <div class="option-grid">
      <div class="field">
        <label for="root-type">Root type/path</label>
        <input id="root-type" v-model="rootTypeName" placeholder="First declaration" />
      </div>
      <div class="field">
        <label for="pointer-size">Pointer bytes</label>
        <select id="pointer-size" v-model.number="pointerSize">
          <option :value="1">1</option>
          <option :value="2">2</option>
          <option :value="4">4</option>
          <option :value="8">8</option>
        </select>
      </div>
      <div class="field">
        <label for="endian">Default byte order</label>
        <select id="endian" v-model="endian" data-testid="endian-select">
          <option value="little">Little endian</option>
          <option value="big">Big endian</option>
        </select>
      </div>
      <div class="field">
        <label for="addressing">Pointer addressing</label>
        <select id="addressing" v-model="addressingMode">
          <option value="Absolute">Absolute</option>
          <option value="Relative">Relative to origin</option>
        </select>
      </div>
      <div class="field">
        <label for="origin">Pointer origin</label>
        <input id="origin" v-model="origin" inputmode="numeric" />
      </div>
      <label class="check-field">
        <input v-model="aligned" type="checkbox" />
        Align fields
      </label>
      <label class="check-field">
        <input v-model="dereferencePointers" type="checkbox" />
        Follow pointers
      </label>
    </div>

    <details class="limits">
      <summary>Safety limits</summary>
      <div class="option-grid">
        <div class="field">
          <label for="max-array">Array elements</label>
          <input id="max-array" v-model.number="maxArrayElements" type="number" min="1" />
        </div>
        <div class="field">
          <label for="max-string">String bytes</label>
          <input id="max-string" v-model.number="maxStringBytes" type="number" min="1" />
        </div>
        <div class="field">
          <label for="max-total">Total bytes</label>
          <input id="max-total" v-model.number="maxTotalBytes" type="number" min="1" />
        </div>
        <div class="field">
          <label for="max-depth">Nesting depth</label>
          <input id="max-depth" v-model.number="maxNestingDepth" type="number" min="1" />
        </div>
      </div>
    </details>

    <button class="btn btn-primary" type="submit" :disabled="disabled">
      {{ disabled ? "WebAssembly is not ready" : `Run ${operation}` }}
    </button>
  </form>
</template>

<style scoped>
.workbench {
  display: grid;
  gap: 16px;
}

.field {
  display: grid;
  gap: 6px;
}

.field label,
.limits summary {
  color: var(--color-text-muted);
  font-size: 12px;
  font-weight: 700;
  letter-spacing: 0.04em;
  text-transform: uppercase;
}

input,
select,
textarea {
  width: 100%;
  border: 1px solid rgba(255, 255, 255, 0.12);
  border-radius: var(--radius-sm);
  background: var(--color-bg-primary);
  color: var(--color-text);
  font: inherit;
  padding: 9px 11px;
}

textarea,
#path,
#root-type,
#origin {
  font-family: var(--font-mono);
}

textarea {
  line-height: 1.45;
  resize: vertical;
}

.binary-input-editor {
  height: 280px;
}

.binary-input-editor :deep(.vuehex) {
  border: 1px solid rgba(255, 255, 255, 0.12);
  border-radius: var(--radius-sm);
}

.field-hint {
  color: var(--color-text-muted);
  font-size: 12px;
}

input:focus,
select:focus,
textarea:focus {
  border-color: var(--color-accent);
  outline: 2px solid var(--color-accent-glow);
}

.operation-field {
  max-width: 360px;
}

.option-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(145px, 1fr));
  gap: 12px;
  align-items: end;
}

.check-field {
  display: flex;
  min-height: 40px;
  align-items: center;
  gap: 8px;
}

.check-field input {
  width: auto;
}

.limits {
  border: 1px solid rgba(255, 255, 255, 0.08);
  border-radius: var(--radius-sm);
  padding: 10px 12px;
}

.limits[open] summary {
  margin-bottom: 12px;
}
</style>
