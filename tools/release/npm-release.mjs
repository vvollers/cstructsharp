import assert from "node:assert/strict";
import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { pathToFileURL } from "node:url";
import { npmArtifacts } from "../packaging/npm-package-utils.mjs";

export function validatePackageInfo(info, bytes) {
  assert.equal(info.name, "cstructsharp");
  assert.match(info.version, /^\d+\.\d+\.\d+$/);
  assert.equal(info.filename, `cstructsharp-${info.version}.tgz`);
  assert.equal(
    info.integrity,
    `sha512-${crypto.createHash("sha512").update(bytes).digest("base64")}`,
  );
  assert.equal(info.sha256, crypto.createHash("sha256").update(bytes).digest("hex"));
}

export async function registryStatus(info, fetchImpl = fetch) {
  const response = await fetchImpl(`https://registry.npmjs.org/cstructsharp/${info.version}`, {
    signal: AbortSignal.timeout(30000),
  });
  if (response.status === 404) return "missing";
  if (!response.ok) throw new Error(`npm registry lookup failed: HTTP ${response.status}`);
  const registered = await response.json();
  assert.equal(registered.name, info.name);
  assert.equal(registered.version, info.version);
  assert.equal(
    registered.dist?.integrity,
    info.integrity,
    "npm version already exists with different integrity; it cannot be overwritten.",
  );
  return "identical";
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const info = JSON.parse(fs.readFileSync(path.join(npmArtifacts, "package-info.json"), "utf8"));
  validatePackageInfo(info, fs.readFileSync(path.join(npmArtifacts, info.filename)));
  const status = await registryStatus(info);
  if (process.argv.includes("--require-missing") && status !== "missing")
    throw new Error(
      "This version already exists. Use release recovery for the original verified artifact.",
    );
  if (process.argv.includes("--require-published") && status !== "identical")
    throw new Error("npm package is not yet published.");
  if (process.env.GITHUB_OUTPUT)
    fs.appendFileSync(process.env.GITHUB_OUTPUT, `status=${status}\nfilename=${info.filename}\n`);
  console.log(`npm cstructsharp@${info.version}: ${status}`);
}
