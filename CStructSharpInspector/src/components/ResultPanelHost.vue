<script setup lang="ts">
// See SchemaPanelHost.vue for why `params.params` (not `params` directly) holds the addPanel() params.
import ResultPanel from "./ResultPanel.vue";
import type { InteropResult } from "../wasm/cstruct-contract";

const props = defineProps<{
  params: {
    params: {
      result: { value: InteropResult | null };
      focusPath: { value: string[] | null };
      onSelectPath: (path: string[] | null) => void;
    };
  };
}>();

const data = props.params.params;
</script>

<template>
  <ResultPanel
    :result="data.result.value"
    :focus-path="data.focusPath.value"
    @select-path="data.onSelectPath"
  />
</template>
