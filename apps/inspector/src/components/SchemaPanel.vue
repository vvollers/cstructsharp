<script setup lang="ts">
import { computed, ref } from "vue";

import SettingStatusItem from "./SettingStatusItem.vue";
import LayoutEditor from "./LayoutEditor.vue";
import type { ParseWithDebugOptions } from "../wasm/cstruct-contract";

const props = defineProps<{
  disabled: boolean;
  running: boolean;
  initialRootType?: string | null;
  initialAligned?: boolean;
  initialLittleEndian?: boolean;
  initialPointerSize?: number;
  initialAddressingMode?: "Absolute" | "Relative";
}>();

const emit = defineEmits<{
  run: [options: ParseWithDebugOptions];
}>();

const definition = defineModel<string>("definition", { required: true });

const rootTypeName = ref(props.initialRootType ?? "");
const aligned = ref(props.initialAligned ?? false);
const pointerSize = ref(props.initialPointerSize ?? 8);
const endian = ref<"little" | "big">(props.initialLittleEndian === false ? "big" : "little");
const addressingMode = ref<"Absolute" | "Relative">(props.initialAddressingMode ?? "Absolute");
const origin = ref("0");
const dereferencePointers = ref(true);
const maxArrayElements = ref(1_000_000);
const maxStringBytes = ref(16 * 1024 * 1024);
const maxTotalBytes = ref(64 * 1024 * 1024);
const maxNestingDepth = ref(256);

const settingsDialog = ref<HTMLDialogElement | null>(null);
const settingsButton = ref<HTMLButtonElement | null>(null);

const formatBytes = (value: number): string =>
  value >= 1_048_576 && value % 1_048_576 === 0
    ? `${value / 1_048_576} MiB`
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
  { label: "Follow pointers", value: dereferencePointers.value, color: "#86efac" },
  { label: "Total", value: formatBytes(maxTotalBytes.value), color: "#fde68a" },
  { label: "Elements", value: maxArrayElements.value.toLocaleString(), color: "#bef264" },
]);

const settingExplanations = computed<Record<string, string>>(() => ({
  Root: rootTypeName.value.trim()
    ? "This name selects the layout type where parsing starts. It must match the declaration's spelling and capitalization."
    : "No root name is supplied, so the first declared struct is used.",
  Order:
    endian.value === "little"
      ? "The least significant byte comes first by default. A field's explicit byte order suffix can override this."
      : "The most significant byte comes first by default. A field's explicit byte order suffix can override this.",
  Aligned: aligned.value
    ? "Fields may have padding bytes before them to meet alignment rules."
    : "Fields are packed together without automatic alignment padding, matching how most real file formats are laid out.",
  Pointers: "Each stored pointer address occupies this many bytes.",
  Address:
    addressingMode.value === "Relative"
      ? "Stored pointer addresses are offsets from an origin."
      : "Stored pointer addresses are positions from the start of the input.",
  "Follow pointers": dereferencePointers.value
    ? "Parsing can visit the data at a pointer's target."
    : "Parsing keeps the pointer address without reading its target.",
  Total: "This budget limits bytes read, including rereads during debug traversal.",
  Elements: "An array may contain at most this many elements.",
}));

function closeSettings(): void {
  settingsDialog.value?.close();
  settingsButton.value?.focus();
}

function submit(): void {
  if (settingsDialog.value?.open) {
    return;
  }

  emit("run", {
    rootTypeName: rootTypeName.value.trim() || null,
    aligned: aligned.value,
    pointerSize: pointerSize.value,
    littleEndian: endian.value === "little",
    addressingMode: addressingMode.value,
    origin: origin.value,
    dereferencePointers: dereferencePointers.value,
    maxArrayElements: maxArrayElements.value,
    maxStringBytes: maxStringBytes.value,
    maxTotalBytesRead: maxTotalBytes.value,
    maxNestingDepth: maxNestingDepth.value,
  });
}
</script>

<template>
  <section class="schema-panel">
    <div class="panel-topbar">
      <button
        ref="settingsButton"
        class="icon-button"
        type="button"
        aria-label="Schema settings"
        title="Schema settings"
        @click="settingsDialog?.showModal()"
      >
        <svg viewBox="0 0 24 24" aria-hidden="true">
          <path d="M4 7h16M4 17h16M8 4v6M16 14v6" />
        </svg>
      </button>
      <p class="settings-summary" aria-label="Current schema settings">
        <SettingStatusItem
          v-for="setting in settingsSummary"
          :key="setting.label"
          v-bind="setting"
          :explanation="settingExplanations[setting.label]!"
        />
      </p>
    </div>

    <div class="editor-body" data-testid="definition-editor">
      <LayoutEditor v-model="definition" fill label="Binary layout (CStruct definition)" />
    </div>

    <div class="panel-bottombar">
      <button class="btn btn-primary run-button" type="button" :disabled="disabled" @click="submit">
        <svg class="button-icon" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
          <path d="M7 4v16l13-8L7 4Z" />
        </svg>
        {{ running ? "Running…" : disabled ? "WebAssembly is not ready" : "Run" }}
      </button>
    </div>

    <dialog
      ref="settingsDialog"
      class="settings-dialog"
      aria-labelledby="settings-title"
      @cancel.prevent="closeSettings"
      @click="$event.target === settingsDialog && closeSettings()"
    >
      <div class="dialog-content">
        <div class="dialog-heading">
          <h2 id="settings-title">Schema settings</h2>
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
        <div class="option-grid">
          <div class="field">
            <label for="root-type">Root type/path</label>
            <input id="root-type" v-model="rootTypeName" placeholder="First declaration" />
          </div>
          <div class="field">
            <label for="endian">Default byte order</label>
            <select id="endian" v-model="endian">
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
  </section>
</template>

<style scoped>
.schema-panel {
  display: grid;
  grid-template-rows: auto minmax(0, 1fr) auto;
  height: 100%;
  min-height: 0;
  min-width: 0;
}
.panel-topbar {
  display: flex;
  align-items: flex-start;
  gap: 10px;
  padding: 8px 12px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.08);
  background: var(--color-bg-secondary);
}
.icon-button {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 30px;
  height: 30px;
  flex-shrink: 0;
  padding: 6px;
  border: 1px solid var(--color-text-muted);
  border-radius: 6px;
  background: var(--color-bg-primary);
  color: var(--color-text);
  cursor: pointer;
}
.icon-button:hover {
  border-color: var(--color-accent);
}
.icon-button svg {
  width: 18px;
  height: 18px;
  fill: none;
  stroke: currentColor;
  stroke-width: 1.7;
  stroke-linecap: round;
  stroke-linejoin: round;
}
.settings-summary {
  display: flex;
  flex: 1;
  flex-wrap: wrap;
  align-items: center;
  gap: 5px 14px;
  margin: 0;
  padding-top: 5px;
  font-size: 11px;
  line-height: 1.5;
  color: var(--color-text-muted);
  overflow-wrap: anywhere;
}
.editor-body {
  min-height: 0;
  padding: 10px;
  overflow: hidden;
}
.editor-body :deep(.layout-editor) {
  height: 100%;
}
.panel-bottombar {
  display: flex;
  justify-content: stretch;
  padding: 12px 14px;
  border-top: 1px solid rgba(255, 255, 255, 0.08);
  background: var(--color-bg-secondary);
}
.run-button {
  width: 100%;
  padding: 16px 24px;
  font-size: 15px;
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
.dialog-heading {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.dialog-heading h2 {
  margin: 0;
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
select {
  width: 100%;
  border: 1px solid rgba(255, 255, 255, 0.12);
  border-radius: var(--radius-sm);
  background: var(--color-bg-primary);
  color: var(--color-text);
  font: inherit;
  padding: 9px 11px;
}
#root-type,
#origin {
  font-family: var(--font-mono);
}
input:focus,
select:focus {
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
