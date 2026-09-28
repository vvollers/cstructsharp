<script setup lang="ts">
import { ref, watch } from "vue";
import LayoutEditor from "./LayoutEditor.vue";
import SchemaSettings, { type SchemaSettingsValues } from "./SchemaSettings.vue";
import type { InspectorExample } from "../schema-catalog";
import type { ParseWithDebugOptions } from "@cstructsharp/app-shared/wasm/contract";
import { OPTION_DEFAULTS } from "@cstructsharp/app-shared/options";

const props = defineProps<{
  disabled: boolean;
  running: boolean;
  loadingFile: boolean;
  example: InspectorExample | null;
  revision: number;
}>();
const emit = defineEmits<{
  run: [options: ParseWithDebugOptions];
  "settings-change": [];
}>();
const definition = defineModel<string>("definition", { required: true });
const settingsOpen = ref(false);
const options = ref<SchemaSettingsValues>(initialOptions());

// A result describes both the schema and the settings used to parse it. Clear that result when
// a setting changes. `deep` also detects edits to individual properties inside the options object.
watch(options, () => emit("settings-change"), { deep: true });

/**
 * The settings a schema starts with: the example's parser options and root, and the library defaults for the
 * read limits.
 * @returns The initial settings.
 */
function initialOptions(): SchemaSettingsValues {
  const defaults = props.example?.parserOptions;

  return {
    root: props.example?.rootType ?? "",
    aligned: defaults?.aligned ?? false,
    littleEndian: defaults?.littleEndian ?? true,
    pointerSize: defaults?.pointerSize ?? 8,
    addressingMode: defaults?.addressingMode ?? "Absolute",
    origin: "0",
    dereferencePointers: OPTION_DEFAULTS.dereferencePointers,
    maxArrayElements: OPTION_DEFAULTS.maxArrayElements,
    maxStringBytes: OPTION_DEFAULTS.maxStringBytes,
    maxTotalBytesRead: OPTION_DEFAULTS.maxTotalBytesRead,
    maxNestingDepth: OPTION_DEFAULTS.maxNestingDepth,
  };
}

// The session changes this counter only when the user chooses a schema or starts a new one.
// Loading another file manually leaves it unchanged, so the user's current settings are preserved.
watch(
  () => props.revision,
  () => {
    options.value = initialOptions();
    settingsOpen.value = false;
  },
);

/**
 * Emits `run` with a copy of the settings, unless the panel is disabled or the settings are open.
 */
function submit(): void {
  if (props.disabled || settingsOpen.value) return;

  // Send a copy of the settings for this run. An empty root name means "use the first declaration".
  emit("run", { ...options.value, root: options.value.root.trim() || null });
}
</script>

<template>
  <section class="schema-panel">
    <SchemaSettings v-model="options" v-model:open="settingsOpen" />
    <div class="editor-body" data-testid="definition-editor">
      <LayoutEditor v-model="definition" fill label="Binary layout (CStruct definition)" />
    </div>

    <div class="panel-bottombar">
      <button class="btn btn-primary run-button" type="button" :disabled="disabled" @click="submit">
        <svg class="button-icon" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
          <path d="M7 4v16l13-8L7 4Z" />
        </svg>
        {{
          running
            ? "Running…"
            : loadingFile
              ? "Loading file…"
              : disabled
                ? "WebAssembly is not ready"
                : "Run"
        }}
      </button>
    </div>
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
</style>
