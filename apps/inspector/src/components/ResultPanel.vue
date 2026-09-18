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
    !props.result?.success ||
    props.result.operation !== "parse" ||
    props.result.data instanceof Uint8Array
  ) {
    return null;
  }

  return { [props.result.root ?? "root"]: props.result.data };
});

const recovery = computed(() => {
  const hints: Record<string, string> = {
    "invalid-layout": "Check the declaration spelling and supported layout syntax.",
    "invalid-path": "Check the root and field names, including their letter case.",
    "read-failed":
      "Check that all required bytes are present and that the root, byte order, and pointer settings match the format.",
    "read-budget":
      "Compare the expected field sizes with Decoded-data budgets in the schema settings.",
    "invalid-input":
      "Check the input and schema settings. File size is independent of the read safety limits.",
    "file-read-failed": "Reload the file after checking its location and access permissions.",
  };

  return (
    hints[props.result?.error?.code ?? ""] ??
    "Review the code, path, and offset below and compare with the schema."
  );
});

const selectionTypes: readonly string[] = Object.values(SelectionType);

// This handler can receive either a JSON-tree selection or a normal browser `select` event.
// Only JSON selections have one of these recognized types, so check before reading their path.
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

// A click in the hex view gives us a JSON path such as ["root", "header", "size"].
// Wait until Vue has updated the result panel, then select that field and scroll to it.
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
  { flush: "post" },
);
</script>

<template>
  <section class="result-panel">
    <div class="result-body">
      <p v-if="!result" class="placeholder">
        Run the schema against the binary data to see a result.
      </p>
      <template v-else>
        <div class="result-status" :class="result.success ? 'success' : 'error'">
          {{ result.success ? "Parse completed" : result.error?.message }}
        </div>
        <dl v-if="!result.success && result.error" class="error-details">
          <div>
            <dt>Code</dt>
            <dd>{{ result.error.code }}</dd>
          </div>
          <div v-if="result.error.path">
            <dt>Path</dt>
            <dd>{{ result.error.path }}</dd>
          </div>
          <div v-if="result.error.offset !== null">
            <dt>Offset</dt>
            <dd>
              {{ result.error.offset }} (0x{{ result.error.offset.toString(16).toUpperCase() }})
            </dd>
          </div>
        </dl>
        <p v-if="!result.success" class="recovery">{{ recovery }}</p>
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
  display: flex;
  flex-direction: column;
  height: 100%;
  min-height: 0;
  min-width: 0;
}
.result-body {
  display: flex;
  flex: 1;
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
