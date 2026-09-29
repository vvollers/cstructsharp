/** Checks the golden-manifest comparison the engine-golden tool reports. Usage: node --test tools/lib/golden-manifests.test.mjs. */
import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import test from "node:test";
import { compareGoldenManifests, formatGoldenChanges, goldenSections, readGoldenManifests } from "./golden-manifests.mjs";

const header = "# Golden outcomes of Sample.\n# @test starts a test.\n";

// Sections run from their @test line to the next one; the header and the separating blank lines belong to none.
test("a manifest splits into its sections by test id", () => {
  const text = `${header}\n@test A\n@case x\n  a = 1\n\n  b = 2\n\n@test B("y", 2)\n@hash ${"0".repeat(64)} 3\n`;
  const sections = goldenSections(text);
  assert.deepEqual([...sections.keys()], ["A", 'B("y", 2)']);
  assert.equal(sections.get("A"), "@test A\n@case x\n  a = 1\n\n  b = 2");
  assert.equal(goldenSections(text.replaceAll("\n", "\r\n")).get("A"), sections.get("A"), "line endings do not matter");
});

// A new manifest's sections are all added, a deleted one's all removed, and only differing sections count as changed.
test("the comparison lists added, removed and changed sections per manifest", () => {
  const before = new Map([
    ["Kept.txt", `${header}\n@test A\n@case x\n  a = 1\n\n@test B\n@case y\n  b = 1\n`],
    ["Gone.txt", `${header}\n@test C\n@case z\n  c = 1\n`],
    ["Same.txt", `${header}\n@test D\n@case w\n  d = 1\n`],
  ]);
  const after = new Map([
    ["Kept.txt", `${header}\n@test A\n@case x\n  a = 2\n\n@test E\n@case v\n  e = 1\n`],
    ["New.txt", `${header}\n@test F\n@case u\n  f = 1\n`],
    ["Same.txt", before.get("Same.txt")],
  ]);
  const changes = compareGoldenManifests(before, after);
  assert.deepEqual(changes, [
    { file: "Gone.txt", added: [], removed: ["C"], changed: [] },
    { file: "Kept.txt", added: ["E"], removed: ["B"], changed: ["A"] },
    { file: "New.txt", added: ["F"], removed: [], changed: [] },
  ]);
  assert.equal(formatGoldenChanges(changes).split("\n")[1], "  - C");
  assert.match(formatGoldenChanges(changes), /^Kept\.txt: 1 added, 1 removed, 1 changed\n {2}\+ E\n {2}- B\n {2}~ A$/m);
  assert.equal(formatGoldenChanges([]), "The golden manifests are unchanged.\n");
});

// Only manifest files are read, and a directory that does not exist yet has none.
test("reading takes the text manifests of a directory", (t) => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), "cstruct-golden-"));
  // Remove the test-owned directory after the assertions, including when one fails.
  t.after(() => fs.rmSync(directory, { recursive: true, force: true }));
  fs.writeFileSync(path.join(directory, "B.txt"), "b");
  fs.writeFileSync(path.join(directory, "A.txt"), "a");
  fs.writeFileSync(path.join(directory, "notes.md"), "ignored");
  assert.deepEqual([...readGoldenManifests(directory)], [["A.txt", "a"], ["B.txt", "b"]]);
  assert.equal(readGoldenManifests(path.join(directory, "missing")).size, 0);
});
