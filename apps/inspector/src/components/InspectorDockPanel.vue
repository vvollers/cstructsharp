<script lang="ts">
import type { Inspector } from "../composables/useInspector";

export interface InspectorPanelParams {
  panel: "schema" | "binary" | "result";
  inspector: Inspector;
  onLoadFile: () => void;
}
</script>

<script setup lang="ts">
import { computed } from "vue";
import SchemaPanel from "./SchemaPanel.vue";
import BinaryPanel from "./BinaryPanel.vue";
import ResultPanel from "./ResultPanel.vue";

// Dockview supplies two nested `params` objects: its own renderer information, then the data
// we passed to addPanel(). Unwrap that here so the ordinary panels only need props and events.
const props = defineProps<{ params: { params: InspectorPanelParams } }>();
const data = computed(() => props.params.params);
const inspector = computed(() => data.value.inspector);
</script>

<template>
  <SchemaPanel
    v-if="data.panel === 'schema'"
    :definition="inspector.definition.value"
    :example="inspector.selectedExample.value"
    :revision="inspector.schemaRevision.value"
    :disabled="inspector.schemaDisabled.value"
    :running="inspector.isRunning.value"
    :loading-file="inspector.isLoadingFile.value"
    @update:definition="inspector.setDefinition"
    @settings-change="inspector.invalidateResult"
    @run="inspector.runParse"
  />
  <BinaryPanel
    v-else-if="data.panel === 'binary'"
    :bytes="inspector.bytes.value"
    :source="inspector.fileSource.value"
    :debug-data="inspector.debugData.value"
    :selected-indices="inspector.selectedDebugIndices.value"
    @update:bytes="inspector.editBytes"
    @update:source="inspector.editSource"
    @byte-click="inspector.selectByte"
    @file-dropped="inspector.loadFile"
    @load-file="data.onLoadFile"
  />
  <ResultPanel
    v-else
    :result="inspector.result.value"
    :focus-path="inspector.focusPath.value"
    @select-path="inspector.selectPath"
  />
</template>
