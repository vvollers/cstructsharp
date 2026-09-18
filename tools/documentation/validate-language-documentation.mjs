#!/usr/bin/env node
/**
 * Validates the language manual against the Portable contract, the manual fixtures, and the feature-operation
 * matrix: required pages, one fixture pair per feature with well-formed valid/invalid halves, manual anchors that
 * exist, grammar productions with index entries, every canonical primitive spelling named, and every unsupported
 * construct named in the differences-from-C reference.
 *
 *   node tools/documentation/validate-language-documentation.mjs [--self-test] [--contract-path ...]
 *     [--fixture-path ...] [--matrix-path ...] [--language-root docs/language]
 */
import fs from "node:fs";
import path from "node:path";
import { assertCondition, main, parseArguments, repositoryRoot } from "../lib/tooling.mjs";
import { isFile } from "../lib/files.mjs";

const options = parseArguments(
  process.argv.slice(2),
  { "self-test": "flag", "contract-path": "string", "fixture-path": "string", "matrix-path": "string", "language-root": "string" },
  {
    defaults: {
      "self-test": false,
      "contract-path": path.join(repositoryRoot, "contracts/language/portable-v1.json"),
      "fixture-path": path.join(repositoryRoot, "contracts/language/manual-fixtures-v1.json"),
      "matrix-path": path.join(repositoryRoot, "contracts/quality/feature-operation-matrix.json"),
      "language-root": path.join(repositoryRoot, "docs/language"),
    },
  },
);
const blank = (value) => value === undefined || value === null || String(value).trim() === "";
const sortedJoin = (items) => [...items].map(String).sort().join(",");

export function markdownAnchor(heading) {
  let anchor = heading.trim().toLowerCase();
  anchor = anchor.replace(/<[^>]+>/g, "");
  anchor = anchor.replace(/[^a-z0-9 _-]/g, "");
  anchor = anchor.replace(/\s+/g, "-");
  return anchor.replace(/^-+|-+$/g, "");
}

function assertManualReference(reference, context) {
  const separator = reference.indexOf("#");
  assertCondition(separator >= 0, `${context} manual reference '${reference}' must use repository-path#anchor format.`);
  const page = reference.slice(0, separator);
  const anchor = reference.slice(separator + 1);
  assertCondition(/^docs\/language\/.+\.md$/.test(page), `${context} manual reference '${reference}' must target the tracked language manual.`);
  assertCondition(/^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(anchor), `${context} manual reference '${reference}' has an invalid anchor.`);
  const pagePath = path.join(repositoryRoot, page);
  assertCondition(isFile(pagePath), `${context} manual page '${page}' does not exist.`);
  const text = fs.readFileSync(pagePath, "utf8");
  const anchors = [...text.matchAll(/^#{1,6}\s+(.+?)\s*$/gm)].map((match) => markdownAnchor(match[1]));
  assertCondition(anchors.includes(anchor), `${context} manual anchor '#${anchor}' does not exist in '${page}'.`);
}

await main(() => {
  if (options["self-test"]) {
    let caught = false;
    try {
      assertCondition(false, "expected language-validator self-test failure");
    } catch (error) {
      caught = error.message === "expected language-validator self-test failure";
    }
    assertCondition(caught, "The language-validator self-test did not observe its expected failure.");
    console.log("Validate-LanguageDocumentation self-test passed.");
    return;
  }
  const languageRoot = options["language-root"];
  for (const input of [options["contract-path"], options["fixture-path"], options["matrix-path"], languageRoot]) {
    assertCondition(fs.existsSync(input), `Required language-documentation input does not exist: ${input}`);
  }
  const requiredPages = [
    "index.md",
    "tutorial/index.md",
    "tutorial/01-first-layout.md",
    "tutorial/02-composites-and-layout.md",
    "tutorial/03-runtime-data.md",
    "lexical-rules.md",
    "grammar.md",
    "primitive-types.md",
    "structs-unions-enums-typedefs.md",
    "names-and-scopes.md",
    "arrays-and-strings.md",
    "bitfields.md",
    "expressions-defines-and-variables.md",
    "layout-alignment-and-padding.md",
    "pointers-and-addressing.md",
    "paths-and-selection.md",
    "values-reading-and-writing.md",
    "writing-and-updating.md",
    "compilation-and-operations.md",
    "operation-matrix.md",
    "limits-and-diagnostics.md",
    "differences-from-c.md",
    "cookbook/index.md",
  ];
  for (const relative of requiredPages) assertCondition(isFile(path.join(languageRoot, relative)), `Required language-manual page is missing: ${relative}`);

  const contract = JSON.parse(fs.readFileSync(options["contract-path"], "utf8"));
  const fixtures = JSON.parse(fs.readFileSync(options["fixture-path"], "utf8"));
  const matrix = JSON.parse(fs.readFileSync(options["matrix-path"], "utf8"));
  assertCondition(fixtures.schemaVersion === 1, "Unsupported manual-fixture schema version.");
  assertCondition(fixtures.profile === "Portable", "Manual fixtures must describe the Portable profile.");
  assertCondition(fixtures.contractRevision === contract.contractRevision, "Manual fixtures and the canonical Portable contract use different revisions.");

  const featureIds = matrix.features.map((feature) => String(feature.id));
  const fixturePairs = fixtures.featurePairs ?? [];
  const fixtureIds = fixturePairs.map((pair) => String(pair.id));
  const fixtureFeatureIds = fixturePairs.map((pair) => String(pair.featureId));
  assertCondition(new Set(fixtureIds).size === fixtureIds.length, "Manual fixture-pair ids are not unique.");
  assertCondition(new Set(fixtureFeatureIds).size === fixtureFeatureIds.length, "Each operation-matrix feature must have exactly one manual fixture pair.");
  assertCondition(sortedJoin(featureIds) === sortedJoin(fixtureFeatureIds), "Manual fixture pairs do not cover exactly the operation-matrix feature ids.");

  const unsupportedIds = new Set((contract.unsupportedConstructs ?? []).map((item) => String(item.id)));
  for (const pair of fixturePairs) {
    const context = `Manual fixture '${pair.id}'`;
    assertCondition(/^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(pair.id), `${context} id is not kebab-case.`);
    assertCondition(featureIds.includes(pair.featureId), `${context} names an unknown matrix feature.`);
    assertManualReference(String(pair.manual), context);
    assertCondition(!blank(pair.valid?.definition), `${context} has no valid definition.`);
    assertCondition(!blank(pair.valid?.root), `${context} has no valid root.`);
    assertCondition([1, 2, 4, 8].includes(pair.valid?.pointerSize), `${context} has an invalid pointer width.`);
    assertCondition(/^(?:[0-9A-F]{2})+$/.test(String(pair.valid?.bytes ?? "")), `${context} valid bytes must be non-empty uppercase hexadecimal octets.`);
    assertCondition(Object.keys(pair.valid?.offsets ?? {}).length > 0, `${context} has no exact offset prediction.`);
    assertCondition(Object.keys(pair.valid?.values ?? {}).length > 0, `${context} has no exact value prediction.`);
    assertCondition(["compile", "read"].includes(pair.invalid?.stage), `${context} has an unsupported invalid-fixture stage.`);
    assertCondition(["InvalidLayout", "ReadFailed", "ReadLimitExceeded"].includes(pair.invalid?.errorCode), `${context} has an unsupported stable error category.`);
    if (pair.invalid.stage === "compile") {
      assertCondition(unsupportedIds.has(String(pair.invalid.unsupportedId)), `${context} references unknown unsupported construct '${pair.invalid.unsupportedId}'.`);
    } else {
      assertCondition(!blank(pair.invalid.definition), `${context} read failure has no definition.`);
      assertCondition(/^(?:[0-9A-F]{2})*$/.test(String(pair.invalid.bytes ?? "")), `${context} invalid read bytes are not uppercase hexadecimal octets.`);
    }
  }
  for (const feature of matrix.features) {
    const context = `Feature '${feature.id}'`;
    assertCondition(Object.hasOwn(feature, "manual"), `${context} has no manual reference.`);
    assertCondition(Object.hasOwn(feature, "fixture"), `${context} has no executable manual-fixture id.`);
    assertManualReference(String(feature.manual), context);
    const pairs = fixturePairs.filter((pair) => pair.id === feature.fixture);
    assertCondition(pairs.length === 1, `${context} fixture '${feature.fixture}' does not resolve exactly once.`);
    assertCondition(pairs[0].featureId === feature.id, `${context} fixture belongs to '${pairs[0].featureId}'.`);
    assertCondition(pairs[0].manual === feature.manual, `${context} manual reference differs from its fixture.`);
  }

  const grammar = fs.readFileSync(path.join(languageRoot, "grammar.md"), "utf8");
  const productions = [...new Set([...grammar.matchAll(/^([a-z][a-z0-9-]*)\s*=/gm)].map((match) => match[1]))];
  assertCondition(productions.length >= 35, `The complete source/path/lexical grammar must expose at least 35 productions; found ${productions.length}.`);
  for (const production of productions) assertCondition(grammar.includes(`| \`${production}\` |`), `Grammar production '${production}' has no production-index explanation.`);

  const primitiveReference = fs.readFileSync(path.join(languageRoot, "primitive-types.md"), "utf8");
  const primitives = [...(contract.fixedPrimitives ?? []), ...(contract.terminatedPrimitives ?? [])];
  for (const primitive of primitives) assertCondition(primitiveReference.includes(`\`${primitive.spelling}\``), `Primitive reference does not name canonical spelling '${primitive.spelling}'.`);
  const differences = fs.readFileSync(path.join(languageRoot, "differences-from-c.md"), "utf8");
  for (const unsupported of contract.unsupportedConstructs ?? []) {
    assertCondition(differences.includes(`\`${unsupported.id}\``), `Differences-from-C reference does not name unsupported fixture '${unsupported.id}'.`);
  }
  assertCondition(differences.includes("`InvalidLayout`"), "Differences-from-C reference must state the stable InvalidLayout category.");
  for (const example of contract.layoutExamples ?? []) {
    assertCondition(Object.keys(example.values ?? {}).length > 0, `Canonical predictive example '${example.id}' has no exact value prediction.`);
  }

  console.log("Language documentation validation passed.");
  console.log(`Grammar productions: ${productions.length}`);
  console.log(`Canonical primitive spellings: ${primitives.length}`);
  console.log(`Predictive layout examples: ${(contract.layoutExamples ?? []).length}`);
  console.log(`Valid/invalid feature pairs: ${fixturePairs.length}`);
  console.log(`Unsupported C constructs: ${(contract.unsupportedConstructs ?? []).length}`);
});
