import { defineConfig } from "vite";
import vue from "@vitejs/plugin-vue";

// https://vite.dev/config/
export default defineConfig({
  base: "/cstructsharp/explorer/",
  plugins: [vue()],
  build: {
    rolldownOptions: {
      output: {
        codeSplitting: {
          // The generated test catalog is data, about 1.5 MB with the tests' documentation. Its own chunk keeps the
          // entry chunk to application code, so the entry-bundle budget measures code, and a catalog change does
          // not invalidate the cached application code.
          groups: [{ name: "test-catalog", test: /[\\/]generated[\\/]test-demos\.json$/ }],
        },
      },
    },
  },
});
