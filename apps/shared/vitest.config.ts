import { defineConfig } from "vitest/config";
import vue from "@vitejs/plugin-vue";

// The shared unit tests mount single-file components, so they need the Vue plugin like the apps' Vite configs.
export default defineConfig({
  plugins: [vue()],
});
