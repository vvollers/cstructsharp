/** Tests script documentation with the app's parsers. Usage: node --test tools/quality/script-documentation.spec.mjs (requires explorer npm dependencies). */
import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { spawnSync } from "node:child_process";
import test from "node:test";
import { changedRanges, inspectScripts } from "./changed-documentation.mjs";

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

// Git anchors a removed comment to the preceding line; inspect the following declaration, not its legacy neighbor.
test("deleting a comment selects the newly undocumented declaration", (t) => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), "cstruct-comment-deletion-"));
  // Remove only these test-owned source copies after the assertion, including when it fails.
  t.after(() => fs.rmSync(directory, { recursive: true, force: true }));
  const source = "function legacy() {}\nfunction current() {}\n";
  const before = path.join(directory, "before.ts");
  const after = path.join(directory, "after.ts");
  fs.writeFileSync(before, source.replace("function current", "/** Current operation. */\nfunction current"));
  fs.writeFileSync(after, source);
  const diff = spawnSync("git", ["diff", "--no-index", "--unified=0", "--", before, after], { encoding: "utf8" });
  assert.equal(diff.status, 1, diff.stderr);
  const issues = inspectScripts([{ file: "sample.ts", source, ranges: changedRanges(diff.stdout) }]);
  assert.equal(issues.length, 1);
  assert.match(issues[0], /sample.ts:2:/);
});
