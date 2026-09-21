#!/usr/bin/env node
/**
 * Validates contracts/quality/feature-operation-matrix.json: vocabulary, every feature's dimensions, operation
 * statuses and executable evidence (path#method references that must exist), the generated-parity status of every
 * feature against the parity project's layout index, the memory I/O, compiled execution,
 * operation context, managed and browser compatibility contracts, round-trip contracts, known limits, exclusions,
 * and the linked domain contracts.
 *
 *   node tools/quality/feature-operation-matrix.mjs [--matrix-path <json>] [--manual-fixture-path <json>]
 */
import fs from "node:fs";
import path from "node:path";
import { assertCondition, main, parseArguments, repositoryRoot } from "../lib/tooling.mjs";

const options = parseArguments(process.argv.slice(2), { "matrix-path": "string", "manual-fixture-path": "string" }, {
  defaults: {
    "matrix-path": path.join(repositoryRoot, "contracts/quality/feature-operation-matrix.json"),
    "manual-fixture-path": path.join(repositoryRoot, "contracts/language/manual-fixtures-v1.json"),
  },
});
const isFile = (file) => fs.existsSync(file) && fs.statSync(file).isFile();
const blank = (value) => value === undefined || value === null || String(value).trim() === "";
const sortedJoin = (items) => [...items].map(String).sort().join(",");
const strings = (items) => (items ?? []).map(String);
const escapeRegex = (text) => text.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");

function assertUniqueIds(items, collectionName) {
  const ids = items.map((item) => String(item.id));
  assertCondition(new Set(ids).size === ids.length, `${collectionName} contains duplicate ids.`);
  for (const id of ids) assertCondition(/^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(id), `${collectionName} id '${id}' must be lowercase kebab-case.`);
}

function assertWorkItems(workItems, context, required = false) {
  const items = strings(workItems).filter((item) => !blank(item));
  if (required) assertCondition(items.length > 0, `${context} must name at least one work item.`);
  assertCondition(new Set(items).size === items.length, `${context} contains duplicate work-item ids.`);
  for (const item of items) assertCondition(/^[A-Z]+-\d{2}$/.test(item), `${context} contains invalid traceability id '${item}'.`);
}

function assertEvidenceReference(reference, context) {
  const separator = reference.indexOf("#");
  const parts = separator < 0 ? [reference] : [reference.slice(0, separator), reference.slice(separator + 1)];
  assertCondition(parts.length === 2 && !blank(parts[0]) && !blank(parts[1]), `${context} evidence '${reference}' must use path#test-method format.`);
  const candidate = path.join(repositoryRoot, parts[0]);
  assertCondition(isFile(candidate), `${context} evidence file '${parts[0]}' does not exist.`);
  const source = fs.readFileSync(candidate, "utf8");
  assertCondition(new RegExp(`\\b${escapeRegex(parts[1])}\\s*\\(`).test(source), `${context} evidence method '${parts[1]}' was not found in '${parts[0]}'.`);
}

await main(() => {
  const matrix = JSON.parse(fs.readFileSync(options["matrix-path"], "utf8"));
  const manualFixtures = JSON.parse(fs.readFileSync(options["manual-fixture-path"], "utf8"));
  assertCondition(matrix.schemaVersion === 2, "Unsupported feature-operation matrix schema version.");
  assertCondition(manualFixtures.schemaVersion === 1, "Unsupported manual-fixture schema version.");

  const manualFixtureById = new Map();
  for (const pair of manualFixtures.featurePairs ?? []) {
    const id = String(pair.id);
    assertCondition(!manualFixtureById.has(id), `Duplicate manual fixture id '${id}'.`);
    manualFixtureById.set(id, pair);
  }

  const operationIds = strings((matrix.operations ?? []).map((operation) => operation.id));
  assertUniqueIds(matrix.operations ?? [], "operations");
  assertCondition(operationIds.length > 0, "The matrix must define at least one operation.");
  const allowedStatuses = Object.keys(matrix.statuses ?? {});
  assertCondition(sortedJoin(allowedStatuses) === sortedJoin(["blocked", "limited", "notApplicable", "verified"]), "The status vocabulary must be exactly blocked, limited, notApplicable, and verified.");
  const dimensionIds = Object.keys(matrix.dimensions ?? {});
  assertCondition(dimensionIds.length > 0, "The matrix must define dimensions.");
  const allowedDimensionValues = new Map();
  for (const [name, values] of Object.entries(matrix.dimensions)) {
    const list = strings(values);
    assertCondition(list.length > 0, `Dimension '${name}' has no allowed values.`);
    assertCondition(new Set(list).size === list.length, `Dimension '${name}' contains duplicate values.`);
    allowedDimensionValues.set(name, list);
  }

  const allPrimitiveSpellings = [...strings(matrix.primitiveSpellings?.dynamicNumeric), ...strings(matrix.primitiveSpellings?.fixed), ...strings(matrix.primitiveSpellings?.terminated)];
  assertCondition(allPrimitiveSpellings.length > 0, "primitiveSpellings must not be empty.");
  assertCondition(new Set(allPrimitiveSpellings).size === allPrimitiveSpellings.length, "primitiveSpellings contains duplicate names.");

  assertUniqueIds(matrix.features ?? [], "features");
  const generated = matrix.generatedEvidence;
  assertCondition(generated, "generatedEvidence is required.");
  assertCondition(Number(generated.schemaVersion) === 1, "Unsupported generatedEvidence schema version.");
  for (const property of ["claim", "generator", "project", "layouts", "tool", "guide"]) {
    assertCondition(!blank(generated[property]), `generatedEvidence has no ${property}.`);
  }
  for (const property of ["generator", "project", "layouts", "tool", "guide"]) {
    assertCondition(isFile(path.join(repositoryRoot, String(generated[property]))), `generatedEvidence ${property} '${generated[property]}' does not exist.`);
  }
  const generatedStatuses = Object.keys(generated.statuses ?? {});
  assertCondition(sortedJoin(generatedStatuses) === sortedJoin(["parity", "runtime-only"]), "generatedEvidence must define exactly the parity and runtime-only statuses.");
  assertCondition(sortedJoin(strings(generated.operations)) === sortedJoin(["parse", "serialize", "address", "truncation"]), "generatedEvidence must list the exact compared operations.");
  const generatedTests = strings(generated.tests);
  assertCondition(generatedTests.length > 0, "generatedEvidence has no executable evidence.");
  for (const reference of generatedTests) assertEvidenceReference(reference, "generatedEvidence");
  const generatedIndex = JSON.parse(fs.readFileSync(path.join(repositoryRoot, String(generated.layouts)), "utf8"));
  const generatedFixtures = new Set((generatedIndex.layouts ?? []).filter((layout) => layout.source === "Manual").map((layout) => String(layout.id)));
  assertCondition(generatedFixtures.size > 0, "The parity layouts index lists no Manual fixtures.");

  for (const feature of matrix.features) {
    const context = `Feature '${feature.id}'`;
    assertCondition(!blank(feature.manual), `${context} has no language-manual reference.`);
    const manualSeparator = String(feature.manual).indexOf("#");
    assertCondition(manualSeparator >= 0, `${context} manual reference must use repository-path#anchor format.`);
    const manualPage = String(feature.manual).slice(0, manualSeparator);
    assertCondition(isFile(path.join(repositoryRoot, manualPage)), `${context} manual page '${manualPage}' does not exist.`);
    assertCondition(!blank(feature.fixture), `${context} has no executable manual fixture.`);
    assertCondition(manualFixtureById.has(String(feature.fixture)), `${context} fixture '${feature.fixture}' does not exist.`);
    const manualPair = manualFixtureById.get(String(feature.fixture));
    assertCondition(manualPair.featureId === feature.id, `${context} fixture belongs to '${manualPair.featureId}'.`);
    assertCondition(manualPair.manual === feature.manual, `${context} manual reference differs from its executable fixture.`);
    assertCondition(["supported", "limited"].includes(feature.support), `${context} has unknown support value '${feature.support}'.`);
    assertCondition(!blank(feature.title), `${context} has no title.`);
    assertCondition(sortedJoin(Object.keys(feature.dimensions ?? {})) === sortedJoin(dimensionIds), `${context} must fill every dimension exactly once.`);
    for (const [name, values] of Object.entries(feature.dimensions)) {
      const list = strings(values);
      assertCondition(list.length > 0, `${context} dimension '${name}' is empty.`);
      for (const value of list) assertCondition(allowedDimensionValues.get(name).includes(value), `${context} dimension '${name}' contains unknown value '${value}'.`);
    }
    assertCondition(sortedJoin(Object.keys(feature.operations ?? {})) === sortedJoin(operationIds), `${context} must classify every public operation exactly once.`);
    const evidenceCoverage = new Map(operationIds.map((id) => [id, []]));
    for (const evidence of feature.evidence ?? []) {
      const reference = String(evidence.test);
      assertEvidenceReference(reference, context);
      const evidenceOperations = strings(evidence.operations);
      assertCondition(evidenceOperations.length > 0, `${context} evidence '${reference}' covers no operations.`);
      for (const operationId of evidenceOperations) {
        assertCondition(operationIds.includes(operationId), `${context} evidence '${reference}' names unknown operation '${operationId}'.`);
        evidenceCoverage.get(operationId).push(reference);
      }
    }
    let requiresWorkItem = false;
    for (const [operationName, statusValue] of Object.entries(feature.operations)) {
      const status = String(statusValue);
      assertCondition(allowedStatuses.includes(status), `${context} operation '${operationName}' has unknown status '${status}'.`);
      if (status === "verified" || status === "limited") {
        assertCondition(evidenceCoverage.get(operationName).length > 0, `${context} operation '${operationName}' is ${status} but has no executable evidence.`);
      }
      if (status === "blocked") requiresWorkItem = true;
    }
    const generatedStatus = String(feature.generated);
    assertCondition(generatedStatuses.includes(generatedStatus), `${context} has unknown generated status '${generatedStatus}'.`);
    if (generatedStatus === "parity") {
      assertCondition(generatedFixtures.has(String(feature.fixture)), `${context} claims generated parity but '${feature.fixture}' is not among the parity project's Manual layouts (run ${generated.tool}).`);
    } else {
      assertCondition(!blank(feature.generatedLimitation), `${context} is runtime-only for the generator but has no generatedLimitation.`);
    }
    const limitations = strings(feature.limitations);
    if (feature.support === "limited" || Object.values(feature.operations).includes("limited")) {
      assertCondition(limitations.length > 0, `${context} is limited but has no written limitation.`);
    }
    assertWorkItems(feature.workItems ?? [], context, requiresWorkItem);
  }

  const memoryIo = matrix.memoryIoContract;
  assertCondition(memoryIo, "memoryIoContract is required.");
  for (const property of ["lifetime", "pointerCoordinates", "partialFailure", "webIntegration"]) {
    assertCondition(!blank(memoryIo[property]), `memoryIoContract has no ${property}.`);
  }
  const requiredMemoryInputApis = [
    "Parse(ReadOnlySpan<byte>)",
    "Parse(ReadOnlyMemory<byte>)",
    "ReadValue(ReadOnlySpan<byte>)",
    "ReadValue(ReadOnlyMemory<byte>)",
    "ReadValue<T>(ReadOnlySpan<byte>)",
    "ReadValue<T>(ReadOnlyMemory<byte>)",
    "TryReadValue<T>(ReadOnlySpan<byte>)",
    "TryReadValue<T>(ReadOnlyMemory<byte>)",
    "Parse(ReadOnlySequence<byte>)",
    "ParseWithDebug(ReadOnlySequence<byte>)",
    "ReadValue(ReadOnlySequence<byte>)",
    "ReadValue<T>(ReadOnlySequence<byte>)",
    "ReadValueWithDebug(ReadOnlySequence<byte>)",
    "TryReadValue<T>(ReadOnlySequence<byte>)",
    "ResolveAddress(ReadOnlySequence<byte>)",
    "GetArrayLength(ReadOnlySequence<byte>)",
    "ParseMany(ReadOnlyMemory<byte>)",
    "ParseMany(ReadOnlySequence<byte>)",
  ];
  const memoryInputApis = strings(memoryIo.inputApis);
  assertCondition(sortedJoin(memoryInputApis) === sortedJoin(requiredMemoryInputApis), "memoryIoContract must list the exact span/memory input overload family.");
  const memoryOutputApis = strings(memoryIo.outputApis);
  assertCondition(sortedJoin(memoryOutputApis) === sortedJoin(["Serialize(Span<byte>)", "Serialize(IBufferWriter<byte>)"]), "memoryIoContract must list the exact caller-owned output overload family.");
  const memoryEvidence = strings(memoryIo.evidence);
  assertCondition(memoryEvidence.length > 0, "memoryIoContract has no executable evidence.");
  for (const reference of memoryEvidence) assertEvidenceReference(reference, "memoryIoContract");
  assertWorkItems([memoryIo.workItem], "memoryIoContract", true);

  const compiledExecution = matrix.compiledExecutionContract;
  assertCondition(compiledExecution, "compiledExecutionContract is required.");
  for (const property of ["structTraversal", "unionTraversal", "debug"]) assertCondition(!blank(compiledExecution[property]), `compiledExecutionContract has no ${property}.`);
  const readRoutes = strings(compiledExecution.readRoutes);
  assertCondition(sortedJoin(readRoutes) === sortedJoin(["root", "nested-field", "selected-struct", "struct-array-element", "pointer-target", "union-member"]), "compiledExecutionContract must list the exact read route families.");
  const writeRoutes = strings(compiledExecution.writeRoutes);
  assertCondition(sortedJoin(writeRoutes) === sortedJoin(["root", "nested-field", "selected-field", "array-element", "pointer-value", "union-member"]), "compiledExecutionContract must list the exact write route families.");
  const compiledEvidence = strings(compiledExecution.evidence);
  assertCondition(compiledEvidence.length > 0, "compiledExecutionContract has no executable evidence.");
  for (const reference of compiledEvidence) assertEvidenceReference(reference, "compiledExecutionContract");
  assertWorkItems([compiledExecution.workItem], "compiledExecutionContract", true);

  const operationContext = matrix.operationContextContract;
  assertCondition(operationContext, "operationContextContract is required.");
  for (const property of ["snapshotBoundary", "readState", "selectedState", "cycleKey"]) assertCondition(!blank(operationContext[property]), `operationContextContract has no ${property}.`);
  const readLikeRoutes = strings(operationContext.readLikeRoutes);
  assertCondition(sortedJoin(readLikeRoutes) === sortedJoin(["parse", "debug", "address", "length", "read-value", "update-address"]), "operationContextContract must list the exact read-like route families.");
  const operationContextEvidence = strings(operationContext.evidence);
  assertCondition(operationContextEvidence.length > 0, "operationContextContract has no executable evidence.");
  for (const reference of operationContextEvidence) assertEvidenceReference(reference, "operationContextContract");
  assertWorkItems([operationContext.workItem], "operationContextContract", true);

  const asyncContract = matrix.asyncContract;
  assertCondition(asyncContract, "asyncContract is required.");
  assertCondition(Number(asyncContract.schemaVersion) === 1, "Unsupported asyncContract schema version.");
  for (const property of ["rule", "pointerCoordinates", "cancellation"]) assertCondition(!blank(asyncContract[property]), `asyncContract has no ${property}.`);
  assertCondition(sortedJoin(strings(asyncContract.readForms)) === sortedJoin(["ParseAsync", "ParseWithDebugAsync", "ReadValueAsync", "ReadValueAsync<T>", "ReadValueWithDebugAsync", "TryReadValueAsync<T>", "ResolveAddressAsync", "GetArrayLengthAsync", "ParseManyAsync"]), "asyncContract must list the exact awaitable read forms.");
  assertCondition(sortedJoin(strings(asyncContract.writeForms)) === sortedJoin(["WriteAsync", "UpdateAsync"]), "asyncContract must list the exact awaitable write forms.");
  assertCondition(sortedJoin(strings(asyncContract.generatedForms)) === sortedJoin(["Parse<Name>Async", "Write<Name>Async", "Records<Name>Async"]), "asyncContract must list the exact generated awaitable forms.");
  for (const property of ["read", "write", "records"]) assertCondition(!blank(asyncContract.positionRules?.[property]), `asyncContract has no ${property} position rule.`);
  for (const property of ["reads", "UpdateAsync", "records"]) assertCondition(!blank(asyncContract.seekableRequirements?.[property]), `asyncContract has no ${property} seekable requirement.`);
  const asyncEvidence = strings(asyncContract.evidence);
  assertCondition(asyncEvidence.length > 0, "asyncContract has no executable evidence.");
  for (const reference of asyncEvidence) assertEvidenceReference(reference, "asyncContract");
  assertWorkItems([asyncContract.workItem], "asyncContract", true);

  const managed = matrix.managedApiCompatibilityContract;
  assertCondition(managed, "managedApiCompatibilityContract is required.");
  assertCondition(managed.baselineId === "managed-rc1", "managedApiCompatibilityContract names an unexpected baseline.");
  assertCondition(managed.baselineRevision === 1, "managedApiCompatibilityContract names an unexpected baseline revision.");
  assertCondition(managed.status === "frozen", "managedApiCompatibilityContract must be frozen.");
  assertCondition(managed.packageVersion === "0.2.0-preview", "managedApiCompatibilityContract names an unexpected package version.");
  assertCondition(managed.exportedTypes === 20, "managedApiCompatibilityContract must retain the reviewed 20-type surface.");
  assertCondition(managed.canonicalLines === 227, "managedApiCompatibilityContract must retain the reviewed 227-line surface.");
  const managedFrameworks = strings(managed.frameworks);
  assertCondition(sortedJoin(managedFrameworks) === sortedJoin(["net8.0", "net10.0"]), "managedApiCompatibilityContract must list exactly net8.0 and net10.0.");
  for (const property of ["manifest", "canonical", "gate", "policy"]) {
    assertCondition(!blank(managed[property]), `managedApiCompatibilityContract has no ${property}.`);
    assertCondition(isFile(path.join(repositoryRoot, managed[property])), `managedApiCompatibilityContract ${property} '${managed[property]}' does not exist.`);
  }
  assertCondition(!blank(managed.browserIntegration), "managedApiCompatibilityContract has no browser-integration boundary.");
  assertWorkItems([managed.workItem], "managedApiCompatibilityContract", true);

  const browser = matrix.browserApiCompatibilityContract;
  assertCondition(browser, "browserApiCompatibilityContract is required.");
  assertCondition(browser.baselineId === "browser-rc1", "browserApiCompatibilityContract names an unexpected baseline.");
  assertCondition(browser.status === "frozen", "browserApiCompatibilityContract must be frozen.");
  assertCondition(browser.packageVersion === "0.2.0-preview", "browserApiCompatibilityContract names an unexpected package version.");
  // The counts mirror the frozen browser baseline; browser-contract.mjs checks that baseline against the sources.
  const browserBaseline = JSON.parse(fs.readFileSync(path.join(repositoryRoot, "contracts/api/browser-rc1/contract.json"), "utf8"));
  assertCondition(browser.contractVersion === browserBaseline.contractVersion, "browserApiCompatibilityContract must record the browser baseline contract version.");
  assertCondition(browser.managedExports === browserBaseline.managedExports.length, "browserApiCompatibilityContract must record the browser baseline managed export count.");
  assertCondition(browser.operations === browserBaseline.operations.length, "browserApiCompatibilityContract must record the browser baseline operation count.");
  assertCondition(browser.optionFields === browserBaseline.optionFields.length, "browserApiCompatibilityContract must record the browser baseline option field count.");
  assertCondition(browser.errorCodes === browserBaseline.errorCodes.length, "browserApiCompatibilityContract must record the browser baseline error code count.");
  for (const property of ["manifest", "gate", "policy"]) {
    assertCondition(!blank(browser[property]), `browserApiCompatibilityContract has no ${property}.`);
    assertCondition(isFile(path.join(repositoryRoot, browser[property])), `browserApiCompatibilityContract ${property} '${browser[property]}' does not exist.`);
  }
  for (const evidencePath of browser.evidence ?? []) {
    assertCondition(isFile(path.join(repositoryRoot, evidencePath)), `browserApiCompatibilityContract evidence '${evidencePath}' does not exist.`);
  }
  assertWorkItems([browser.workItem], "browserApiCompatibilityContract", true);

  const allowedRoundTripStatuses = Object.keys(matrix.roundTripStatuses ?? {});
  assertCondition(sortedJoin(allowedRoundTripStatuses) === sortedJoin(["blocked", "conditional", "notApplicable", "verified"]), "The round-trip status vocabulary must be exactly blocked, conditional, notApplicable, and verified.");
  assertUniqueIds((matrix.roundTripContracts ?? []).map((contract) => ({ id: contract.featureId })), "roundTripContracts");
  const featureIds = strings(matrix.features.map((feature) => feature.id));
  const roundTripFeatureIds = strings((matrix.roundTripContracts ?? []).map((contract) => contract.featureId));
  assertCondition(sortedJoin(roundTripFeatureIds) === sortedJoin(featureIds), "roundTripContracts must classify every feature exactly once.");
  for (const contract of matrix.roundTripContracts) {
    for (const propertyName of ["value", "bytes"]) {
      const classification = contract[propertyName];
      const context = `Round-trip contract '${contract.featureId}.${propertyName}'`;
      const status = String(classification.status);
      assertCondition(allowedRoundTripStatuses.includes(status), `${context} has unknown status '${status}'.`);
      const conditions = strings(classification.conditions).filter((item) => !blank(item));
      assertCondition(conditions.length > 0, `${context} has no conditions.`);
      const evidence = strings(classification.evidence);
      if (status !== "notApplicable") assertCondition(evidence.length > 0, `${context} is ${status} but has no executable evidence.`);
      for (const reference of evidence) assertEvidenceReference(reference, context);
      assertWorkItems(classification.workItems ?? [], context, status === "blocked");
    }
  }

  assertUniqueIds(matrix.knownContractLimits ?? [], "knownContractLimits");
  for (const limit of matrix.knownContractLimits) {
    const context = `Known contract limit '${limit.id}'`;
    assertCondition(!blank(limit.summary), `${context} has no summary.`);
    assertWorkItems(limit.workItems, context, true);
  }
  assertUniqueIds(matrix.exclusions ?? [], "exclusions");
  for (const exclusion of matrix.exclusions) {
    const context = `Exclusion '${exclusion.id}'`;
    for (const property of ["syntax", "rationale", "diagnosticPolicy"]) assertCondition(!blank(exclusion[property]), `${context} has no ${property}.`);
    assertWorkItems(exclusion.workItems, context, true);
  }

  for (const relativePath of matrix.domainContracts ?? []) {
    const domain = JSON.parse(fs.readFileSync(path.join(repositoryRoot, relativePath), "utf8"));
    assertCondition(domain.contractId === "managed-memory-v1", "Unknown domain contract.");
    assertCondition(domain.matrix.length === 6, "Memory matrix must describe all six type kinds.");
    for (const row of domain.matrix) {
      for (const operation of domain.operations) assertCondition(Object.hasOwn(row, operation), `Memory kind '${row.kind}' omits operation '${operation}'.`);
    }
    for (const test of domain.tests) assertCondition(fs.existsSync(path.join(repositoryRoot, `tests/CStructSharpTests/${test}.cs`)), `Missing memory contract tests: ${test}.`);
  }

  console.log("Feature-operation matrix validation passed.");
  console.log(`Operations: ${operationIds.length}`);
  console.log(`Features: ${matrix.features.length}`);
  console.log(`Round-trip contracts: ${matrix.roundTripContracts.length}`);
  console.log(`Generated parity fixtures: ${matrix.features.filter((feature) => feature.generated === "parity").length} of ${matrix.features.length}`);
  console.log(`Primitive spellings: ${allPrimitiveSpellings.length}`);
  console.log(`Memory I/O APIs: ${memoryInputApis.length + memoryOutputApis.length}`);
  console.log(`Compiled read/write routes: ${readRoutes.length + writeRoutes.length}`);
  console.log(`Shared read-like context routes: ${readLikeRoutes.length}`);
  console.log(`Frozen managed API frameworks: ${managedFrameworks.length}`);
  console.log(`Known contract limits: ${matrix.knownContractLimits.length}`);
  console.log(`Deliberate exclusions: ${matrix.exclusions.length}`);
  console.log(`Manual valid/invalid pairs: ${manualFixtureById.size}`);
});
