// The compiler-comparison fixture: records what a real C compiler does with the shapes in
// tools/compiler-fixtures/portable-host-facts.c, validates the checked-in observations, and renders the comparison
// table in docs/language/differences-from-c.md.
//
//   node tools/quality/compiler-fixture.mjs record --compiler gcc [--flags "-m32"] --output <file.json>
//   node tools/quality/compiler-fixture.mjs validate [<file-or-directory> ...]
//   node tools/quality/compiler-fixture.mjs table
//
// `record` accepts gcc, clang, clang-cl, and cl (MSVC); it compiles the fixture in strict C11 mode, runs it, and
// writes an observation-only evidence record with the compiler's identity, target, host, and the fixture's SHA-256.
// `validate` checks provenance and freshness (a record made from an older fixture source is stale) and the shape of
// every recorded layout. `table` rewrites the generated block of differences-from-c.md from the baselines and the
// Portable claims in contracts/quality/compiler-fixtures/shapes.json (which the managed tests verify).
import { spawnSync } from "node:child_process";
import crypto from "node:crypto";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const sourcePath = path.join(root, "tools/compiler-fixtures/portable-host-facts.c");
const shapesPath = path.join(root, "contracts/quality/compiler-fixtures/shapes.json");
const baselinesDirectory = path.join(root, "contracts/quality/compiler-fixtures/baselines");
const docPath = path.join(root, "docs/language/differences-from-c.md");
const sourceRelative = "tools/compiler-fixtures/portable-host-facts.c";

const args = process.argv.slice(2);
const mode = args[0];
function option(name, fallback) {
  const index = args.indexOf(name);
  return index >= 0 ? args[index + 1] : fallback;
}
function fail(message) {
  console.error(message);
  process.exit(1);
}
function sha256(file) {
  return crypto.createHash("sha256").update(fs.readFileSync(file)).digest("hex").toUpperCase();
}
function run(command, commandArgs, options = {}) {
  const result = spawnSync(command, commandArgs, { encoding: "utf8", ...options });
  if (result.error) throw new Error(`${command} could not be started: ${result.error.message}`);
  return result;
}

if (mode === "record") {
  const compiler = option("--compiler");
  const output = option("--output");
  const extraFlags = (option("--flags", "") || "").split(/\s+/).filter(Boolean);
  if (!compiler || !output) fail("record needs --compiler <gcc|clang|clang-cl|cl> and --output <file>.");
  const record = recordObservation(compiler, extraFlags);
  fs.mkdirSync(path.dirname(output), { recursive: true });
  fs.writeFileSync(output, JSON.stringify(record, null, 2) + "\n");
  console.log(`Compiler fixture evidence written to '${output}'.`);
  console.log(`Compiler: ${record.compiler.family} ${record.compiler.version} (${record.compiler.target}); host ${record.host.os} ${record.host.architecture}.`);
  process.exit(0);
}

if (mode === "validate") {
  const inputs = args.slice(1);
  const usingDefault = inputs.length === 0;
  const files = collectFiles(usingDefault ? [baselinesDirectory] : inputs);
  if (files.length === 0) fail("No compiler evidence JSON files were found.");
  const sourceHash = sha256(sourcePath);
  const shapes = JSON.parse(fs.readFileSync(shapesPath, "utf8"));
  const families = new Set();
  for (const file of files) {
    validateRecord(file, JSON.parse(fs.readFileSync(file, "utf8")), sourceHash, shapes, families);
  }
  if (usingDefault && !families.has("GCC")) fail("The checked-in baseline set must contain a GCC observation.");
  console.log(`Compiler-differential fixture validation passed: ${files.length} evidence file(s), families ${[...families].sort().join(", ")}.`);
  process.exit(0);
}

if (mode === "table") {
  const shapes = JSON.parse(fs.readFileSync(shapesPath, "utf8"));
  const files = collectFiles([baselinesDirectory]);
  const baselines = files.map((file) => JSON.parse(fs.readFileSync(file, "utf8")));
  const table = renderTable(shapes, baselines);
  const doc = fs.readFileSync(docPath, "utf8");
  const start = "<!-- compiler-fixture-table:start -->";
  const end = "<!-- compiler-fixture-table:end -->";
  if (!doc.includes(start) || !doc.includes(end)) fail(`${docPath} has no compiler-fixture table markers.`);
  const updated = doc.slice(0, doc.indexOf(start) + start.length) + "\n" + table + "\n" + doc.slice(doc.indexOf(end));
  if (args.includes("--check")) {
    if (updated !== doc) fail("The compiler comparison table in differences-from-c.md is out of date; run `node tools/quality/compiler-fixture.mjs table`.");
    console.log("The compiler comparison table is up to date.");
  } else {
    fs.writeFileSync(docPath, updated);
    console.log(`Rendered ${shapes.shapes.length} shapes × ${baselines.length} baseline(s) into ${path.relative(root, docPath)}.`);
  }
  process.exit(0);
}

fail("Usage: compiler-fixture.mjs record|validate|table ...");

/** Compiles and runs the fixture with one compiler and wraps the facts in an observation record. */
function recordObservation(compiler, extraFlags) {
  const identity = identifyCompiler(compiler);
  const temporary = fs.mkdtempSync(path.join(os.tmpdir(), "cstructsharp-compiler-fixture-"));
  try {
    const binary = path.join(temporary, process.platform === "win32" ? "portable-host-facts.exe" : "portable-host-facts");
    const flags = [...identity.flags, ...extraFlags];
    const compileArgs = identity.family === "MSVC"
      ? [...flags, sourcePath, `/Fe:${binary}`, `/Fo:${path.join(temporary, "portable-host-facts.obj")}`]
      : [...flags, sourcePath, "-o", binary];
    const compile = run(identity.executable, compileArgs, { cwd: temporary });
    if (compile.status !== 0) throw new Error(`Fixture compilation failed with exit code ${compile.status}.\n${compile.stdout}\n${compile.stderr}`);
    const execution = run(binary, []);
    if (execution.status !== 0) throw new Error(`Fixture execution failed with exit code ${execution.status}.\n${execution.stdout}\n${execution.stderr}`);
    let facts;
    try {
      facts = JSON.parse(execution.stdout.trim());
    } catch (error) {
      throw new Error(`Fixture output was not valid JSON: ${error.message}\n${execution.stdout}`);
    }
    return {
      schemaVersion: 2,
      evidenceKind: "compiler-observation",
      claim: "observation-only",
      fixture: { id: "portable-host-facts", source: sourceRelative, sha256: sha256(sourcePath) },
      compiler: {
        family: identity.family,
        executable: path.basename(identity.executable),
        version: identity.version,
        versionOutput: identity.versionOutput,
        target: identity.target,
        abi: identity.abi,
        language: "C11",
        flags,
      },
      host: { os: hostOs(), architecture: os.arch() },
      facts,
    };
  } finally {
    fs.rmSync(temporary, { recursive: true, force: true });
  }
}

/** Works out the family, version, target triple, and ABI family (sysv or msvc) of a compiler command. */
function identifyCompiler(compiler) {
  const base = path.basename(compiler).toLowerCase().replace(/\.exe$/, "");
  if (base === "cl") {
    // cl prints its banner on stderr and has no --version; the banner carries the version and architecture.
    const banner = run(compiler, []);
    const text = `${banner.stdout}\n${banner.stderr}`.trim();
    const version = /Version\s+([\d.]+)/i.exec(text)?.[1] ?? "unknown";
    const arch = /for\s+(\w+)/i.exec(text)?.[1] ?? os.arch();
    return { executable: compiler, family: "MSVC", version, versionOutput: text, target: `${arch}-pc-windows-msvc`, abi: "msvc", flags: ["/std:c11", "/W4", "/WX", "/nologo"] };
  }
  const versionResult = run(compiler, ["--version"]);
  if (versionResult.status !== 0) throw new Error(`Compiler version query failed.\n${versionResult.stdout}\n${versionResult.stderr}`);
  const versionOutput = versionResult.stdout.trim();
  const clang = /clang version\s+([^\s]+)/i.exec(versionOutput);
  const gcc = /\b(?:gcc|g\+\+)(?:\.exe)?\b.*?\s(\d+\.\d+(?:\.\d+)?)/i.exec(versionOutput);
  let target;
  if (base === "clang-cl") {
    target = /Target:\s*(\S+)/i.exec(versionOutput)?.[1] ?? "x86_64-pc-windows-msvc";
    return { executable: compiler, family: "Clang", version: clang?.[1] ?? "unknown", versionOutput, target, abi: "msvc", flags: ["/std:c11", "/W4", "/WX", "-Wno-language-extension-token"] };
  }
  const dump = run(compiler, ["-dumpmachine"]);
  target = dump.status === 0 ? dump.stdout.trim() : (/Target:\s*(\S+)/i.exec(versionOutput)?.[1] ?? "unknown");
  const abi = /windows-msvc|-msvc$/i.test(target) ? "msvc" : "sysv";
  if (clang) return { executable: compiler, family: "Clang", version: clang[1], versionOutput, target, abi, flags: ["-std=c11", "-Wall", "-Wextra", "-Werror", "-pedantic"] };
  if (gcc) return { executable: compiler, family: "GCC", version: gcc[1], versionOutput, target, abi, flags: ["-std=c11", "-Wall", "-Wextra", "-Werror", "-pedantic"] };
  throw new Error(`Only gcc, clang, clang-cl, and cl are supported by this fixture runner.\n${versionOutput}`);
}

function hostOs() {
  return process.platform === "win32" ? "Windows" : process.platform === "darwin" ? "macOS" : "Linux";
}

function collectFiles(inputs) {
  const files = [];
  for (const input of inputs) {
    const resolved = path.resolve(root, input);
    if (!fs.existsSync(resolved)) fail(`Compiler evidence path '${input}' does not exist.`);
    if (fs.statSync(resolved).isDirectory()) {
      for (const name of fs.readdirSync(resolved).sort()) {
        if (name.endsWith(".json")) files.push(path.join(resolved, name));
      }
    } else {
      files.push(resolved);
    }
  }
  return files;
}

function assertThat(condition, message) {
  if (!condition) fail(message);
}

/** Checks one evidence record's provenance, freshness, and recorded layouts. */
function validateRecord(file, evidence, sourceHash, shapes, families) {
  const context = `Compiler evidence '${path.relative(root, file)}'`;
  assertThat(evidence.schemaVersion === 2, `${context} has an unsupported schema version (expected 2).`);
  assertThat(evidence.evidenceKind === "compiler-observation", `${context} has an invalid evidence kind.`);
  assertThat(evidence.claim === "observation-only", `${context} must be explicitly observation-only.`);
  assertThat(!("profile" in evidence), `${context} must not claim an implemented ABI profile.`);
  assertThat(evidence.fixture?.id === "portable-host-facts", `${context} has an unexpected fixture id.`);
  assertThat(evidence.fixture?.source === sourceRelative, `${context} has an unexpected fixture source.`);
  assertThat(evidence.fixture?.sha256 === sourceHash, `${context} is stale: its source SHA-256 does not match the checked-in fixture.`);
  const compiler = evidence.compiler ?? {};
  assertThat(["Clang", "GCC", "MSVC"].includes(compiler.family), `${context} names an unsupported compiler family.`);
  for (const key of ["executable", "version", "versionOutput", "target"]) {
    assertThat(typeof compiler[key] === "string" && compiler[key].trim().length > 0, `${context} has no compiler ${key}.`);
  }
  assertThat(["sysv", "msvc"].includes(compiler.abi), `${context} must record the ABI family (sysv or msvc).`);
  assertThat(compiler.language === "C11", `${context} must record C11 as its language mode.`);
  assertThat(Array.isArray(compiler.flags) && compiler.flags.length >= 4, `${context} does not record the complete strict compilation flags.`);
  families.add(compiler.family);
  assertThat(["Linux", "macOS", "Windows"].includes(evidence.host?.os), `${context} has an unsupported host OS label.`);
  assertThat(typeof evidence.host?.architecture === "string" && evidence.host.architecture.length > 0, `${context} has no host architecture.`);
  const facts = evidence.facts ?? {};
  assertThat(["little", "big"].includes(facts.endian), `${context} has an unsupported byte order.`);
  assertThat(typeof facts.char?.signed === "boolean", `${context} does not report plain-char signedness.`);
  for (const scalar of ["char", "short", "int", "long", "longLong", "wchar", "pointer", "enum"]) {
    assertThat(facts[scalar]?.size >= 1 && facts[scalar]?.alignment >= 1, `${context} scalar '${scalar}' is missing or invalid.`);
  }
  for (const [name, requireBytes] of [["fixedWidthAggregate", true], ["nestedArray", true], ["union", true], ["bitfield", true], ["pointerAggregate", false]]) {
    validateLayout(facts[name], `${context} ${name}`, requireBytes);
  }
  const recorded = facts.shapes ?? {};
  for (const shape of shapes.shapes) {
    assertThat(shape.id in recorded, `${context} is missing shape '${shape.id}'.`);
    validateLayout(recorded[shape.id], `${context} shape '${shape.id}'`, true);
  }
  for (const id of Object.keys(recorded)) {
    assertThat(shapes.shapes.some((shape) => shape.id === id), `${context} records shape '${id}' that shapes.json does not describe.`);
  }
}

function validateLayout(layout, context, requireBytes) {
  assertThat(layout && layout.size >= 1 && layout.alignment >= 1, `${context} has an invalid size or alignment.`);
  assertThat(layout.size % layout.alignment === 0, `${context} size is not a multiple of its alignment.`);
  for (const [name, offset] of Object.entries(layout.offsets ?? {})) {
    assertThat(offset >= 0 && offset < layout.size, `${context} offset '${name}' is outside the object.`);
  }
  if (requireBytes) {
    assertThat(/^(?:[0-9A-F]{2})+$/.test(layout.bytes ?? ""), `${context} bytes must be uppercase hexadecimal octets.`);
    assertThat(layout.bytes.length / 2 === layout.size, `${context} byte-image length does not match its size.`);
  }
}

/** The Markdown comparison table: one row per shape, one column per baseline, plus the verified Portable claim. */
function renderTable(shapes, baselines) {
  const columns = baselines.map((baseline) => ({
    label: `${baseline.compiler.family} ${baseline.compiler.version} (${baseline.host.os} ${baseline.host.architecture}, ${baseline.compiler.abi})`,
    facts: baseline.facts.shapes ?? {},
  }));
  const lines = [];
  lines.push("| Shape | C declaration | Portable | " + columns.map((column) => column.label).join(" | ") + " |");
  lines.push("| --- | --- | --- | " + columns.map(() => "---").join(" | ") + " |");
  const recordedAbis = new Set(baselines.map((baseline) => baseline.compiler.abi));
  const describeClaim = (mode, abi) => (recordedAbis.has(abi) ? `\`${mode}\`` : `\`${mode}\` (modelled, no ${abi} baseline yet)`);
  for (const shape of shapes.shapes) {
    const claim = [shape.portable.sysv ? describeClaim("SysV", "sysv") : null, shape.portable.msvc ? describeClaim("Msvc", "msvc") : null].filter(Boolean).join(", ") || "neither";
    const cells = columns.map((column) => {
      const layout = column.facts[shape.id];
      return layout ? `size ${layout.size}, align ${layout.alignment}: \`${layout.bytes}\`` : "not recorded";
    });
    lines.push(`| \`${shape.id}\` | \`${shape.c}\` | ${claim} | ${cells.join(" | ")} |`);
  }
  lines.push("");
  lines.push(`Baselines: ${baselines.length === 0 ? "none recorded yet" : baselines.map((baseline) => `${baseline.compiler.family} ${baseline.compiler.version} on ${baseline.host.os} ${baseline.host.architecture} (${baseline.compiler.target})`).join("; ")}. The *Portable* column names the \`BitfieldPacking\` mode(s) in which the library reproduces the compiler of the same ABI family byte for byte; \`CompilerDifferentialFixtureTests\` verifies every claim against every baseline. Compilers not recorded here (MSVC, clang-cl, macOS clang, 32-bit targets) are recorded by the \`compiler-fixtures\` workflow when it runs.`);
  return lines.join("\n");
}
