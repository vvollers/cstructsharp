<script setup lang="ts">
import { computed, ref, watch } from "vue";

import { isRunnable, type TestEntry, type TestManifest } from "../demo-types";

const props = defineProps<{
  manifest: TestManifest;
  selectedId: string;
}>();

const emit = defineEmits<{
  "update:selectedId": [id: string];
}>();

const query = ref("");
const runnableOnly = ref(true);

const visible = computed(() => {
  const normalized = query.value.trim().toLowerCase();
  return props.manifest.tests.filter((test) => {
    if (runnableOnly.value && !isRunnable(test)) {
      return false;
    }
    return (
      !normalized ||
      `${test.id} ${test.className} ${test.methodName} ${test.filePath}`
        .toLowerCase()
        .includes(normalized)
    );
  });
});

const groups = computed(() => {
  const byClass = new Map<string, TestEntry[]>();
  for (const test of visible.value) {
    const entries = byClass.get(test.className) ?? [];
    entries.push(test);
    byClass.set(test.className, entries);
  }

  return [...byClass]
    .sort(([left], [right]) => left.localeCompare(right))
    .map(([name, tests]) => ({
      name,
      tests: tests.sort((left, right) => left.methodName.localeCompare(right.methodName)),
    }));
});

watch(visible, (tests) => {
  if (!tests.some((test) => test.id === props.selectedId)) {
    emit("update:selectedId", tests[0]?.id ?? "");
  }
});
</script>

<template>
  <aside class="card navigator">
    <h2>Examples</h2>
    <p class="summary">
      {{ manifest.runnableTests }} runnable from {{ manifest.totalTests }} test methods
    </p>
    <input v-model="query" aria-label="Filter examples" placeholder="Filter examples…" />
    <label class="runnable-filter">
      <input v-model="runnableOnly" type="checkbox" />
      Runnable only
    </label>
    <div class="groups">
      <section v-for="group in groups" :key="group.name">
        <h3>
          {{ group.name }} <span>{{ group.tests.length }}</span>
        </h3>
        <button
          v-for="test in group.tests"
          :key="test.id"
          type="button"
          :class="{ active: selectedId === test.id, unsupported: !test.runnable }"
          @click="emit('update:selectedId', test.id)"
        >
          {{ test.methodName }}
          <small v-if="!test.runnable">reference</small>
        </button>
      </section>
    </div>
  </aside>
</template>

<style scoped>
.navigator {
  align-self: start;
  max-height: calc(100vh - 132px);
  overflow: auto;
  position: sticky;
  top: 104px;
}

h2 {
  font-size: 20px;
}

.summary {
  color: var(--color-text-muted);
  font-size: 12px;
  margin: 4px 0 12px;
}

input {
  width: 100%;
  border: 1px solid rgba(255, 255, 255, 0.12);
  border-radius: var(--radius-sm);
  background: var(--color-bg-primary);
  color: var(--color-text);
  padding: 9px 10px;
}

.runnable-filter {
  display: flex;
  align-items: center;
  gap: 8px;
  color: var(--color-text-muted);
  font-size: 13px;
  margin: 10px 0 16px;
}

.runnable-filter input {
  width: auto;
}

.groups {
  display: grid;
  gap: 16px;
}

h3 {
  color: var(--color-text-muted);
  font-size: 11px;
  letter-spacing: 0.05em;
  margin-bottom: 5px;
  overflow-wrap: anywhere;
  text-transform: uppercase;
}

h3 span {
  opacity: 0.7;
}

button {
  display: flex;
  width: 100%;
  justify-content: space-between;
  border: 0;
  border-left: 2px solid transparent;
  background: transparent;
  color: var(--color-text-muted);
  cursor: pointer;
  font-size: 12px;
  padding: 6px 8px;
  text-align: left;
}

button:hover,
button.active {
  border-left-color: var(--color-accent);
  background: rgba(0, 212, 255, 0.08);
  color: var(--color-text);
}

button.unsupported {
  opacity: 0.6;
}

small {
  font-size: 9px;
}

@media (max-width: 900px) {
  .navigator {
    max-height: 360px;
    position: static;
  }
}
</style>
