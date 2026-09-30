/**
 * Tests the workflow YAML reader in tools/lib/workflow-yaml.mjs: the supported subset, the loud failure outside it,
 * GitHub's matrix expansion, and that every real workflow in .github/workflows parses.
 *
 *   node --test tools/lib/workflow-yaml.test.mjs
 */
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";
import { expandMatrix, jobDisplayNames, parseWorkflow, readWorkflow } from "./workflow-yaml.mjs";
import { repositoryRoot } from "./tooling.mjs";

test("mappings, sequences, quoted and flow values, block scalars and comments parse", () => {
  const text = [
    "name: Sample # trailing comment",
    "on:",
    "  workflow_call:",
    "  push:",
    "    paths:",
    '      - "src/**"',
    "      - 'docs/**'",
    "jobs:",
    "  build:",
    "    needs: [a, b]",
    "    if: >-",
    "      always() &&",
    "      success()",
    "    steps:",
    "      - name: Run",
    "        run: |",
    "          echo one # not a YAML comment",
    "",
    "          echo two",
    "      - { uses: x@1, with: \"a, b\" }",
    "",
  ].join("\n");
  assert.deepEqual(parseWorkflow(text), {
    name: "Sample",
    on: { workflow_call: null, push: { paths: ["src/**", "docs/**"] } },
    jobs: {
      build: {
        needs: ["a", "b"],
        if: "always() && success()",
        steps: [{ name: "Run", run: "echo one # not a YAML comment\n\necho two\n" }, { uses: "x@1", with: "a, b" }],
      },
    },
  });
});

test("YAML outside the supported subset fails loudly", () => {
  assert.throws(() => parseWorkflow("a: &anchor 1\n"), /anchors/);
  assert.throws(() => parseWorkflow("a: plain\n  continued\n"), /multi-line plain/);
  assert.throws(() => parseWorkflow("a: [b, [c]]\n"), /nested flow/);
  assert.throws(() => parseWorkflow("a: 1\na: 2\n"), /duplicate key/);
  assert.throws(() => parseWorkflow("a:\n\tb: 1\n"), /tabs/);
});

test("matrices expand with GitHub's exclude and include rules", () => {
  assert.deepEqual(expandMatrix(undefined), [{}]);
  assert.deepEqual(expandMatrix({ os: ["u", "w"], node: ["22"], include: [{ os: "u", node: "26" }, { os: "w", extra: "x" }] }), [
    { os: "u", node: "22" },
    { os: "w", node: "22", extra: "x" },
    { os: "u", node: "26" },
  ]);
  assert.deepEqual(expandMatrix({ os: ["u", "w"], node: ["22", "26"], exclude: [{ os: "w", node: "26" }] }), [
    { os: "u", node: "22" },
    { os: "u", node: "26" },
    { os: "w", node: "22" },
  ]);
  assert.deepEqual(expandMatrix({ include: [{ label: "a" }, { label: "b" }] }), [{ label: "a" }, { label: "b" }]);
  assert.throws(() => expandMatrix("${{ fromJSON(needs.plan.outputs.matrix) }}"), /run-time matrix/);
});

test("job names substitute matrix values or append them", () => {
  const job = { name: "Tests on ${{ matrix.os }}", strategy: { matrix: { os: ["u", "w"] } } };
  assert.deepEqual(jobDisplayNames("tests", job), ["Tests on u", "Tests on w"]);
  assert.deepEqual(jobDisplayNames("tests", { strategy: { matrix: { os: ["u"], node: ["22"] } } }), ["tests (u, 22)"]);
  assert.deepEqual(jobDisplayNames("plain", {}), ["plain"]);
  assert.throws(() => jobDisplayNames("x", { name: "On ${{ inputs.mode }}" }), /run-time expression/);
});

test("every repository workflow parses", () => {
  const directory = path.join(repositoryRoot, ".github/workflows");
  const files = fs.readdirSync(directory).filter((name) => /\.ya?ml$/.test(name));
  assert.ok(files.length > 0);
  for (const name of files) {
    const workflow = readWorkflow(repositoryRoot, `.github/workflows/${name}`);
    assert.equal(typeof workflow.jobs, "object", `${name} has no jobs mapping.`);
  }
});
