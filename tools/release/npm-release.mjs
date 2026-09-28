/**
 * Checks the npm package against the registry before and after publication. It validates
 * artifacts/npm/package-info.json against the packed tarball, then asks registry.npmjs.org whether that version is
 * missing or already published with the same integrity; it prints the status and, when GITHUB_OUTPUT is set, appends
 * `status` and `filename` to it.
 *
 *   node tools/release/npm-release.mjs [--require-missing | --require-published]
 *
 * `--require-missing` fails when the version exists; `--require-published` waits up to five minutes for the version to
 * appear and fails when it does not. release-artifacts.mjs and the release tests import `validatePackageInfo`,
 * `registryStatus` and `awaitPublishedStatus`.
 */
import assert from "node:assert/strict";
import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { pathToFileURL } from "node:url";
import { npmArtifacts } from "../packaging/npm-package-utils.mjs";
import { parseArguments } from "../lib/tooling.mjs";

/**
 * Asserts that package information describes the tarball: package name, stable version, file name, and SHA-512
 * integrity and SHA-256 hash of the bytes.
 * @param {object} info The parsed package-info.json.
 * @param {Buffer} bytes The tarball.
 * @throws {assert.AssertionError} When any field does not match.
 */
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

/**
 * Looks up the package version on the npm registry (30-second timeout).
 * @param {object} info The package information.
 * @param {typeof fetch} [fetchImpl] The fetch function; tests supply a fake.
 * @returns {Promise<"missing" | "identical">} `missing` for HTTP 404, `identical` when the registered version has the
 *   same integrity.
 * @throws {Error} When the lookup fails or the registered version differs; neither case counts as missing.
 */
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
  const options = parseArguments(
    process.argv.slice(2),
    { "require-missing": "flag", "require-published": "flag" },
    { defaults: { "require-missing": false, "require-published": false } },
  );
  const info = JSON.parse(
    fs.readFileSync(path.join(npmArtifacts, "package-info.json"), "utf8"),
  );
  validatePackageInfo(
    info,
    fs.readFileSync(path.join(npmArtifacts, info.filename)),
  );
  const status = options["require-published"]
    ? await awaitPublishedStatus(info)
    : await registryStatus(info);
  if (options["require-missing"] && status !== "missing")
    throw new Error(
      "This version already exists. Use release recovery for the original verified artifact.",
    );
  if (options["require-published"] && status !== "identical")
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
