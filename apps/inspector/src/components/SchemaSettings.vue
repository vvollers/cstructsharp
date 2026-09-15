<script lang="ts">
export interface SchemaSettingsValues {
  rootTypeName: string;
  aligned: boolean;
  pointerSize: number;
  littleEndian: boolean;
  addressingMode: "Absolute" | "Relative";
  origin: string;
  dereferencePointers: boolean;
  maxArrayElements: number;
  maxStringBytes: number;
  maxTotalBytesRead: number;
  maxNestingDepth: number;
}
</script>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import SettingStatusItem from "./SettingStatusItem.vue";

const options = defineModel<SchemaSettingsValues>({ required: true });
const open = defineModel<boolean>("open", { default: false });
const settingsDialog = ref<HTMLDialogElement | null>(null);
const settingsButton = ref<HTMLButtonElement | null>(null);
const endian = computed({
  get: () => (options.value.littleEndian ? "little" : "big"),
  set: (value) => {
    options.value.littleEndian = value === "little";
  },
});

const formatBytes = (value: number): string =>
  value >= 1_048_576 && value % 1_048_576 === 0
    ? `${value / 1_048_576} MiB`
    : `${value.toLocaleString()} B`;

const settingsSummary = computed(() => [
  {
    label: "Root",
    value: options.value.rootTypeName.trim() || "first declaration",
    color: "#67e8f9",
    explanation: options.value.rootTypeName.trim()
      ? "This name selects the layout type where parsing starts. It must match the declaration's spelling and capitalization."
      : "No root name is supplied, so the first declared struct is used.",
  },
  {
    label: "Order",
    value: `${endian.value === "little" ? "Little" : "Big"} endian`,
    color: "#93c5fd",
    explanation: options.value.littleEndian
      ? "The least significant byte comes first by default. A field's explicit byte order suffix can override this."
      : "The most significant byte comes first by default. A field's explicit byte order suffix can override this.",
  },
  {
    label: "Aligned",
    value: options.value.aligned,
    color: "#c4b5fd",
    explanation: options.value.aligned
      ? "Fields may have padding bytes before them to meet alignment rules."
      : "Fields are packed together without automatic alignment padding, matching how most real file formats are laid out.",
  },
  {
    label: "Pointers",
    value: `${options.value.pointerSize} B`,
    color: "#f9a8d4",
    explanation: "Each stored pointer address occupies this many bytes.",
  },
  {
    label: "Address",
    value: options.value.addressingMode,
    color: "#fda4af",
    explanation:
      options.value.addressingMode === "Relative"
        ? "Stored pointer addresses are offsets from an origin."
        : "Stored pointer addresses are positions from the start of the input.",
  },
  {
    label: "Follow pointers",
    value: options.value.dereferencePointers,
    color: "#86efac",
    explanation: options.value.dereferencePointers
      ? "Parsing can visit the data at a pointer's target."
      : "Parsing keeps the pointer address without reading its target.",
  },
  {
    label: "Total",
    value: formatBytes(options.value.maxTotalBytesRead),
    color: "#fde68a",
    explanation:
      "Limits bytes actually decoded, including debug rereads. File size and pointer distance are unrestricted by this budget: a pointer beyond 4 GiB can still read only a few bytes.",
  },
  {
    label: "Elements",
    value: options.value.maxArrayElements.toLocaleString(),
    color: "#bef264",
    explanation: "An array may contain at most this many elements.",
  },
]);

// The Close button, Escape key and backdrop click all change the same `open` value.
// Once Vue has updated the component, open/close the browser's dialog and return keyboard focus
// to the settings button. This keeps keyboard navigation predictable.
function closeSettings(): void {
  open.value = false;
}
watch(
  open,
  (value) => {
    if (value) settingsDialog.value?.showModal();
    else {
      settingsDialog.value?.close();
      settingsButton.value?.focus();
    }
  },
  { flush: "post" },
);
</script>

<template>
  <div class="panel-topbar">
    <button
      ref="settingsButton"
      class="icon-button"
      type="button"
      aria-label="Schema settings"
      title="Schema settings"
      @click="open = true"
    >
      <svg viewBox="0 0 24 24" aria-hidden="true">
        <path d="M4 7h16M4 17h16M8 4v6M16 14v6" />
      </svg>
    </button>
    <p class="settings-summary" aria-label="Current schema settings">
      <SettingStatusItem v-for="setting in settingsSummary" :key="setting.label" v-bind="setting" />
    </p>
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
          <input id="root-type" v-model="options.rootTypeName" placeholder="First declaration" />
        </div>
        <div class="field">
          <label for="endian">Default byte order</label>
          <select id="endian" v-model="endian">
            <option value="little">Little endian</option>
            <option value="big">Big endian</option>
          </select>
        </div>
        <label class="check-field">
          <input v-model="options.aligned" type="checkbox" />
          Align fields (insert padding)
        </label>
      </div>
      <section class="settings-group" aria-labelledby="pointer-settings-title">
        <h3 id="pointer-settings-title">Pointer settings</h3>
        <div class="option-grid">
          <div class="field">
            <label for="pointer-size">Pointer bytes</label>
            <select id="pointer-size" v-model.number="options.pointerSize">
              <option :value="1">1</option>
              <option :value="2">2</option>
              <option :value="4">4</option>
              <option :value="8">8</option>
            </select>
          </div>
          <div class="field">
            <label for="addressing">Pointer addressing</label>
            <select id="addressing" v-model="options.addressingMode">
              <option value="Absolute">Absolute</option>
              <option value="Relative">Relative to origin</option>
            </select>
          </div>
          <div class="field">
            <label for="origin">Pointer origin</label>
            <input id="origin" v-model="options.origin" inputmode="numeric" />
          </div>
          <label class="check-field">
            <input v-model="options.dereferencePointers" type="checkbox" />
            Follow pointers
          </label>
        </div>
      </section>
      <section class="settings-group" aria-labelledby="safety-settings-title">
        <h3 id="safety-settings-title">Decoded-data budgets</h3>
        <p>
          These limits count decoded work, not file size or pointer distance. Raise them when
          reading larger payloads.
        </p>
        <div class="option-grid">
          <div class="field">
            <label for="max-array">Array elements</label>
            <input id="max-array" v-model.number="options.maxArrayElements" type="number" min="1" />
          </div>
          <div class="field">
            <label for="max-string">String bytes</label>
            <input id="max-string" v-model.number="options.maxStringBytes" type="number" min="1" />
          </div>
          <div class="field">
            <label for="max-total">Total bytes read</label>
            <input
              id="max-total"
              v-model.number="options.maxTotalBytesRead"
              type="number"
              min="1"
            />
          </div>
          <div class="field">
            <label for="max-depth">Nesting depth</label>
            <input id="max-depth" v-model.number="options.maxNestingDepth" type="number" min="1" />
          </div>
        </div>
      </section>
      <button class="btn btn-primary" type="button" @click="closeSettings">Done</button>
    </div>
  </dialog>
</template>

<style scoped>
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
