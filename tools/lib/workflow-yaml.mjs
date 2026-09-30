/**
 * A small, dependency-free reader for the YAML subset that the repository's GitHub Actions workflows use, and the
 * job-name expansion that GitHub applies to them. The release publisher runs without `npm ci`, so it cannot import a
 * YAML package; this reader lets it and the documentation gate read the real workflow files instead of repeating
 * their job names as constants.
 *
 * Supported: block mappings and sequences (including `- key: value` items), plain, single- and double-quoted
 * scalars, one-level flow sequences (`[a, b]`) and flow mappings (`{ a: b }`), `|`/`>` block scalars (kept as
 * text), and `#` comments.
 * Anything else (anchors, multi-line plain scalars, nested flow collections) throws, so a workflow that this reader
 * cannot represent fails loudly instead of yielding a wrong job list. Every scalar is returned as a string.
 *
 *   import { parseWorkflow, readWorkflow, jobDisplayNames } from "../lib/workflow-yaml.mjs";
 */
import fs from "node:fs";
import path from "node:path";

/**
 * Removes a trailing `#` comment that is outside quotes. A `#` starts a comment only at the start of the text or
 * after whitespace, which is the YAML rule; `${{ }}` expressions never contain one.
 * @param {string} text One line's content.
 * @returns {string} The content without its comment, right-trimmed.
 */
function stripComment(text) {
  let quote = null;
  for (let index = 0; index < text.length; index++) {
    const character = text[index];
    if (quote) {
      if (character === quote) quote = null;
    } else if (character === '"' || character === "'") {
      quote = character;
    } else if (character === "#" && (index === 0 || /\s/.test(text[index - 1]))) {
      return text.slice(0, index).trimEnd();
    }
  }
  return text.trimEnd();
}

/**
 * Splits the inside of a one-level flow collection at the commas that are outside quotes.
 * @param {string} inner The text between the brackets or braces.
 * @param {number} lineNumber The 1-based source line, for error messages.
 * @returns {string[]} The trimmed items; empty for an empty collection.
 */
function splitFlowItems(inner, lineNumber) {
  if (inner.trim() === "") return [];
  const items = [];
  let quote = null;
  let start = 0;
  for (let index = 0; index < inner.length; index++) {
    const character = inner[index];
    if (quote) {
      if (character === quote) quote = null;
    } else if (character === '"' || character === "'") {
      quote = character;
    } else if ("[]{}".includes(character)) {
      throw new Error(`Line ${lineNumber}: nested flow collections are not supported.`);
    } else if (character === ",") {
      items.push(inner.slice(start, index).trim());
      start = index + 1;
    }
  }
  items.push(inner.slice(start).trim());
  return items;
}

/**
 * Converts one inline value to a string, a one-level flow sequence, or a one-level flow mapping.
 * @param {string} text The value after `key:` or `- `, without its comment.
 * @param {number} lineNumber The 1-based source line, for error messages.
 * @returns {string | string[] | object} The parsed value.
 */
function parseScalar(text, lineNumber) {
  const value = text.trim();
  if (value.startsWith("[")) {
    if (!value.endsWith("]")) throw new Error(`Line ${lineNumber}: unsupported flow sequence '${value}'.`);
    return splitFlowItems(value.slice(1, -1), lineNumber).map((item) => parseScalar(item, lineNumber));
  }
  if (value.startsWith("{")) {
    if (!value.endsWith("}")) throw new Error(`Line ${lineNumber}: unsupported flow mapping '${value}'.`);
    const result = {};
    for (const item of splitFlowItems(value.slice(1, -1), lineNumber)) {
      const match = /^("[^"]*"|'[^']*'|[^\s"':][^:]*?):\s+(.*)$/.exec(item);
      if (!match) throw new Error(`Line ${lineNumber}: expected 'key: value' in flow mapping, found '${item}'.`);
      result[parseScalar(match[1], lineNumber)] = parseScalar(match[2], lineNumber);
    }
    return result;
  }
  if (/^[&*!]/.test(value)) throw new Error(`Line ${lineNumber}: anchors, aliases and tags are not supported.`);
  if (value.startsWith('"')) {
    if (!value.endsWith('"') || value.length < 2) throw new Error(`Line ${lineNumber}: unterminated string ${value}.`);
    return value.slice(1, -1).replace(/\\(["\\])/g, "$1");
  }
  if (value.startsWith("'")) {
    if (!value.endsWith("'") || value.length < 2) throw new Error(`Line ${lineNumber}: unterminated string ${value}.`);
    return value.slice(1, -1).replaceAll("''", "'");
  }
  return value;
}

/**
 * Parses workflow YAML text into plain objects, arrays and strings.
 * @param {string} text The workflow file's contents.
 * @returns {object} The top-level mapping.
 * @throws {Error} When the text uses YAML outside the supported subset.
 */
export function parseWorkflow(text) {
  // Each entry keeps the raw text so block scalars can be taken verbatim; `content` is set only for lines that
  // carry structure (not blank, not comment-only).
  const lines = text.split(/\r?\n/).map((raw, index) => {
    if (raw.includes("\t")) throw new Error(`Line ${index + 1}: tabs are not supported.`);
    const content = stripComment(raw.trimStart());
    return { raw, number: index + 1, indent: raw.length - raw.trimStart().length, content };
  });
  let cursor = 0;

  /** Advances past blank and comment-only lines and returns the next structural line, or undefined at the end. */
  function peek() {
    while (cursor < lines.length && lines[cursor].content === "") cursor++;
    return lines[cursor];
  }

  /**
   * Reads a `|` (literal) or `>` (folded) block scalar that follows the current key: every blank line and every
   * line indented deeper than the key, with the first content line's indentation removed. Folding joins adjacent
   * lines with a space; `-` chomping drops the final line break, other indicators keep one.
   */
  function blockScalar(keyIndent, indicator) {
    const collected = [];
    while (cursor < lines.length && (lines[cursor].raw.trim() === "" || lines[cursor].indent > keyIndent)) {
      collected.push(lines[cursor].raw);
      cursor++;
    }
    while (collected.length > 0 && collected.at(-1).trim() === "") collected.pop();
    const contentIndent = Math.min(...collected.filter((raw) => raw.trim() !== "").map((raw) => raw.length - raw.trimStart().length));
    const body = collected.map((raw) => raw.slice(contentIndent));
    const text = indicator.startsWith(">") ? body.join("\n").replace(/([^\n])\n(?=[^\n ])/g, "$1 ") : body.join("\n");
    return indicator.endsWith("-") ? text : `${text}\n`;
  }

  /** Parses the node that starts at the next structural line, which must be indented deeper than `parentIndent`. */
  function node(parentIndent, allowSequenceAtParent = false) {
    const next = peek();
    if (!next) return null;
    const isItem = next.content === "-" || next.content.startsWith("- ");
    if (next.indent > parentIndent || (allowSequenceAtParent && isItem && next.indent === parentIndent)) {
      return isItem ? sequence(next.indent) : mapping(next.indent);
    }
    return null;
  }

  /** Parses a key's value: an inline scalar, a block scalar, or a nested node on the following lines. */
  function value(rest, keyIndent, lineNumber) {
    if (rest === "") return node(keyIndent, true);
    if (/^[|>][+-]?$/.test(rest)) return blockScalar(keyIndent, rest);
    const scalar = parseScalar(rest, lineNumber);
    const next = peek();
    if (next && next.indent > keyIndent) throw new Error(`Line ${next.number}: multi-line plain scalars are not supported.`);
    return scalar;
  }

  /** Parses a block mapping whose keys start at `indent`. */
  function mapping(indent) {
    const result = {};
    for (let line = peek(); line && line.indent === indent; line = peek()) {
      if (line.content === "-" || line.content.startsWith("- ")) break;
      const match = /^("[^"]*"|'[^']*'|[^\s"'#:][^:]*?):(?:\s+(.*))?$/.exec(line.content);
      if (!match) throw new Error(`Line ${line.number}: expected 'key: value', found '${line.content}'.`);
      const key = parseScalar(match[1], line.number);
      if (Object.hasOwn(result, key)) throw new Error(`Line ${line.number}: duplicate key '${key}'.`);
      cursor++;
      result[key] = value(match[2] ?? "", indent, line.number);
    }
    const next = peek();
    if (next && next.indent > indent) throw new Error(`Line ${next.number}: unexpected indentation.`);
    return result;
  }

  /** Parses a block sequence whose `-` markers are at `indent`. */
  function sequence(indent) {
    const result = [];
    for (let line = peek(); line && line.indent === indent && (line.content === "-" || line.content.startsWith("- ")); line = peek()) {
      const rest = line.content.slice(1).trimStart();
      if (rest === "") {
        cursor++;
        result.push(node(indent));
      } else if (/^("[^"]*"|'[^']*'|[^\s"'#:[{][^:]*?):(?:\s|$)/.test(rest)) {
        // `- key: value` starts a mapping whose keys line up with `key`: re-read this line as that mapping's first line.
        const offset = line.content.length - rest.length;
        lines[cursor] = { ...line, indent: indent + offset, content: rest };
        result.push(mapping(indent + offset));
      } else {
        cursor++;
        result.push(value(rest, indent, line.number));
      }
    }
    return result;
  }

  const document = mapping(peek()?.indent ?? 0);
  const rest = peek();
  if (rest) throw new Error(`Line ${rest.number}: unexpected content '${rest.content}'.`);
  return document;
}

/**
 * Reads and parses a workflow file.
 * @param {string} root The repository root.
 * @param {string} relativePath The workflow path relative to the root, such as `.github/workflows/web.yml`.
 * @returns {object} The parsed workflow.
 */
export function readWorkflow(root, relativePath) {
  const fullPath = path.join(root, relativePath);
  try {
    return parseWorkflow(fs.readFileSync(fullPath, "utf8"));
  } catch (error) {
    throw new Error(`${relativePath}: ${error.message}`, { cause: error });
  }
}

/**
 * Expands a job's `strategy.matrix` into its combinations the way GitHub Actions does: the cross product of the
 * axes, minus `exclude` entries, then each `include` entry is merged into every combination whose original axis
 * values it does not change, or added as a new combination when it matches none.
 * @param {object | undefined} matrix The parsed matrix, or undefined for a job without one.
 * @returns {object[]} One object per job instance, mapping matrix keys to values; `[{}]` without a matrix.
 * @throws {Error} When the matrix is computed at run time (an expression) and so cannot be read from the file.
 */
export function expandMatrix(matrix) {
  if (matrix === undefined || matrix === null) return [{}];
  if (typeof matrix !== "object" || Array.isArray(matrix)) throw new Error(`A run-time matrix (${JSON.stringify(matrix)}) cannot be expanded from the workflow file.`);
  const { include = [], exclude = [], ...axes } = matrix;
  let combinations = Object.keys(axes).length === 0 ? [] : [{}];
  for (const [key, values] of Object.entries(axes)) {
    if (!Array.isArray(values)) throw new Error(`Matrix axis '${key}' must be a list.`);
    combinations = combinations.flatMap((combination) => values.map((item) => ({ ...combination, [key]: item })));
  }
  /** True when every key of `entry` that it shares with `keys` has the same value in `combination`. */
  const matches = (combination, entry, keys) => Object.entries(entry).filter(([key]) => keys.has(key)).every(([key, item]) => combination[key] === item);
  combinations = combinations.filter((combination) => !exclude.some((entry) => matches(combination, entry, new Set(Object.keys(entry)))));
  const originalKeys = new Set(Object.keys(axes));
  const original = combinations.length;
  for (const entry of include) {
    let merged = false;
    for (let index = 0; index < original; index++) {
      if (matches(combinations[index], entry, originalKeys)) {
        combinations[index] = { ...combinations[index], ...entry };
        merged = true;
      }
    }
    if (!merged) combinations.push({ ...entry });
  }
  return combinations;
}

/**
 * Returns the names GitHub shows for a job's instances: its `name` (or its id) with `${{ matrix.key }}` replaced,
 * or with the matrix values appended in parentheses when the name does not use them.
 * @param {string} id The job id.
 * @param {object} job The parsed job.
 * @returns {string[]} One name per matrix combination.
 * @throws {Error} When the name uses an expression other than a matrix value.
 */
export function jobDisplayNames(id, job) {
  const template = job.name ?? id;
  return expandMatrix(job.strategy?.matrix).map((combination) => {
    let usedMatrix = false;
    const name = template.replace(/\$\{\{\s*matrix\.([A-Za-z0-9_-]+)\s*\}\}/g, (_, key) => {
      if (!Object.hasOwn(combination, key)) throw new Error(`Job '${id}' names matrix value '${key}', which its matrix does not define.`);
      usedMatrix = true;
      return combination[key];
    });
    if (name.includes("${{")) throw new Error(`Job '${id}' has a name that depends on a run-time expression: ${template}`);
    const values = Object.values(combination);
    return usedMatrix || values.length === 0 ? name : `${name} (${values.join(", ")})`;
  });
}
