/**
 * Reads and compares the golden manifests of the engine differential tests (tests/CStructSharpTests/Engine/Golden/),
 * so a recording can report which tests' outcomes it changed. A manifest holds one section per test, started by an
 * `@test <id>` line; this module compares sections as text and never interprets their outcomes.
 */
import fs from "node:fs";
import path from "node:path";

/** The line that starts a test's section. */
const testDirective = "@test ";

/**
 * Splits a manifest into its sections: the text from each `@test` line up to the next one, without the blank lines
 * that separate sections. The comment header before the first section is not part of any section.
 * @param {string} text The manifest text.
 * @returns {Map<string, string>} Each section's text by test id.
 */
export function goldenSections(text) {
  const sections = new Map();
  let id = null;
  let lines = [];
  /** Stores the section being read, if any, without its trailing blank lines. */
  const flush = () => {
    if (id !== null) sections.set(id, lines.join("\n").replace(/\n+$/, ""));
  };
  for (const line of text.replaceAll("\r\n", "\n").split("\n")) {
    if (line.startsWith(testDirective)) {
      flush();
      id = line.slice(testDirective.length);
      lines = [line];
    } else if (id !== null) {
      lines.push(line);
    }
  }
  flush();
  return sections;
}

/**
 * Reads every manifest (`*.txt`) of a directory.
 * @param {string} directory The manifest directory; a missing directory has no manifests.
 * @returns {Map<string, string>} Each manifest's text by file name.
 */
export function readGoldenManifests(directory) {
  if (!fs.existsSync(directory)) return new Map();
  return new Map(
    fs.readdirSync(directory)
      .filter((name) => name.endsWith(".txt"))
      .sort()
      .map((name) => [name, fs.readFileSync(path.join(directory, name), "utf8")]),
  );
}

/**
 * Compares two sets of manifests section by section.
 * @param {Map<string, string>} before The manifests before a recording, by file name.
 * @param {Map<string, string>} after The manifests after it, by file name.
 * @returns {{ file: string, added: string[], removed: string[], changed: string[] }[]} The manifests whose sections
 *   differ, in file-name order, each with the test ids of its added, removed and changed sections in id order; a new or
 *   deleted manifest lists all of its sections as added or removed.
 */
export function compareGoldenManifests(before, after) {
  const files = [...new Set([...before.keys(), ...after.keys()])].sort();
  const changes = [];
  for (const file of files) {
    const old = goldenSections(before.get(file) ?? "");
    const current = goldenSections(after.get(file) ?? "");
    const ids = [...new Set([...old.keys(), ...current.keys()])].sort();
    const added = ids.filter((id) => !old.has(id));
    const removed = ids.filter((id) => !current.has(id));
    const changed = ids.filter((id) => old.has(id) && current.has(id) && old.get(id) !== current.get(id));
    if (added.length + removed.length + changed.length > 0) changes.push({ file, added, removed, changed });
  }
  return changes;
}

/**
 * Formats the comparison for a person reviewing a recording: one line per manifest with its counts, then one line per
 * affected test marked `+` (added), `-` (removed) or `~` (changed).
 * @param {{ file: string, added: string[], removed: string[], changed: string[] }[]} changes The comparison.
 * @returns {string} The report, ending with a line feed; a single line when nothing changed.
 */
export function formatGoldenChanges(changes) {
  if (changes.length === 0) return "The golden manifests are unchanged.\n";
  const lines = [];
  for (const { file, added, removed, changed } of changes) {
    lines.push(`${file}: ${added.length} added, ${removed.length} removed, ${changed.length} changed`);
    lines.push(...added.map((id) => `  + ${id}`), ...removed.map((id) => `  - ${id}`), ...changed.map((id) => `  ~ ${id}`));
  }
  return `${lines.join("\n")}\n`;
}
