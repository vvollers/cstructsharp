<script setup lang="ts">
/**
 * The teaching notes of a lesson below its explanation: what to know first, an exercise to try in the panel, the
 * answer behind a disclosure so the reader can predict before looking, and the guide that explains the topic.
 */
import type { Lesson } from "../lessons";

defineProps<{
  /** The selected lesson. */
  lesson: Pick<Lesson, "prerequisite" | "exercise" | "answer" | "guide">;
  /** The documentation site's base URL, ending in a slash; the lesson's guide path is relative to it. */
  docsBase: string;
}>();
</script>

<template>
  <div class="lesson-notes">
    <p><strong>Before you start:</strong> {{ lesson.prerequisite }}</p>
    <p><strong>Try it:</strong> {{ lesson.exercise }}</p>
    <details>
      <summary>Show the answer</summary>
      <p>{{ lesson.answer }}</p>
    </details>
    <a :href="`${docsBase}${lesson.guide}`">Read the guide for this topic</a>
  </div>
</template>

<style scoped>
.lesson-notes {
  display: grid;
  gap: 6px;
  font-size: 14px;
}

.lesson-notes p {
  margin: 0;
}

.lesson-notes strong {
  color: var(--color-accent);
}

.lesson-notes summary {
  cursor: pointer;
  color: var(--color-text-muted);
}

.lesson-notes details p {
  margin-top: 4px;
}

.lesson-notes a {
  color: var(--color-accent);
}
</style>
