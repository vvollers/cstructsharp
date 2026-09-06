<script setup lang="ts">
import { nextTick, ref } from "vue";
import LayoutEditor from "./LayoutEditor.vue";
import type { WorkbenchRequest } from "./OperationWorkbench.vue";
import { generateExample } from "../generate-example";
import {
  hexToBytes,
  parseWithDebug,
  serializeToBase64,
  updateStreamToBase64,
} from "../wasm/cstruct-wasm";

type Language = "csharp" | "javascript";
const dialog = ref<HTMLDialogElement | null>(null);
const language = ref<Language>("csharp");
const sources = ref({ csharp: "", javascript: "" });
const opened = ref(false);
const error = ref("");
const copyStatus = ref("");
const revision = ref(0);
let opener: HTMLElement | null = null;
function open(request: WorkbenchRequest, tab: Language) {
  opener = document.activeElement as HTMLElement;
  language.value = tab;
  error.value = "";
  copyStatus.value = "";
  revision.value++;
  try {
    // Run a fresh operation on this snapshot; never reuse a potentially stale result panel.
    const result =
      request.operation === "parse"
        ? parseWithDebug(request.definition, hexToBytes(request.binaryHex), request.options)
        : request.operation === "serialize"
          ? serializeToBase64(request.definition, JSON.parse(request.jsonValue), request.options)
          : updateStreamToBase64(
              request.definition,
              hexToBytes(request.binaryHex),
              request.path,
              JSON.parse(request.jsonValue),
              request.options,
            );
    sources.value = generateExample(request, result);
  } catch (caught) {
    error.value = `Could not generate the example: ${caught instanceof Error ? caught.message : String(caught)}. Check the workbench inputs and try again.`;
  }
  opened.value = true;
  dialog.value?.showModal();
}
function close() {
  dialog.value?.close();
  opened.value = false;
  opener?.focus();
}
async function selectTab(tab: Language, focus = false) {
  language.value = tab;
  copyStatus.value = "";
  if (focus) {
    await nextTick();
    dialog.value?.querySelector<HTMLButtonElement>(`#generated-tab-${tab}`)?.focus();
  }
}
async function copy() {
  try {
    await navigator.clipboard.writeText(sources.value[language.value]);
    copyStatus.value = "Copied.";
  } catch {
    copyStatus.value = "Select the code in the editor and copy it manually.";
  }
}
function download() {
  const blob = new Blob([sources.value[language.value]], { type: "text/plain;charset=utf-8" });
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = language.value === "csharp" ? "Program.cs" : "app.js";
  link.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
defineExpose({ open });
</script>

<template>
  <dialog
    ref="dialog"
    class="code-dialog"
    aria-labelledby="generated-title"
    @cancel.prevent="close"
    @click="$event.target === dialog && close()"
  >
    <div class="code-content">
      <header>
        <h2 id="generated-title">Generated example</h2>
        <button type="button" aria-label="Close generated code" title="Close" @click="close">
          ×
        </button>
      </header>
      <p class="hint">
        Snapshot of your current inputs. Edit or copy the example here; changes do not affect the
        workbench.
      </p>
      <div class="code-toolbar">
        <div
          role="tablist"
          aria-label="Source language"
          @keydown.right.prevent="selectTab(language === 'csharp' ? 'javascript' : 'csharp', true)"
          @keydown.left.prevent="selectTab(language === 'csharp' ? 'javascript' : 'csharp', true)"
        >
          <button
            v-for="tab in ['csharp', 'javascript'] as const"
            :id="`generated-tab-${tab}`"
            :key="tab"
            role="tab"
            type="button"
            :aria-selected="language === tab"
            :tabindex="language === tab ? 0 : -1"
            aria-controls="generated-panel"
            @click="selectTab(tab)"
          >
            {{ tab === "csharp" ? "C#" : "JavaScript" }}
          </button>
        </div>
        <button type="button" :disabled="!!error" @click="copy">Copy code</button>
        <button type="button" :disabled="!!error" @click="download">Download</button>
        <span role="status">{{ copyStatus }}</span>
      </div>
      <p v-if="error" role="alert">{{ error }}</p>
      <div
        v-else
        id="generated-panel"
        role="tabpanel"
        :aria-labelledby="`generated-tab-${language}`"
        class="code-panel"
      >
        <LayoutEditor
          v-if="opened"
          :key="`${revision}-${language}`"
          v-model="sources[language]"
          :language="language"
          :label="`${language} generated example`"
          fill
        />
      </div>
    </div>
  </dialog>
</template>

<style scoped>
.code-dialog {
  width: min(1280px, calc(100vw - 32px));
  height: min(900px, calc(100dvh - 32px));
  margin: auto;
  padding: 0;
  border: 1px solid #526580;
  border-radius: 12px;
  background: var(--color-bg-secondary);
  color: var(--color-text);
}
.code-dialog::backdrop {
  background: #000a;
}
.code-content {
  display: flex;
  flex-direction: column;
  gap: 12px;
  height: 100%;
  padding: 18px;
}
header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
h2,
p {
  margin: 0;
}
.hint {
  font-size: 12px;
  color: var(--color-text-muted);
}
.code-toolbar {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
  font-size: 12px;
}
[role="tablist"] {
  display: flex;
  gap: 4px;
  margin-right: auto;
}
button {
  padding: 6px 12px;
  border: 1px solid #526580;
  border-radius: 5px;
  background: var(--color-bg-primary);
  color: var(--color-text);
  cursor: pointer;
}
[aria-selected="true"] {
  border-color: var(--color-accent);
  color: var(--color-accent);
  background: #173147;
}
.code-panel {
  flex: 1;
  min-height: 0;
}
.editor-tab {
  height: 100%;
}
</style>
