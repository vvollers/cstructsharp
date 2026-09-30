import fs from "node:fs";
import path from "node:path";

const webRoot = process.cwd();
const repoRoot = path.resolve(webRoot, "../..");
const testsRoot = path.resolve(repoRoot, "tests/CStructSharpTests");
const outPath = path.resolve(webRoot, "src/generated/test-demos.json");
const githubSourceRoot = "https://github.com/vvollers/cstructsharp/blob/main/";

/**
 * Recursively lists the C# source files under a directory, skipping hidden and build-output
 * folders.
 * @param {string} dir Directory to search.
 * @returns {string[]} Absolute paths of the `.cs` files found.
 */
function walkCsFiles(dir) {
  const results = [];
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    if (entry.name.startsWith(".") || ["bin", "obj", "artifacts"].includes(entry.name)) continue;
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

/**
 * Decodes the escape sequences of a regular C# string literal body (`\n`, `\u0041`, `\x41`, ...).
 * @param {string} content Literal text between the quotes, with escapes still encoded.
 * @returns {string} The decoded string value.
 */
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

/**
 * Trims the blank first and last lines of a C# raw string literal and removes their common
 * indentation.
 * @param {string} value Text between the raw literal's quote delimiters.
 * @returns {string} The literal's value as C# would produce it.
 */
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

/**
 * Parses a C# string literal (regular, verbatim `@"..."`, or raw `"""..."""`) that starts at an
 * index.
 * @param {string} text Source text.
 * @param {number} start Index of the literal's first character.
 * @returns {{value: string, end: number} | null} The decoded value and the index just past the
 *   literal, or null when no complete literal starts there.
 */
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

/**
 * Parses one or more string literals joined with `+` into a single value.
 * @param {string} text Source text.
 * @param {number} start Index of the first literal.
 * @returns {{value: string, end: number} | null} The concatenated value and the index after the
 *   last literal, or null when any part is not a string literal.
 */
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

/**
 * Finds the `}` that closes the brace at `openIndex`, ignoring braces inside comments, strings, and
 * chars.
 * @param {string} text C# source text.
 * @param {number} openIndex Index of the opening `{`.
 * @returns {number} Index of the matching closing brace, or -1 when it is missing.
 */
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

/**
 * Returns the 1-based line number of a character index.
 * @param {string} text Source text.
 * @param {number} upToIndex Character index to locate.
 * @returns {number} Line number containing that index.
 */
function countLines(text, upToIndex) {
  let lines = 1;
  for (let i = 0; i < upToIndex; i++) {
    if (text[i] === "\n") lines++;
  }
  return lines;
}

/**
 * Splits a C# argument list at top-level commas, keeping nested brackets, strings, and chars
 * intact.
 * @param {string} argText Text between the call's parentheses.
 * @returns {string[]} Trimmed argument texts in order.
 */
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

/**
 * Parses a C# integer literal (decimal, hex, binary, octal, or a `(byte)'c'` cast), ignoring digit
 * separators.
 * @param {string} text Literal text.
 * @returns {number | null} The value, or null when the text is not a supported integer literal.
 */
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

/**
 * Parses a comma-separated list of byte literals.
 * @param {string} text List text from an array or collection expression.
 * @returns {Uint8Array | null} The bytes, or null when the list is empty or any value is outside
 *   0-255.
 */
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

/**
 * Reads the bytes of a `byte[] name = [ ... ];` collection expression in a test body.
 * @param {string} body Test method body.
 * @returns {Uint8Array | null} The bytes, or null when no such array is found.
 */
function parseIntArrayLiteral(body) {
  const m = body.match(/byte\[\]\s+\w+\s*=\s*\[(?<vals>[\s\S]*?)\];/m);
  if (!m?.groups?.vals) return null;
  return parseByteList(m.groups.vals);
}

/**
 * Reads the bytes of a `new MemoryStream([ ... ])` expression in a test body.
 * @param {string} body Test method body.
 * @returns {Uint8Array | null} The bytes, or null when no such stream is found.
 */
function parseInlineMemoryStream(body) {
  const m = body.match(/new\s+MemoryStream\s*\(\s*\[(?<vals>[\s\S]*?)\]\s*\)/m);
  return m?.groups?.vals ? parseByteList(m.groups.vals) : null;
}

/**
 * Reads a `long[]` literal that the test copies to bytes with `Buffer.BlockCopy`, as little-endian
 * int64 values.
 * @param {string} body Test method body.
 * @returns {Uint8Array | null} Eight bytes per value, or null when the pattern is absent.
 */
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

/**
 * Reads bytes from a `name.ParseHexDataContent()` call whose string variable holds hexadecimal
 * pairs.
 * @param {string} body Test method body.
 * @param {Record<string, string>} stringMap String variables declared in the body.
 * @returns {Uint8Array | null} The decoded bytes, or null when the pattern or its string is absent.
 */
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

/**
 * Reads bytes produced by `Encoding.Unicode.GetBytes(...)` from a string variable or literal
 * (UTF-16LE).
 * @param {string} body Test method body.
 * @param {Record<string, string>} stringMap String variables declared in the body.
 * @returns {Uint8Array | null} The encoded bytes, or null when the pattern is absent.
 */
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

/**
 * Reads bytes produced by casting each character of a string variable to `byte`.
 * @param {string} body Test method body.
 * @param {Record<string, string>} stringMap String variables declared in the body.
 * @returns {Uint8Array | null} The low byte of each character, or null when the pattern is absent.
 */
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

/**
 * Formats bytes as space-separated two-digit lowercase hexadecimal pairs.
 * @param {Uint8Array} bytes Bytes to format.
 * @returns {string} Hexadecimal text such as `01 ff`.
 */
function toHex(bytes) {
  return Array.from(bytes)
    .map((b) => b.toString(16).padStart(2, "0"))
    .join(" ");
}

/**
 * Collects the `string name = <literal>;` declarations of a test body.
 * @param {string} body Test method body.
 * @returns {Record<string, string>} Decoded values keyed by variable name.
 */
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

/**
 * Returns the argument text of the first `new CStruct(...)` call, up to its matching parenthesis. String literals
 * are skipped whole, so a parenthesis inside the layout text (for example `@count(n)`) does not end the call.
 */
function cstructConstructorArguments(body) {
  const constructor = /new\s+CStruct\s*\(/.exec(body);
  if (!constructor) return null;

  const start = constructor.index + constructor[0].length;
  let depth = 1;
  for (let i = start; i < body.length; i++) {
    const ch = body[i];
    if (ch === '"' || (ch === "@" && body[i + 1] === '"')) {
      const literal = parseStringLiteralFromIndex(body, i);
      if (!literal) return null;
      i = literal.end - 1;
      continue;
    }
    if (ch === "(") depth++;
    if (ch === ")" && --depth === 0) return body.slice(start, i);
  }
  return null;
}

/** Reads the parser options (alignment, byte order, pointer width) from the test's `new CStruct(...)` call. */
function extractCStructOptions(body) {
  const argumentText = cstructConstructorArguments(body);
  if (!argumentText) {
    return { aligned: false, littleEndian: true, pointerSize: 8 };
  }

  const args = splitArgs(argumentText);
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

// A layout read is `<layout>.Parse(...)` / `<layout>.ParseWithDebug(...)`, or `<layout>.ReadValue(...)` /
// `<layout>.ReadValueWithDebug(...)` selecting a bare root name (a union root, which Parse rejects). The lookbehind
// skips the BCL parsers (`long.Parse`, `JsonDocument.Parse`, ...) that tests also call.
const bclParsers = String.raw`(?<!\b(?:long|ulong|int|uint|short|ushort|byte|sbyte|double|float|decimal|BigInteger|Guid|DateTime|JsonDocument|JsonNode|JsonSerializer|Enum|Version|TimeSpan|Uri))`;
const parseCallPattern = String.raw`${bclParsers}\.Parse(?:WithDebug)?\s*\(`;
const rootReadCallPattern = String.raw`\.ReadValue(?:WithDebug)?\s*\([^)]*,\s*"(?<name>[A-Za-z_]\w*)"`;

/**
 * Finds the root type a test reads: the root-name argument of a Parse call or of a root ReadValue
 * call.
 * @param {string} body Test method body.
 * @returns {string | null} The root name, or null when the test uses the layout's default root.
 */
function extractParseRootType(body) {
  const withRoot = body.match(new RegExp(`${parseCallPattern}[^)]*,\\s*"(?<name>[^"]+)"`, "m"));
  if (withRoot?.groups?.name) return withRoot.groups.name;
  const rootRead = body.match(new RegExp(rootReadCallPattern, "m"));
  if (rootRead?.groups?.name) return rootRead.groups.name;
  return null;
}

/**
 * Finds the layout text of a test: a well-known string variable, any string that declares a type,
 * or the first argument of `new CStruct(...)`.
 * @param {string} body Test method body.
 * @param {Record<string, string>} stringMap String variables declared in the body.
 * @returns {string | null} The trimmed layout text, or null when none is found.
 */
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

/**
 * Extracts the input bytes of a test by trying each supported byte-construction pattern in turn.
 * @param {string} body Test method body.
 * @param {Record<string, string>} stringMap String variables declared in the body.
 * @returns {Uint8Array | null} The first bytes found, or null when no pattern matches.
 */
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

/**
 * Reports whether a test body reads a layout through Parse/ParseWithDebug or a root ReadValue call.
 * @param {string} body Test method body.
 * @returns {boolean} True when a layout read is present.
 */
function hasParseCall(body) {
  return new RegExp(parseCallPattern).test(body) || new RegExp(rootReadCallPattern).test(body);
}

/**
 * Reports whether a test expects a layout read to throw (`Assert.Throws` around a read call).
 * @param {string} body Test method body.
 * @returns {boolean} True when the test verifies a read failure.
 */
function hasExpectedParseFailure(body) {
  return new RegExp(
    String.raw`Assert\.(?:Throws|ThrowsExactly)[\s\S]*?(?:${parseCallPattern}|${rootReadCallPattern})`,
  ).test(body);
}

/**
 * Builds a demo entry for every `[TestMethod]` in a C# test file. Entries the browser can replay
 * carry the layout, input bytes, root, and parser options; the others carry the reason they cannot
 * run.
 * @param {string} filePath Absolute path of the test file.
 * @returns {object[]} One entry per test method, with its documentation and source location.
 */
function extractMethods(filePath) {
  const text = fs.readFileSync(filePath, "utf8");
  const relativePath = path.relative(repoRoot, filePath).replaceAll("\\", "/");
  const classMatch = text.match(/public\s+class\s+(?<name>\w+)/);
  const className = classMatch?.groups?.name ?? path.basename(filePath, ".cs");

  const tests = [];

  const testAttrRegex = /\[TestMethod\]/g;
  let attrMatch;
  while ((attrMatch = testAttrRegex.exec(text)) !== null) {
    // Keep each attribute attached to its own method, including data-driven and async tests.
    // Searching for the next parameterless method could silently borrow a later test's body and ID.
    const nextTest = text.indexOf("[TestMethod]", attrMatch.index + attrMatch[0].length);
    const afterAttr = text.slice(attrMatch.index, nextTest === -1 ? undefined : nextTest);
    // A test method has a block body or an expression body (`=> AssertCase(id);`).
    const methodMatch =
      /public\s+(?:(?:async|unsafe)\s+)*(?:void|Task)\s+(?<name>\w+)\s*\((?<parameters>[^)]*)\)\s*(?<open>\{|=>)/.exec(
        afterAttr,
      );
    if (!methodMatch?.groups?.name) continue;

    const methodName = methodMatch.groups.name;
    const methodStartInSlice = methodMatch.index;
    let body;
    if (methodMatch.groups.open === "=>") {
      const expressionStart = attrMatch.index + methodStartInSlice + methodMatch[0].length;
      const expressionEnd = text.indexOf(";", expressionStart);
      if (expressionEnd === -1) continue;
      body = text.slice(expressionStart, expressionEnd);
    } else {
      const openBrace = attrMatch.index + methodStartInSlice + methodMatch[0].lastIndexOf("{");
      const closeBrace = findMatchingBrace(text, openBrace);
      if (closeBrace === -1) continue;
      body = text.slice(openBrace + 1, closeBrace);
    }
    const line = countLines(text, attrMatch.index + methodStartInSlice);
    const documentation = extractDocumentationFromXmlDoc(text, attrMatch.index);

    if (methodMatch.groups.parameters.trim()) {
      tests.push({
        id: `${className}.${methodName}`,
        className,
        methodName,
        filePath: relativePath,
        line,
        documentation,
        runnable: false,
        reason:
          "This parameterized test uses data rows or fixtures supplied by the C# test runner.",
      });
      continue;
    }

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
        reason: "No Parse/ParseWithDebug (or root ReadValue) call in this test.",
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

    if (
      /\b(?:CLongWidth|DefaultEnumStorage|BitfieldAllocation|Defined|Codecs|Prelude)\s*=/.test(body)
    ) {
      // The browser bridge exposes the constructor's pointer size, alignment, and byte order but not the compilation
      // options (CLongWidth, DefaultEnumStorage, BitfieldAllocation, Defined, Codecs, Prelude); a layout compiled
      // with one would read different bytes here.
      tests.push({
        id: `${className}.${methodName}`,
        className,
        methodName,
        filePath: relativePath,
        line,
        documentation,
        runnable: false,
        reason:
          "This test compiles the layout with compilation options the browser bridge does not expose.",
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

/**
 * Decodes the five predefined XML entities.
 * @param {string} text XML text.
 * @returns {string} Text with `&lt;`, `&gt;`, `&amp;`, `&quot;`, and `&apos;` replaced.
 */
function decodeXmlEntities(text) {
  return text
    .replaceAll("&lt;", "<")
    .replaceAll("&gt;", ">")
    .replaceAll("&amp;", "&")
    .replaceAll("&quot;", '"')
    .replaceAll("&apos;", "'");
}

/**
 * Collapses whitespace in an XML documentation value and decodes its entities.
 * @param {string} value Raw element content.
 * @returns {string} Single-line plain text.
 */
function normalizeXmlDocValue(value) {
  return decodeXmlEntities(value.replace(/\s+/g, " ").trim());
}

/**
 * Returns the normalized content of the first `<tag>` element in XML documentation text.
 * @param {string} xml XML documentation text.
 * @param {string} tag Element name, such as `summary`.
 * @returns {string} The element's text, or an empty string when it is absent.
 */
function extractTag(xml, tag) {
  const m = xml.match(new RegExp(`<${tag}>([\\s\\S]*?)<\\/${tag}>`, "i"));
  if (!m?.[1]) return "";
  return normalizeXmlDocValue(m[1]);
}

/**
 * Reads the `///` XML documentation comment directly above a test's attribute.
 * @param {string} sourceText Whole C# file text.
 * @param {number} beforeIndex Index of the `[TestMethod]` attribute.
 * @returns {{summary: string, usage: string}} The summary, and the remarks (or summary) as usage
 *   text; empty strings when the test has no documentation comment.
 */
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
    usage: extractTag(xml, "remarks") || extractTag(xml, "summary"),
  };
}

const csFiles = walkCsFiles(testsRoot);
const allTests = csFiles
  .flatMap((file) => extractMethods(file))
  .map((test) => ({
    ...test,
    sourceUrl: `${githubSourceRoot}${test.filePath.split("/").map(encodeURIComponent).join("/")}#L${test.line}`,
  }));
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
