<script setup lang="ts">
/**
 * The schema panel's settings bar: the shared settings button and dialog, and a summary of the current settings,
 * each with an explanation. The dialog edits the `v-model` settings object in place; `open` shows the dialog.
 */
import { computed } from "vue";
import SettingStatusItem from "@cstructsharp/app-shared/components/SettingStatusItem.vue";
import LayoutSettingsDialog, {
  type LayoutSettingsValues,
} from "@cstructsharp/app-shared/components/LayoutSettingsDialog.vue";
import { formatBytes } from "@cstructsharp/app-shared/options";

const options = defineModel<LayoutSettingsValues>({ required: true });
const open = defineModel<boolean>("open", { default: false });

const settingsSummary = computed(() => [
  {
    label: "Root",
    value: options.value.root.trim() || "first declaration",
    color: "#67e8f9",
    explanation: options.value.root.trim()
      ? "This name selects the layout type where parsing starts. It must match the declaration's spelling and capitalization."
      : "No root name is supplied, so the first declared struct is used.",
  },
  {
    label: "Order",
    value: `${options.value.littleEndian ? "Little" : "Big"} endian`,
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
</script>

<template>
  <div class="panel-topbar">
    <LayoutSettingsDialog v-model="options" v-model:open="open" />
    <p class="settings-summary" aria-label="Current schema settings">
      <SettingStatusItem v-for="setting in settingsSummary" :key="setting.label" v-bind="setting" />
    </p>
  </div>
</template>

<!-- Unscoped: the inspector's square icon buttons. The shared settings dialog's open and close buttons use this class,
and this settings bar is their only place in the inspector. -->
<style>
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
</style>

<style scoped>
.panel-topbar {
  display: flex;
  align-items: flex-start;
  gap: 10px;
  padding: 8px 12px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.08);
  background: var(--color-bg-secondary);
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
</style>
