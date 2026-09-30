<script lang="ts">
/**
 * The parser settings the dialog edits. The byte limits count decoded work; the explorer applies
 * `maxTotalBytesRead` to bytes written as well.
 */
export interface LayoutSettingsValues {
  /** The root type or path; empty text selects the first declaration. */
  root: string;
  aligned: boolean;
  /** The stored width of a pointer address, in bytes. */
  pointerSize: number;
  littleEndian: boolean;
  addressingMode: "Absolute" | "Relative";
  /** The byte position that relative pointer addresses are added to, as decimal text. */
  origin: string;
  dereferencePointers: boolean;
  maxArrayElements: number;
  maxStringBytes: number;
  maxTotalBytesRead: number;
  maxNestingDepth: number;
}
</script>

<script setup lang="ts">
/**
 * The settings button and the modal settings dialog it opens, shared by both apps: root, byte order, alignment,
 * pointer settings and the decoded-data limits. Fields edit the `v-model` object in place, so changes apply
 * immediately. The `open` model shows and hides the browser dialog; the Close button, the Escape key, a backdrop
 * click and Done all set it to false, and closing returns keyboard focus to the settings button. The component
 * renders the button where the owner places it; the closed dialog takes no space there. It uses the owning app's
 * global classes for the parts each app styles its own way: `icon-button` for the open and close buttons,
 * `btn btn-primary` for Done, and `field` for the labelled controls. Both buttons take their accessible names
 * from their `title`.
 */
import { ref, watch } from "vue";

const settings = defineModel<LayoutSettingsValues>({ required: true });
const open = defineModel<boolean>("open", { default: false });
const settingsDialog = ref<HTMLDialogElement | null>(null);

// The numeric limits as [input id, setting, label].
const limits = [
  ["max-array", "maxArrayElements", "Array elements"],
  ["max-string", "maxStringBytes", "String bytes"],
  ["max-total", "maxTotalBytesRead", "Total bytes"],
  ["max-depth", "maxNestingDepth", "Nesting depth"],
] as const;

/** Closes the settings dialog. */
function closeSettings(): void {
  open.value = false;
}

// Every way of closing changes the same `open` value, and the owner may change it too. Once Vue has updated the
// component, show or close the browser's dialog; after closing, return keyboard focus to the settings button (the
// element just before the dialog in this template) so keyboard navigation stays predictable.
watch(
  open,
  (value) => {
    if (value) settingsDialog.value?.showModal();
    else {
      settingsDialog.value?.close();
      (settingsDialog.value?.previousElementSibling as HTMLElement | null)?.focus();
    }
  },
  { flush: "post" },
);
</script>

<template>
  <button class="icon-button" type="button" title="Operation settings" @click="open = true">
    <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 7h16M4 17h16M8 4v6M16 14v6" /></svg>
  </button>
  <dialog
    ref="settingsDialog"
    class="settings-dialog"
    aria-labelledby="settings-title"
    @cancel.prevent="closeSettings"
    @click="$event.target === settingsDialog && closeSettings()"
  >
    <div class="dialog-content">
      <div class="dialog-heading">
        <h2 id="settings-title">Operation settings</h2>
        <button class="icon-button" type="button" title="Close settings" @click="closeSettings">
          <svg viewBox="0 0 24 24" aria-hidden="true"><path d="m6 6 12 12M18 6 6 18" /></svg>
        </button>
      </div>
      <div class="option-grid">
        <div class="field">
          <label for="root-type">Root type/path</label>
          <input id="root-type" v-model="settings.root" placeholder="First declaration" />
        </div>
        <div class="field">
          <label for="endian">Default byte order</label>
          <select id="endian" v-model="settings.littleEndian">
            <option :value="true">Little endian</option>
            <option :value="false">Big endian</option>
          </select>
        </div>
        <label class="check-field">
          <input v-model="settings.aligned" type="checkbox" />
          Align fields (insert padding)
        </label>
      </div>
      <section class="settings-group">
        <h3>Pointer settings</h3>
        <div class="option-grid">
          <div class="field">
            <label for="pointer-size">Pointer bytes</label>
            <select id="pointer-size" v-model.number="settings.pointerSize">
              <option v-for="size in [1, 2, 4, 8]" :key="size" :value="size">{{ size }}</option>
            </select>
          </div>
          <div class="field">
            <label for="addressing">Pointer addressing</label>
            <select id="addressing" v-model="settings.addressingMode">
              <option value="Absolute">Absolute</option>
              <option value="Relative">Relative to origin</option>
            </select>
          </div>
          <div class="field">
            <label for="origin">Pointer origin</label>
            <input id="origin" v-model="settings.origin" inputmode="numeric" />
          </div>
          <label class="check-field">
            <input v-model="settings.dereferencePointers" type="checkbox" />
            Follow pointers
          </label>
        </div>
      </section>
      <section class="settings-group">
        <h3>Safety limits</h3>
        <div class="option-grid">
          <div v-for="[id, key, label] in limits" :key="id" class="field">
            <label :for="id">{{ label }}</label>
            <input :id="id" v-model.number="settings[key]" type="number" min="1" />
          </div>
        </div>
      </section>
      <button class="btn btn-primary" type="button" @click="closeSettings">Done</button>
    </div>
  </dialog>
</template>

<!-- Unscoped: every selector is under the dialog's own class names or ids, and one set of plain selectors is smaller
than scoped copies. `.field` (a label above its control, which is a direct child) is the form field style of both
apps; the explorer's operation panel uses it too. Child selectors leave controls inside nested components, such as
VueHex's search box, alone. -->
<style>
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
.settings-dialog {
  width: min(620px, calc(100vw - 24px));
  max-height: calc(100dvh - 32px);
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
.field > input,
.field > select,
.field > textarea {
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
.field > input:focus,
.field > select:focus,
.field > textarea:focus {
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
.settings-group {
  border-top: 1px solid rgba(255, 255, 255, 0.12);
  padding-top: 14px;
}
.settings-group h3 {
  margin-bottom: 10px;
}
</style>
