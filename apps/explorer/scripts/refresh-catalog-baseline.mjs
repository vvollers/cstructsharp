// Rewrites tests/catalog-baseline.json from the current generated test catalog. Run it after adding, removing, or
// renaming managed tests that the explorer extracts; the baseline test then guards the catalog against accidental
// loss until the next deliberate refresh.
import { createHash } from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(scriptDirectory, "..");
const generator = path.join(scriptDirectory, "generate-test-demos.mjs");
const outputPath = path.join(webRoot, "src", "generated", "test-demos.json");
const baselinePath = path.join(webRoot, "tests", "catalog-baseline.json");

const result = spawnSync(process.execPath, [generator], { cwd: webRoot, stdio: "inherit" });
if (result.status !== 0) {
  process.exit(result.status ?? 1);
}
const manifest = JSON.parse(fs.readFileSync(outputPath, "utf8"));
const fields = ["id", "runnable", "definition", "binaryHex", "rootType", "parserOptions", "reason"];
const inputs = manifest.tests.map((entry) =>
  Object.fromEntries(
    fields.filter((key) => Object.hasOwn(entry, key)).map((key) => [key, entry[key]]),
  ),
);
const baseline = JSON.parse(fs.readFileSync(baselinePath, "utf8"));
baseline.totalTests = manifest.totalTests;
baseline.runnableTests = manifest.runnableTests;
baseline.testInputsSha256 = createHash("sha256").update(JSON.stringify(inputs)).digest("hex");
fs.writeFileSync(baselinePath, `${JSON.stringify(baseline, null, 2)}\n`);
console.log(`Catalog baseline: ${baseline.totalTests} tests, ${baseline.runnableTests} runnable.`);
