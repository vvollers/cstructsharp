#!/usr/bin/env node
/**
 * Validates the canonical Portable contract (contracts/language/portable-v1.json) for internal consistency, its
 * agreement with the feature-operation matrix, and that the split language manual names every primitive,
 * alias, and predictive layout example.
 *
 *   node tools/documentation/validate-canonical-reference.mjs [--contract-path ...] [--reference-path docs/language/index.md] [--matrix-path ...]
 */
import fs from "node:fs";
import path from "node:path";
import { assertCondition, main, parseArguments, repositoryRoot } from "../lib/tooling.mjs";
import { escapeRegex, isFile, listFiles } from "../lib/files.mjs";

const options = parseArguments(process.argv.slice(2), { "contract-path": "string", "reference-path": "string", "matrix-path": "string" }, {
  defaults: {
    "contract-path": path.join(repositoryRoot, "contracts/language/portable-v1.json"),
    "reference-path": path.join(repositoryRoot, "docs/language/index.md"),
    "matrix-path": path.join(repositoryRoot, "contracts/quality/feature-operation-matrix.json"),
  },
});
const blank = (value) => value === undefined || value === null || String(value).trim() === "";
const sortedJoin = (items) => [...items].map(String).sort().join(",");
const unique = (items) => new Set(items).size === items.length;

await main(() => {
  assertCondition(isFile(options["contract-path"]), `Canonical Portable contract '${options["contract-path"]}' does not exist.`);
  assertCondition(isFile(options["reference-path"]), `Canonical layout reference '${options["reference-path"]}' does not exist.`);
  assertCondition(isFile(options["matrix-path"]), `Feature-operation matrix '${options["matrix-path"]}' does not exist.`);
  const contract = JSON.parse(fs.readFileSync(options["contract-path"], "utf8"));
  const matrix = JSON.parse(fs.readFileSync(options["matrix-path"], "utf8"));
  const referenceFiles = listFiles(path.dirname(path.resolve(options["reference-path"])), (file) => file.endsWith(".md"));
  assertCondition(referenceFiles.length >= 23, `The split language manual is incomplete; found only ${referenceFiles.length} pages.`);
  const reference = referenceFiles.map((file) => fs.readFileSync(file, "utf8")).join("\n");

  assertCondition(contract.schemaVersion === 1, "Unsupported canonical Portable contract schema version.");
  assertCondition(contract.contractRevision === 2, "Unsupported canonical Portable contract revision.");
  assertCondition(contract.profile === "Portable", "The canonical contract must describe the Portable profile.");
  assertCondition((contract.shippedProfiles ?? []).length === 1 && contract.shippedProfiles[0] === "Portable", "Portable must be the sole shipped profile.");
  const canonical = matrix.canonicalReference;
  assertCondition(canonical.profile === contract.profile, "The feature-operation matrix names a different canonical profile.");
  assertCondition(canonical.contractRevision === contract.contractRevision, "The feature-operation matrix names a different canonical contract revision.");
  assertCondition(canonical.contract === "contracts/language/portable-v1.json", "The feature-operation matrix has an unexpected canonical contract path.");
  assertCondition(canonical.manualFixtures === "contracts/language/manual-fixtures-v1.json", "The feature-operation matrix has an unexpected manual-fixture contract path.");
  assertCondition(canonical.reference === "docs/language/index.md", "The feature-operation matrix has an unexpected human reference path.");
  assertCondition(canonical.validator === "tools/documentation/validate-canonical-reference.mjs", "The feature-operation matrix has an unexpected canonical validator path.");
  assertCondition(canonical.workItem === "DOC-01", "The feature-operation matrix must assign the canonical reference to DOC-01.");

  const fixedSpellings = (contract.fixedPrimitives ?? []).map((item) => String(item.spelling));
  const terminatedSpellings = (contract.terminatedPrimitives ?? []).map((item) => String(item.spelling));
  const dynamicSpellings = (contract.dynamicNumericPrimitives ?? []).map((item) => String(item.spelling));
  const spellings = matrix.primitiveSpellings ?? {};
  assertCondition(dynamicSpellings.length === 4 && unique(dynamicSpellings), "The canonical dynamic integer table must contain four distinct width/signedness combinations.");
  assertCondition(sortedJoin(spellings.dynamicNumeric ?? []) === sortedJoin(dynamicSpellings), "The canonical dynamic integer spellings differ from the feature-operation matrix.");
  for (const primitive of contract.dynamicNumericPrimitives ?? []) {
    const match = /^([us])leb128_(32|64)$/.exec(primitive.spelling);
    assertCondition(match, "Unsupported dynamic integer spelling.");
    const expectedClr = match[1] === "u" ? `UInt${match[2]}` : `Int${match[2]}`;
    const expectedMaximum = match[2] === "32" ? 5 : 10;
    assertCondition(primitive.clr === expectedClr && primitive.maximumBytes === expectedMaximum && primitive.alignment === 1, `Incorrect LEB128 width/result/alignment contract: ${primitive.spelling}`);
    assertCondition(!blank(primitive.reader) && !blank(primitive.writer), `Missing LEB128 read/write policy: ${primitive.spelling}`);
  }
  assertCondition(unique(fixedSpellings), "The canonical fixed-primitive table contains duplicate spellings.");
  assertCondition(unique(terminatedSpellings), "The canonical terminated-primitive table contains duplicate spellings.");
  assertCondition(sortedJoin(spellings.fixed ?? []) === sortedJoin(fixedSpellings), "The canonical fixed-primitive spellings differ from the feature-operation matrix.");
  assertCondition(sortedJoin(spellings.terminated ?? []) === sortedJoin(terminatedSpellings), "The canonical terminated-primitive spellings differ from the feature-operation matrix.");

  for (const primitive of contract.fixedPrimitives ?? []) {
    const context = `Fixed primitive '${primitive.spelling}'`;
    assertCondition(!blank(primitive.canonical), `${context} has no canonical codec.`);
    assertCondition([1, 2, 3, 4, 6, 8, 16].includes(primitive.bytes), `${context} has an invalid byte width.`);
    const expectedAlignment = [3, 6].includes(primitive.bytes) || ["uuid", "guid"].includes(primitive.spelling) ? 1 : primitive.bytes;
    assertCondition(primitive.alignment === expectedAlignment, `${context} has an incorrect Portable alignment.`);
    if (primitive.bytes === 3) {
      assertCondition(/^(?:u?int24)[<>]?$/i.test(primitive.spelling), `${context} is not a supported three-byte integer.`);
      assertCondition(["Int32", "UInt32"].includes(primitive.clr), `${context} must use a 32-bit CLR result.`);
    }
    if (primitive.bytes === 6) {
      assertCondition(/^(?:u?int48)[<>]?$/i.test(primitive.spelling), `${context} is not a supported six-byte integer.`);
      assertCondition(["Int64", "UInt64"].includes(primitive.clr), `${context} must use a 64-bit CLR result.`);
    }
    if (primitive.bytes === 16 && ["uuid", "guid"].includes(primitive.spelling)) {
      assertCondition(primitive.clr === "Guid" && primitive.endian === "independent", `${context} must use explicit identifier byte order.`);
    } else if (primitive.bytes === 16) {
      assertCondition(/^(?:u?int128)[<>]?$/i.test(primitive.spelling), `${context} is not a supported sixteen-byte integer.`);
      assertCondition(["Int128", "UInt128"].includes(primitive.clr), `${context} must use a 128-bit CLR result.`);
    }
    if (primitive.signedness === "fixed-point") {
      assertCondition(/^(?:u?fixed16_16|fixed2_30|ufixed8_8)[<>]?$/i.test(primitive.spelling), `${context} has an unsupported fixed-point scale.`);
      assertCondition(primitive.clr === "Double", `${context} must preserve fixed-point values as Double.`);
    }
    assertCondition(["signed", "unsigned", "code-unit", "boolean", "floating", "fixed-point", "identifier"].includes(primitive.signedness), `${context} has an invalid signedness classification.`);
    assertCondition(["independent", "layout", "little", "big"].includes(primitive.endian), `${context} has an invalid endian classification.`);
    assertCondition(!blank(primitive.clr), `${context} has no CLR result type.`);
    assertCondition(!blank(primitive.writerDomain), `${context} has no writer domain.`);
  }
  for (const primitive of contract.terminatedPrimitives ?? []) {
    const context = `Terminated primitive '${primitive.spelling}'`;
    assertCondition(!blank(primitive.encoding), `${context} has no encoding.`);
    assertCondition(["NUL", "LF"].includes(primitive.terminator), `${context} has an invalid terminator.`);
    assertCondition(["independent", "layout", "little", "big"].includes(primitive.endian), `${context} has an invalid endian classification.`);
    assertCondition(primitive.alignment === 1, `${context} alignment must be one.`);
    assertCondition(primitive.clr === "String", `${context} must return System.String.`);
  }

  const examples = contract.layoutExamples ?? [];
  const exampleIds = examples.map((example) => String(example.id));
  assertCondition(exampleIds.length >= 6, "The canonical contract must contain at least six predictive examples.");
  assertCondition(unique(exampleIds), "The canonical layout examples contain duplicate ids.");
  for (const example of examples) {
    const context = `Layout example '${example.id}'`;
    assertCondition(/^[a-z0-9]+(?:-[a-z0-9]+)*$/i.test(example.id), `${context} id must be lowercase kebab-case.`);
    assertCondition(!blank(example.definition), `${context} has no definition.`);
    assertCondition(!blank(example.root), `${context} has no root.`);
    assertCondition([1, 2, 4, 8].includes(example.pointerSize), `${context} has an invalid pointer size.`);
    assertCondition(example.size >= 0, `${context} has a negative size.`);
    assertCondition(example.alignment >= 1, `${context} has an invalid alignment.`);
    assertCondition(Object.keys(example.offsets ?? {}).length > 0, `${context} has no predicted offsets.`);
    assertCondition(Object.keys(example.values ?? {}).length > 0, `${context} has no predicted values.`);
    assertCondition(/^(?:[0-9A-F]{2})*$/i.test(String(example.bytes ?? "")), `${context} bytes must be uppercase hexadecimal octets without separators.`);
    assertCondition(String(example.bytes ?? "").length / 2 === example.size, `${context} byte image length does not match its predicted size.`);
    assertCondition(reference.includes(`\`${example.id}\``), `${context} is not named in the canonical reference.`);
  }
  const unsupported = contract.unsupportedConstructs ?? [];
  const unsupportedIds = unsupported.map((item) => String(item.id));
  assertCondition(unsupportedIds.length >= 15, "The canonical contract must contain a representative valid-C unsupported corpus.");
  assertCondition(unique(unsupportedIds), "The canonical unsupported corpus contains duplicate ids.");
  for (const item of unsupported) {
    const context = `Unsupported construct '${item.id}'`;
    assertCondition(/^[a-z0-9]+(?:-[a-z0-9]+)*$/i.test(item.id), `${context} id must be lowercase kebab-case.`);
    assertCondition(!blank(item.definition), `${context} has no definition.`);
    assertCondition(!blank(item.category), `${context} has no category.`);
  }
  for (const testReference of canonical.tests ?? []) {
    const separator = String(testReference).indexOf("#");
    assertCondition(separator >= 0, `Canonical test reference '${testReference}' must use path#method format.`);
    const [file, method] = [String(testReference).slice(0, separator), String(testReference).slice(separator + 1)];
    const sourcePath = path.join(repositoryRoot, file);
    assertCondition(isFile(sourcePath), `Canonical test source '${file}' does not exist.`);
    assertCondition(new RegExp(`\\b${escapeRegex(method)}\\s*\\(`).test(fs.readFileSync(sourcePath, "utf8")), `Canonical test method '${method}' does not exist in '${file}'.`);
  }
  for (const heading of ["# Portable v1 rules", "# Complete Portable grammar", "# Primitive types", "## Checked layout examples", "# Differences from C"]) {
    assertCondition(reference.includes(heading), `The canonical reference is missing heading '${heading}'.`);
  }
  for (const spelling of [...fixedSpellings, ...terminatedSpellings]) {
    assertCondition(reference.includes(`\`${spelling}\``), `The canonical reference does not name primitive spelling '${spelling}'.`);
  }
  const aliasSpellings = (contract.aliasSpellings ?? []).map((item) => String(item.spelling));
  assertCondition(unique(aliasSpellings), "The canonical alias table contains duplicate spellings.");
  // The matrix catalogues each spelling once: an alias that is also a terminated-string spelling (`string`, `cstring`)
  // sits in its terminated list rather than in aliases.
  const matrixAliases = [...(spellings.aliases ?? []), ...(spellings.pointerSized ?? []), ...(spellings.terminated ?? []).filter((item) => aliasSpellings.includes(String(item)))].map(String);
  assertCondition(sortedJoin(matrixAliases) === sortedJoin(aliasSpellings), "The canonical alias spellings differ from the feature-operation matrix.");
  for (const alias of contract.aliasSpellings ?? []) {
    assertCondition(!blank(alias.canonical), `Alias '${alias.spelling}' has no canonical codec.`);
    assertCondition(reference.includes(`\`${alias.spelling}\``), `The canonical reference does not name alias spelling '${alias.spelling}'.`);
  }

  console.log("Canonical Portable reference validation passed.");
  console.log(`Fixed primitives: ${fixedSpellings.length}`);
  console.log(`Terminated primitives: ${terminatedSpellings.length}`);
  console.log(`Predictive layout examples: ${exampleIds.length}`);
  console.log(`Alias spellings: ${aliasSpellings.length}`);
  console.log(`Unsupported C constructs: ${unsupportedIds.length}`);
});
