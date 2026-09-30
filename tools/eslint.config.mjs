/**
 * ESLint configuration for the repository tools and the npm package sources: the recommended rules with Node and
 * browser globals, and worker globals for the source worker. ESLint and its plugins are the root workspace's
 * devDependencies (the apps use the same ones). CI (the web workflow) runs it from the repository root over the
 * tools, the package sources and the package tests:
 *
 *   node node_modules/eslint/bin/eslint.js --config tools/eslint.config.mjs <file globs> --max-warnings 0
 */
import js from "@eslint/js";
import globals from "globals";
import jsdoc from "eslint-plugin-jsdoc";

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
