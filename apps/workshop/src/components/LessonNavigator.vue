<script setup lang="ts">
import { computed, ref } from "vue";
import { lessons } from "../lessons";
defineProps<{ selectedId: string }>();
defineEmits<{ select: [id: string] }>();
const query = ref("");
const levels = ["Beginner", "Intermediate", "Advanced"] as const;
const filtered = computed(() =>
  lessons.filter((lesson) =>
    `${lesson.title} ${lesson.documentation?.summary} ${lesson.tags.join(" ")}`
      .toLowerCase()
      .includes(query.value.trim().toLowerCase()),
  ),
);
</script>

<template>
  <aside class="card lesson-navigator">
    <h2>Learn step by step</h2>
    <label for="lesson-search">Find a task or topic</label>
    <input
      id="lesson-search"
      v-model="query"
      type="search"
      placeholder="Try write, string, or byte order"
    />
    <section
      v-for="level in levels.filter((value) => filtered.some((lesson) => lesson.level === value))"
      :key="level"
    >
      <h3>{{ level }}</h3>
      <button
        v-for="lesson in filtered.filter((item) => item.level === level)"
        :key="lesson.id"
        type="button"
        :aria-current="selectedId === lesson.id ? 'page' : undefined"
        @click="$emit('select', lesson.id)"
      >
        {{ lesson.title }}
      </button>
    </section>
    <p v-if="!filtered.length">No lessons match. Try a shorter topic or browse All tests.</p>
  </aside>
</template>

<style scoped>
.lesson-navigator {
  align-self: start;
  display: grid;
  gap: 1rem;
}
input {
  width: 100%;
  padding: 0.6rem;
  font: inherit;
  color: var(--color-text);
  background: var(--color-bg-primary);
  border: 1px solid var(--color-text-muted);
  border-radius: 6px;
}
button {
  display: block;
  width: 100%;
  text-align: left;
  padding: 0.65rem;
  margin-top: 0.4rem;
  border: 1px solid var(--color-text-muted);
  background: var(--color-bg-secondary);
  color: var(--color-text);
  cursor: pointer;
}
button[aria-current] {
  border: 2px solid var(--color-accent);
}
</style>
