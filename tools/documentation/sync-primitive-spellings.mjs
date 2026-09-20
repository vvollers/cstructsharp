// Keeps every view of the primitive alias table in step with src/CStructSharp.Core/Codecs/PrimitiveSpellings.cs:
//   - contracts/language/portable-v1.json      -> aliasSpellings
//   - contracts/quality/feature-operation-matrix.json -> primitiveSpellings.aliases
//   - docs/language/primitive-types.md         -> the generated table between the sync markers
// Usage: node tools/documentation/sync-primitive-spellings.mjs [--check]
// With --check the script exits 1 instead of writing when any view is stale.
import { readFileSync, writeFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const root = join(dirname(fileURLToPath(import.meta.url)), "..", "..");
const check = process.argv.includes("--check");

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
  const block = source.slice(source.indexOf("LongFamilyIsUnsigned ="), source.indexOf("}.ToFrozenDictionary"));
  for (const match of block.matchAll(/\["([^"]+)"\]\s*=\s*(true|false)/g)) {
    const unsigned = match[2] === "true";
    longFamily.push({
      spelling: match[1],
      canonical: unsigned ? "uint64" : "int64",
      cLongWidth32: unsigned ? "uint32" : "int32",
    });
  }
  const pointerSized = [];
  const pointerBlock = source.slice(source.indexOf("PointerSizedIsUnsigned ="), source.indexOf("}.ToFrozenDictionary", source.indexOf("PointerSizedIsUnsigned =")));
  for (const match of pointerBlock.matchAll(/\["([^"]+)"\]\s*=\s*(true|false)/g)) {
    pointerSized.push({ spelling: match[1], canonical: (match[2] === "true" ? "uint" : "int") + "{pointer bits}" });
  }
  const pointerSpellings = [];
  const pointerSpellingBlock = source.slice(source.indexOf("PointerSpellings ="), source.indexOf("}.ToFrozenDictionary", source.indexOf("PointerSpellings =")));
  for (const match of pointerSpellingBlock.matchAll(/\["([^"]+)"\]\s*=\s*"([^"]+)"/g)) {
    pointerSpellings.push({ spelling: match[1], canonical: match[2] + "*" });
  }
  return { aliases, longFamily, pointerSized, pointerSpellings };
}

function stableJson(value) {
  return JSON.stringify(value, null, 2) + "\n";
}

function syncJson(relativePath, mutate) {
  const path = join(root, relativePath);
  const before = readFileSync(path, "utf8");
  const document = JSON.parse(before);
  mutate(document);
  const after = stableJson(document);
  return apply(path, before, after);
}

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

syncJson("contracts/quality/feature-operation-matrix.json", (matrix) => {
  // The pre-parity terminated list already names `string`/`cstring`; a spelling is catalogued once.
  const catalogued = new Set([...matrix.primitiveSpellings.dynamicNumeric, ...matrix.primitiveSpellings.fixed, ...matrix.primitiveSpellings.terminated]);
  matrix.primitiveSpellings.aliases = [...sortedAliases.map((item) => item.spelling), ...longFamily.map((item) => item.spelling)]
    .filter((spelling) => !catalogued.has(spelling))
    .sort();
  matrix.primitiveSpellings.pointerSized = [...pointerSized.map((item) => item.spelling), ...pointerSpellings.map((item) => item.spelling)].sort();
});

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
