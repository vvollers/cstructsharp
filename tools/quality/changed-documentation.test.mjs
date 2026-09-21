/** Tests changed-line scoping and Roslyn parsing. Usage: node --test tools/quality/changed-documentation.test.mjs (requires the pinned SDK). */
import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import test from "node:test";
import { changedRanges } from "./changed-documentation.mjs";
import { repositoryRoot } from "../lib/tooling.mjs";

// Deleted comment lines must still select the declaration at their surviving boundary.
test("changed ranges retain deletion boundaries and exact added spans", () => {
  assert.deepEqual(changedRanges("@@ -1,2 +1,0 @@\n@@ -8 +7,3 @@"), [{ start: 1, end: 1 }, { start: 7, end: 9 }]);
});

// Run the real Roslyn checker, including a constructor, local function and undocumented untouched method.
test("C# checker detects missing XML comments only in changed declaration spans", () => {
  const source = "/// <summary>A sample.</summary>\nclass Sample {\nvoid Legacy() {}\n/// <summary>Constructs.</summary>\npublic Sample() {}\nvoid Missing() { void Local() {} }\n}";
  const entries = [
    { file: "passing.cs", source, ranges: [{ start: 5, end: 5 }] },
    { file: "failing.cs", source, ranges: [{ start: 6, end: 6 }] },
  ];
  const output = execFileSync("dotnet", ["run", "--file", "tools/quality/CSharpComments.cs"], {
    cwd: repositoryRoot, input: JSON.stringify({ entries }), encoding: "utf8",
  });
  const marker = "DOC-COMMENT-RESULT:";
  const issues = JSON.parse(output.split(/\r?\n/).find((line) => line.startsWith(marker)).slice(marker.length));
  assert.equal(issues.length, 2);
  assert.ok(issues.every((issue) => issue.startsWith("failing.cs:6:")));
});
