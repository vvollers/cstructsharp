/**
 * Checks that every generated benchmark layout (benchmarks/CStructSharp.Benchmarks/GeneratedLayouts/*.cs) still
 * copies its fixture exactly. A `[CStructLayout]` attribute needs a constant string, so each of these classes repeats
 * the definition, root and options of a fixture under benchmarks/fixtures/cases/. If a copy drifts, the benchmarks
 * that compare generated code with the runtime measure two different layouts. The pairing of a class with its fixture
 * is read from the benchmark sources themselves: `FixtureCase.LoadMatching("<fixture id>", typeof(<class>))`. A class
 * used only through `FixtureCase.CompileLike(typeof(<class>))` has no fixture: its runtime layout is compiled from the
 * same attribute, so it cannot drift.
 *
 * Run: node --test tools/quality/benchmark-generated-layouts.test.mjs (CI's quality-contract step runs every *.test.mjs under tools/).
 */
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";
import { repositoryRoot } from "../lib/tooling.mjs";

const benchmarkDirectory = path.join(repositoryRoot, "benchmarks/CStructSharp.Benchmarks");
const layoutDirectory = path.join(benchmarkDirectory, "GeneratedLayouts");
const casesDirectory = path.join(repositoryRoot, "benchmarks/fixtures/cases");

/**
 * Decodes the body of a C# regular string literal (the text between its quotes).
 * @param {string} body The literal's characters without the surrounding quotes.
 * @returns {string} The string value.
 */
function decodeRegularLiteral(body) {
  const simple = { n: "\n", r: "\r", t: "\t", 0: "\0", '"': '"', "'": "'", "\\": "\\" };
  return body.replace(/\\(u[0-9A-Fa-f]{4}|.)/g, (escape, code) => {
    if (code.length === 5) return String.fromCharCode(Number.parseInt(code.slice(1), 16));
    if (!(code in simple)) throw new Error(`Unsupported escape \\${code} in a [CStructLayout] definition.`);
    return simple[code];
  });
}

/**
 * Reads the `[CStructLayout]` attribute of one generated layout file.
 * @param {string} file The absolute path of the .cs file.
 * @returns {{ className: string, definition: string, root: string, pointerSize: number, aligned: boolean, littleEndian: boolean }}
 *   The class name and the attribute's definition and settings.
 */
function readLayoutAttribute(file) {
  const text = fs.readFileSync(file, "utf8");
  const name = path.relative(repositoryRoot, file);
  const className = /\bclass\s+([A-Za-z_][A-Za-z0-9_]*)/.exec(text)?.[1];
  assert.ok(className, `${name} declares no class.`);

  // Only a regular literal is accepted, so the decoding above is complete; a verbatim or raw literal fails here.
  const attribute = /\[CStructLayout\(\s*"((?:[^"\\]|\\.)*)"\s*,([^\]]*)\)\]/.exec(text);
  assert.ok(attribute, `${name} has no [CStructLayout("...", ...)] with a regular string literal.`);
  const named = attribute[2];

  /** Returns the text of one named attribute argument, failing when the file leaves it to its default. */
  const argument = (key) => {
    const match = new RegExp(`\\b${key}\\s*=\\s*("(?:[^"\\\\]|\\\\.)*"|[A-Za-z0-9]+)`).exec(named);
    assert.ok(match, `${name}: [CStructLayout] must state ${key} explicitly, so the comparison with its fixture is complete.`);
    return match[1];
  };
  const root = argument("Root");
  return {
    className,
    definition: decodeRegularLiteral(attribute[1]),
    root: root.startsWith('"') ? decodeRegularLiteral(root.slice(1, -1)) : root,
    pointerSize: Number(argument("PointerSize")),
    aligned: argument("Aligned") === "true",
    littleEndian: argument("LittleEndian") === "true",
  };
}

/**
 * Collects how the benchmark sources use the generated layout classes.
 * @returns {{ matching: Map<string, Set<string>>, compiledLike: Set<string> }} For each class, the fixture ids it is
 *   loaded against with `LoadMatching`, and the classes compiled from their own attribute with `CompileLike`.
 */
function readBenchmarkUsage() {
  const matching = new Map();
  const compiledLike = new Set();
  const sources = fs.readdirSync(benchmarkDirectory, { recursive: true }).filter((file) => file.endsWith(".cs") && !/(^|[\\/])(bin|obj)[\\/]/.test(file));
  for (const file of sources) {
    const text = fs.readFileSync(path.join(benchmarkDirectory, file), "utf8");
    for (const [, id, className] of text.matchAll(/LoadMatching\(\s*"([^"]+)"\s*,\s*typeof\(\s*([A-Za-z_][A-Za-z0-9_]*)\s*\)\s*\)/g)) {
      if (!matching.has(className)) matching.set(className, new Set());
      matching.get(className).add(id);
    }
    for (const [, className] of text.matchAll(/CompileLike\(\s*typeof\(\s*([A-Za-z_][A-Za-z0-9_]*)\s*\)\s*\)/g)) compiledLike.add(className);
  }
  return { matching, compiledLike };
}

/**
 * Reads the definition, root and options of one fixture, with the fixture loader's option defaults.
 * @param {string} id The fixture id, which is also its file name under benchmarks/fixtures/cases/.
 * @returns {{ definition: string, root: string, pointerSize: number, aligned: boolean, littleEndian: boolean }} The settings to compare.
 */
function readFixture(id) {
  const file = path.join(casesDirectory, `${id}.json`);
  assert.ok(fs.existsSync(file), `Fixture '${id}' named by a LoadMatching call does not exist (${path.relative(repositoryRoot, file)}).`);
  const fixture = JSON.parse(fs.readFileSync(file, "utf8"));
  const options = fixture.options ?? {};
  return {
    definition: fixture.definition,
    root: fixture.root,
    pointerSize: options.pointerSize ?? 8,
    aligned: options.aligned ?? false,
    littleEndian: options.littleEndian ?? true,
  };
}

const layoutFiles = fs.readdirSync(layoutDirectory).filter((file) => file.endsWith(".cs")).sort();
const usage = readBenchmarkUsage();

test("the generated benchmark layouts directory is not empty", () => {
  assert.ok(layoutFiles.length > 0, `No layouts found under ${path.relative(repositoryRoot, layoutDirectory)}.`);
});

for (const file of layoutFiles) {
  test(`GeneratedLayouts/${file} matches the fixture the benchmarks compare it with`, () => {
    const layout = readLayoutAttribute(path.join(layoutDirectory, file));
    const fixtures = usage.matching.get(layout.className);
    if (!fixtures) {
      assert.ok(
        usage.compiledLike.has(layout.className),
        `${layout.className} is neither checked against a fixture (FixtureCase.LoadMatching) nor compiled from its own attribute ` +
          "(FixtureCase.CompileLike); a generated-versus-runtime comparison over it could measure two different layouts.",
      );
      return;
    }

    for (const id of fixtures) {
      const fixture = readFixture(id);
      for (const key of ["definition", "root", "pointerSize", "aligned", "littleEndian"]) {
        assert.equal(
          layout[key],
          fixture[key],
          `GeneratedLayouts/${file} (${layout.className}) no longer matches fixture '${id}': its ${key} differs. ` +
            `Copy the fixture's definition, root and options from benchmarks/fixtures/cases/${id}.json into the [CStructLayout] attribute.`,
        );
      }
    }
  });
}
