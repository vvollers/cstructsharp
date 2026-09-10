<script setup lang="ts">
/**
 * dockview-vue's `components` map (see App.vue) renders a registered component with a single `params`
 * prop - there is no slot-based content mechanism despite what the library's README shows (confirmed
 * against the installed 8.3.0 bundle: zero references to Vue slots anywhere in it). Confirmed against the
 * same bundle's VueRenderer.init(): that outer `params` prop is itself `{ params: <the object passed to
 * addPanel's own params option>, api, containerApi, tabLocation }` - a double "params" wrapper, not the
 * addPanel params object directly - so `data` below unwraps that one extra level.
 *
 * `data`'s fields carry the actual refs/computeds/callbacks by reference rather than snapshotted values,
 * so this host stays fully reactive from one addPanel() call - no dockview updateParameters() calls are
 * needed as the underlying App.vue state changes later.
 */
import SchemaPanel from "./SchemaPanel.vue";
import type { FormatExample } from "../formats";
import type { ParseWithDebugOptions } from "../wasm/cstruct-contract";

const props = defineProps<{
  params: {
    params: {
      definition: { value: string };
      disabled: { value: boolean };
      running: { value: boolean };
      selectedExample: { value: FormatExample | null };
      resetCount: { value: number };
      onRun: (options: ParseWithDebugOptions) => void;
    };
  };
}>();

const data = props.params.params;

function setDefinition(value: string): void {
  data.definition.value = value;
}
</script>

<template>
  <SchemaPanel
    :key="`${data.selectedExample.value?.id ?? 'new'}-${data.resetCount.value}`"
    :definition="data.definition.value"
    @update:definition="setDefinition"
    :disabled="data.disabled.value"
    :running="data.running.value"
    :initial-root-type="data.selectedExample.value?.rootType"
    :initial-aligned="data.selectedExample.value?.parserOptions.aligned"
    :initial-little-endian="data.selectedExample.value?.parserOptions.littleEndian"
    :initial-pointer-size="data.selectedExample.value?.parserOptions.pointerSize"
    :initial-addressing-mode="data.selectedExample.value?.parserOptions.addressingMode"
    @run="data.onRun"
  />
</template>
