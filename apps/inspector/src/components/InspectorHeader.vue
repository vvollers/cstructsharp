<script setup lang="ts">
defineProps<{
  sourceLabel: string;
  isRunning: boolean;
  wasmStatus: "loading" | "ready" | "error";
  wasmVersion: string;
  wasmError: string;
}>();
const emit = defineEmits<{ cancel: [] }>();
</script>

<template>
  <header class="top-bar">
    <div class="top-bar-left">
      <h1>CStructSharp Binary Inspector</h1>
      <nav class="header-links" aria-label="Project links">
        <a href="https://vvollers.github.io/cstructsharp/">
          <svg
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="1.8"
            aria-hidden="true"
          >
            <path d="m3 10 9-7 9 7M5 9v12h5v-7h4v7h5V9" />
          </svg>
          Home
        </a>
        <a
          href="https://github.com/vvollers/cstructsharp"
          target="_blank"
          rel="noopener noreferrer"
        >
          <svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
            <path
              d="M12 .75a11.25 11.25 0 0 0-3.56 21.92c.56.1.77-.24.77-.54v-2.1c-3.13.68-3.79-1.33-3.79-1.33-.51-1.3-1.25-1.65-1.25-1.65-1.02-.7.08-.69.08-.69 1.13.08 1.72 1.16 1.72 1.16 1 1.72 2.63 1.22 3.27.93.1-.73.39-1.22.71-1.5-2.5-.28-5.13-1.25-5.13-5.56 0-1.23.44-2.23 1.16-3.02-.12-.28-.5-1.43.11-2.98 0 0 .95-.3 3.1 1.15A10.8 10.8 0 0 1 12 6.16c.96 0 1.92.13 2.82.38 2.15-1.45 3.1-1.15 3.1-1.15.61 1.55.23 2.7.11 2.98.72.79 1.16 1.79 1.16 3.02 0 4.32-2.63 5.28-5.14 5.56.4.35.76 1.04.76 2.1v3.08c0 .3.2.65.78.54A11.25 11.25 0 0 0 12 .75Z"
            />
          </svg>
          GitHub
        </a>
        <a
          href="https://vvollers.github.io/cstructsharp/docs/"
          target="_blank"
          rel="noopener noreferrer"
        >
          <svg
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="1.8"
            aria-hidden="true"
          >
            <path d="M12 5v16M12 5C9 3 5 3 2 4v15c3-1 7-1 10 2 3-3 7-3 10-2V4c-3-1-7-1-10 1Z" />
          </svg>
          Documentation
        </a>
      </nav>
      <button
        v-if="isRunning"
        class="btn cancel-parse-button"
        type="button"
        @click="emit('cancel')"
      >
        Cancel parse
      </button>
    </div>
    <span class="file-name">
      {{ sourceLabel }}
    </span>
    <div
      class="status-badge"
      :class="{ ready: wasmStatus === 'ready', error: wasmStatus === 'error' }"
    >
      <span v-if="wasmStatus === 'loading'">Loading WebAssembly…</span>
      <span v-else-if="wasmStatus === 'ready'">Ready · {{ wasmVersion }}</span>
      <span v-else>Unavailable · {{ wasmError }}</span>
    </div>
  </header>
</template>

<style scoped>
.top-bar {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1fr) minmax(0, 1fr);
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  padding: 10px 20px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.08);
  background: var(--color-bg-secondary);
  flex-shrink: 0;
}
.top-bar-left {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 14px;
  min-width: 0;
}
.top-bar h1 {
  margin: 0;
  font-size: 15px;
  white-space: nowrap;
}
.header-links {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px;
  font-size: 12px;
}
.header-links a {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 5px 9px;
  border: 1px solid #4b6173;
  border-radius: var(--radius-sm);
  background: var(--color-bg-tertiary);
  color: #9cdcfe;
  font-weight: 500;
  text-decoration: none;
}
.header-links a:hover,
.header-links a:focus-visible {
  border-color: #75beff;
  background: #193b54;
  color: #ffffff;
}
.header-links svg {
  width: 16px;
  height: 16px;
  flex-shrink: 0;
}
.cancel-parse-button {
  padding: 7px 16px;
  font-size: 12px;
  border-radius: var(--radius-sm);
  background: var(--color-bg-tertiary);
  color: var(--color-text);
  border: 1px solid rgba(255, 255, 255, 0.12);
  cursor: pointer;
  text-transform: none;
  letter-spacing: normal;
}
.cancel-parse-button:hover {
  border-color: var(--color-accent);
}
.file-name {
  min-width: 0;
  text-align: center;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  color: var(--color-text-muted);
  font-size: 12px;
  font-family: var(--font-mono);
}
.status-badge {
  justify-self: end;
  flex-shrink: 0;
  padding: 6px 14px;
  border-radius: 999px;
  font-size: 12px;
  font-weight: 600;
  background: var(--color-bg-tertiary);
  color: var(--color-text-muted);
}
.status-badge.ready {
  color: var(--color-success);
}
.status-badge.error {
  color: var(--color-error);
}
@media (max-width: 1100px) {
  .top-bar {
    grid-template-columns: minmax(0, 1fr) auto;
  }
  .file-name {
    grid-column: 1 / -1;
    grid-row: 2;
  }
  .status-badge {
    grid-column: 2;
    grid-row: 1;
  }
}
</style>
