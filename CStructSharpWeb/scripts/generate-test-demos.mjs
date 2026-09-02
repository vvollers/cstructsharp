import fs from "node:fs";
import path from "node:path";

const webRoot = process.cwd();
const repoRoot = path.resolve(webRoot, "..");
const testsRoot = path.resolve(repoRoot, "CStructSharpTests");
const outPath = path.resolve(webRoot, "src/generated/test-demos.json");

function walkCsFiles(dir) {
  const results = [];
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    if (entry.name.startsWith(".")) continue;
    const fullPath = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      results.push(...walkCsFiles(fullPath));
      continue;
    }
    if (entry.isFile() && entry.name.endsWith(".cs")) {
      results.push(fullPath);
    }
  }
  return results;
}

function decodeEscapedString(content) {
  let out = "";
  for (let i = 0; i < content.length; i++) {
    const ch = content[i];
    if (ch !== "\\") {
      out += ch;
      continue;
    }

    i++;
    if (i >= content.length) break;
    const e = content[i];
    switch (e) {
      case "\\":
        out += "\\";
        break;
      case '"':
        out += '"';
        break;
      case "'":
        out += "'";
        break;
      case "0":
        out += "\0";
        break;
      case "a":
        out += "\x07";
        break;
      case "b":
        out += "\b";
        break;
      case "f":
        out += "\f";
        break;
      case "n":
        out += "\n";
        break;
      case "r":
        out += "\r";
        break;
      case "t":
        out += "\t";
        break;
      case "v":
        out += "\v";
        break;
      case "u": {
        const hex = content.slice(i + 1, i + 5);
        if (/^[0-9a-fA-F]{4}$/.test(hex)) {
          out += String.fromCharCode(parseInt(hex, 16));
          i += 4;
        }
        break;
      }
      case "x": {
        let hex = "";
        let j = i + 1;
        while (j < content.length && hex.length < 4 && /[0-9a-fA-F]/.test(content[j])) {
          hex += content[j];
          j++;
        }
        if (hex.length > 0) {
          out += String.fromCharCode(parseInt(hex, 16));
          i = j - 1;
        }
        break;
      }
      default:
        out += e;
        break;
    }
  }
  return out;
}

function normalizeRawString(value) {
  const lines = value.split(/\r?\n/);
  while (lines.length > 0 && lines[0].trim() === "") lines.shift();
  while (lines.length > 0 && lines[lines.length - 1].trim() === "") lines.pop();

  let minIndent = Number.MAX_SAFE_INTEGER;
  for (const line of lines) {
    if (line.trim() === "") continue;
    const indent = line.match(/^\s*/)?.[0].length ?? 0;
    minIndent = Math.min(minIndent, indent);
  }

  if (Number.isFinite(minIndent) && minIndent > 0 && minIndent < Number.MAX_SAFE_INTEGER) {
    return lines.map((line) => line.slice(minIndent)).join("\n");
  }

  return lines.join("\n");
}

function parseStringLiteralFromIndex(text, start) {
  if (start >= text.length) return null;

  if (text[start] === "@" && text[start + 1] === '"') {
    let i = start + 2;
    let content = "";
    while (i < text.length) {
      if (text[i] === '"' && text[i + 1] === '"') {
        content += '"';
        i += 2;
        continue;
      }
      if (text[i] === '"') {
        return { value: content, end: i + 1 };
      }
      content += text[i];
      i++;
    }
    return null;
  }

  if (text[start] === '"') {
    let quoteCount = 1;
    while (text[start + quoteCount] === '"') quoteCount++;

    if (quoteCount >= 3) {
      const delim = '"'.repeat(quoteCount);
      const from = start + quoteCount;
      const to = text.indexOf(delim, from);
      if (to === -1) return null;
      const raw = text.slice(from, to);
      return { value: normalizeRawString(raw), end: to + quoteCount };
    }

    let i = start + 1;
    let content = "";
    while (i < text.length) {
      if (text[i] === "\\") {
        content += text[i];
        if (i + 1 < text.length) {
          content += text[i + 1];
          i += 2;
          continue;
        }
      }
      if (text[i] === '"') {
        return { value: decodeEscapedString(content), end: i + 1 };
      }
      content += text[i];
      i++;
    }
    return null;
  }

  return null;
}

function parseConcatenatedStringLiterals(text, start) {
  let literal = parseStringLiteralFromIndex(text, start);
  if (!literal) return null;

  let value = literal.value;
  let end = literal.end;
  while (end < text.length) {
    while (end < text.length && /\s/.test(text[end])) end++;
    if (text[end] !== "+") break;

    end++;
    while (end < text.length && /\s/.test(text[end])) end++;
    literal = parseStringLiteralFromIndex(text, end);
    if (!literal) return null;
    value += literal.value;
    end = literal.end;
  }

  return { value, end };
}

function findMatchingBrace(text, openIndex) {
  let i = openIndex;
  let depth = 0;
  let mode = "normal";
  let rawQuoteCount = 0;

  while (i < text.length) {
    const ch = text[i];
    const next = text[i + 1];

    if (mode === "normal") {
      if (ch === "/" && next === "/") {
        mode = "lineComment";
        i += 2;
        continue;
      }
      if (ch === "/" && next === "*") {
        mode = "blockComment";
        i += 2;
        continue;
      }
      if (ch === "@" && next === '"') {
        mode = "verbatimString";
        i += 2;
        continue;
      }
      if (ch === '"') {
        let quoteCount = 1;
        while (text[i + quoteCount] === '"') quoteCount++;
        if (quoteCount >= 3) {
          mode = "rawString";
          rawQuoteCount = quoteCount;
          i += quoteCount;
          continue;
        }
        mode = "string";
        i += 1;
        continue;
      }
      if (ch === "'") {
        mode = "char";
        i += 1;
        continue;
      }
      if (ch === "{") {
        depth++;
      } else if (ch === "}") {
        depth--;
        if (depth === 0) return i;
      }
      i++;
      continue;
    }

    if (mode === "lineComment") {
      if (ch === "\n") mode = "normal";
      i++;
      continue;
    }

    if (mode === "blockComment") {
      if (ch === "*" && next === "/") {
        mode = "normal";
        i += 2;
        continue;
      }
      i++;
      continue;
    }

    if (mode === "string") {
      if (ch === "\\") {
        i += 2;
        continue;
      }
      if (ch === '"') {
        mode = "normal";
      }
      i++;
      continue;
    }

    if (mode === "verbatimString") {
      if (ch === '"' && next === '"') {
        i += 2;
        continue;
      }
      if (ch === '"') {
        mode = "normal";
      }
      i++;
      continue;
    }

    if (mode === "rawString") {
      const delim = '"'.repeat(rawQuoteCount);
      if (text.startsWith(delim, i)) {
        mode = "normal";
        i += rawQuoteCount;
        continue;
      }
      i++;
      continue;
    }

    if (mode === "char") {
      if (ch === "\\") {
        i += 2;
        continue;
      }
      if (ch === "'") {
        mode = "normal";
      }
      i++;
    }
  }

  return -1;
}

function countLines(text, upToIndex) {
  let lines = 1;
  for (let i = 0; i < upToIndex; i++) {
    if (text[i] === "\n") lines++;
  }
  return lines;
}

function splitArgs(argText) {
  const args = [];
  let current = "";
  let depthParen = 0;
  let depthBracket = 0;
  let depthBrace = 0;
  let mode = "normal";

  for (let i = 0; i < argText.length; i++) {
    const ch = argText[i];
    const next = argText[i + 1];

    if (mode === "normal") {
      if (ch === '"' || (ch === "@" && next === '"')) {
        current += ch;
        if (ch === "@") {
          current += next;
          i += 1;
          mode = "verbatimString";
        } else {
          mode = "string";
        }
        continue;
      }
      if (ch === "'") {
        current += ch;
        mode = "char";
        continue;
      }
      if (ch === "(") depthParen++;
      if (ch === ")") depthParen--;
      if (ch === "[") depthBracket++;
      if (ch === "]") depthBracket--;
      if (ch === "{") depthBrace++;
      if (ch === "}") depthBrace--;
      if (ch === "," && depthParen === 0 && depthBracket === 0 && depthBrace === 0) {
        args.push(current.trim());
        current = "";
        continue;
      }
      current += ch;
      continue;
    }

    current += ch;
    if (mode === "string") {
      if (ch === "\\") {
        if (i + 1 < argText.length) {
          current += argText[i + 1];
          i += 1;
        }
        continue;
      }
      if (ch === '"') mode = "normal";
      continue;
    }

    if (mode === "verbatimString") {
      if (ch === '"' && next === '"') {
        current += next;
        i += 1;
        continue;
      }
      if (ch === '"') mode = "normal";
      continue;
    }

    if (mode === "char") {
      if (ch === "\\") {
        if (i + 1 < argText.length) {
          current += argText[i + 1];
          i += 1;
        }
        continue;
      }
      if (ch === "'") mode = "normal";
    }
  }

  if (current.trim()) args.push(current.trim());
  return args;
}

function parseNumericLiteral(text) {
  const token = text.trim().replace(/_/g, "");
  if (!token) return null;

  const byteCharacter = token.match(/^\(byte\)'(?<value>(?:\\.|[^'\\]))'$/);
  if (byteCharacter?.groups?.value) {
    const decoded = decodeEscapedString(byteCharacter.groups.value);
    return decoded.length === 1 ? decoded.charCodeAt(0) & 0xff : null;
  }

  const negative = token.startsWith("-");
  const core = negative ? token.slice(1) : token;

  let value = null;
  if (/^0x[0-9a-fA-F]+$/.test(core)) {
    value = Number.parseInt(core.slice(2), 16);
  } else if (/^0b[01]+$/.test(core)) {
    value = Number.parseInt(core.slice(2), 2);
  } else if (/^0o[0-7]+$/.test(core)) {
    value = Number.parseInt(core.slice(2), 8);
  } else if (/^\d+$/.test(core)) {
    value = Number.parseInt(core, 10);
  }

  if (value === null || !Number.isFinite(value)) return null;
  return negative ? -value : value;
}

function parseByteList(text) {
  const values = text
    .split(",")
    .map((value) => value.trim())
    .filter(Boolean)
    .map((value) => parseNumericLiteral(value));
  if (
    !values.length ||
    values.some((value) => !Number.isFinite(value) || value < 0 || value > 255)
  ) {
    return null;
  }
  return Uint8Array.from(values);
}

function parseIntArrayLiteral(body) {
  const m = body.match(/byte\[\]\s+\w+\s*=\s*\[(?<vals>[\s\S]*?)\];/m);
  if (!m?.groups?.vals) return null;
  return parseByteList(m.groups.vals);
}

function parseInlineMemoryStream(body) {
  const m = body.match(/new\s+MemoryStream\s*\(\s*\[(?<vals>[\s\S]*?)\]\s*\)/m);
  return m?.groups?.vals ? parseByteList(m.groups.vals) : null;
}

function parseLongArrayBlockCopy(body) {
  const m = body.match(/long\[\]\s+\w+\s*=\s*\[(?<vals>[\s\S]*?)\];/m);
  if (!m?.groups?.vals) return null;
  if (!/Buffer\.BlockCopy\s*\(/.test(body)) return null;

  const values = m.groups.vals
    .split(",")
    .map((v) => parseNumericLiteral(v))
    .filter((v) => v !== null);

  if (!values.length) return null;

  const bytes = [];
  for (const n of values) {
    const buffer = Buffer.alloc(8);
    buffer.writeBigInt64LE(BigInt(n), 0);
    bytes.push(...buffer);
  }

  return Uint8Array.from(bytes);
}

function parseHexBuf(body, stringMap) {
  const m = body.match(/byte\[\]\?\s+\w+\s*=\s*(?<name>\w+)\.ParseHexDataContent\(\);/);
  if (!m?.groups?.name) return null;
  const raw = stringMap[m.groups.name];
  if (!raw) return null;
  const hexPairs = raw.match(/[0-9A-Fa-f]{2}/g);
  if (!hexPairs || hexPairs.length === 0) return null;
  const bytes = Uint8Array.from(hexPairs.map((p) => Number.parseInt(p, 16)));
  return bytes;
}

function parseUnicodeBytes(body, stringMap) {
  const m = body.match(/byte\[\]\s+\w+\s*=\s*Encoding\.Unicode\.GetBytes\((?<expr>[^)]+)\);/);
  if (!m?.groups?.expr) return null;
  const expr = m.groups.expr.trim();

  let value = null;
  if (stringMap[expr] !== undefined) {
    value = stringMap[expr];
  } else {
    const literal = parseStringLiteralFromIndex(expr, 0);
    if (literal) value = literal.value;
  }

  if (value === null) return null;
  return Uint8Array.from(Buffer.from(value, "utf16le"));
}

function parseCharCastBytes(body, stringMap) {
  const m = body.match(
    /byte\[\]\s+\w+\s*=\s*(?<src>\w+)\.Select\(o\s*=>\s*\(byte\)o\)\.ToArray\(\);/,
  );
  if (!m?.groups?.src) return null;
  const src = stringMap[m.groups.src];
  if (src === undefined) return null;
  const bytes = Uint8Array.from(Array.from(src, (ch) => ch.charCodeAt(0) & 0xff));
  return bytes;
}

function toHex(bytes) {
  return Array.from(bytes)
    .map((b) => b.toString(16).padStart(2, "0"))
    .join(" ");
}

function extractStringVariables(body) {
  const vars = {};
  const regex = /(?:const\s+)?string\s+(?<name>\w+)\s*=/g;
  let m;
  while ((m = regex.exec(body)) !== null) {
    const name = m.groups?.name;
    if (!name) continue;

    let i = regex.lastIndex;
    while (i < body.length && /\s/.test(body[i])) i++;

    const literal = parseStringLiteralFromIndex(body, i);
    if (!literal) continue;

    vars[name] = literal.value;
    regex.lastIndex = literal.end;
  }
  return vars;
}

function extractCStructOptions(body) {
  const m = body.match(/new\s+CStruct\s*\((?<args>[\s\S]*?)\)/m);
  if (!m?.groups?.args) {
    return { aligned: false, littleEndian: true, pointerSize: 8 };
  }

  const args = splitArgs(m.groups.args);
  let aligned = false;
  let littleEndian = true;
  let pointerSize = 8;

  for (const arg of args) {
    const alignedOption = arg.match(/^aligned\s*:\s*(true|false)$/i);
    if (alignedOption) {
      aligned = alignedOption[1].toLowerCase() === "true";
      continue;
    }

    const endianOption = arg.match(/^isLittleEndian\s*:\s*(true|false)$/i);
    if (endianOption) {
      littleEndian = endianOption[1].toLowerCase() === "true";
      continue;
    }

    const pointerSizeOption = arg.match(/^pointerSize\s*:\s*(\d+)$/i);
    if (pointerSizeOption) {
      const parsed = Number.parseInt(pointerSizeOption[1], 10);
      if (parsed > 0 && parsed <= 8) {
        pointerSize = parsed;
      }
      continue;
    }
  }

  if (args.length >= 2 && !args[1].includes(":")) {
    const parsed = Number.parseInt(args[1], 10);
    if (Number.isFinite(parsed) && parsed > 0 && parsed <= 8) {
      pointerSize = parsed;
    }
  }

  return { aligned, littleEndian, pointerSize };
}

function extractParseRootType(body) {
  const withRoot = body.match(/ParseStream(?:WithDebug)?\s*\([^)]*,\s*"(?<name>[^"]+)"/m);
  if (withRoot?.groups?.name) return withRoot.groups.name;
  return null;
}

function extractDefinition(body, stringMap) {
  const preferredNames = ["structDef", "d", "cdef", "definition", "def"];
  for (const name of preferredNames) {
    const value = stringMap[name];
    if (value && /(struct|union|enum|typedef)/.test(value)) {
      return value.trim();
    }
  }

  for (const value of Object.values(stringMap)) {
    if (/(struct|union|enum|typedef)/.test(value)) {
      return value.trim();
    }
  }

  const constructor = /new\s+CStruct\s*\(/g.exec(body);
  if (constructor) {
    let index = constructor.index + constructor[0].length;
    while (index < body.length && /\s/.test(body[index])) index++;
    const literal = parseConcatenatedStringLiterals(body, index);
    if (literal && /(struct|union|enum|typedef)/.test(literal.value)) {
      return literal.value.trim();
    }
  }

  return null;
}

function extractDemoData(body, stringMap) {
  const hexBytes = parseHexBuf(body, stringMap);
  if (hexBytes) return hexBytes;

  const intArray = parseIntArrayLiteral(body);
  if (intArray) return intArray;

  const longArrayBytes = parseLongArrayBlockCopy(body);
  if (longArrayBytes) return longArrayBytes;

  const unicodeBytes = parseUnicodeBytes(body, stringMap);
  if (unicodeBytes) return unicodeBytes;

  const charCastBytes = parseCharCastBytes(body, stringMap);
  if (charCastBytes) return charCastBytes;

  const inlineMemoryBytes = parseInlineMemoryStream(body);
  if (inlineMemoryBytes) return inlineMemoryBytes;

  return null;
}

function hasParseCall(body) {
  return /ParseStream(?:WithDebug)?\s*\(/.test(body);
}

function hasExpectedParseFailure(body) {
  return /Assert\.(?:Throws|ThrowsExactly)[\s\S]*?ParseStream(?:WithDebug)?\s*\(/.test(body);
}

function extractMethods(filePath) {
  const text = fs.readFileSync(filePath, "utf8");
  const relativePath = path.relative(repoRoot, filePath).replaceAll("\\", "/");
  const classMatch = text.match(/public\s+class\s+(?<name>\w+)/);
  const className = classMatch?.groups?.name ?? path.basename(filePath, ".cs");

  const tests = [];

  const testAttrRegex = /\[TestMethod\]/g;
  let attrMatch;
  while ((attrMatch = testAttrRegex.exec(text)) !== null) {
    const afterAttr = text.slice(attrMatch.index);
    const methodMatch = /public\s+void\s+(?<name>\w+)\s*\(\s*\)\s*\{/.exec(afterAttr);
    if (!methodMatch?.groups?.name) continue;

    const methodName = methodMatch.groups.name;
    const methodStartInSlice = methodMatch.index;
    const openBraceInSlice = methodStartInSlice + methodMatch[0].lastIndexOf("{");
    const openBrace = attrMatch.index + openBraceInSlice;
    const closeBrace = findMatchingBrace(text, openBrace);
    if (closeBrace === -1) continue;

    const body = text.slice(openBrace + 1, closeBrace);
    const line = countLines(text, attrMatch.index);
    const documentation = extractDocumentationFromXmlDoc(text, attrMatch.index);

    const stringMap = extractStringVariables(body);
    const parseCall = hasParseCall(body);

    if (!parseCall) {
      tests.push({
        id: `${className}.${methodName}`,
        className,
        methodName,
        filePath: relativePath,
        line,
        documentation,
        runnable: false,
        reason: "No ParseStream/ParseStreamWithDebug call in this test.",
      });
      continue;
    }

    if (hasExpectedParseFailure(body)) {
      tests.push({
        id: `${className}.${methodName}`,
        className,
        methodName,
        filePath: relativePath,
        line,
        documentation,
        runnable: false,
        reason: "This test verifies an expected parse failure.",
      });
      continue;
    }

    const definition = extractDefinition(body, stringMap);
    const binaryBytes = extractDemoData(body, stringMap);

    if (!definition || !binaryBytes || binaryBytes.length === 0) {
      tests.push({
        id: `${className}.${methodName}`,
        className,
        methodName,
        filePath: relativePath,
        line,
        documentation,
        runnable: false,
        reason: "Could not automatically extract both definition and binary input.",
      });
      continue;
    }

    const rootType = extractParseRootType(body);
    const options = extractCStructOptions(body);

    tests.push({
      id: `${className}.${methodName}`,
      className,
      methodName,
      filePath: relativePath,
      line,
      documentation,
      runnable: true,
      definition,
      binaryHex: toHex(binaryBytes),
      rootType,
      parserOptions: {
        aligned: options.aligned,
        littleEndian: options.littleEndian,
        pointerSize: options.pointerSize,
      },
    });
  }

  return tests;
}

function decodeXmlEntities(text) {
  return text
    .replaceAll("&lt;", "<")
    .replaceAll("&gt;", ">")
    .replaceAll("&amp;", "&")
    .replaceAll("&quot;", '"')
    .replaceAll("&apos;", "'");
}

function normalizeXmlDocValue(value) {
  return decodeXmlEntities(value.replace(/\s+/g, " ").trim());
}

function extractTag(xml, tag) {
  const m = xml.match(new RegExp(`<${tag}>([\\s\\S]*?)<\\/${tag}>`, "i"));
  if (!m?.[1]) return "";
  return normalizeXmlDocValue(m[1]);
}

function extractDocumentationFromXmlDoc(sourceText, beforeIndex) {
  const upToAttr = sourceText.slice(0, beforeIndex);
  const lines = upToAttr.split(/\r?\n/);

  const docLines = [];
  let i = lines.length - 1;
  while (i >= 0 && lines[i].trim() === "") i--;
  while (i >= 0 && lines[i].trim().startsWith("///")) {
    docLines.unshift(lines[i].replace(/^\s*\/\/\/\s?/, ""));
    i--;
  }

  if (!docLines.length) {
    return {
      summary: "",
      usage: "",
    };
  }

  const xml = docLines.join("\n");
  return {
    summary: extractTag(xml, "summary"),
    usage: extractTag(xml, "remarks"),
  };
}

const csFiles = walkCsFiles(testsRoot);
const allTests = csFiles.flatMap((file) => extractMethods(file));
allTests.sort((a, b) => a.id.localeCompare(b.id));

const missingDocs = allTests.filter((t) => !t.documentation?.summary);
if (missingDocs.length > 0) {
  const ids = missingDocs.map((t) => t.id).join(", ");
  throw new Error(`Missing documentation for tests: ${ids}`);
}

const output = {
  sourceRoot: path.relative(webRoot, testsRoot).replaceAll("\\", "/"),
  totalTests: allTests.length,
  runnableTests: allTests.filter((t) => t.runnable).length,
  tests: allTests,
};

fs.mkdirSync(path.dirname(outPath), { recursive: true });
fs.writeFileSync(outPath, JSON.stringify(output, null, 2) + "\n", "utf8");

console.log(
  `Generated ${output.totalTests} test entries (${output.runnableTests} runnable) at ${path.relative(webRoot, outPath)}`,
);
