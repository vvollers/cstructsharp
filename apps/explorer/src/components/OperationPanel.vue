<script setup lang="ts">
/**
 * The explorer's operation form: the layout editor, the input bytes or JSON value, the update path, and the shared
 * settings button and dialog with a summary of the current settings. Submitting emits `run` with a request that
 * applies one byte budget to reading and writing; any edit emits `changed`, and the reset button emits `reset`.
 */
import { computed, reactive, ref, watch } from "vue";

import { VueHex } from "vuehex";
import SettingStatusItem from "@cstructsharp/app-shared/components/SettingStatusItem.vue";
import LayoutEditor from "@cstructsharp/app-shared/components/LayoutEditor.vue";
import LayoutSettingsDialog, {
  type LayoutSettingsValues,
} from "@cstructsharp/app-shared/components/LayoutSettingsDialog.vue";
import GeneratedCodeDialog from "./GeneratedCodeDialog.vue";
import { formatLayout } from "@cstructsharp/app-shared/format-layout";
import { loadMonaco } from "../load-monaco";

import type {
  ParseWithDebugOptions,
  SerializeOptions,
  UpdateOptions,
} from "@cstructsharp/app-shared/wasm/contract";
import { bytesToHex, hexToBytes } from "@cstructsharp/app-shared/hex";
import { formatBytes, OPTION_DEFAULTS } from "@cstructsharp/app-shared/options";
import type { LessonOperation } from "../lessons";

export type PanelOperation = "parse" | "serialize" | "update";

export interface OperationRequest {
  operation: PanelOperation;
  definition: string;
  binaryHex: string;
  jsonValue: string;
  path: string;
  options: ParseWithDebugOptions & SerializeOptions & UpdateOptions;
}

const props = defineProps<{
  binaryHex: string;
  definition: string;
  disabled: boolean;
  operation?: PanelOperation;
  initialAligned?: boolean;
  initialLittleEndian?: boolean;
  initialPointerSize?: number;
  initialRootType?: string | null;
  presets?: Partial<Record<PanelOperation, LessonOperation>>;
  initialOptions?: ParseWithDebugOptions;
  running?: boolean;
}>();

const emit = defineEmits<{
  run: [request: OperationRequest];
  changed: [];
  reset: [];
}>();

const operation = ref<PanelOperation>(props.operation ?? "parse");
const definition = ref(formatLayout(props.definition));
const binaryHex = ref(props.binaryHex);
const binaryEditorBytes = ref<Uint8Array<ArrayBufferLike>>(parseBinaryHex(props.binaryHex));
const jsonValue = ref(props.presets?.[operation.value]?.json ?? '{\n  "value": 42\n}');
const path = ref(
  props.presets?.update?.path ??
    (props.initialRootType ? `${props.initialRootType}.value` : "root.value"),
);
// The settings dialog edits this object in place. One byte budget (maxTotalBytesRead) limits both reading and
// writing in the explorer.
const settings = reactive<LayoutSettingsValues>({
  root: props.initialRootType ?? "",
  aligned: props.initialAligned ?? false,
  pointerSize: props.initialPointerSize ?? OPTION_DEFAULTS.pointerSize,
  littleEndian: props.initialLittleEndian !== false,
  addressingMode: "Absolute",
  origin: "0",
  dereferencePointers: OPTION_DEFAULTS.dereferencePointers,
  maxArrayElements: OPTION_DEFAULTS.maxArrayElements,
  maxStringBytes: OPTION_DEFAULTS.maxStringBytes,
  maxTotalBytesRead: props.initialOptions?.maxTotalBytesRead ?? OPTION_DEFAULTS.maxTotalBytesRead,
  maxNestingDepth: OPTION_DEFAULTS.maxNestingDepth,
});
const settingsOpen = ref(false);

const generatedCode = ref<InstanceType<typeof GeneratedCodeDialog> | null>(null);
const settingsSummary = computed(() => [
  {
    label: "Root",
    value: settings.root.trim() || "first declaration",
    color: "#67e8f9",
    explanation: settings.root.trim()
      ? "This name selects the layout type or path where the operation starts. It must match the declaration's spelling and capitalization."
      : "No root name is supplied, so the first declaration in the layout is used.",
  },
  {
    label: "Order",
    value: `${settings.littleEndian ? "Little" : "Big"} endian`,
    color: "#93c5fd",
    explanation: settings.littleEndian
      ? "The least significant byte comes first. For example, 01 00 represents 1 in a two-byte integer. A field's explicit byte order can override this default."
      : "The most significant byte comes first. For example, 00 01 represents 1 in a two-byte integer. A field's explicit byte order can override this default.",
  },
  {
    label: "Aligned",
    value: settings.aligned,
    color: "#c4b5fd",
    explanation: settings.aligned
      ? "Fields may have padding bytes before them to meet alignment rules. This can increase the total size of a record."
      : "Fields are packed together without automatic alignment padding. The next field starts immediately after the previous field.",
  },
  {
    label: "Pointers",
    value: `${settings.pointerSize} B`,
    color: "#f9a8d4",
    explanation:
      "Each stored pointer address occupies this many bytes. This is the address width, not the size of the data it points to.",
  },
  {
    label: "Address",
    value: settings.addressingMode,
    color: "#fda4af",
    explanation:
      settings.addressingMode === "Relative"
        ? "Stored pointer addresses are offsets from the configured origin. Add the origin to the stored address to locate the target."
        : "Stored pointer addresses are positions from the start of the input. The origin is not added to them.",
  },
  {
    label: "Origin",
    value: settings.origin,
    color: "#fdba74",
    explanation:
      settings.addressingMode === "Relative"
        ? "This byte position is added to a stored relative pointer address to find its target. It does not move the start of the root record."
        : "This byte position would be added to relative pointer addresses. It is currently unused because addressing is Absolute.",
  },
  {
    label: "Follow pointers",
    value: settings.dereferencePointers,
    color: "#86efac",
    explanation: settings.dereferencePointers
      ? "Reads can visit the data at a pointer's target. Updates may also traverse a pointer when the selected path requires it."
      : "Reads keep the pointer address without reading its target. Updates cannot follow a pointer to change its target.",
  },
  {
    label: "Total",
    value: formatBytes(settings.maxTotalBytesRead),
    color: "#fde68a",
    explanation:
      "This budget limits bytes read or written, including rereads during traversal. Debug parsing also rereads field bytes for its debug view, so the budget can need to be larger than the input. Exceeding the applicable budget stops the operation.",
  },
  {
    label: "Elements",
    value: settings.maxArrayElements.toLocaleString(),
    color: "#bef264",
    explanation:
      "An array may contain at most this many elements. This limits work on large or untrusted inputs; it does not set the array's declared length.",
  },
  {
    label: "Text",
    value: formatBytes(settings.maxStringBytes),
    color: "#5eead4",
    explanation:
      "A string may use at most this many encoded bytes. Bytes and characters are not always the same count, especially with multi-byte encodings.",
  },
  {
    label: "Depth",
    value: settings.maxNestingDepth.toLocaleString(),
    color: "#d8b4fe",
    explanation:
      "This limits how deeply an operation can nest or traverse values. Deeper structures fail instead of continuing without a bound.",
  },
]);

// Any edit, including a single setting inside the settings object (hence `deep`), makes the last result stale.
watch([operation, definition, binaryHex, jsonValue, path, settings], () => emit("changed"), {
  deep: true,
});

watch(
  () => props.binaryHex,
  (value) => {
    binaryHex.value = value;
    binaryEditorBytes.value = parseBinaryHex(value);
  },
);

/**
 * Converts hex text to bytes for the input hex view; invalid text yields no bytes.
 * @param value The hex text.
 * @returns The bytes, or an empty array when the text is not valid hex.
 */
function parseBinaryHex(value: string): Uint8Array {
  try {
    return hexToBytes(value);
  } catch {
    return new Uint8Array();
  }
}

/**
 * Keeps the hex input in step with bytes edited in the input hex view.
 * @param bytes The edited bytes.
 */
function handleBinaryEdited(bytes: Uint8Array): void {
  binaryEditorBytes.value = bytes;
  binaryHex.value = bytesToHex(bytes);
}

/** Emits `run` with the current request, unless the settings dialog is open. */
function submit(): void {
  if (settingsOpen.value) return;
  emit("run", currentRequest());
}
/**
 * Collects the selected operation, inputs and settings into a request.
 * @returns The request; one byte budget applies to both reading and writing.
 */
function currentRequest(): OperationRequest {
  // The generated examples list the options in key order: the settings in their declared order up to
  // maxTotalBytesRead, then the written and traversal byte budgets, then the two depth limits. Taking
  // maxNestingDepth out first lets it follow the byte budgets.
  const { maxNestingDepth, ...rest } = settings;
  return {
    operation: operation.value,
    definition: definition.value,
    binaryHex: binaryHex.value,
    jsonValue: jsonValue.value,
    path: path.value,
    options: {
      ...rest,
      root: rest.root.trim() || null,
      maxTotalBytesWritten: rest.maxTotalBytesRead,
      maxTraversalBytesRead: rest.maxTotalBytesRead,
      maxNestingDepth,
      maxTraversalNestingDepth: maxNestingDepth,
    },
  };
}
</script>

<template>
  <form class="operation-panel" @submit.prevent="submit">
    <div class="operation-heading">
      <LayoutSettingsDialog v-model:open="settingsOpen" :model-value="settings" />
      <button
        class="icon-button"
        type="button"
        aria-label="Reset example"
        title="Reset example"
        @click="emit('reset')"
      >
        <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 10a8 8 0 1 1 1 8M4 4v6h6" /></svg>
      </button>
      <h2>Operation panel</h2>
      <button
        class="icon-button"
        type="button"
        aria-label="Generate C#"
        title="Generate C#"
        :disabled="disabled"
        @click="generatedCode?.open(currentRequest(), 'csharp')"
      >
        <span aria-hidden="true">C#</span>
      </button>
      <button
        class="icon-button"
        type="button"
        aria-label="Generate JavaScript"
        title="Generate JavaScript"
        :disabled="disabled"
        @click="generatedCode?.open(currentRequest(), 'javascript')"
      >
        <span aria-hidden="true">JS</span>
      </button>
    </div>
    <p class="settings-summary" aria-label="Current operation settings">
      <SettingStatusItem v-for="setting in settingsSummary" :key="setting.label" v-bind="setting" />
    </p>

    <div class="field">
      <label>Binary layout (C-like definition)</label>
      <LayoutEditor v-model="definition" :loadMonaco />
    </div>

    <div v-if="operation !== 'serialize'" class="field">
      <label for="binary">Binary data (hex)</label>
      <div id="binary" class="binary-input-editor" data-testid="binary-input">
        <VueHex
          v-model="binaryEditorBytes"
          data-mode="buffer"
          theme="dark"
          :editable="true"
          :cursor="true"
          :search="true"
          statusbar="bottom"
          :bytes-per-row="16"
          aria-label="Binary data editor"
          @update:model-value="handleBinaryEdited"
        />
      </div>
      <p class="field-hint">Edit bytes directly or paste hexadecimal data into the hex column.</p>
    </div>

    <div v-if="operation !== 'parse'" class="field">
      <label for="json-value"
        >{{ operation === "serialize" ? "Value" : "Replacement" }} (JSON)</label
      >
      <textarea
        id="json-value"
        v-model="jsonValue"
        data-testid="json-input"
        rows="7"
        spellcheck="false"
      ></textarea>
    </div>

    <div v-if="operation === 'update'" class="field">
      <label for="path">Update path</label>
      <input id="path" v-model="path" data-testid="path-input" spellcheck="false" />
    </div>

    <button class="btn btn-primary" type="submit" :disabled="disabled">
      {{ running ? "Running…" : disabled ? "WebAssembly is not ready" : `Run ${operation}` }}
    </button>
  </form>
  <GeneratedCodeDialog ref="generatedCode" />
</template>

<style scoped>
.operation-heading {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}
.operation-heading h2 {
  margin-inline-end: auto;
}
.settings-summary {
  display: flex;
  flex-wrap: wrap;
  gap: 5px 16px;
  margin: -8px 0 0;
  width: 100%;
  padding: 9px 12px;
  border: 1px solid #3c526f;
  border-left: 3px solid var(--color-accent);
  border-radius: 6px;
  background: linear-gradient(110deg, #172a40, #202039);
  font-size: 12px;
  line-height: 1.5;
  color: var(--color-text-muted);
  overflow-wrap: anywhere;
}
.operation-panel {
  display: grid;
  grid-template-columns: minmax(0, 1fr);
  gap: 16px;
}

textarea,
#path {
  font-family: var(--font-mono);
}

textarea {
  line-height: 1.45;
  resize: vertical;
}

.binary-input-editor {
  height: 150px;
}

.binary-input-editor :deep(.vuehex) {
  border: 1px solid rgba(255, 255, 255, 0.12);
  border-radius: var(--radius-sm);
}
</style>
