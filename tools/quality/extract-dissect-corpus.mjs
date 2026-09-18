#!/usr/bin/env node
/**
 * Extracts every cstruct definition string from the dissect ecosystem into one JSON corpus file.
 *
 *   node tools/quality/extract-dissect-corpus.mjs <ecosystem-dir> <output.json>
 *
 * The corpus is consumed by tests/CStructSharpTests/Dissect/DissectCorpusSweepTests.cs (opt-in through
 * CSTRUCTSHARP_DISSECT_CORPUS). Entries are {id, repository, path, line, source}. Definitions are recognized the same
 * way the parity inventory recognizes them: a Python string literal that starts a line with
 * typedef/struct/union/enum/flag/#define. Docstrings that merely mention a struct and fragments assembled from
 * several literals (unbalanced braces, or text before the first declaration) are not definitions and are skipped.
 * Adjacent literals are concatenated the way Python's parser does, so a definition split across lines counts once.
 */
import fs from "node:fs";
import path from "node:path";

const [ecosystemDirectory, outputPath] = process.argv.slice(2);
if (!ecosystemDirectory || !outputPath) {
  console.error("Usage: extract-dissect-corpus.mjs <ecosystem-dir> <output.json>");
  process.exit(1);
}
const root = path.resolve(ecosystemDirectory);
const skip = ["dissect.cstruct/", "dissect.cstruct_legacy", "dissect_legacy", "/tests/", "/build/", "dissect-docs", "-templates", "splunk"];

function isDefinition(text) {
  if ((text.match(/\{/g) ?? []).length !== (text.match(/\}/g) ?? []).length) return false;
  const body = text.replace(/\/\*[\s\S]*?\*\//g, "").replace(/\/\/[^\n]*/g, "");
  const first = body.split("\n").map((line) => line.trim()).find((line) => line.length > 0) ?? "";
  return /^(typedef|struct|union|enum|flag|#)\b/.test(first);
}

/** Decodes the escapes a non-raw Python literal can carry (the subset that appears in definitions). */
function decodeEscapes(text) {
  return text.replace(/\\(x[0-9A-Fa-f]{2}|u[0-9A-Fa-f]{4}|U[0-9A-Fa-f]{8}|[0-7]{1,3}|\r\n|\n|.)/g, (match, code) => {
    switch (code[0]) {
      case "n": return "\n";
      case "t": return "\t";
      case "r": return "\r";
      case "\\": return "\\";
      case "'": return "'";
      case '"': return '"';
      case "\n": case "\r": return "";
      case "x": return String.fromCharCode(parseInt(code.slice(1), 16));
      case "u": case "U": return String.fromCodePoint(parseInt(code.slice(1), 16));
      default: return /^[0-7]/.test(code) ? String.fromCharCode(parseInt(code, 8)) : match;
    }
  });
}

/** Every string literal in a Python source with its line number, adjacent literals concatenated (implicit joining). */
function pythonStringLiterals(source) {
  const literals = [];
  let index = 0;
  let line = 1;
  let pending = null; // an implicit-concatenation group: { line, value }
  const flush = () => {
    if (pending) literals.push(pending);
    pending = null;
  };
  while (index < source.length) {
    const character = source[index];
    if (character === "\n") {
      line++;
      index++;
      continue;
    }
    if (character === "#") {
      const end = source.indexOf("\n", index);
      index = end < 0 ? source.length : end;
      continue;
    }
    const prefix = /^([rRbBuUfF]{0,2})("""|'''|"|')/.exec(source.slice(index, index + 5));
    const identifierBefore = index > 0 && /[A-Za-z0-9_]/.test(source[index - 1]);
    if (prefix && !identifierBefore) {
      const flags = prefix[1].toLowerCase();
      const quote = prefix[2];
      const start = index + prefix[0].length;
      const startLine = line;
      let end = start;
      while (end < source.length) {
        if (source[end] === "\\") {
          end += 2;
          continue;
        }
        if (source.startsWith(quote, end)) break;
        if (quote.length === 1 && source[end] === "\n") break;
        end++;
      }
      const raw = source.slice(start, end);
      line += (raw.match(/\n/g) ?? []).length;
      index = Math.min(source.length, end + quote.length);
      if (flags.includes("b") || flags.includes("f")) {
        flush();
        continue;
      }
      const value = flags.includes("r") ? raw : decodeEscapes(raw);
      if (pending) pending.value += value;
      else pending = { line: startLine, value };
      continue;
    }
    if (/\s/.test(character) || character === "(" || character === "\\") {
      index++;
      continue;
    }
    flush();
    index++;
  }
  flush();
  return literals;
}

function listPythonFiles(directory) {
  const files = [];
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    const full = path.join(directory, entry.name);
    if (entry.isDirectory()) files.push(...listPythonFiles(full));
    else if (entry.isFile() && entry.name.endsWith(".py")) files.push(full);
  }
  return files;
}

const entries = [];
for (const file of listPythonFiles(root).sort()) {
  if (skip.some((fragment) => file.includes(fragment))) continue;
  const relative = file.slice(root.length + 1);
  const repository = relative.split("/")[0];
  const source = fs.readFileSync(file, "utf8");
  for (const literal of pythonStringLiterals(source)) {
    const text = literal.value;
    if (text.length > 40 && /^\s*(typedef|struct|union|enum|flag|#define)\b/m.test(text) && isDefinition(text)) {
      entries.push({ id: `${relative}:${literal.line}`, repository, path: relative, line: literal.line, source: text });
    }
  }
}
fs.writeFileSync(outputPath, JSON.stringify(entries, null, 1));
console.log(`${entries.length} definitions -> ${outputPath}`);
