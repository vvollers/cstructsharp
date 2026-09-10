import { createRequire } from "node:module";
import { pathToFileURL } from "node:url";

// Reuse the pinned workshop tooling without sharing either application's source.
const require = createRequire(new URL("../apps/workshop/package.json", import.meta.url));
const js = require("@eslint/js");
const { default: globals } = await import(pathToFileURL(require.resolve("globals")).href);

export default [
  { ignores: ["**/bin/**", "**/obj/**", "**/node_modules/**"] },
  js.configs.recommended,
  { languageOptions: { globals: { ...globals.node, ...globals.browser }, sourceType: "module" } },
];
