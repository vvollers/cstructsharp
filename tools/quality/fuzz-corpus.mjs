#!/usr/bin/env node
/**
 * Validates the managed fuzz corpus (tests/CStructSharp.Fuzz/corpus/fuzz-corpus.json) and the fuzz evidence the
 * feature-operation matrix records for it.
 *
 *   node tools/quality/fuzz-corpus.mjs [--corpus-path <json>] [--matrix-path <json>]
 */
import fs from "node:fs";
import path from "node:path";
import { assertCondition, main, parseArguments, repositoryRoot } from "../lib/tooling.mjs";

const options = parseArguments(process.argv.slice(2), { "corpus-path": "string", "matrix-path": "string" }, {
  defaults: {
    "corpus-path": path.join(repositoryRoot, "tests/CStructSharp.Fuzz/corpus/fuzz-corpus.json"),
    "matrix-path": path.join(repositoryRoot, "contracts/quality/feature-operation-matrix.json"),
  },
});
const isFile = (file) => fs.existsSync(file) && fs.statSync(file).isFile();
const sameSet = (expected, actual) => expected.length === actual.length && [...expected].sort().every((item, index) => item === [...actual].sort()[index]);

await main(() => {
  assertCondition(isFile(options["corpus-path"]), `Managed fuzz corpus '${options["corpus-path"]}' does not exist.`);
  assertCondition(isFile(options["matrix-path"]), `Feature-operation matrix '${options["matrix-path"]}' does not exist.`);
  const corpus = JSON.parse(fs.readFileSync(options["corpus-path"], "utf8"));
  const matrix = JSON.parse(fs.readFileSync(options["matrix-path"], "utf8"));
  assertCondition(corpus.schemaVersion === 1, "Unsupported managed fuzz corpus schema version.");
  assertCondition(/^0x[0-9A-F]{16}$/.test(corpus.seed), "The managed fuzz seed must be a 16-digit uppercase hexadecimal UInt64.");
  assertCondition(corpus.iterationsPerTarget >= 1 && corpus.iterationsPerTarget <= 4096, "Iterations per target must be between 1 and 4096.");
  assertCondition(corpus.maxInputBytes >= 1 && corpus.maxInputBytes <= 4096, "Maximum input bytes must be between 1 and 4096.");

  const limits = corpus.limits;
  for (const property of [
    "maxDefinitionLength",
    "maxLayoutNestingDepth",
    "maxExpressionNestingDepth",
    "maxExpressionTokens",
    "maxArrayElements",
    "maxStringBytes",
    "maxTotalBytesRead",
    "maxNestingDepth",
    "maxPointerDepth",
    "maxPointerTargetBytes",
    "maxTotalBytesWritten",
  ]) {
    assertCondition(Object.hasOwn(limits, property), `Managed fuzz limit '${property}' is missing.`);
    assertCondition(limits[property] >= 1, `Managed fuzz limit '${property}' must be positive.`);
  }
  assertCondition(limits.maxDefinitionLength <= corpus.maxInputBytes * 4, "The definition limit is not meaningfully bounded by the input limit.");
  assertCondition(limits.maxArrayElements <= 256, "The fuzz array limit must not exceed 256.");
  assertCondition(limits.maxStringBytes <= 4096, "The fuzz string-byte limit must not exceed 4096.");
  assertCondition(limits.maxTotalBytesRead <= 4096, "The fuzz read-byte limit must not exceed 4096.");
  assertCondition(limits.maxTotalBytesWritten <= 4096, "The fuzz write-byte limit must not exceed 4096.");

  const expectedTargets = ["binary-roundtrip", "definition", "expression", "path", "pointer-union"];
  const targetIds = corpus.targets.map((target) => String(target.id));
  assertCondition(new Set(targetIds).size === targetIds.length, "The managed fuzz corpus contains duplicate target ids.");
  assertCondition(sameSet(expectedTargets, targetIds), "The managed fuzz corpus does not contain exactly the five QA-04 targets.");

  let seedCount = 0;
  for (const target of corpus.targets) {
    assertCondition((target.seeds ?? []).length >= 4, `Fuzz target '${target.id}' must retain at least four seeds.`);
    for (const seed of target.seeds) {
      const context = `Fuzz seed '${target.id}/${seed.id}'`;
      assertCondition(/^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(seed.id), `${context} id must be lowercase kebab-case.`);
      assertCondition(["hex", "utf8"].includes(seed.encoding), `${context} has an invalid encoding.`);
      assertCondition(typeof seed.data === "string" && seed.data.length > 0, `${context} is empty.`);
      let byteCount;
      if (seed.encoding === "hex") {
        assertCondition(/^(?:[0-9A-F]{2})+$/.test(seed.data), `${context} must use uppercase hexadecimal octets.`);
        byteCount = seed.data.length / 2;
      } else {
        byteCount = Buffer.byteLength(seed.data, "utf8");
      }
      assertCondition(byteCount <= corpus.maxInputBytes, `${context} exceeds the configured maximum input size.`);
      seedCount++;
    }
  }

  const contract = matrix.fuzzEvidence;
  assertCondition(contract.schemaVersion === corpus.schemaVersion, "The feature-operation matrix names a different fuzz schema.");
  assertCondition(contract.corpus === "tests/CStructSharp.Fuzz/corpus/fuzz-corpus.json", "The feature-operation matrix names an unexpected fuzz corpus.");
  assertCondition(contract.project === "tests/CStructSharp.Fuzz/CStructSharp.Fuzz.csproj", "The feature-operation matrix names an unexpected fuzz project.");
  assertCondition(contract.validator === "tools/quality/fuzz-corpus.mjs", "The feature-operation matrix names an unexpected fuzz validator.");
  assertCondition(contract.guide === "docs/project/testing.md", "The feature-operation matrix names an unexpected fuzz guide.");
  assertCondition(contract.workItem === "QA-04", "The feature-operation matrix must assign fuzz evidence to QA-04.");
  assertCondition(sameSet(expectedTargets, contract.targets ?? []), "The feature-operation matrix names a different managed fuzz target set.");
  assertCondition((contract.deferredTargets ?? []).length === 1 && contract.deferredTargets[0] === "json-wasm", "The feature-operation matrix must defer only the JSON/WASM fuzz target.");
  for (const relative of [contract.corpus, contract.project, contract.validator, contract.guide]) {
    assertCondition(isFile(path.join(repositoryRoot, relative)), `Managed fuzz evidence file '${relative}' does not exist.`);
  }
  for (const reference of contract.tests ?? []) {
    const parts = String(reference).split("#");
    assertCondition(parts.length === 2, `Managed fuzz test reference '${reference}' must use path#method format.`);
    const testPath = path.join(repositoryRoot, parts[0]);
    assertCondition(isFile(testPath), `Managed fuzz test source '${parts[0]}' does not exist.`);
    const source = fs.readFileSync(testPath, "utf8");
    assertCondition(new RegExp(`\\b${parts[1].replace(/[.*+?^${}()|[\]\\]/g, "\\$&")}\\s*\\(`).test(source), `Managed fuzz test method '${parts[1]}' does not exist in '${parts[0]}'.`);
  }

  console.log("Managed fuzz corpus validation passed.");
  console.log(`Targets: ${targetIds.length}`);
  console.log(`Seeds: ${seedCount}`);
  console.log(`Iterations per target: ${corpus.iterationsPerTarget}`);
  console.log(`Maximum input bytes: ${corpus.maxInputBytes}`);
});
