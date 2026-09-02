<script setup lang="ts">
import { computed, onMounted, ref, shallowRef, watch } from "vue";

import OperationWorkbench, { type WorkbenchRequest } from "./components/OperationWorkbench.vue";
import ResultPanel from "./components/ResultPanel.vue";
import TestNavigator from "./components/TestNavigator.vue";
import { isRunnable, type TestManifest } from "./demo-types";
import rawTestDemos from "./generated/test-demos.json";
import {
  getVersion,
  hexToBytes,
  initWasm,
  isLoaded,
  parseWithDebug,
  serializeToBase64,
  updateStreamToBase64,
  type InteropResult,
} from "./wasm/cstruct-wasm";

const testManifest = rawTestDemos as TestManifest;
const firstRunnable = testManifest.tests.find(isRunnable);
const selectedTestId = ref(firstRunnable?.id ?? testManifest.tests[0]?.id ?? "");
const selectedTest = computed(
  () => testManifest.tests.find((test) => test.id === selectedTestId.value) ?? null,
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

const statusText = computed(() => {
  if (wasmStatus.value === "ready") {
    return `Ready · ${wasmVersion.value}`;
  }
  if (wasmStatus.value === "error") {
    return `Unavailable · ${wasmError.value}`;
  }
  return "Loading WebAssembly…";
});

watch(selectedTestId, () => {
  result.value = null;
  resultBytes.value = new Uint8Array();
});

onMounted(async () => {
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

function failure(operation: WorkbenchRequest["operation"], error: unknown): InteropResult {
  return {
    ContractVersion: 4,
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

function base64ToBytes(value: string | null): Uint8Array {
  if (!value) {
    return new Uint8Array();
  }
  const binary = atob(value);
  return Uint8Array.from(binary, (character) => character.charCodeAt(0));
}

function parseJson(value: string): unknown {
  return JSON.parse(value, (_key, current: unknown) => current);
}

async function run(request: WorkbenchRequest): Promise<void> {
  if (wasmStatus.value !== "ready") {
    return;
  }

  isProcessing.value = true;
  result.value = null;
  resultBytes.value = new Uint8Array();
  await Promise.resolve();

  try {
    if (request.operation === "parse") {
      const bytes = hexToBytes(request.binaryHex);
      resultBytes.value = bytes;
      result.value = parseWithDebug(request.definition, bytes, request.options);
    } else if (request.operation === "serialize") {
      result.value = serializeToBase64(
        request.definition,
        parseJson(request.jsonValue),
        request.options,
      );
      resultBytes.value = result.value.Success
        ? base64ToBytes(result.value.Data)
        : new Uint8Array();
    } else {
      result.value = updateStreamToBase64(
        request.definition,
        hexToBytes(request.binaryHex),
        request.path,
        parseJson(request.jsonValue),
        request.options,
      );
      resultBytes.value = result.value.Success
        ? base64ToBytes(result.value.Data)
        : new Uint8Array();
    }
  } catch (error) {
    result.value = failure(request.operation, error);
  } finally {
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
      </div>
      <div
        class="status-badge"
        :class="{ ready: wasmStatus === 'ready', error: wasmStatus === 'error' }"
      >
        <i></i>{{ statusText }}
      </div>
    </header>

    <main>
      <TestNavigator v-model:selected-id="selectedTestId" :manifest="testManifest" />

      <div class="workspace">
        <section class="card example-context">
          <template v-if="selectedTest">
            <div>
              <p class="eyebrow">Example from the executable test suite</p>
              <h2>{{ selectedTest.id }}</h2>
              <p class="source">{{ selectedTest.filePath }}:{{ selectedTest.line }}</p>
            </div>
            <p v-if="selectedTest.documentation?.summary">
              {{ selectedTest.documentation.summary }}
            </p>
            <p v-else-if="!selectedRunnable" class="unsupported">
              {{ selectedTest.reason }}
            </p>
          </template>
          <p v-else>No matching examples.</p>
        </section>

        <section v-if="selectedRunnable" class="card">
          <h2>Workbench</h2>
          <p class="section-intro">
            Start with the selected test case, then edit every input. Advanced safety limits stay
            explicit and bounded by the managed bridge.
          </p>
          <OperationWorkbench
            :key="selectedTestId"
            :binary-hex="selectedRunnable.binaryHex"
            :definition="selectedRunnable.definition"
            :disabled="wasmStatus !== 'ready' || isProcessing"
            :initial-aligned="selectedRunnable.parserOptions?.aligned"
            :initial-little-endian="selectedRunnable.parserOptions?.littleEndian"
            :initial-pointer-size="selectedRunnable.parserOptions?.pointerSize"
            :initial-root-type="selectedRunnable.rootType"
            @run="run"
          />
        </section>

        <ResultPanel :bytes="resultBytes" :result="result" />
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
  background: linear-gradient(180deg, var(--color-bg-secondary), transparent);
  padding: 22px clamp(20px, 4vw, 52px);
  position: sticky;
  top: 0;
  z-index: 10;
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
  gap: 22px;
  min-width: 0;
}

.workspace h2 {
  font-size: 20px;
}

.section-intro {
  margin: 4px 0 18px;
}

.example-context {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(240px, 0.8fr);
  gap: 24px;
  align-items: start;
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
