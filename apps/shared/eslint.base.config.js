/**
 * The ESLint flat configuration the explorer, the inspector and the shared source build on: the recommended
 * JavaScript, TypeScript and Vue rules with browser globals, Node globals for configuration files and scripts, and
 * the documentation rule from AGENTS.md. Each package's `eslint.config.js` calls it, adding its own ignores:
 *
 *   import { appEslintConfig } from "@cstructsharp/app-shared/eslint-config";
 *   export default appEslintConfig({ ignores: ["src/generated/**"] });
 */
import js from "@eslint/js";
import globals from "globals";
import jsdoc from "eslint-plugin-jsdoc";
import pluginVue from "eslint-plugin-vue";
import tseslint from "typescript-eslint";

/**
 * Builds the flat configuration array for one package.
 * @param {{ ignores?: string[] }} [options] Extra ignore globs, relative to the package directory.
 * @returns {import("eslint").Linter.Config[]} The configuration to export from `eslint.config.js`.
 */
export function appEslintConfig({ ignores = [] } = {}) {
  return [
    {
      // Build output, test reports, the staged WASM publication (public/) and generated Vite files.
      ignores: [
        "dist/**",
        "artifacts/**",
        "node_modules/**",
        "playwright-report/**",
        "public/**",
        "src/vite-env.d.ts",
        "test-results/**",
        "vite.config.d.ts",
        "vite.config.js",
        ...ignores,
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
    },
    {
      files: ["*.config.{js,ts}", "scripts/**/*.mjs"],
      languageOptions: {
        globals: {
          ...globals.browser,
          ...globals.node,
        },
      },
    },
    {
      // Every function, method and class is documented (AGENTS.md), including nested named functions.
      plugins: { jsdoc },
      rules: {
        "jsdoc/require-jsdoc": [
          "error",
          {
            publicOnly: false,
            require: {
              FunctionDeclaration: true,
              MethodDefinition: true,
              ClassDeclaration: true,
              ArrowFunctionExpression: false,
              FunctionExpression: false,
            },
            contexts: [
              "VariableDeclarator > ArrowFunctionExpression",
              "VariableDeclarator > FunctionExpression",
            ],
          },
        ],
      },
    },
  ];
}
