<script setup lang="ts">
/**
 * One setting in a status line: a coloured label and value that shows an explanation tooltip on hover or focus.
 * The tooltip is placed below the item, or above it when it would leave the viewport, and closes on Escape.
 */
import { nextTick, onBeforeUnmount, onMounted, ref } from "vue";

/** The setting name, its current value, the accent colour, and the explanation the tooltip shows. */
const props = defineProps<{
  label: string;
  value: string | boolean;
  color: string;
  explanation: string;
}>();
const trigger = ref<HTMLElement | null>(null);
const tooltip = ref<HTMLElement | null>(null);
const visible = ref(false);
const position = ref({ left: "0px", top: "0px" });
const id = `setting-help-${props.label.toLowerCase().replace(/ /g, "-")}`;
let closeTimer: ReturnType<typeof setTimeout> | undefined;
/** Cancels a pending close, so moving from the item into the tooltip keeps it open. */
function keepOpen() {
  clearTimeout(closeTimer);
}
/** Opens the tooltip and positions it inside the viewport once it has rendered and has a size. */
async function show() {
  keepOpen();
  visible.value = true;
  await nextTick();
  if (!trigger.value || !tooltip.value) return;
  const anchor = trigger.value.getBoundingClientRect();
  const box = tooltip.value.getBoundingClientRect();
  position.value = {
    left: `${Math.max(8, Math.min(anchor.left, window.innerWidth - box.width - 8))}px`,
    top: `${anchor.bottom + box.height + 8 <= window.innerHeight ? anchor.bottom + 6 : Math.max(8, anchor.top - box.height - 6)}px`,
  };
}
/** Closes the tooltip after a short delay, so the pointer can cross the gap between item and tooltip. */
function hideSoon() {
  closeTimer = setTimeout(() => {
    visible.value = false;
  }, 120);
}
/** Closes the tooltip immediately. */
function hide() {
  keepOpen();
  visible.value = false;
}
/**
 * Closes the tooltip on Escape.
 * @param event The window key event.
 */
function onKey(event: KeyboardEvent) {
  if (event.key === "Escape") hide();
}
/** Keeps an open tooltip next to its item when the page scrolls or resizes. */
function reposition() {
  if (visible.value) void show();
}
// Window listeners follow the item into scroll containers and close it from the keyboard.
onMounted(() => {
  window.addEventListener("keydown", onKey);
  window.addEventListener("resize", reposition);
  window.addEventListener("scroll", reposition, true);
});
// Removes the window listeners and any pending close timer.
onBeforeUnmount(() => {
  keepOpen();
  window.removeEventListener("keydown", onKey);
  window.removeEventListener("resize", reposition);
  window.removeEventListener("scroll", reposition, true);
});
</script>

<template>
  <span
    ref="trigger"
    class="setting-item"
    tabindex="0"
    :aria-describedby="visible ? id : undefined"
    :style="{ '--setting-color': color }"
    @mouseenter="show"
    @mouseleave="hideSoon"
    @focus="show"
    @blur="hideSoon"
  >
    <span class="setting-label">{{ label }}:</span>
    <span
      v-if="typeof value === 'boolean'"
      class="setting-value setting-boolean"
      role="img"
      :aria-label="value ? 'Enabled' : 'Disabled'"
    >
      <svg viewBox="0 0 16 16" aria-hidden="true">
        <rect x="1.5" y="1.5" width="13" height="13" rx="3" />
        <path v-if="value" d="m4 8 2.5 2.5 5.5-5" />
      </svg>
    </span>
    <span v-else class="setting-value">{{ value }}</span>
    <Teleport to="body">
      <span
        v-if="visible"
        :id="id"
        ref="tooltip"
        role="tooltip"
        class="setting-tooltip"
        :style="{ ...position, '--setting-color': color }"
        @mouseenter="keepOpen"
        @mouseleave="hideSoon"
      >
        <span class="tooltip-heading"
          >{{ label }}:
          <strong class="setting-value">{{
            typeof value === "boolean" ? (value ? "Enabled" : "Disabled") : value
          }}</strong></span
        >
        {{ explanation }}
      </span>
    </Teleport>
  </span>
</template>

<style scoped>
.setting-item {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  min-width: 0;
  cursor: help;
  border-radius: 3px;
}
.setting-item:focus-visible {
  outline: 2px solid var(--setting-color);
  outline-offset: 3px;
}
.setting-label {
  color: #a6b1c4;
}
.setting-value {
  color: var(--setting-color);
  font-weight: 650;
}
.setting-boolean {
  display: inline-flex;
}
.setting-boolean svg {
  width: 14px;
  height: 14px;
  fill: none;
  stroke: currentColor;
  stroke-width: 1.6;
  stroke-linecap: round;
  stroke-linejoin: round;
}
.setting-tooltip {
  position: fixed;
  z-index: 100;
  width: max-content;
  max-width: min(320px, calc(100vw - 16px));
  padding: 11px 13px;
  border: 1px solid #526580;
  border-top: 2px solid var(--setting-color);
  border-radius: 7px;
  background: #182338;
  color: #e8e8f0;
  box-shadow: 0 8px 24px #0006;
  font-size: 13px;
  line-height: 1.5;
  overflow-wrap: anywhere;
}
.tooltip-heading {
  display: block;
  margin-bottom: 4px;
}
</style>
