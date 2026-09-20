#!/usr/bin/env node
// Writes tests/CStructSharp.Generated.Parity/Layouts.g.cs and layouts.json: one [CStructLayout] class per layout
// definition the repository's fixture sources declare, so the parity project compiles every one of them with the
// generator and its tests can compare the generated code with the runtime. Sources: benchmarks/fixtures/cases/*.json,
// contracts/language/manual-fixtures-v1.json (the valid fixtures), contracts/quality/compiler-fixtures/shapes.json,
// benchmarks/fixtures/conditional-cases.json, and the layouts the docs recipes construct with a literal.
//
// Usage: node tools/quality/generate-parity-layouts.mjs          # rewrite both files
//        node tools/quality/generate-parity-layouts.mjs --check  # fail when the committed files are stale
import { execFileSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const projectDirectory = path.join(root, "tests/CStructSharp.Generated.Parity");
const check = process.argv.includes("--check");

const layouts = [];
const skipped = [];

function pascal(id) {
  return id.split(/[^A-Za-z0-9]+/).filter(Boolean).map((part) => part[0].toUpperCase() + part.slice(1)).join("");
}

function add(source, id, definition, options) {
  const className = pascal(id);
  if (/^[0-9]/.test(className)) return skipped.push({ source, id, reason: "class name would start with a digit" });
  if (options.root && options.root.includes("[")) return skipped.push({ source, id, reason: "the root is a type spelling, not a declaration (CSG004)" });
  if (layouts.some((layout) => layout.source === source && layout.className === className)) return skipped.push({ source, id, reason: "duplicate class name in its source group" });
  layouts.push({ source, id, className, definition, ...options });
}

// benchmarks/fixtures/cases
for (const file of fs.readdirSync(path.join(root, "benchmarks/fixtures/cases")).filter((name) => name.endsWith(".json")).sort()) {
  const fixture = JSON.parse(fs.readFileSync(path.join(root, "benchmarks/fixtures/cases", file), "utf8"));
  if (fixture.root.includes("[")) {
    skipped.push({ source: "Benchmarks", id: fixture.id, reason: "the root is a type spelling, not a declaration (CSG004)" });
    continue;
  }
  // A compile-only fixture may name a root the definition does not declare (CSG004); the plain Parse then uses the first declaration.
  add("Benchmarks", fixture.id, fixture.definition, {
    root: new RegExp(`\\b${fixture.root.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")}\\b`).test(fixture.definition) ? fixture.root : null,
    pointerSize: fixture.options.pointerSize,
    aligned: fixture.options.aligned,
    littleEndian: fixture.options.littleEndian,
  });
}

// contracts/language/manual-fixtures-v1.json
const manual = JSON.parse(fs.readFileSync(path.join(root, "contracts/language/manual-fixtures-v1.json"), "utf8"));
for (const pair of manual.featurePairs) {
  const valid = pair.valid;
  const compilation = valid.compilation ?? {};
  add("Manual", pair.id, valid.definition, {
    root: valid.root,
    pointerSize: valid.pointerSize,
    aligned: valid.aligned,
    littleEndian: valid.littleEndian,
    bitfieldAllocation: compilation.bitfieldAllocation ?? null,
    cLongWidth: compilation.cLongWidth ?? 0,
    defaultEnumStorage: compilation.defaultEnumStorage ?? null,
    bytes: valid.bytes ?? null,
    variables: valid.variables ?? {},
  });
}

// contracts/quality/compiler-fixtures/shapes.json: the portable layout, one class per (shape, packing) that applies
const shapes = JSON.parse(fs.readFileSync(path.join(root, "contracts/quality/compiler-fixtures/shapes.json"), "utf8"));
for (const shape of shapes.shapes) {
  for (const packing of ["sysv", "msvc"]) {
    if (!shape.portable[packing]) continue;
    add("Shapes", `${shape.id}-${packing}`, shape.portable.layout, {
      root: "s",
      pointerSize: 8,
      aligned: shape.portable.aligned !== false,
      littleEndian: true,
      bitfieldPacking: packing === "msvc" ? "Msvc" : "SysV",
      values: shape.portable.values ?? null,
    });
  }
}

// benchmarks/fixtures/conditional-cases.json
const conditional = JSON.parse(fs.readFileSync(path.join(root, "benchmarks/fixtures/conditional-cases.json"), "utf8"));
for (const item of conditional) {
  add("Conditional", item.name, item.definition, { root: "root", pointerSize: 4, aligned: false, littleEndian: true, size: item.size, fill: item.fill });
}

// docs/examples/recipes/*.cs: `new CStruct("literal" | identifier, named options)` where the identifier is a string
// constant of the same file (a regular or raw literal); other forms are listed as skipped. The recipes are exported
// (git-ignored) files, so they are regenerated first: a fresh checkout has only the recipe index, and the layouts
// would silently be missing.
execFileSync(process.execPath, [path.join(root, "tools/documentation/export-documentation-examples.mjs")], { stdio: "ignore" });
const recipeDirectory = path.join(root, "docs/examples/recipes");
for (const file of fs.readdirSync(recipeDirectory).filter((name) => name.endsWith(".cs")).sort()) {
  const text = fs.readFileSync(path.join(recipeDirectory, file), "utf8");
  const id = path.basename(file, ".cs");
  const constructions = [...text.matchAll(/new CStruct\(\s*("""[\s\S]*?"""|"(?:[^"\\]|\\.)*"|[A-Za-z_][A-Za-z0-9_]*)\s*([^;]*?)\)/g)];
  let index = 0;
  for (const construction of constructions) {
    let literal = construction[1];
    if (!literal.startsWith('"')) {
      const declaration = new RegExp(`string\\s+${literal}\\s*=\\s*("""[\\s\\S]*?"""|"(?:[^"\\\\]|\\\\.)*")\\s*;`).exec(text);
      if (!declaration) {
        skipped.push({ source: "Recipes", id, reason: `${literal} is not a string constant of the file` });
        continue;
      }
      literal = declaration[1];
    }
    const definition = decodeLiteral(literal);
    if (definition === null) {
      skipped.push({ source: "Recipes", id, reason: "the literal could not be decoded" });
      continue;
    }
    const named = construction[2];
    const option = (name, fallback) => {
      const match = new RegExp(`${name}:\\s*([A-Za-z0-9]+)`).exec(named);
      return match ? match[1] : fallback;
    };
    if (/\bcompilationOptions:|\bcodecs:/.test(named)) {
      skipped.push({ source: "Recipes", id, reason: "compilation options or codecs are constructed in code" });
      continue;
    }
    add("Recipes", index === 0 ? id : `${id}-${index + 1}`, definition, {
      root: null,
      pointerSize: Number(option("pointerSize", "8")),
      aligned: option("aligned", "false") === "true",
      littleEndian: option("isLittleEndian", "true") !== "false",
    });
    index++;
  }
}

function decodeLiteral(literal) {
  if (literal.startsWith('"""')) {
    const body = literal.slice(3, -3);
    const lines = body.split("\n");
    if (lines.length < 2) return null;
    const closing = lines[lines.length - 1];
    const indent = closing.length - closing.trimStart().length;
    return lines.slice(1, -1).map((line) => line.slice(Math.min(indent, line.length - line.trimStart().length))).join("\n").replace(/\r/g, "");
  }
  try {
    return JSON.parse(literal.replace(/\\'/g, "'"));
  } catch {
    return null;
  }
}

function csharpLiteral(text) {
  return '"' + text.replace(/\\/g, "\\\\").replace(/"/g, '\\"').replace(/\r/g, "\\r").replace(/\n/g, "\\n").replace(/\t/g, "\\t") + '"';
}

const lines = [
  "// <auto-generated/>",
  "// Written by tools/quality/generate-parity-layouts.mjs from the repository's fixture sources. Do not edit; rerun the tool.",
  "#pragma warning disable",
  "",
  "using CStructSharp;",
  "",
];
for (const source of ["Benchmarks", "Manual", "Shapes", "Conditional", "Recipes"]) {
  lines.push(`namespace CStructSharp.Generated.Parity.Layouts.${source}`, "{");
  for (const layout of layouts.filter((item) => item.source === source)) {
    const attributeArguments = [];
    if (layout.root) attributeArguments.push(`Root = ${csharpLiteral(layout.root)}`);
    attributeArguments.push(`PointerSize = ${layout.pointerSize}`, `Aligned = ${layout.aligned ? "true" : "false"}`, `LittleEndian = ${layout.littleEndian ? "true" : "false"}`);
    if (layout.bitfieldPacking) attributeArguments.push(`BitfieldPacking = global::CStructSharp.BitfieldPacking.${layout.bitfieldPacking}`);
    if (layout.bitfieldAllocation) attributeArguments.push(`BitfieldAllocation = global::CStructSharp.BitfieldAllocation.${layout.bitfieldAllocation}`);
    if (layout.cLongWidth) attributeArguments.push(`CLongWidth = ${layout.cLongWidth}`);
    if (layout.defaultEnumStorage) attributeArguments.push(`DefaultEnumStorage = ${csharpLiteral(layout.defaultEnumStorage)}`);
    lines.push(`    /// <summary>${source}: <c>${layout.id}</c>.</summary>`);
    lines.push(`    [CStructLayout(${csharpLiteral(layout.definition)}, ${attributeArguments.join(", ")})]`);
    lines.push(`    public static partial class ${layout.className} { }`, "");
  }
  if (lines[lines.length - 1] === "") lines.pop();
  lines.push("}", "");
}

const generated = lines.join("\n");
const index = JSON.stringify({ layouts, skipped }, null, 2) + "\n";
const sourcePath = path.join(projectDirectory, "Layouts.g.cs");
const indexPath = path.join(projectDirectory, "layouts.json");
if (check) {
  const same = fs.existsSync(sourcePath) && fs.readFileSync(sourcePath, "utf8") === generated && fs.existsSync(indexPath) && fs.readFileSync(indexPath, "utf8") === index;
  if (!same) {
    console.error("tests/CStructSharp.Generated.Parity/Layouts.g.cs or layouts.json is stale; run node tools/quality/generate-parity-layouts.mjs.");
    process.exit(1);
  }
  console.log(`Parity layouts are current (${layouts.length} layouts, ${skipped.length} skipped).`);
} else {
  fs.mkdirSync(projectDirectory, { recursive: true });
  fs.writeFileSync(sourcePath, generated);
  fs.writeFileSync(indexPath, index);
  console.log(`Wrote ${layouts.length} parity layouts (${skipped.length} skipped) to ${path.relative(root, sourcePath)} and layouts.json.`);
}
