import js from "@eslint/js";
import globals from "globals";
import pluginVue from "eslint-plugin-vue";
import tseslint from "typescript-eslint";

export default [
  {
    ignores: [
      "dist/**",
      "node_modules/**",
      "playwright-report/**",
      "public/**",
      "src/generated/**",
      "src/vite-env.d.ts",
      "test-results/**",
      "vite.config.d.ts",
      "vite.config.js",
      "wasm/bin/**",
      "wasm/obj/**",
    ],
  },
  js.configs.recommended,
  ...tseslint.configs.recommended,
  ...pluginVue.configs["flat/essential"],
  {
    languageOptions: {
      globals: globals.browser,
    },
  },
  {
    files: ["**/*.{ts,vue}"],
    languageOptions: {
      parserOptions: {
        parser: tseslint.parser,
        extraFileExtensions: [".vue"],
      },
    },
    rules: {
      "vue/no-v-html": "off",
    },
  },
  {
    files: ["*.config.{js,ts}", "scripts/**/*.mjs", "wasm/**/*.js"],
    languageOptions: {
      globals: {
        ...globals.browser,
        ...globals.node,
      },
    },
  },
];
