#!/usr/bin/env node
/**
 * Validates the generated API documentation (docs/api metadata and the built site) against the managed API
 * baseline: every baseline type has metadata and a TOC link, the UID count matches what the baseline implies
 * (including compiler-synthesized record members), every public item carries a summary, parameter, type
 * parameter, return, and exception descriptions, no placeholders, and the reviewed complex models render their
 * remarks and tested scenarios.
 *
 *   node tools/documentation/validate-api-documentation.mjs [--api-directory docs/api] [--baseline-path <txt>]
 *     [--docfx-config-path docs/docfx.json] [--site-api-directory docs/_site/api] [--search-index-path docs/_site/index.json]
 */
import fs from "node:fs";
import path from "node:path";
import { assertCondition, main, parseArguments, repositoryRoot } from "../lib/tooling.mjs";
import { escapeRegex, isDirectory, isFile, lines, listFiles, normalizeNewlines } from "../lib/files.mjs";

const options = parseArguments(
  process.argv.slice(2),
  { "api-directory": "string", "baseline-path": "string", "docfx-config-path": "string", "site-api-directory": "string", "search-index-path": "string" },
  {
    defaults: {
      "api-directory": path.join(repositoryRoot, "docs/api"),
      "baseline-path": path.join(repositoryRoot, "contracts/api/managed-rc1/CStructSharp.public-api.txt"),
      "docfx-config-path": path.join(repositoryRoot, "docs/docfx.json"),
      "site-api-directory": path.join(repositoryRoot, "docs/_site/api"),
      "search-index-path": path.join(repositoryRoot, "docs/_site/index.json"),
    },
  },
);

const TYPE_DECLARATION = /^( *)public (?:abstract |sealed |static |readonly )*(?:class|enum|struct|interface) ([A-Za-z][A-Za-z0-9]*)/;

/** Compiler-generated public and protected record member UIDs omitted from the API snapshot (one namespace block). */
function recordSynthesizedUids(block, namespace, positionalRecords, apiDirectory) {
  // Record classes and record structs both declare IEquatable<T> in the snapshot; a record struct is `readonly struct`.
  const declarationPattern = /^\s*public\s+(sealed\s+)?(readonly\s+)?(class|struct)\s+([A-Za-z][A-Za-z0-9]*)(?:\s*:\s*([^\r\n{]+))?/gm;
  const declarations = [...block.matchAll(declarationPattern)].map((match) => ({ sealed: !!match[1], kind: match[3], name: match[4], bases: match[5] ?? null }));
  const recordNames = new Set(
    declarations
      .filter((declaration) => declaration.bases && new RegExp(`System\\.IEquatable<${escapeRegex(namespace)}\\.${declaration.name}>`).test(declaration.bases))
      .map((declaration) => declaration.name),
  );
  const uids = new Set();
  const typeBody = (name) => new RegExp(`^    public [^\\r\\n]*\\b${escapeRegex(name)}\\b[^\\r\\n]*\\r?\\n    \\{([\\s\\S]*?)^    \\}`, "m").exec(block)?.[1] ?? "";
  for (const declaration of declarations) {
    const { name } = declaration;
    if (!recordNames.has(name)) continue;
    const qualified = `${namespace}.${name}`;
    if (positionalRecords.has(qualified) && !/\bDeconstruct\(/.test(typeBody(name))) {
      const metadata = fs.readFileSync(path.join(apiDirectory, `${qualified}.yml`), "utf8");
      const deconstructs = [...metadata.matchAll(new RegExp(`^- uid: (${escapeRegex(qualified)}\\.Deconstruct\\([^\\r\\n]+\\))\\r?$`, "gm"))];
      assertCondition(deconstructs.length === 1, `Expected one generated Deconstruct member for ${qualified}.`);
      uids.add(deconstructs[0][1]);
    }
    const isStruct = declaration.kind === "struct";
    const structBody = isStruct ? typeBody(name) : "";
    for (const member of ["ToString", `op_Inequality(${qualified},${qualified})`, `op_Equality(${qualified},${qualified})`, "GetHashCode", "Equals(System.Object)", `Equals(${qualified})`]) {
      // A record struct's explicit override (ToString) already appears in the snapshot as an authored member.
      if (isStruct && member === "ToString" && /\boverride string ToString\(/.test(structBody)) continue;
      uids.add(`${qualified}.${member}`);
    }
    if (isStruct) continue;
    if (!declaration.sealed) {
      uids.add(`${qualified}.#ctor(${qualified})`);
      uids.add(`${qualified}.PrintMembers(System.Text.StringBuilder)`);
      uids.add(`${qualified}.EqualityContract`);
    }
    const baseType = declaration.bases ? new RegExp(`^\\s*${escapeRegex(namespace)}\\.([A-Za-z][A-Za-z0-9]*)`).exec(declaration.bases) : null;
    if (baseType && recordNames.has(baseType[1])) {
      uids.add(`${qualified}.Equals(${namespace}.${baseType[1]})`);
      // Sealed derived records still override their base's protected record members.
      uids.add(`${qualified}.PrintMembers(System.Text.StringBuilder)`);
      uids.add(`${qualified}.EqualityContract`);
    }
  }
  return uids;
}

await main(() => {
  const apiDirectory = options["api-directory"];
  const siteApiDirectory = options["site-api-directory"];
  assertCondition(isDirectory(apiDirectory), "Generated API metadata is missing.");
  assertCondition(isFile(options["baseline-path"]), "Managed API baseline is missing.");
  assertCondition(isFile(options["docfx-config-path"]), "DocFX configuration is missing.");
  assertCondition(isDirectory(siteApiDirectory), "Built API pages are missing.");
  assertCondition(isFile(options["search-index-path"]), "Built search index is missing.");

  const baseline = fs.readFileSync(options["baseline-path"], "utf8");
  const baselineLines = lines(baseline);
  // Track namespaces and declaring types so every public type maps to its complete DocFX UID.
  const typeNames = new Set();
  const namespaceNames = [];
  let namespace = null;
  let declaringType = null;
  for (const line of baselineLines) {
    const namespaceMatch = /^namespace (.+)$/.exec(line);
    if (namespaceMatch) {
      namespace = namespaceMatch[1];
      namespaceNames.push(namespace);
      continue;
    }
    const type = TYPE_DECLARATION.exec(line);
    if (!type) continue;
    if (type[1].length <= 4) {
      declaringType = `${namespace}.${type[2]}`;
      typeNames.add(declaringType);
    } else {
      typeNames.add(`${declaringType}.${type[2]}`);
    }
  }
  const sortedTypeNames = [...typeNames].sort();
  // The managed API manifest counts the top-level exported types; nested public types (indented deeper) add to it.
  const manifest = JSON.parse(fs.readFileSync(path.join(repositoryRoot, "contracts/api/managed-rc1/manifest.json"), "utf8"));
  const nestedTypeCount = baselineLines.filter((line) => /^ {5,}public (?:abstract |sealed |static |readonly )*(?:class|enum|struct|interface) /.test(line)).length;
  const expectedTypeCount = Number(manifest.exportedTypes) + nestedTypeCount;
  assertCondition(sortedTypeNames.length === expectedTypeCount, `Expected ${expectedTypeCount} baseline types, found ${sortedTypeNames.length}.`);
  // Generic metadata file names carry arity, for example PrimitiveArray-1.yml.
  const missingTypes = sortedTypeNames.filter((name) => !isFile(path.join(apiDirectory, `${name}.yml`)) && !isFile(path.join(apiDirectory, `${name}-1.yml`)));
  assertCondition(missingTypes.length === 0, `Generated API metadata is missing baseline types: ${missingTypes.join(", ")}`);

  // Positional record declarations generate Deconstruct members absent from the API snapshot.
  const positionalRecords = new Set();
  for (const sourceFile of listFiles(path.join(repositoryRoot, "src/CStructSharp"), (file) => file.endsWith(".cs") && !/[/\\](?:obj|bin)[/\\]/.test(file))) {
    const source = fs.readFileSync(sourceFile, "utf8");
    const sourceNamespace = /^namespace ([A-Za-z0-9_.]+);/m.exec(source)?.[1];
    if (!sourceNamespace) continue;
    for (const declaration of source.matchAll(/\bpublic\s+(?:sealed\s+)?record\s+([A-Za-z0-9_]+)\s*\(/g)) positionalRecords.add(`${sourceNamespace}.${declaration[1]}`);
  }
  const synthesized = new Set();
  for (const block of baseline.split(/(?=^namespace )/m)) {
    const blockNamespace = /^namespace ([^\r\n]+)/.exec(block)?.[1];
    if (blockNamespace) for (const uid of recordSynthesizedUids(block, blockNamespace, positionalRecords, apiDirectory)) synthesized.add(uid);
  }
  const expectedUidCount =
    namespaceNames.length +
    baselineLines.filter((line) => /^\s*(?:public|protected) /.test(line)).length +
    baselineLines.filter((line) => /^\s{8}[A-Za-z][A-Za-z0-9]* = -?\d+,/.test(line)).length +
    // Interface members carry no access modifier (ICustomCodec).
    baselineLines.filter((line) => /^\s{8}(?!public |protected )[A-Za-z][\w?<>., [\]]* [A-Za-z]\w*(?: \{ get; \}|\(.*\);)$/.test(line)).length +
    synthesized.size;

  const uids = [];
  const missingSummaries = [];
  const missingParameters = [];
  const missingTypeParameters = [];
  const missingReturns = [];
  const missingExceptionDescriptions = [];
  const placeholderContent = [];
  let parameterCount = 0;
  let parameterDescriptionCount = 0;
  let typeParameterCount = 0;
  let typeParameterDescriptionCount = 0;
  let returnCount = 0;
  let returnDescriptionCount = 0;
  let exceptionCount = 0;
  const primaryItems = new Map();
  const descriptionOf = (entry) => /^ {6}description:\s*(.*?)\r?$/m.exec(entry)?.[1]?.trim() ?? "";
  for (const file of listFiles(apiDirectory, (candidate) => candidate.endsWith(".yml") && path.basename(candidate) !== "toc.yml" && path.dirname(candidate) === path.resolve(apiDirectory))) {
    let contents = normalizeNewlines(fs.readFileSync(file, "utf8"));
    const referencesIndex = contents.indexOf("references:\n");
    if (referencesIndex >= 0) contents = contents.slice(0, referencesIndex);
    for (const item of contents.split(/(?=^- uid: )/m)) {
      const uidMatch = /^- uid: (.+?)\r?$/m.exec(item);
      if (!uidMatch) continue;
      const uid = uidMatch[1].trim();
      uids.push(uid);
      primaryItems.set(uid, item);
      if (synthesized.has(uid)) continue;
      if (!/^ {2}summary:/m.test(item)) missingSummaries.push(uid);
      else if (/^ {2}summary:\s*(?:TODO|TBD|Gets the value\.?|Sets the value\.?)\s*\r?$/im.test(item)) placeholderContent.push(`${uid} :: summary`);

      const parameters = /^ {4}parameters:\r?\n([\s\S]*?)(?=^ {4}(?:typeParameters:|return:|content\.vb:)|^ {2}[A-Za-z]|(?![\s\S]))/m.exec(item);
      if (parameters) {
        for (const entry of parameters[1].split(/(?=^ {4}- id: )/m)) {
          const id = /^ {4}- id: (.+?)\r?$/m.exec(entry)?.[1]?.trim();
          if (!id) continue;
          parameterCount++;
          const description = descriptionOf(entry);
          if (description) {
            parameterDescriptionCount++;
            if (/^(?:TODO|TBD|The value\.?)$/i.test(description)) placeholderContent.push(`${uid} :: parameter ${id}`);
          } else {
            missingParameters.push(`${uid} :: ${id}`);
          }
        }
      }
      const typeParameters = /^ {4}typeParameters:\r?\n([\s\S]*?)(?=^ {4}(?:parameters:|return:|content\.vb:)|^ {2}[A-Za-z]|(?![\s\S]))/m.exec(item);
      if (typeParameters) {
        for (const entry of typeParameters[1].split(/(?=^ {4}- id: )/m)) {
          const id = /^ {4}- id: (.+?)\r?$/m.exec(entry)?.[1]?.trim();
          if (!id) continue;
          typeParameterCount++;
          if (descriptionOf(entry)) typeParameterDescriptionCount++;
          else missingTypeParameters.push(`${uid} :: ${id}`);
        }
      }
      if (/^ {2}type: Method\r?$/m.test(item) && /^ {4}content: (?!(?:public (?:static |readonly |override |virtual |sealed )*)?void )/m.test(item)) {
        returnCount++;
        const returnBlock = /^ {4}return:\r?\n([\s\S]*?)(?=^ {4}content\.vb:|^ {2}[A-Za-z]|(?![\s\S]))/m.exec(item);
        if (returnBlock && descriptionOf(returnBlock[1])) returnDescriptionCount++;
        else missingReturns.push(uid);
      }
      const exceptions = /^ {2}exceptions:\r?\n([\s\S]*?)(?=^ {2}[A-Za-z]|(?![\s\S]))/m.exec(item);
      if (exceptions) {
        for (const entry of exceptions[1].split(/(?=^ {2}- type: )/m)) {
          const type = /^ {2}- type: (.+?)\r?$/m.exec(entry)?.[1]?.trim();
          if (!type) continue;
          exceptionCount++;
          if (!/^ {4}description:\s*\S/m.test(entry)) missingExceptionDescriptions.push(`${uid} :: ${type}`);
        }
      }
    }
  }

  assertCondition(uids.length === expectedUidCount, `The baseline implies ${expectedUidCount} public UIDs, but DocFX generated ${uids.length}.`);
  assertCondition(new Set(uids).size === uids.length, "Generated API UIDs are not unique.");
  const missingPublicSummaries = missingSummaries.filter((uid) => !namespaceNames.includes(uid));
  assertCondition(missingPublicSummaries.length === 0, `Generated public API items lack summaries:\n${missingPublicSummaries.join("\n")}`);
  assertCondition(missingParameters.length === 0, `Generated API parameters lack descriptions:\n${missingParameters.join("\n")}`);
  assertCondition(missingTypeParameters.length === 0, `Generated API type parameters lack descriptions:\n${missingTypeParameters.join("\n")}`);
  assertCondition(missingReturns.length === 0, `Generated API return values lack descriptions:\n${missingReturns.join("\n")}`);
  assertCondition(missingExceptionDescriptions.length === 0, `Generated API exception references lack descriptions:\n${missingExceptionDescriptions.join("\n")}`);
  assertCondition(placeholderContent.length === 0, `Generated API content contains placeholders or tautologies:\n${placeholderContent.join("\n")}`);

  const docfxConfig = JSON.parse(fs.readFileSync(options["docfx-config-path"], "utf8"));
  assertCondition(docfxConfig.metadata.length === 1, "DocFX must use one explicit library metadata source.");
  assertCondition(docfxConfig.metadata[0].memberLayout === "samePage", "The reviewed overload layout must remain 'samePage'.");

  const testedComplexModels = [
    ["CStructSharp.CStruct", "DecodeHeader"],
    ["CStructSharp.Diagnostics.DebugData", "InspectRanges"],
    ["CStructSharp.Values.EnumValueResult", "PreserveEnum"],
    ["CStructSharp.Values.Pointer", "FollowPointer"],
    ["CStructSharp.ReadOptions", "FollowPointer"],
    ["CStructSharp.Values.UnionValue", "PreserveUnion"],
    ["CStructSharp.WriteOptions", "RoundTrip"],
    ["CStructSharp.UpdateOptions", "PatchField"],
  ];
  for (const [model, scenario] of testedComplexModels) {
    assertCondition(primaryItems.has(model), `Complex public model is absent: ${model}`);
    assertCondition(/^ {2}remarks:\s*\S/m.test(primaryItems.get(model)), `Complex public model lacks contract remarks: ${model}`);
    const pagePath = path.join(siteApiDirectory, `${model}.html`);
    assertCondition(isFile(pagePath), `Complex public model page is absent: ${model}`);
    const page = fs.readFileSync(pagePath, "utf8");
    assertCondition(page.includes(">Remarks<"), `Complex public model page does not render remarks: ${model}`);
    assertCondition(page.toLowerCase().includes("compiled and executed"), `Complex public model page does not identify its executable example: ${model}`);
    assertCondition(page.includes(scenario), `Complex public model page does not render the tested '${scenario}' scenario: ${model}`);
  }

  const apiTocPath = path.join(siteApiDirectory, "toc.html");
  assertCondition(isFile(apiTocPath), "Built API TOC is missing.");
  const apiToc = fs.readFileSync(apiTocPath, "utf8");
  for (const typeName of sortedTypeNames) {
    assertCondition(apiToc.includes(`${typeName}.html`) || apiToc.includes(`${typeName}-1.html`), `Built API TOC does not link the baseline type ${typeName}.`);
  }
  const searchIndex = fs.readFileSync(options["search-index-path"], "utf8");
  for (const evidence of ['"api/CStructSharp.CStruct.html"', "TryReadValue", "CStructReadException", "UnionValue"]) {
    assertCondition(searchIndex.includes(evidence), `Local search index lacks API evidence '${evidence}'.`);
  }

  console.log("API documentation validation passed.");
  console.log(`Baseline types: ${sortedTypeNames.length}`);
  console.log(`Primary UIDs: ${uids.length}`);
  console.log(`Parameters: ${parameterCount}/${parameterDescriptionCount} documented`);
  console.log(`Type parameters: ${typeParameterCount}/${typeParameterDescriptionCount} documented`);
  console.log(`Return values: ${returnCount}/${returnDescriptionCount} documented`);
  console.log(`Documented exceptions: ${exceptionCount}`);
  console.log(`Overload layout: ${docfxConfig.metadata[0].memberLayout}`);
  console.log(`Tested complex-model pages: ${testedComplexModels.length}`);
  console.log(`API TOC baseline links: ${sortedTypeNames.length}`);
});
