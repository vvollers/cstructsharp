<script setup lang="ts">
// See SchemaPanelHost.vue for why `params.params` (not `params` directly) holds the addPanel() params.
import BinaryPanel from "./BinaryPanel.vue";
import type { DebugDataItem } from "../wasm/cstruct-contract";

const props = defineProps<{
  params: {
    params: {
      bytes: { value: Uint8Array };
      debugData: { value: DebugDataItem[] };
      selectedIndices: { value: ReadonlySet<number> };
      onBytesEdited: (bytes: Uint8Array) => void;
      onByteClick: (offset: number) => void;
      onFileDropped: (file: File) => void;
    };
  };
}>();

const data = props.params.params;
</script>

<template>
  <BinaryPanel
    :bytes="data.bytes.value"
    :debug-data="data.debugData.value"
    :selected-indices="data.selectedIndices.value"
    @update:bytes="data.onBytesEdited"
    @byte-click="data.onByteClick"
    @file-dropped="data.onFileDropped"
  />
</template>
