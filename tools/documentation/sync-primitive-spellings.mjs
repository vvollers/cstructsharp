// Keeps every view of the primitive alias table in step with src/CStructSharp.Core/Codecs/PrimitiveSpellings.cs:
//   - contracts/language/portable-v1.json      -> aliasSpellings
//   - contracts/quality/feature-operation-matrix.json -> primitiveSpellings.aliases
//   - docs/language/primitive-types.md         -> the generated table between the sync markers
// Usage: node tools/documentation/sync-primitive-spellings.mjs [--check]
// With --check the script exits 1 instead of writing when any view is stale.
import { readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { parseArguments, repositoryRoot as root } from "../lib/tooling.mjs";

const { check } = parseArguments(process.argv.slice(2), { check: "flag" }, { defaults: { check: false } });

/**
 * Returns the initializer of one dictionary field in PrimitiveSpellings.cs: the text from the field name to the
 * closing brace of its collection initializer.
 * @param {string} source The C# source.
 * @param {string} field The field name, such as `LongFamilyIsUnsigned`.
 * @returns {string} The initializer text.
 * @throws {Error} When the field or the end of its initializer is missing, so a renamed field fails loudly instead
 *   of reading a neighbouring table.
 */
function fieldInitializer(source, field) {
  const start = source.indexOf(`${field} =`);
  const end = start < 0 ? -1 : source.indexOf("\n    }", start);
  if (start < 0 || end < 0) throw new Error(`PrimitiveSpellings.cs has no initializer for ${field}`);
  return source.slice(start, end);
}

/**
 * Reads the primitive alias tables from PrimitiveSpellings.cs: the fixed aliases, the `long` family that depends on
 * `CLongWidth`, the pointer-sized integers, and the pointer spellings.
 * @returns {{aliases: object[], longFamily: object[], pointerSized: object[], pointerSpellings: object[]}}
 *   Spelling-to-canonical records for each table.
 */
function readAliasTable() {
  const source = readFileSync(join(root, "src/CStructSharp.Core/Codecs/PrimitiveSpellings.cs"), "utf8");
  const aliases = [];
  for (const match of source.matchAll(/Add\("([^"]+)"((?:,\s*"[^"]+")+)\);/g)) {
    const canonical = match[1];
    for (const spelling of match[2].matchAll(/"([^"]+)"/g)) {
      aliases.push({ spelling: spelling[1], canonical });
    }
  }
  const longFamily = [];
  for (const match of fieldInitializer(source, "LongFamilyIsUnsigned").matchAll(/\["([^"]+)"\]\s*=\s*(true|false)/g)) {
    const unsigned = match[2] === "true";
    longFamily.push({
      spelling: match[1],
      canonical: unsigned ? "uint64" : "int64",
      cLongWidth32: unsigned ? "uint32" : "int32",
    });
  }
  const pointerSized = [];
  for (const match of fieldInitializer(source, "PointerSizedIsUnsigned").matchAll(/\["([^"]+)"\]\s*=\s*(true|false)/g)) {
    pointerSized.push({ spelling: match[1], canonical: (match[2] === "true" ? "uint" : "int") + "{pointer bits}" });
  }
  const pointerSpellings = [];
  for (const match of fieldInitializer(source, "PointerSpellings").matchAll(/\["([^"]+)"\]\s*=\s*"([^"]+)"/g)) {
    pointerSpellings.push({ spelling: match[1], canonical: match[2] + "*" });
  }
  return { aliases, longFamily, pointerSized, pointerSpellings };
}

/** Serializes a value as two-space-indented JSON with a trailing newline. */
function stableJson(value) {
  return JSON.stringify(value, null, 2) + "\n";
}

/**
 * Applies a change to a JSON file and writes it back (or reports it as stale with `--check`).
 * @param {string} relativePath Repository-relative path of the JSON file.
 * @param {(document: object) => void} mutate Updates the parsed document in place.
 * @returns {boolean} True when the file differs from the synchronized content.
 */
function syncJson(relativePath, mutate) {
  const path = join(root, relativePath);
  const before = readFileSync(path, "utf8");
  const document = JSON.parse(before);
  mutate(document);
  const after = stableJson(document);
  return apply(path, before, after);
}

/**
 * Writes the synchronized content when it differs; with `--check` it reports the file as stale and sets exit code 1
 * instead.
 * @param {string} path File to update.
 * @param {string} before Current content.
 * @param {string} after Synchronized content.
 * @returns {boolean} True when the content differs.
 */
function apply(path, before, after) {
  if (before === after) {
    return false;
  }
  if (check) {
    console.error(`stale: ${path}`);
    process.exitCode = 1;
    return true;
  }
  writeFileSync(path, after);
  console.log(`updated: ${path}`);
  return true;
}

const { aliases, longFamily, pointerSized, pointerSpellings } = readAliasTable();
const sortedAliases = [...aliases].sort((a, b) => (a.canonical === b.canonical ? a.spelling.localeCompare(b.spelling) : a.canonical.localeCompare(b.canonical)));

syncJson("contracts/language/portable-v1.json", (contract) => {
  contract.aliasSpellings = [...sortedAliases, ...longFamily, ...pointerSized, ...pointerSpellings];
});

/**
 * Replaces one string array inside the matrix's `primitiveSpellings` object, leaving the rest of the hand-formatted
 * file byte-for-byte unchanged.
 * @param {string} text The matrix file content.
 * @param {string} key The array's key, such as `aliases`.
 * @param {string[]} values The new array items.
 * @returns {string} The file content with that array replaced.
 */
function replaceSpellingArray(text, key, values) {
  const section = text.indexOf('"primitiveSpellings": {');
  const start = text.indexOf(`\n    "${key}": [`, section);
  const end = text.indexOf("\n    ]", start);
  if (section < 0 || start < 0 || end < 0) throw new Error(`feature-operation-matrix.json has no primitiveSpellings.${key} array`);
  const items = values.map((value) => `      ${JSON.stringify(value)}`).join(",\n");
  return `${text.slice(0, start)}\n    "${key}": [\n${items}${text.slice(end)}`;
}

{
  const path = join(root, "contracts/quality/feature-operation-matrix.json");
  const before = readFileSync(path, "utf8");
  const spellings = JSON.parse(before).primitiveSpellings;
  // The terminated list already names `string`/`cstring`; a spelling is catalogued once.
  const catalogued = new Set([...spellings.dynamicNumeric, ...spellings.fixed, ...spellings.terminated]);
  const aliasNames = [...sortedAliases.map((item) => item.spelling), ...longFamily.map((item) => item.spelling)]
    .filter((spelling) => !catalogued.has(spelling))
    .sort();
  const pointerNames = [...pointerSized.map((item) => item.spelling), ...pointerSpellings.map((item) => item.spelling)].sort();
  apply(path, before, replaceSpellingArray(replaceSpellingArray(before, "aliases", aliasNames), "pointerSized", pointerNames));
}

// Documentation table.
const docPath = join(root, "docs/language/primitive-types.md");
const doc = readFileSync(docPath, "utf8");
const start = "<!-- sync-primitive-spellings:start -->";
const end = "<!-- sync-primitive-spellings:end -->";
const startIndex = doc.indexOf(start);
const endIndex = doc.indexOf(end);
if (startIndex < 0 || endIndex < 0) {
  throw new Error("primitive-types.md is missing the sync markers");
}
const byCanonical = new Map();
for (const item of sortedAliases) {
  if (!byCanonical.has(item.canonical)) {
    byCanonical.set(item.canonical, []);
  }
  byCanonical.get(item.canonical).push(item.spelling);
}
const lines = ["", "| Canonical codec | Accepted alias spellings |", "| --- | --- |"];
for (const [canonical, spellings] of byCanonical) {
  lines.push(`| \`${canonical}\` | ${spellings.map((s) => `\`${s}\``).join(", ")} |`);
}
lines.push(
  `| \`int64\` / \`uint64\` (\`CLongWidth\` 64, the default) or \`int32\` / \`uint32\` (\`CLongWidth\` 32) | ${longFamily
    .map((item) => `\`${item.spelling}\``)
    .join(", ")} |`,
);
lines.push(
  `| \`uintN\` / \`intN\` where N is the layout's pointer width in bits | ${pointerSized.map((item) => `\`${item.spelling}\``).join(", ")} |`,
);
const pointerGroups = new Map();
for (const item of pointerSpellings) {
  if (!pointerGroups.has(item.canonical)) pointerGroups.set(item.canonical, []);
  pointerGroups.get(item.canonical).push(item.spelling);
}
const pointerNotes = { "void*": "an opaque address of pointer width", "char*": "a pointer to a byte string", "wchar*": "a pointer to a UTF-16 string" };
for (const [canonical, spellings] of pointerGroups) {
  lines.push(`| \`${canonical}\` (${pointerNotes[canonical] ?? "a pointer of pointer width"}) | ${spellings.map((item) => `\`${item}\``).join(", ")} |`);
}
lines.push("");
const generated = `${start}\n${lines.join("\n")}\n${end}`;
apply(docPath, doc, doc.slice(0, startIndex) + generated + doc.slice(endIndex + end.length));
if (!process.exitCode) {
  console.log(`${aliases.length + longFamily.length} alias spellings in sync`);
}
