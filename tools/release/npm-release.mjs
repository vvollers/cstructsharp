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
  assert.equal(
    info.sha256,
    crypto.createHash("sha256").update(bytes).digest("hex"),
  );
}

export async function registryStatus(info, fetchImpl = fetch) {
  const response = await fetchImpl(
    `https://registry.npmjs.org/cstructsharp/${info.version}`,
    {
      signal: AbortSignal.timeout(30000),
    },
  );
  if (response.status === 404) return "missing";
  if (!response.ok)
    throw new Error(`npm registry lookup failed: HTTP ${response.status}`);
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

/**
 * The registry can accept a publication before the version is readable through its API ("your package is being
 * processed"), so a publication check polls until the version appears: "identical" as soon as the registry
 * returns it, "missing" only after the whole window has passed without it. A different integrity or a registry
 * failure surfaces immediately - neither is a processing delay.
 */
export async function awaitPublishedStatus(
  info,
  {
    fetchImpl = fetch,
    timeoutMs = 5 * 60 * 1000,
    intervalMs = 10 * 1000,
    sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms)),
    clock = Date,
    log = console.log,
  } = {},
) {
  const deadline = clock.now() + timeoutMs;
  for (let attempt = 1; ; attempt++) {
    const status = await registryStatus(info, fetchImpl);
    if (status !== "missing" || clock.now() >= deadline) return status;
    log(
      `npm cstructsharp@${info.version} is not visible yet (attempt ${attempt}); waiting ${Math.round(intervalMs / 1000)} s for the registry to finish processing.`,
    );
    await sleep(intervalMs);
  }
}

if (
  process.argv[1] &&
  import.meta.url === pathToFileURL(process.argv[1]).href
) {
  const info = JSON.parse(
    fs.readFileSync(path.join(npmArtifacts, "package-info.json"), "utf8"),
  );
  validatePackageInfo(
    info,
    fs.readFileSync(path.join(npmArtifacts, info.filename)),
  );
  const status = process.argv.includes("--require-published")
    ? await awaitPublishedStatus(info)
    : await registryStatus(info);
  if (process.argv.includes("--require-missing") && status !== "missing")
    throw new Error(
      "This version already exists. Use release recovery for the original verified artifact.",
    );
  if (process.argv.includes("--require-published") && status !== "identical")
    throw new Error(
      "npm package is not yet published after waiting for the registry to process it.",
    );
  if (process.env.GITHUB_OUTPUT)
    fs.appendFileSync(
      process.env.GITHUB_OUTPUT,
      `status=${status}\nfilename=${info.filename}\n`,
    );
  console.log(`npm cstructsharp@${info.version}: ${status}`);
}
