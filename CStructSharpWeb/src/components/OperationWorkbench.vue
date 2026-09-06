<script setup lang="ts">
import { computed, ref, watch } from "vue";

import { VueHex } from "vuehex";
import SettingStatusItem from "./SettingStatusItem.vue";
import LayoutEditor from "./LayoutEditor.vue";
import GeneratedCodeDialog from "./GeneratedCodeDialog.vue";
import { formatLayout } from "../format-layout";

import type {
  ParseWithDebugOptions,
  SerializeOptions,
  UpdateOptions,
} from "../wasm/cstruct-contract";
import { hexToBytes } from "../wasm/cstruct-wasm";
import type { LessonOperation } from "../lessons";

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
  operation?: WorkbenchOperation;
  initialAligned?: boolean;
  initialLittleEndian?: boolean;
  initialPointerSize?: number;
  initialRootType?: string | null;
  presets?: Partial<Record<WorkbenchOperation, LessonOperation>>;
  initialOptions?: ParseWithDebugOptions;
  running?: boolean;
}>();

const emit = defineEmits<{
  run: [request: WorkbenchRequest];
  changed: [];
  reset: [];
}>();

const operation = ref<WorkbenchOperation>(props.operation ?? "parse");
const definition = ref(formatLayout(props.definition));
const binaryHex = ref(props.binaryHex);
const binaryEditorBytes = ref<Uint8Array<ArrayBufferLike>>(parseBinaryHex(props.binaryHex));
const jsonValue = ref(props.presets?.[operation.value]?.json ?? '{\n  "value": 42\n}');
const path = ref(
  props.presets?.update?.path ??
    (props.initialRootType ? `${props.initialRootType}.value` : "root.value"),
);
const rootTypeName = ref(props.initialRootType ?? "");
const aligned = ref(props.initialAligned ?? false);
const pointerSize = ref(props.initialPointerSize ?? 8);
const endian = ref<"little" | "big">(props.initialLittleEndian === false ? "big" : "little");
const addressingMode = ref<"Absolute" | "Relative">("Absolute");
const origin = ref("0");
const dereferencePointers = ref(true);
const maxArrayElements = ref(1_000_000);
const maxStringBytes = ref(16 * 1024 * 1024);
const maxTotalBytes = ref(props.initialOptions?.maxTotalBytesRead ?? 64 * 1024 * 1024);
const maxNestingDepth = ref(256);

const settingsDialog = ref<HTMLDialogElement | null>(null);
const generatedCode = ref<InstanceType<typeof GeneratedCodeDialog> | null>(null);
const settingsButton = ref<HTMLButtonElement | null>(null);
const formatBytes = (value: number) =>
  value >= 1048576 && value % 1048576 === 0
    ? `${value / 1048576} MiB`
    : `${value.toLocaleString()} B`;
const settingsSummary = computed(() => [
  { label: "Root", value: rootTypeName.value.trim() || "first declaration", color: "#67e8f9" },
  {
    label: "Order",
    value: `${endian.value === "little" ? "Little" : "Big"} endian`,
    color: "#93c5fd",
  },
  { label: "Aligned", value: aligned.value, color: "#c4b5fd" },
  { label: "Pointers", value: `${pointerSize.value} B`, color: "#f9a8d4" },
  { label: "Address", value: addressingMode.value, color: "#fda4af" },
  { label: "Origin", value: origin.value, color: "#fdba74" },
  { label: "Follow pointers", value: dereferencePointers.value, color: "#86efac" },
  { label: "Total", value: formatBytes(maxTotalBytes.value), color: "#fde68a" },
  { label: "Elements", value: maxArrayElements.value.toLocaleString(), color: "#bef264" },
  { label: "Text", value: formatBytes(maxStringBytes.value), color: "#5eead4" },
  { label: "Depth", value: maxNestingDepth.value.toLocaleString(), color: "#d8b4fe" },
]);
function closeSettings(): void {
  settingsDialog.value?.close();
  settingsButton.value?.focus();
}

const settingExplanations = computed<Record<string, string>>(() => ({
  Root: rootTypeName.value.trim()
    ? "This name selects the layout type or path where the operation starts. It must match the declaration's spelling and capitalization."
    : "No root name is supplied, so the first declaration in the layout is used.",
  Order:
    endian.value === "little"
      ? "The least significant byte comes first. For example, 01 00 represents 1 in a two-byte integer. A field's explicit byte order can override this default."
      : "The most significant byte comes first. For example, 00 01 represents 1 in a two-byte integer. A field's explicit byte order can override this default.",
  Aligned: aligned.value
    ? "Fields may have padding bytes before them to meet alignment rules. This can increase the total size of a record."
    : "Fields are packed together without automatic alignment padding. The next field starts immediately after the previous field.",
  Pointers:
    "Each stored pointer address occupies this many bytes. This is the address width, not the size of the data it points to.",
  Address:
    addressingMode.value === "Relative"
      ? "Stored pointer addresses are offsets from the configured origin. Add the origin to the stored address to locate the target."
      : "Stored pointer addresses are positions from the start of the input. The origin is not added to them.",
  Origin:
    addressingMode.value === "Relative"
      ? "This byte position is added to a stored relative pointer address to find its target. It does not move the start of the root record."
      : "This byte position would be added to relative pointer addresses. It is currently unused because addressing is Absolute.",
  "Follow pointers": dereferencePointers.value
    ? "Reads can visit the data at a pointer's target. Updates may also traverse a pointer when the selected path requires it."
    : "Reads keep the pointer address without reading its target. Updates cannot follow a pointer to change its target.",
  Total:
    "This budget limits bytes read or written, including rereads during traversal. Workbench parsing also rereads field bytes for its debug view, so the budget can need to be larger than the input. Exceeding the applicable budget stops the operation.",
  Elements:
    "An array may contain at most this many elements. This limits work on large or untrusted inputs; it does not set the array's declared length.",
  Text: "A string may use at most this many encoded bytes. Bytes and characters are not always the same count, especially with multi-byte encodings.",
  Depth:
    "This limits how deeply an operation can nest or traverse values. Deeper structures fail instead of continuing without a bound.",
}));

watch(
  [
    operation,
    definition,
    binaryHex,
    jsonValue,
    path,
    rootTypeName,
    aligned,
    pointerSize,
    endian,
    addressingMode,
    origin,
    dereferencePointers,
    maxArrayElements,
    maxStringBytes,
    maxTotalBytes,
    maxNestingDepth,
  ],
  () => emit("changed"),
);

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
  if (settingsDialog.value?.open) return;
  emit("run", currentRequest());
}
function currentRequest(): WorkbenchRequest {
  return {
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
  };
}
</script>

<template>
  <form class="workbench" @submit.prevent="submit">
    <div class="workbench-heading">
      <button
        ref="settingsButton"
        class="icon-button"
        type="button"
        aria-label="Workbench settings"
        title="Workbench settings"
        @click="settingsDialog?.showModal()"
      >
        <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 7h16M4 17h16M8 4v6M16 14v6" /></svg>
      </button>
      <button
        class="icon-button"
        type="button"
        aria-label="Reset example"
        title="Reset example"
        @click="emit('reset')"
      >
        <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 10a8 8 0 1 1 1 8M4 4v6h6" /></svg>
      </button>
      <h2>Workbench</h2>
      <button
        class="icon-button"
        type="button"
        aria-label="Generate C#"
        title="Generate C#"
        :disabled="disabled"
        @click="generatedCode?.open(currentRequest(), 'csharp')"
      >
        <span aria-hidden="true">C#</span>
      </button>
      <button
        class="icon-button"
        type="button"
        aria-label="Generate JavaScript"
        title="Generate JavaScript"
        :disabled="disabled"
        @click="generatedCode?.open(currentRequest(), 'javascript')"
      >
        <span aria-hidden="true">JS</span>
      </button>
    </div>
    <p class="settings-summary" aria-label="Current workbench settings">
      <SettingStatusItem
        v-for="setting in settingsSummary"
        :key="setting.label"
        v-bind="setting"
        :explanation="settingExplanations[setting.label]!"
      />
    </p>
    <dialog
      ref="settingsDialog"
      class="settings-dialog"
      aria-labelledby="settings-title"
      @cancel.prevent="closeSettings"
      @click="$event.target === settingsDialog && closeSettings()"
    >
      <div class="dialog-content">
        <div class="dialog-heading">
          <h2 id="settings-title">Workbench settings</h2>
          <button
            class="icon-button"
            type="button"
            aria-label="Close settings"
            title="Close settings"
            @click="closeSettings"
          >
            <svg viewBox="0 0 24 24" aria-hidden="true"><path d="m6 6 12 12M18 6 6 18" /></svg>
          </button>
        </div>
        <p class="field-hint">
          Changes apply immediately. Reset example restores the lesson's settings.
        </p>
        <div class="option-grid">
          <div class="field">
            <label for="root-type">Root type/path</label>
            <input id="root-type" v-model="rootTypeName" placeholder="First declaration" />
          </div>
          <div class="field">
            <label for="endian">Default byte order</label>
            <select id="endian" v-model="endian" data-testid="endian-select">
              <option value="little">Little endian</option>
              <option value="big">Big endian</option>
            </select>
          </div>
          <label class="check-field">
            <input v-model="aligned" type="checkbox" />
            Align fields (insert padding)
          </label>
        </div>
        <section class="settings-group" aria-labelledby="pointer-settings-title">
          <h3 id="pointer-settings-title">Pointer settings</h3>
          <div class="option-grid">
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
              <input v-model="dereferencePointers" type="checkbox" />
              Follow pointers
            </label>
          </div>
        </section>

        <section class="settings-group" aria-labelledby="safety-settings-title">
          <h3 id="safety-settings-title">Safety limits</h3>
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
        </section>

        <button class="btn btn-primary" type="button" @click="closeSettings">Done</button>
      </div>
    </dialog>

    <div class="field">
      <label>Binary layout (C-like definition)</label>
      <LayoutEditor v-model="definition" />
    </div>

    <div v-if="operation !== 'serialize'" class="field">
      <label for="binary">Binary data (hex)</label>
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

    <button class="btn btn-primary" type="submit" :disabled="disabled">
      {{ running ? "Running…" : disabled ? "WebAssembly is not ready" : `Run ${operation}` }}
    </button>
  </form>
  <GeneratedCodeDialog ref="generatedCode" />
</template>

<style scoped>
.workbench-heading,
.dialog-heading {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}
.workbench-heading h2,
.dialog-heading h2 {
  margin: 0;
}
.dialog-heading {
  justify-content: space-between;
}
.workbench-heading h2 {
  margin-inline-end: auto;
}
.icon-button {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 32px;
  height: 32px;
  flex-shrink: 0;
  padding: 6px;
  border: 1px solid var(--color-text-muted);
  border-radius: 6px;
  background: var(--color-bg-secondary);
  color: var(--color-text);
  cursor: pointer;
}
.icon-button:hover {
  background: var(--color-bg-primary);
  border-color: var(--color-accent);
}
.icon-button svg {
  width: 20px;
  height: 20px;
  fill: none;
  stroke: currentColor;
  stroke-width: 1.7;
  stroke-linecap: round;
  stroke-linejoin: round;
}
.settings-summary {
  display: flex;
  flex-wrap: wrap;
  gap: 5px 16px;
  margin: -8px 0 0;
  width: 100%;
  padding: 9px 12px;
  border: 1px solid #3c526f;
  border-left: 3px solid var(--color-accent);
  border-radius: 6px;
  background: linear-gradient(110deg, #172a40, #202039);
  font-size: 12px;
  line-height: 1.5;
  color: var(--color-text-muted);
  overflow-wrap: anywhere;
}
.settings-dialog {
  width: min(620px, calc(100vw - 24px));
  max-height: calc(100dvh - 32px);
  padding: 0;
  margin: auto;
  border: 1px solid var(--color-text-muted);
  border-radius: 12px;
  background: var(--color-bg-secondary);
  color: var(--color-text);
  box-shadow: 0 24px 80px #0008;
}
.settings-dialog::backdrop {
  background: #0009;
}
.dialog-content {
  display: grid;
  gap: 18px;
  padding: 24px;
}
.dialog-content .field-hint {
  margin: 0;
}

.workbench {
  display: grid;
  grid-template-columns: minmax(0, 1fr);
  gap: 16px;
}

.field {
  display: grid;
  grid-template-columns: minmax(0, 1fr);
  gap: 6px;
}

.field label,
.settings-group h3 {
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
  height: 150px;
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

.settings-group {
  border-top: 1px solid rgba(255, 255, 255, 0.12);
  padding-top: 14px;
}

.settings-group h3 {
  margin-bottom: 10px;
}
</style>
