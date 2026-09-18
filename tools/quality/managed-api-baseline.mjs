#!/usr/bin/env node
// Frozen managed API baseline: compares the public surface of src/CStructSharp against
// contracts/api/managed-rc1 (compare, the CI gate), or rewrites that baseline from the current source after an
// intentional public change (update). Node port of Compare-ManagedApiBaseline.ps1 plus the manual refresh
// procedure it documented.
//
// Usage: node tools/quality/managed-api-baseline.mjs [compare]
//        node tools/quality/managed-api-baseline.mjs update --kind additive|breaking|correction \
//             --rationale "<why the surface changed>" --impact "<what consumers do>"
import crypto from "node:crypto";
import { execFileSync, spawnSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const manifestPath = path.join(root, "contracts/api/managed-rc1/manifest.json");
const projectPath = path.join(root, "src/CStructSharp/CStructSharp.csproj");
const outputRoot = path.join(root, "artifacts/api-compat/managed-current");
const frameworks = ["net8.0", "net10.0"];

const args = process.argv.slice(2);
const mode = args[0] === "update" ? "update" : "compare";
const option = (name) => {
  const index = args.indexOf(name);
  return index >= 0 ? args[index + 1] : undefined;
};

/** Normalizes line endings so hashes and diffs ignore platform conventions. */
// The Native AOT claim is per framework (net10.0 only), so its assembly-metadata line is a placeholder that each
// framework fills with the attribute or with nothing; `aotCompatible` in the manifest says which.
const aotMetadataLine = '[assembly: System.Reflection.AssemblyMetadata("IsAotCompatible", "True")]\n';
const aotPlaceholder = "<AOT_METADATA>\n";

const normalize = (text) => text.replaceAll("\r\n", "\n").replace(/\n+$/, "") + "\n";
const hashOf = (text) => crypto.createHash("sha256").update(normalize(text), "utf8").digest("hex").toUpperCase();

function fail(message) {
  console.error(message);
  process.exit(1);
}

const manifest = JSON.parse(fs.readFileSync(manifestPath, "utf8"));
if (manifest.schemaVersion !== 1 || manifest.baselineId !== "managed-rc1") fail("Unexpected managed API baseline manifest.");
const canonicalPath = path.join(root, manifest.canonical.path);
const canonicalText = normalize(fs.readFileSync(canonicalPath, "utf8"));
if (mode === "compare") {
  if (hashOf(canonicalText) !== manifest.canonical.normalizedSha256) fail("The canonical managed API baseline does not match its recorded SHA-256.");
  const history = manifest.history;
  const latest = history[history.length - 1];
  if (manifest.baselineRevision !== latest.revision) fail("The managed API baseline revision does not match its latest history entry.");
  for (const entry of manifest.frameworks) {
    if (hashOf(expectedFor(entry)) !== entry.normalizedSha256) fail(`Managed API framework '${entry.tfm}' does not match its recorded SHA-256.`);
  }
  const combined = manifest.frameworks.map((entry) => `${entry.tfm}:${entry.normalizedSha256}`).join("\n") + "\n";
  if (latest.combinedSha256 !== hashOf(combined)) fail("The latest managed API history entry does not approve the current baseline hashes.");
}

/** The frozen generator, restored through the repository tool manifest. */
function generatorCommand() {
  const packages = execFileSync("dotnet", ["nuget", "locals", "global-packages", "--list"], { encoding: "utf8" });
  const directory = packages.match(/^[^:]+:\s*(.+)$/m)?.[1]?.trim();
  const assembly = path.join(directory ?? "", `publicapigenerator.tool/${manifest.generator.version}/tools/net6.0/any/PublicApiGenerator.Tool.dll`);
  if (!fs.existsSync(assembly)) fail("PublicApiGenerator.Tool is not restored. Run 'dotnet tool restore'.");
  return ["dotnet", [assembly]];
}

const [executable, prefix] = generatorCommand();
const run = path.join(outputRoot, `run-${crypto.randomUUID().replaceAll("-", "")}`);
const work = path.join(run, "work");
const generated = path.join(run, "generated");
fs.mkdirSync(work, { recursive: true });
fs.mkdirSync(generated, { recursive: true });
const generation = spawnSync(
  executable,
  [...prefix, "--target-frameworks", ...frameworks, "--project-path", projectPath, "--assembly", manifest.assembly, "--generator-version", manifest.generator.version, "--working-directory", work, "--output-directory", generated],
  { stdio: "inherit", env: { ...process.env, DOTNET_ROLL_FORWARD: process.env.DOTNET_ROLL_FORWARD ?? "LatestMajor" } },
);
if (generation.status !== 0) fail(`Managed API generation failed with exit code ${generation.status}.`);

const actual = Object.fromEntries(frameworks.map((tfm) => [tfm, normalize(fs.readFileSync(path.join(generated, `CStructSharp.${tfm}.received.txt`), "utf8"))]));

/** The baseline text for one framework: a canonical text with its placeholders filled in. */
function expectedFor(entry, text = canonicalText) {
  return text
    .replaceAll("<TARGET_FRAMEWORK>", entry.targetFramework)
    .replaceAll("<FRAMEWORK_DISPLAY>", entry.frameworkDisplayName)
    .replaceAll(aotPlaceholder, entry.aotCompatible ? aotMetadataLine : "");
}

function writeDiff(expected, received, file) {
  const a = expected.replace(/\n$/, "").split("\n");
  const b = received.replace(/\n$/, "").split("\n");
  const lines = [];
  for (let index = 0; index < Math.max(a.length, b.length) && lines.length < 900; index++) {
    const left = a[index] ?? "<missing>";
    const right = b[index] ?? "<missing>";
    if (left !== right) lines.push(`line ${index + 1}`, `- ${left}`, `+ ${right}`);
  }
  fs.writeFileSync(file, lines.join("\n") + "\n");
}

if (mode === "compare") {
  const failures = [];
  for (const entry of manifest.frameworks) {
    const expected = expectedFor(entry);
    if (expected !== actual[entry.tfm]) {
      const file = path.join(run, `CStructSharp.${entry.tfm}.diff.txt`);
      writeDiff(expected, actual[entry.tfm], file);
      failures.push(`${entry.tfm} differs; inspect '${file}'.`);
    }
  }
  if (failures.length > 0) fail(`Managed API compatibility failed.\n${failures.join("\n")}\nRun 'node tools/quality/managed-api-baseline.mjs update --kind ... --rationale ... --impact ...' after reviewing the change.`);
  console.log(`Frozen managed API compatibility validation passed (baseline ${manifest.baselineId} revision ${manifest.baselineRevision}).`);
  process.exit(0);
}

// update: derive the canonical text from the net10.0 output, check that the net8.0 output differs only by the
// framework placeholders, and rewrite the manifest with fresh hashes and a new history entry.
const kind = option("--kind");
const rationale = option("--rationale");
const impact = option("--impact");
if (!["additive", "breaking", "correction"].includes(kind ?? "") || !rationale || !impact) {
  fail("update needs --kind additive|breaking|correction, --rationale, and --impact.");
}
const reference = manifest.frameworks.find((entry) => entry.tfm === "net10.0");
let canonical = actual["net10.0"]
  .replaceAll(reference.targetFramework, "<TARGET_FRAMEWORK>")
  .replaceAll(reference.frameworkDisplayName, "<FRAMEWORK_DISPLAY>")
  .replaceAll(aotMetadataLine, aotPlaceholder);
for (const entry of manifest.frameworks) {
  entry.aotCompatible = actual[entry.tfm].includes(aotMetadataLine);
  const filled = expectedFor(entry, canonical);
  if (filled !== actual[entry.tfm]) {
    const file = path.join(run, `CStructSharp.${entry.tfm}.framework-diff.txt`);
    writeDiff(filled, actual[entry.tfm], file);
    fail(`The ${entry.tfm} surface differs from net10.0 beyond the framework placeholders; inspect '${file}'.`);
  }
}
canonical = normalize(canonical);
fs.writeFileSync(canonicalPath, canonical);
const project = fs.readFileSync(projectPath, "utf8");
const versionPrefix = project.match(/<VersionPrefix>([^<]+)<\/VersionPrefix>/)?.[1] ?? manifest.packageVersion;
const exportedTypes = (canonical.match(/^ {4}public (?:sealed |static |abstract |readonly |partial )*(?:class|struct|interface|enum|record) /gm) ?? []).length;
manifest.canonical.lines = canonical.replace(/\n$/, "").split("\n").length;
manifest.canonical.normalizedSha256 = hashOf(canonical);
manifest.exportedTypes = exportedTypes;
manifest.packageVersion = versionPrefix;
for (const entry of manifest.frameworks) entry.normalizedSha256 = hashOf(expectedFor(entry, canonical));
const combined = manifest.frameworks.map((entry) => `${entry.tfm}:${entry.normalizedSha256}`).join("\n") + "\n";
manifest.baselineRevision += 1;
manifest.history.push({
  revision: manifest.baselineRevision,
  kind,
  date: new Date().toISOString().slice(0, 10),
  packageVersion: versionPrefix,
  combinedSha256: hashOf(combined),
  rationale,
  releaseImpact: impact,
});
fs.writeFileSync(manifestPath, JSON.stringify(manifest, null, 2) + "\n");
console.log(`Managed API baseline updated to revision ${manifest.baselineRevision} (${exportedTypes} exported types, ${manifest.canonical.lines} lines).`);
