<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, shallowRef, watch } from "vue";

import OperationWorkbench, { type WorkbenchRequest } from "./components/OperationWorkbench.vue";
import ResultPanel from "./components/ResultPanel.vue";
import TestNavigator from "./components/TestNavigator.vue";
import LessonNavigator from "./components/LessonNavigator.vue";
import { lessons, compareLessonResult, type LessonOperation } from "./lessons";
import { isRunnable, type TestManifest } from "./demo-types";
import rawTestDemos from "./generated/test-demos.json";
import { formatTestTitle } from "./format-test-title";
import {
  getVersion,
  hexToBytes,
  initWasm,
  INTEROP_CONTRACT_VERSION,
  isLoaded,
  parseWithDebug,
  serialize,
  updateStream,
  type InteropResult,
} from "./wasm/cstruct-wasm";

const testManifest = rawTestDemos as TestManifest;
const mode = ref<"learn" | "tests">("learn");
const selectedLessonId = ref("header");
const selectedLesson = computed(() =>
  mode.value === "learn"
    ? (lessons.find((item) => item.id === selectedLessonId.value) ?? lessons[0]!)
    : null,
);
const resetCount = ref(0);
const stale = ref(false);
const expected = ref<LessonOperation["expected"] | null>(null);
const expectationMatches = ref<boolean | null>(null);
const routeMessage = ref("");
const docsBase = new URL(
  import.meta.env.VITE_DOCS_BASE_URL || "../docs/",
  new URL(import.meta.env.BASE_URL, window.location.origin),
).href.replace(/\/?$/, "/");
const catalogExpanded = ref(window.innerWidth > 900);
const narrowViewport = window.matchMedia("(max-width: 900px)");
function updateCatalog(event: MediaQueryListEvent): void {
  catalogExpanded.value = !event.matches;
}
function readRoute(): void {
  const route = new URLSearchParams(window.location.hash.slice(1));
  const lessonId = route.get("lesson");
  const testId = route.get("test");
  routeMessage.value = "";
  if (testId && testManifest.tests.some((item) => item.id === testId)) {
    mode.value = "tests";
    selectedTestId.value = testId;
  } else {
    mode.value = "learn";
    selectedLessonId.value = lessons.some((item) => item.id === lessonId) ? lessonId! : "header";
    if (lessonId && lessonId !== selectedLessonId.value)
      routeMessage.value = "That lesson was not found. Start with the header lesson below.";
  }
}
function selectLesson(id: string): void {
  window.location.hash = new URLSearchParams({ lesson: id }).toString();
  if (narrowViewport.matches) catalogExpanded.value = false;
}
function selectTest(id: string): void {
  selectedTestId.value = id;
  if (id) window.location.hash = new URLSearchParams({ test: id }).toString();
}
function changeMode(value: "learn" | "tests"): void {
  if (value === "learn") selectLesson(selectedLessonId.value);
  else selectTest(selectedTestId.value);
}
const firstRunnable = testManifest.tests.find(isRunnable);
const selectedTestId = ref(firstRunnable?.id ?? testManifest.tests[0]?.id ?? "");
const selectedTest = computed(
  () =>
    selectedLesson.value ??
    testManifest.tests.find((test) => test.id === selectedTestId.value) ??
    null,
);
const selectedRunnable = computed(() =>
  isRunnable(selectedTest.value) ? selectedTest.value : null,
);

const wasmStatus = ref<"loading" | "ready" | "error">("loading");
const wasmVersion = ref("");
const wasmError = ref("");
const isProcessing = ref(false);
const result = ref<InteropResult | null>(null);
const resultBytes = shallowRef<Uint8Array>(new Uint8Array());
const binaryHexInput = ref("");

const statusText = computed(() => {
  if (wasmStatus.value === "ready") {
    return `Ready · ${wasmVersion.value.split("+")[0]}`;
  }
  if (wasmStatus.value === "error") {
    return `Unavailable · ${wasmError.value}`;
  }
  return "Loading WebAssembly…";
});

watch(selectedTest, () => {
  result.value = null;
  resultBytes.value = new Uint8Array();
  stale.value = false;
  expected.value = null;
  expectationMatches.value = null;
});

function resetExample(): void {
  resetCount.value++;
  binaryHexInput.value = selectedRunnable.value?.binaryHex ?? "";
  result.value = null;
  resultBytes.value = new Uint8Array();
  stale.value = false;
  expected.value = null;
  expectationMatches.value = null;
}

watch(
  selectedRunnable,
  (test) => {
    binaryHexInput.value = test?.binaryHex ?? "";
  },
  { immediate: true },
);

onMounted(async () => {
  readRoute();
  window.addEventListener("hashchange", readRoute);
  narrowViewport.addEventListener("change", updateCatalog);
  try {
    await initWasm();
    if (!isLoaded()) {
      throw new Error("The runtime finished loading without usable exports.");
    }
    wasmVersion.value = getVersion();
    wasmStatus.value = "ready";
  } catch (error) {
    wasmStatus.value = "error";
    wasmError.value = error instanceof Error ? error.message : "Unknown initialization error";
  }
});
onUnmounted(() => {
  window.removeEventListener("hashchange", readRoute);
  narrowViewport.removeEventListener("change", updateCatalog);
});

function failure(operation: WorkbenchRequest["operation"], error: unknown): InteropResult {
  return {
    ContractVersion: INTEROP_CONTRACT_VERSION,
    Operation: operation,
    Success: false,
    Data: null,
    DebugData: [],
    Error: {
      Code: "browser-error",
      Message: error instanceof Error ? error.message : "The browser operation failed.",
      Offset: null,
      Path: null,
    },
  };
}

function successBytes(result: InteropResult): Uint8Array {
  return result.Success && result.Data instanceof Uint8Array ? result.Data : new Uint8Array();
}

function parseJson(value: string): unknown {
  return JSON.parse(value, (_key, current: unknown) => current);
}

function bytesToHex(bytes: Uint8Array): string {
  return Array.from(bytes, (byte) => byte.toString(16).padStart(2, "0")).join(" ");
}

function applyEditedBytes(bytes: Uint8Array): void {
  resultBytes.value = bytes;
  binaryHexInput.value = bytesToHex(bytes);
  stale.value = true;
}

async function run(request: WorkbenchRequest): Promise<void> {
  if (wasmStatus.value !== "ready") {
    return;
  }

  isProcessing.value = true;
  stale.value = false;
  expected.value = selectedLesson.value?.operations[request.operation]?.expected ?? null;
  expectationMatches.value = null;
  result.value = null;
  resultBytes.value = new Uint8Array();
  await Promise.resolve();

  try {
    if (request.operation === "parse") {
      const bytes = hexToBytes(request.binaryHex);
      resultBytes.value = bytes;
      result.value = parseWithDebug(request.definition, bytes, request.options);
    } else if (request.operation === "serialize") {
      result.value = serialize(request.definition, parseJson(request.jsonValue), request.options);
      resultBytes.value = successBytes(result.value);
    } else {
      result.value = updateStream(
        request.definition,
        hexToBytes(request.binaryHex),
        request.path,
        parseJson(request.jsonValue),
        request.options,
      );
      resultBytes.value = successBytes(result.value);
    }
  } catch (error) {
    result.value = failure(request.operation, error);
  } finally {
    if (expected.value && result.value)
      expectationMatches.value = compareLessonResult(
        expected.value,
        result.value,
        resultBytes.value,
      );
    isProcessing.value = false;
  }
}
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
          </template>
          <p v-else>No matching examples.</p>
        </section>

        <section v-if="selectedRunnable" class="card">
          <OperationWorkbench
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
      CStructSharp interactive workbench · generated examples remain tied to the managed tests
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

header p,
.source,
.section-intro {
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
.catalog-modes button,
.reset-example {
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

.section-intro {
  margin: 4px 0 18px;
}

.example-context {
  display: grid;
  grid-template-columns: minmax(0, 1fr);
  gap: 8px;
  align-items: start;
  align-content: start;
}

.eyebrow {
  color: var(--color-accent);
  font-size: 10px;
  font-weight: 700;
  letter-spacing: 0.08em;
  text-transform: uppercase;
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

.unsupported {
  color: var(--color-text-muted);
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
