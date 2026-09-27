<script setup lang="ts">
/**
 * The explorer page: the lesson and test catalog, the selected example with its operation panel, and the result.
 * Routing, the runtime and the operation state live in composables; this component connects them to the panels.
 */
import { computed, ref } from "vue";

import OperationPanel from "./components/OperationPanel.vue";
import ResultPanel from "./components/ResultPanel.vue";
import TestNavigator from "./components/TestNavigator.vue";
import LessonNavigator from "./components/LessonNavigator.vue";
import LessonNotes from "./components/LessonNotes.vue";
import type { TestManifest } from "./demo-types";
import rawTestDemos from "./generated/test-demos.json";
import { formatTestTitle } from "./format-test-title";
import { useCatalogRoute } from "./composables/useCatalogRoute";
import { useOperationRun } from "./composables/useOperationRun";
import { useWasmRuntime } from "@cstructsharp/app-shared/composables/useWasmRuntime";

const testManifest = rawTestDemos as TestManifest;
const docsBase = new URL(
  import.meta.env.VITE_DOCS_BASE_URL || "../docs/",
  new URL(import.meta.env.BASE_URL, window.location.origin),
).href.replace(/\/?$/, "/");

const {
  mode,
  selectedLessonId,
  selectedLesson,
  selectedTestId,
  selectedTest,
  selectedRunnable,
  routeMessage,
  catalogExpanded,
  selectLesson,
  selectTest,
  changeMode,
} = useCatalogRoute(testManifest);
const { status: wasmStatus, version: wasmVersion, error: wasmError } = useWasmRuntime();
const {
  isProcessing,
  result,
  resultBytes,
  binaryHexInput,
  stale,
  expected,
  expectationMatches,
  run,
  reset,
  applyEditedBytes,
} = useOperationRun({
  example: selectedRunnable,
  lesson: selectedLesson,
  ready: computed(() => wasmStatus.value === "ready"),
});

// Remounts the operation panel, so a reset restores its initial inputs as well as the hex.
const resetCount = ref(0);
/** Restores the example's inputs and clears the result. */
function resetExample(): void {
  resetCount.value++;
  reset();
}

const statusText = computed(() => {
  if (wasmStatus.value === "ready") {
    return `Ready · ${wasmVersion.value.split("+")[0]}`;
  }
  if (wasmStatus.value === "error") {
    return `Unavailable · ${wasmError.value}`;
  }
  return "Loading WebAssembly…";
});
</script>

<template>
  <div class="app-shell">
    <header>
      <div>
        <h1><span>CStruct</span>Sharp</h1>
        <p>Inspect, create, and patch binary structures in your browser.</p>
        <a :href="`${docsBase}guides/install-and-first-parse.html`">Use C#</a> ·
        <a :href="`${docsBase}guides/browser/index.html`">Use JavaScript</a> ·
        <a :href="docsBase">Documentation</a>
      </div>
      <div
        class="status-badge"
        :title="wasmVersion"
        :class="{ ready: wasmStatus === 'ready', error: wasmStatus === 'error' }"
      >
        <i></i>{{ statusText }}
      </div>
    </header>

    <main>
      <details
        class="catalog"
        :open="catalogExpanded"
        @toggle="catalogExpanded = ($event.target as HTMLDetailsElement).open"
      >
        <summary>Choose a lesson or test</summary>
        <nav class="catalog-modes" aria-label="Example catalog">
          <button type="button" :aria-pressed="mode === 'learn'" @click="changeMode('learn')">
            Learn
          </button>
          <button type="button" :aria-pressed="mode === 'tests'" @click="changeMode('tests')">
            All tests
          </button>
        </nav>
        <LessonNavigator
          v-if="mode === 'learn'"
          :selected-id="selectedLessonId"
          @select="selectLesson"
        />
        <TestNavigator
          v-else
          :selected-id="selectedTestId"
          :manifest="testManifest"
          @update:selected-id="selectTest"
        />
      </details>

      <div class="workspace">
        <section class="card example-context">
          <p v-if="routeMessage" role="status">{{ routeMessage }}</p>
          <template v-if="selectedTest">
            <div class="example-title-row">
              <h2 :title="selectedLesson ? undefined : selectedTest.id">
                {{ selectedLesson?.title ?? formatTestTitle(selectedTest.methodName) }}
              </h2>
              <a
                v-if="!selectedLesson && selectedTest.sourceUrl"
                class="test-source-link"
                :href="selectedTest.sourceUrl"
                :title="`${selectedTest.filePath}:${selectedTest.line}`"
                target="_blank"
                rel="noopener noreferrer"
              >
                View source on GitHub ↗
              </a>
            </div>
            <p class="example-explanation">
              {{
                selectedLesson?.explanation ??
                [selectedTest.documentation?.summary, selectedTest.documentation?.usage]
                  .filter(Boolean)
                  .join(" ")
              }}
            </p>
            <LessonNotes v-if="selectedLesson" :lesson="selectedLesson" :docs-base="docsBase" />
          </template>
          <p v-else>No matching examples.</p>
        </section>

        <section v-if="selectedRunnable" class="card">
          <OperationPanel
            :key="`${selectedRunnable.id}-${resetCount}`"
            :binary-hex="binaryHexInput"
            :definition="selectedRunnable.definition"
            :disabled="wasmStatus !== 'ready' || isProcessing"
            :running="isProcessing"
            :presets="selectedLesson?.operations ?? { parse: { expected: {} } }"
            :operation="selectedLesson?.operation ?? 'parse'"
            :initial-options="selectedLesson?.options"
            :initial-aligned="selectedRunnable.parserOptions?.aligned"
            :initial-little-endian="selectedRunnable.parserOptions?.littleEndian"
            :initial-pointer-size="selectedRunnable.parserOptions?.pointerSize"
            :initial-root-type="selectedRunnable.rootType"
            @reset="resetExample"
            @run="run"
            @changed="stale = result !== null"
          />
        </section>

        <section v-if="expected" class="card" aria-live="polite">
          <h2>Compare with the starting example</h2>
          <p v-if="stale">Inputs changed. Run again to refresh the result.</p>
          <p v-else>
            {{
              expectationMatches
                ? "Matches the expected result."
                : "Different from the starting result. If you edited the example, check the explanation above to understand the change."
            }}
          </p>
          <pre>{{
            expected.error
              ? `Expected error: ${expected.error}`
              : (expected.hex ?? JSON.stringify(expected.data, null, 2))
          }}</pre>
        </section>
        <p v-else-if="stale" role="status">Inputs changed. Run again to refresh the result.</p>
        <ResultPanel :bytes="resultBytes" :result="result" @bytes-edited="applyEditedBytes" />
      </div>
    </main>

    <footer>
      CStructSharp interactive explorer · generated examples remain tied to the managed tests
    </footer>
  </div>
</template>

<style scoped>
.app-shell {
  min-height: 100vh;
}

header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 24px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.07);
  background: var(--color-bg-secondary);
  padding: 22px clamp(20px, 4vw, 52px);
  position: static;
}

h1 {
  font-size: 28px;
  line-height: 1.1;
}

h1 span {
  color: var(--color-accent);
}

header p {
  color: var(--color-text-muted);
  font-size: 13px;
}

.status-badge {
  display: flex;
  align-items: center;
  gap: 8px;
  border: 1px solid rgba(255, 255, 255, 0.12);
  border-radius: 999px;
  color: var(--color-text-muted);
  font-size: 12px;
  padding: 7px 11px;
}

.status-badge i {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--color-text-muted);
}

.status-badge.ready {
  color: var(--color-success);
}

.status-badge.ready i {
  background: var(--color-success);
}

.status-badge.error {
  color: var(--color-error);
}

.status-badge.error i {
  background: var(--color-error);
}

main {
  display: grid;
  grid-template-columns: minmax(230px, 300px) minmax(0, 1fr);
  gap: 22px;
  margin: 0 auto;
  max-width: 1500px;
  padding: 24px;
}

.workspace {
  display: grid;
  grid-template-columns: minmax(0, 1fr);
  align-content: start;
  gap: 22px;
  min-width: 0;
}

.catalog-modes {
  display: flex;
  gap: 0.5rem;
  margin-bottom: 1rem;
}
.catalog-modes button {
  padding: 0.7rem;
  cursor: pointer;
}
.catalog {
  align-self: start;
}
.catalog > summary {
  cursor: pointer;
  padding: 0.5rem 0;
}
.catalog-modes button {
  color: var(--color-text);
  background: var(--color-bg-tertiary);
  border: 1px solid var(--color-text-muted);
  border-radius: 6px;
  padding: 0.55rem 0.8rem;
  cursor: pointer;
}
.catalog-modes button[aria-pressed="true"] {
  border: 2px solid var(--color-accent);
}
a {
  color: var(--color-accent);
}
pre {
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}

.workspace h2 {
  font-size: 20px;
}

.example-context {
  display: grid;
  grid-template-columns: minmax(0, 1fr);
  gap: 8px;
  align-items: start;
  align-content: start;
}

.example-context h2 {
  overflow-wrap: anywhere;
}

.example-title-row {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  flex-wrap: wrap;
  gap: 8px 16px;
}

.example-title-row h2 {
  flex: 1 1 280px;
  min-width: 0;
}

.test-source-link {
  color: var(--color-accent);
  font-size: 12px;
  white-space: nowrap;
}

footer {
  color: var(--color-text-muted);
  font-size: 12px;
  padding: 20px;
  text-align: center;
}

@media (max-width: 900px) {
  header {
    align-items: flex-start;
    position: static;
  }

  main {
    grid-template-columns: 1fr;
  }

  .example-context {
    grid-template-columns: 1fr;
  }
}

@media (max-width: 600px) {
  header {
    flex-direction: column;
  }

  main {
    padding: 14px;
  }
}
</style>
