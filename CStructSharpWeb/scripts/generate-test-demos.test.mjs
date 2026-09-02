import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { spawnSync } from "node:child_process";
import test from "node:test";
import { fileURLToPath } from "node:url";

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(scriptDirectory, "..");
const generator = path.join(scriptDirectory, "generate-test-demos.mjs");
const outputPath = path.join(webRoot, "src", "generated", "test-demos.json");

function generate() {
  const result = spawnSync(process.execPath, [generator], {
    cwd: webRoot,
    encoding: "utf8",
    shell: false,
  });
  if (result.error) {
    throw result.error;
  }
  assert.equal(result.status, 0, result.stderr);
  return fs.readFileSync(outputPath, "utf8");
}

test("demo generation is byte-for-byte deterministic for unchanged sources", async () => {
  const first = generate();
  await new Promise((resolve) => setTimeout(resolve, 20));
  const second = generate();

  assert.equal(second, first);
  assert.equal(Object.hasOwn(JSON.parse(second), "generatedAtUtc"), false);
});

test("demo generation keeps extracted inputs complete and constructor options intact", () => {
  const manifest = JSON.parse(generate());
  const byId = new Map(manifest.tests.map((entry) => [entry.id, entry]));

  const nestedArray = byId.get("PathAccess.ParseStream_Path_StringInNestedArray_IsExpected_V2");
  assert.equal(nestedArray.runnable, true);
  assert.equal(nestedArray.binaryHex, "6f 6e 65 00 74 65 73 74");
  assert.equal(nestedArray.parserOptions.pointerSize, 1);

  const fixedPrimitive = byId.get(
    "FixedArrayShapeTests.FixedPrimitiveArrays_KeepShapeForZeroAndOneElements",
  );
  assert.equal(fixedPrimitive.runnable, true);
  assert.equal(fixedPrimitive.definition, "struct root { byte values[0]; byte tail; };");
  assert.equal(fixedPrimitive.binaryHex, "a5");

  const fixedCharacter = byId.get(
    "FixedArrayShapeTests.FixedCharacterAndNestedArrays_KeepDeclaredShape",
  );
  assert.equal(fixedCharacter.runnable, true);
  assert.equal(fixedCharacter.binaryHex, "51 a5");

  const concatenatedDefinition = byId.get(
    "CompiledExecutionParityTests.UnionStructMember_UsesSequentialCompiledFieldTraversal",
  );
  assert.equal(concatenatedDefinition.runnable, true);
  assert.match(concatenatedDefinition.definition, /union choice/);

  const compiledTypedef = byId.get(
    "CompiledIntermediateRepresentationTests.PrimitiveTypedefSlice_UsesCompiledFactsAcrossEveryOperation",
  );
  assert.equal(compiledTypedef.parserOptions.pointerSize, 2);

  const bigEndianTraversal = byId.get(
    "TraversalLimitTests.TraversalLimits_AreInclusiveAtConfiguredBoundaries",
  );
  assert.equal(bigEndianTraversal.parserOptions.pointerSize, 1);
  assert.equal(bigEndianTraversal.parserOptions.littleEndian, false);

  const partialCollection = byId.get(
    "StringEncodingTests.ExplicitUtf16NewlineHandlers_StopAtEncodedTerminator",
  );
  assert.equal(partialCollection.runnable, false);

  const expectedFailure = byId.get(
    "EnumDomainTests.WideEnumScalar_ShadowsStaleExpressionVariables",
  );
  assert.equal(expectedFailure.runnable, false);
  assert.equal(expectedFailure.reason, "This test verifies an expected parse failure.");
});
