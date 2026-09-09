<script setup lang="ts">
import type { FormatExample } from "../formats";

defineProps<{
  examples: FormatExample[];
  selectedId: string | null;
}>();
const emit = defineEmits<{
  select: [example: FormatExample];
  new: [];
}>();
</script>

<template>
  <nav class="example-list" aria-label="Binary format examples">
    <h2>Examples</h2>
    <button
      class="example-item new-item"
      type="button"
      data-testid="example-new"
      :class="{ active: selectedId === null }"
      :aria-current="selectedId === null ? 'true' : undefined"
      @click="emit('new')"
    >
      <span class="example-title">+ New</span>
      <span class="example-hint">Start from a blank schema</span>
    </button>
    <ul>
      <li v-for="example in examples" :key="example.id">
        <button
          class="example-item"
          type="button"
          :data-testid="`example-${example.id}`"
          :class="{ active: example.id === selectedId }"
          :aria-current="example.id === selectedId ? 'true' : undefined"
          @click="emit('select', example)"
        >
          <span class="example-title">{{ example.title }}</span>
          <span class="example-hint">{{ example.documentation.summary }}</span>
        </button>
      </li>
    </ul>
  </nav>
</template>

<style scoped>
.example-list {
  display: flex;
  flex-direction: column;
  gap: 10px;
  height: 100%;
  min-height: 0;
  padding: 14px 12px;
  overflow-y: auto;
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
}
.example-item {
  display: flex;
  flex-direction: column;
  gap: 3px;
  width: 100%;
  padding: 10px 12px;
  border: 1px solid transparent;
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--color-text);
  text-align: left;
  cursor: pointer;
}
.example-item:hover {
  background: var(--color-bg-tertiary);
}
.example-item.active {
  border-color: var(--color-accent);
  background: var(--color-bg-tertiary);
}
.new-item {
  border: 1px dashed rgba(255, 255, 255, 0.25);
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
