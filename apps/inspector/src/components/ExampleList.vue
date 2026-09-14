<script setup lang="ts">
import { Icon } from "@iconify/vue";
import { computed, ref } from "vue";
import { filePresentation } from "../file-type-icons";
import type { FormatExample } from "../formats";
import { detectableFormatCount } from "../detected-schemas";

// Bundle the small set of icons locally so the catalog also works offline.
const fileTypes: Record<string, { label: string; kind: string }> = {
  bmp: { label: "BMP", kind: "Bitmap image" },
  wav: { label: "WAV", kind: "Wave audio" },
  zip: { label: "ZIP", kind: "ZIP archive" },
  png: { label: "PNG", kind: "PNG image" },
  jpg: { label: "JPG", kind: "JPEG image" },
  "pe-exe": { label: "EXE", kind: "Executable" },
  "pe-dll": { label: "DLL", kind: "Shared library" },
  ico: { label: "ICO", kind: "Windows icon" },
  tar: { label: "TAR", kind: "TAR archive" },
};

function fileType(example: FormatExample) {
  const extension = example.extension ?? example.id.replace(/^pe-/, "");
  return {
    ...filePresentation(extension),
    ...(fileTypes[example.id] ?? {
      label: extension.toUpperCase(),
      kind: example.title.split(" · ").slice(1).join(" · ") || example.title,
    }),
  };
}

const props = defineProps<{
  examples: FormatExample[];
  selectedId: string | null;
  selectedExtension?: string;
  detecting?: boolean;
  detectionMessage?: string;
}>();
const filter = ref("");
const filteredExamples = computed(() => {
  const terms = filter.value.trim().toLowerCase().replace(/^\./, "").split(/\s+/).filter(Boolean);
  return props.examples.filter((example) => {
    const type = fileType(example);
    const text = `${type.label} ${type.kind} ${type.family} ${example.title}`.toLowerCase();
    return terms.every((term) => text.includes(term));
  });
});
function isSelected(example: FormatExample): boolean {
  return (
    example.id === props.selectedId ||
    (!!props.selectedExtension && example.extension === props.selectedExtension)
  );
}
const emit = defineEmits<{
  select: [example: FormatExample];
  new: [];
  detect: [];
}>();
</script>

<template>
  <nav class="example-list" aria-label="Binary format examples">
    <h2>Examples</h2>
    <button class="btn btn-primary detect-button" type="button" @click="emit('detect')">
      <svg
        class="button-icon"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        stroke-width="1.8"
        aria-hidden="true"
      >
        <path d="M3 10V5h6l2 2h9v3M3 10h19l-3 10H5L3 10Z" />
      </svg>
      {{ detecting ? "Detecting…" : "Load & detect" }}
    </button>
    <p class="detection-status" role="status">
      {{ detectionMessage || `${detectableFormatCount} detectable file types` }}
    </p>
    <button
      class="example-item new-item"
      type="button"
      data-testid="example-new"
      :class="{ active: selectedId === null }"
      :aria-current="selectedId === null ? 'true' : undefined"
      @click="emit('new')"
    >
      <span class="new-icon" aria-hidden="true">+</span>
      <span class="example-title">New schema</span>
    </button>
    <label class="filter-label" for="schema-filter">Filter file types</label>
    <div class="filter-field">
      <input
        id="schema-filter"
        v-model="filter"
        type="search"
        placeholder="Extension or format…"
        autocomplete="off"
      />
      <button v-if="filter" type="button" aria-label="Clear file type filter" @click="filter = ''">
        ×
      </button>
    </div>
    <p class="filter-count" role="status">
      {{ filteredExamples.length }} of {{ examples.length }} schemas
    </p>
    <ul>
      <li v-for="example in filteredExamples" :key="example.id">
        <button
          class="example-item"
          type="button"
          :data-testid="`example-${example.id}`"
          :class="{ active: isSelected(example) }"
          :aria-current="isSelected(example) ? 'true' : undefined"
          :title="example.documentation.summary"
          @click="emit('select', example)"
        >
          <Icon class="file-icon" :icon="fileType(example).icon" aria-hidden="true" />
          <span class="file-label">
            <span class="example-title">{{ fileType(example).label }}</span>
            <span class="example-hint">{{ fileType(example).kind }}</span>
            <span v-if="example.schemaOnly" class="schema-only">Schema · load your file</span>
          </span>
        </button>
      </li>
    </ul>
    <p v-if="!filteredExamples.length" class="detection-status">No matching file types.</p>
  </nav>
</template>

<style scoped>
.example-list {
  display: flex;
  flex: 0 0 210px;
  flex-direction: column;
  gap: 10px;
  height: 100%;
  min-height: 0;
  padding: 14px 12px;
  overflow: hidden;
  border-right: 1px solid rgba(255, 255, 255, 0.08);
  background: var(--color-bg-secondary);
}
h2 {
  margin: 0 4px;
  font-size: 12px;
  font-weight: 700;
  letter-spacing: 0.06em;
  text-transform: uppercase;
  color: var(--color-text-muted);
}
ul {
  display: flex;
  flex-direction: column;
  gap: 6px;
  margin: 0;
  padding: 0;
  list-style: none;
  min-height: 0;
  overflow-y: auto;
  padding: 3px;
}
.example-list > :not(ul) {
  flex-shrink: 0;
}
.filter-label,
.filter-count,
.schema-only {
  font-size: 11px;
  color: var(--color-text-muted);
}
.filter-field {
  position: relative;
}
.filter-field input {
  width: 100%;
  padding: 8px 28px 8px 9px;
  border: 1px solid #606060;
  border-radius: var(--radius-sm);
  background: var(--color-bg-primary);
  color: var(--color-text);
  font: inherit;
  font-size: 12px;
}
.filter-field input::-webkit-search-cancel-button {
  display: none;
}
.filter-field button {
  position: absolute;
  right: 4px;
  top: 4px;
  padding: 0 5px;
  border: none;
  background: transparent;
  color: var(--color-text);
  font-size: 20px;
  cursor: pointer;
}
.example-item {
  display: flex;
  align-items: center;
  gap: 10px;
  width: 100%;
  padding: 10px 12px;
  border: 1px solid rgba(255, 255, 255, 0.14);
  border-radius: var(--radius-sm);
  background: var(--color-bg-tertiary);
  color: var(--color-text);
  text-align: left;
  cursor: pointer;
}
.example-item:hover {
  border-color: #6b8599;
  background: #343b42;
}
.example-item.active {
  border-color: var(--color-accent);
  background: #193b54;
  box-shadow: inset 3px 0 var(--color-accent);
  color: #ffffff;
}
.new-item {
  border: 1px dashed rgba(255, 255, 255, 0.25);
}
.detect-button {
  width: 100%;
  flex-shrink: 0;
}
.detection-status {
  margin: 0 4px;
  font-size: 11px;
  line-height: 1.4;
  color: var(--color-text-muted);
}
.file-icon,
.new-icon {
  width: 28px;
  height: 28px;
  flex-shrink: 0;
}
.new-icon {
  display: grid;
  place-items: center;
  font-size: 24px;
  line-height: 1;
}
.file-label {
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.example-title {
  font-size: 13px;
  font-weight: 600;
}
.example-hint {
  font-size: 11px;
  line-height: 1.4;
  color: var(--color-text-muted);
  overflow-wrap: anywhere;
}
</style>
