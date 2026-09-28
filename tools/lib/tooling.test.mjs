/**
 * Tests the shared argument parser in tools/lib/tooling.mjs: option kinds, defaults, positional arguments and the
 * errors for unknown or incomplete options.
 *
 *   node --test tools/lib/tooling.test.mjs
 */
import assert from "node:assert/strict";
import test from "node:test";
import { parseArguments } from "./tooling.mjs";

test("options of every kind parse, case-insensitively, with defaults", () => {
  const spec = { label: "string", rounds: "number", strict: "flag", categories: "list", filter: "repeat" };
  const values = parseArguments(
    ["--Label", "x", "--rounds=2", "--strict", "--categories", "a,b", "--categories=c", "--filter", "*A,B*", "--filter", "*C*"],
    spec,
    { defaults: { rounds: 1, strict: false } },
  );
  assert.deepEqual(values, { label: "x", rounds: 2, strict: true, categories: ["a", "b", "c"], filter: ["*A,B*", "*C*"] });
  assert.equal(parseArguments(["--strict=false"], spec).strict, false);
});

test("bare arguments are positional only when the caller accepts them", () => {
  assert.deepEqual(parseArguments(["in.json", "--label", "x", "out.json"], { label: "string" }, { positionals: true }), {
    _: ["in.json", "out.json"],
    label: "x",
  });
  assert.throws(() => parseArguments(["in.json"], { label: "string" }), /Unexpected argument: in\.json/);
});

test("unknown options, missing values and non-numbers are rejected", () => {
  assert.throws(() => parseArguments(["--nope"], {}), /Unknown option: --nope/);
  assert.throws(() => parseArguments(["--label"], { label: "string" }), /needs a value/);
  assert.throws(() => parseArguments(["--rounds", "x"], { rounds: "number" }), /must be a number/);
});
