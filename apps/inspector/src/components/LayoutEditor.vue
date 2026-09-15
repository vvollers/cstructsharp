<script setup lang="ts">
import { onMounted, onBeforeUnmount, ref, watch } from "vue";
import type { editor } from "monaco-editor/editor";
import { formatLayout } from "../format-layout";

const props = defineProps<{
  modelValue: string;
  language?: string;
  fill?: boolean;
  label?: string;
  readOnly?: boolean;
}>();
const emit = defineEmits<{ "update:modelValue": [value: string] }>();
const host = ref<HTMLElement | null>(null);
const height = ref(160);
let instance: editor.IStandaloneCodeEditor | undefined;
let model: editor.ITextModel | undefined;
let disposed = false;

// Load Monaco when this editor first appears. The import is asynchronous, so check that the
// component still exists before creating the editor: the user might have closed the panel meanwhile.
onMounted(async () => {
  const { monaco, CSTRUCT_LANGUAGE_ID } = await import("../cstruct-language");
  if (disposed || !host.value) return;

  // The model holds the text; the editor instance provides the visible controls for editing it.
  model = monaco.editor.createModel(props.modelValue, props.language ?? CSTRUCT_LANGUAGE_ID);
  model.updateOptions({ tabSize: 4, insertSpaces: true });
  instance = monaco.editor.create(host.value, {
    model,
    readOnly: props.readOnly ?? false,
    theme: "vs-dark",
    automaticLayout: true,
    minimap: { enabled: false },
    fontSize: 13,
    lineHeight: 21,
    scrollBeyondLastLine: false,
    padding: { top: 10, bottom: 10 },
    lineNumbersMinChars: 3,
    wordWrap: "on",
    folding: true,
    tabSize: 4,
    insertSpaces: true,
    ariaLabel: props.label ?? "Binary layout (CStruct definition)",
    stickyScroll: { enabled: false },
  });

  const resize = () => {
    height.value = Math.min(440, Math.max(130, instance!.getContentHeight()));
  };
  instance.onDidContentSizeChange(resize);
  instance.onDidChangeModelContent(() => emit("update:modelValue", instance!.getValue()));

  // Register our formatter only for CStruct text, not for any other language shown in this editor.
  if (!props.language || props.language === CSTRUCT_LANGUAGE_ID)
    instance.addAction({
      id: "format-binary-layout",
      label: "Format binary layout",
      contextMenuGroupId: "1_modification",
      keybindings: [monaco.KeyMod.Shift | monaco.KeyMod.Alt | monaco.KeyCode.KeyF],
      run: (ed) => {
        ed.executeEdits("format-layout", [
          { range: model!.getFullModelRange(), text: formatLayout(ed.getValue()) },
        ]);
      },
    });

  resize();
});

// Parent updates must reach Monaco too. Skip text that already matches: it may be the edit Monaco
// just sent to the parent, and calling setValue again would unnecessarily clear its undo history.
watch(
  () => props.modelValue,
  (value) => {
    if (instance && instance.getValue() !== value) instance.setValue(value);
  },
);
onBeforeUnmount(() => {
  disposed = true;
  instance?.dispose();
  model?.dispose();
});
</script>

<template>
  <div
    ref="host"
    class="layout-editor"
    :data-testid="readOnly ? 'parsed-json' : fill ? 'generated-source' : 'definition-input'"
    :style="{ height: fill ? '100%' : `${height}px` }"
  ></div>
</template>

<style scoped>
.layout-editor {
  width: 100%;
  min-width: 0;
  border: 1px solid rgba(255, 255, 255, 0.12);
  border-radius: 6px;
  overflow: hidden;
}
</style>
