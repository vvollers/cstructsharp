<script setup lang="ts">
import { useFileDialog } from "@vueuse/core";
import {
  DockviewVue,
  themeVisualStudio,
  type DockviewReadyEvent,
  type VueComponent,
} from "dockview-vue";
import ExampleList from "./components/ExampleList.vue";
import InspectorHeader from "./components/InspectorHeader.vue";
import InspectorDockPanel, { type InspectorPanelParams } from "./components/InspectorDockPanel.vue";
import { schemaCatalog } from "./schema-catalog";
import { useInspector } from "./composables/useInspector";

const inspector = useInspector();
const {
  selectedExample,
  sourceLabel,
  isRunning,
  isLoadingFile,
  isDetecting,
  detectionMessage,
  wasmStatus,
  wasmVersion,
  wasmError,
} = inspector;

// These dialogs only choose a file. useInspector handles reading it, just as it does for a
// file dropped onto the hex panel. The second dialog also asks it to detect the file format.
const { open: openFileDialog, onChange: onFileChange } = useFileDialog({
  multiple: false,
  accept: "*",
});
const { open: openDetectionDialog, onChange: onDetectionChange } = useFileDialog({
  multiple: false,
  accept: "*",
});
onFileChange((files) => {
  const file = files?.item(0);
  if (file) void inspector.loadFile(file);
});
onDetectionChange((files) => {
  const file = files?.item(0);
  if (file) void inspector.loadFile(file, true);
});

// Dockview manages the movable panels. Its component registry accepts very broad prop types,
// so we use `satisfies InspectorPanelParams` below to check the data we pass to our adapter.
const dockComponents = { inspector: InspectorDockPanel as unknown as VueComponent };
function onDockviewReady({ api }: DockviewReadyEvent): void {
  const panels = [
    { id: "schema", title: "Schema" },
    { id: "binary", title: "Binary Data" },
    { id: "result", title: "Result" },
  ] as const;

  // Start with three panels side by side. After this, Dockview handles the user's layout changes.
  panels.forEach(({ id, title }, index) => {
    api.addPanel({
      id,
      title,
      component: "inspector",
      position: index ? { direction: "right", referencePanel: panels[index - 1]!.id } : undefined,
      // Pass the refs themselves, rather than their current .value. This lets each panel see
      // future state changes without us having to update its Dockview parameters every time.
      params: {
        panel: id,
        inspector,
        onLoadFile: () => openFileDialog(),
      } satisfies InspectorPanelParams,
    });
  });
}
</script>

<template>
  <InspectorHeader
    :source-label="sourceLabel"
    :is-running="isRunning"
    :wasm-status="wasmStatus"
    :wasm-version="wasmVersion"
    :wasm-error="wasmError"
    @cancel="inspector.cancelParse"
  />
  <main class="workspace">
    <ExampleList
      :examples="schemaCatalog"
      :selected-id="selectedExample?.id ?? null"
      :selected-extension="selectedExample?.extension"
      :detecting="isDetecting"
      :detection-message="detectionMessage"
      @detect="openDetectionDialog()"
      @select="inspector.selectExample"
      @new="inspector.startNew"
    />
    <div class="dock-area" :aria-busy="isLoadingFile">
      <DockviewVue
        class="dock"
        style="width: 100%; height: 100%"
        :theme="themeVisualStudio"
        :components="dockComponents"
        @ready="onDockviewReady"
      />
    </div>
  </main>
</template>

<style scoped>
.workspace {
  display: flex;
  flex: 1;
  min-height: 0;
}
.dock-area {
  flex: 1;
  min-width: 0;
  min-height: 0;
}
</style>
