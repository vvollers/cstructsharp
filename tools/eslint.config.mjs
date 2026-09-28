/**
 * ESLint configuration for the repository tools and the npm package sources: the recommended rules with Node and
 * browser globals, and worker globals for the source worker. It reuses the ESLint packages installed for apps/explorer.
 * CI (the web workflow) runs it from the repository root over the tools, the package sources and the package tests:
 *
 *   node node_modules/eslint/bin/eslint.js --config tools/eslint.config.mjs <file globs> --max-warnings 0
 */
import { createRequire } from "node:module";
import { pathToFileURL } from "node:url";

// Reuse the pinned explorer tooling without sharing either application's source.
const require = createRequire(new URL("../apps/explorer/package.json", import.meta.url));
const js = require("@eslint/js");
const { default: globals } = await import(pathToFileURL(require.resolve("globals")).href);
const { default: jsdoc } = await import(pathToFileURL(require.resolve("eslint-plugin-jsdoc")).href);

export default [
  { ignores: ["**/bin/**", "**/obj/**", "**/node_modules/**"] },
  js.configs.recommended,
  { languageOptions: { globals: { ...globals.node, ...globals.browser }, sourceType: "module" } },
  {
    files: ["packages/cstructsharp/src/source-worker.js"],
    languageOptions: { globals: globals.worker },
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
          contexts: ["VariableDeclarator > ArrowFunctionExpression", "VariableDeclarator > FunctionExpression"],
        },
      ],
    },
  },
];
