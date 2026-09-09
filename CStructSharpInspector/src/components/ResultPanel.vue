<script setup lang="ts">
import { computed, ref, watch } from "vue";
import JsonEditorVue from "json-editor-vue";
import "vanilla-jsoneditor/themes/jse-theme-dark.css";
import {
  Mode,
  SelectionType,
  createValueSelection,
  getFocusPath,
  type JSONEditorSelection,
} from "vanilla-jsoneditor";

import type { InteropResult } from "../wasm/cstruct-contract";

const props = defineProps<{
  result: InteropResult | null;
  focusPath: string[] | null;
}>();

const emit = defineEmits<{
  "select-path": [path: string[] | null];
}>();

const editorRef = ref<InstanceType<typeof JsonEditorVue> | null>(null);

const parsedJson = computed<unknown>(() => {
  if (
    !props.result?.Success ||
    props.result.Operation !== "parse" ||
    typeof props.result.Data !== "string"
  ) {
    return null;
  }
  try {
    return JSON.parse(props.result.Data);
  } catch {
    return null;
  }
});

const recovery = computed(() => {
  const hints: Record<string, string> = {
    "invalid-layout": "Check the declaration spelling and supported layout syntax.",
    "invalid-path": "Check the root and field names, including their letter case.",
    "read-failed":
      "Check that all required bytes are present and that the root, byte order, and pointer settings match the format.",
    "read-budget": "Compare the expected field sizes with Safety limits in the schema settings.",
  };
  return (
    hints[props.result?.Error?.Code ?? ""] ??
    "Review the code, path, and offset below and compare with the schema."
  );
});

const selectionTypes: readonly string[] = Object.values(SelectionType);

/**
 * json-editor-vue does not declare "select" in its component emits, so Vue also attaches @select as
 * a plain native DOM listener alongside the real onSelect passthrough - a native "select" event (a
 * bare Event, not a JSONEditorSelection) can reach this handler too. Only a real selection object
 * carries a recognized `type` discriminant; anything else is the stray native event and is ignored.
 */
function handleSelect(selection: unknown): void {
  if (
    !selection ||
    typeof selection !== "object" ||
    !("type" in selection) ||
    !selectionTypes.includes((selection as { type: string }).type)
  ) {
    return;
  }

  const typed = selection as JSONEditorSelection;
  if (typed.type === SelectionType.text) {
    emit("select-path", null);
    return;
  }
  const path = getFocusPath(typed);
  emit("select-path", path.length ? path : null);
}

watch(
  () => props.focusPath,
  async (path) => {
    const editor = editorRef.value?.jsonEditor;
    if (!editor) {
      return;
    }
    if (!path) {
      editor.select(undefined);
      return;
    }
    editor.select(createValueSelection(path));
    await editor.scrollTo(path);
  },
);
</script>

<template>
  <section class="result-panel">
    <div class="panel-topbar">
      <h2>Result</h2>
    </div>
    <div class="result-body">
      <p v-if="!result" class="placeholder">
        Run the schema against the binary data to see a result.
      </p>
      <template v-else>
        <div class="result-status" :class="result.Success ? 'success' : 'error'">
          {{ result.Success ? "Parse completed" : result.Error?.Message }}
        </div>
        <dl v-if="!result.Success && result.Error" class="error-details">
          <div>
            <dt>Code</dt>
            <dd>{{ result.Error.Code }}</dd>
          </div>
          <div v-if="result.Error.Path">
            <dt>Path</dt>
            <dd>{{ result.Error.Path }}</dd>
          </div>
          <div v-if="result.Error.Offset !== null">
            <dt>Offset</dt>
            <dd>{{ result.Error.Offset }}</dd>
          </div>
        </dl>
        <p v-if="!result.Success" class="recovery">{{ recovery }}</p>
        <div v-else class="json-body" data-testid="result-json">
          <JsonEditorVue
            ref="editorRef"
            class="jse-theme-dark"
            :model-value="parsedJson"
            :mode="Mode.tree"
            read-only
            :main-menu-bar="false"
            :navigation-bar="false"
            @select="handleSelect"
          />
        </div>
      </template>
    </div>
  </section>
</template>

<style scoped>
.result-panel {
  display: grid;
  grid-template-rows: auto minmax(0, 1fr);
  height: 100%;
  min-height: 0;
  min-width: 0;
}
.panel-topbar {
  display: flex;
  align-items: center;
  padding: 10px 14px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.08);
  background: var(--color-bg-secondary);
}
.panel-topbar h2 {
  margin: 0;
  font-size: 13px;
}
.result-body {
  display: flex;
  flex-direction: column;
  gap: 12px;
  min-height: 0;
  padding: 12px;
  overflow-y: auto;
}
.placeholder {
  color: var(--color-text-muted);
}
.result-status {
  border-radius: var(--radius-sm);
  font-weight: 700;
  padding: 10px 12px;
}
.result-status.success {
  background: rgba(0, 255, 136, 0.08);
  color: var(--color-success);
}
.result-status.error {
  background: rgba(255, 71, 87, 0.08);
  color: var(--color-error);
}
.error-details {
  display: grid;
  gap: 6px;
}
.error-details div {
  display: flex;
  gap: 10px;
}
dt {
  color: var(--color-text-muted);
  width: 70px;
}
dd {
  font-family: var(--font-mono);
  overflow-wrap: anywhere;
}
.recovery {
  color: var(--color-text-muted);
  font-size: 13px;
}
.json-body {
  flex: 1;
  min-height: 0;
}
.json-body :deep(.jse-main) {
  height: 100%;
}
</style>
