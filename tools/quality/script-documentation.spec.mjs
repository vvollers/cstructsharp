/** Tests script documentation with the app's parsers. Usage: node --test tools/quality/script-documentation.spec.mjs (requires explorer npm dependencies). */
import assert from "node:assert/strict";
import test from "node:test";
import { inspectScripts } from "./changed-documentation.mjs";

// Legacy neighbors are deliberately outside the selected line interval; changed undocumented functions fail.
test("script checker scopes named functions, classes, arrows and Vue scripts", () => {
  const source = "function legacy() {}\n/** Returns one. */\nfunction current() { return 1; }\nconst missing = () => 2;\n";
  assert.deepEqual(inspectScripts([{ file: "sample.ts", source, ranges: [{ start: 3, end: 3 }] }]), []);
  assert.equal(inspectScripts([{ file: "sample.ts", source, ranges: [{ start: 4, end: 4 }] }]).length, 1);
  const missing = "<template><p>Not TypeScript</p></template>\n<script setup lang=\"ts\">\nfunction missing() {}\n</script>";
  assert.match(inspectScripts([{ file: "Sample.vue", source: missing, ranges: [{ start: 3, end: 3 }] }])[0], /Sample.vue:3:/);
  const documented = "/** A value. */\nclass Value {\n/** Constructs a value. */\nconstructor() {}\n/** Gets its number. */\ngetNumber() { return 1; }\n}";
  assert.deepEqual(inspectScripts([{ file: "sample.ts", source: documented, ranges: [{ start: 1, end: 7 }] }]), []);
  assert.equal(inspectScripts([{ file: "sample.ts", source: "/** */\nfunction empty() {}", ranges: [{ start: 2, end: 2 }] }]).length, 1);
});
