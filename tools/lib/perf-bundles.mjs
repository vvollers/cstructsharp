/** Builds and fingerprints immutable development benchmark bundles. Used by tools/quality/perf-check.mjs. */
import fs from "node:fs";
import path from "node:path";
import crypto from "node:crypto";
import { spawnSync } from "node:child_process";

/** Runs a read-only Git query in the selected checkout; missing Git state is an error. */
function git(checkout, args) {
  const result = spawnSync("git", ["-C", checkout, ...args], {
    encoding: "utf8",
    windowsHide: true,
  });
  if (result.status !== 0) throw result.error ?? new Error(result.stderr || "Git query failed.");
  return result.stdout;
}

/** Hashes bytes with SHA-256; hashes identify content, not elapsed benchmark results. */
function hash(bytes) {
  return crypto.createHash("sha256").update(bytes).digest("hex");
}

/** Lists files recursively in stable order and rejects links that could escape an immutable bundle. */
export function inventory(directory, prefix = "") {
  const files = [];
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    const name = `${prefix}${entry.name}`;
    if (entry.isSymbolicLink()) throw new Error(`Bundle inputs cannot contain links: ${name}`);
    if (entry.isDirectory()) files.push(...inventory(path.join(directory, entry.name), `${name}/`));
    else if (entry.isFile()) files.push(name);
  }
  return files.sort();
}

/** Records all tracked/untracked inputs owned by the benchmark's projects, fixtures, SDK and shared build tooling. */
export function sourceIdentity(checkout, sdk) {
  const listed = git(checkout, ["ls-files", "-z", "--cached", "--others", "--exclude-standard"]);
  // Include build configuration at the root and the entire dependency source trees, not file timestamps.
  const names = [...new Set(listed.split("\0"))]
    .filter(
      (name) =>
        name &&
        (/^(src\/|tools\/build\/|benchmarks\/(CStructSharp\.(Benchmarks|FixtureTool|Comparison)\/|fixtures\/))/.test(
          name,
        ) ||
          (!name.includes("/") &&
            (/\.(props|targets|json|config|rsp)$/i.test(name) || name === ".editorconfig"))),
    )
    .sort();
  // A tracked deletion changes the digest too; it must not silently reuse a build of the deleted file.
  const files = names.map((name) => ({
    name,
    sha256: fs.existsSync(path.join(checkout, name))
      ? hash(fs.readFileSync(path.join(checkout, name)))
      : null,
  }));
  return {
    sdk,
    revision: git(checkout, ["rev-parse", "HEAD"]).trim(),
    dirty: git(checkout, ["status", "--porcelain"]).trim().length > 0,
    digest: hash(JSON.stringify({ sdk, files })),
    files,
  };
}

/** Validates exact bundle contents, detecting replaced binaries, fixtures, missing files and added runtime inputs. */
export function validateBundle(directory) {
  const manifest = JSON.parse(fs.readFileSync(path.join(directory, "bundle.json"), "utf8"));
  if (
    manifest.schemaVersion !== 1 ||
    !Array.isArray(manifest.files) ||
    !manifest.source?.digest ||
    manifest.fixtureVerification !== "passed"
  )
    throw new Error(`Invalid or unverified bundle: ${directory}`);
  const names = inventory(directory);
  // bundle.json describes the payload but is not recursively hashed into itself.
  const payload = names.filter((name) => name !== "bundle.json");
  // Reject path traversal and duplicates before reading any manifest-supplied path.
  const expected = manifest.files.map((file) => file.name);
  if (
    expected.some(
      (name) =>
        typeof name !== "string" ||
        path.isAbsolute(name) ||
        name.split(/[\\/]/).some((part) => part === ".."),
    ) ||
    JSON.stringify(payload) !== JSON.stringify([...expected].sort())
  )
    throw new Error(`Bundle file list changed: ${directory}`);
  for (const file of manifest.files) {
    if (hash(fs.readFileSync(path.join(directory, file.name))) !== file.sha256)
      throw new Error(`Bundle content changed: ${file.name}`);
  }
  if (
    !payload.includes("host/CStructSharp.Benchmarks.dll") ||
    !payload.includes("fixtures/manifest.json")
  )
    throw new Error("Bundle lacks its host or fixtures.");
  return manifest;
}

/** Requires equal fixture documents and bytes so both revisions perform equivalent benchmark work. */
export function validateFixtures(before, after) {
  // Runtime binaries may differ; fixture contents and paths must match exactly.
  const a = before.files.filter((file) => file.name.startsWith("fixtures/"));
  // Apply the identical fixture selection to the candidate bundle.
  const b = after.files.filter((file) => file.name.startsWith("fixtures/"));
  if (JSON.stringify(a) !== JSON.stringify(b))
    throw new Error(
      "Fixture inputs differ between bundles; compare with identical fixture documents and bytes.",
    );
}

/** Rebuilds the benchmark project and snapshots binaries/fixtures; fails on concurrent source edits or an existing destination. */
export function buildBundle(checkout, destination, source, logPath) {
  if (fs.existsSync(destination))
    throw new Error(`Bundle already exists; choose a new capture name: ${destination}`);
  const fd = fs.openSync(logPath, "wx");
  const result = spawnSync(
    "dotnet",
    [
      "build",
      "benchmarks/CStructSharp.Benchmarks/CStructSharp.Benchmarks.csproj",
      "-c",
      "Release",
      "-f",
      "net10.0",
      "-t:Rebuild",
    ],
    { cwd: checkout, stdio: ["ignore", fd, fd], windowsHide: true },
  );
  fs.closeSync(fd);
  if (result.status !== 0)
    throw result.error ?? new Error(`Benchmark build failed; see ${logPath}`);
  if (sourceIdentity(checkout, source.sdk).digest !== source.digest)
    throw new Error("Sources changed during the build; run again after editing finishes.");
  fs.mkdirSync(destination, { recursive: true });
  fs.cpSync(
    path.join(checkout, "benchmarks/CStructSharp.Benchmarks/bin/Release/net10.0"),
    path.join(destination, "host"),
    { recursive: true, errorOnExist: true, force: false },
  );
  fs.cpSync(path.join(checkout, "benchmarks/fixtures"), path.join(destination, "fixtures"), {
    recursive: true,
    errorOnExist: true,
    force: false,
  });
  const verification = spawnSync(
    "dotnet",
    [
      path.join(destination, "host/CStructSharp.FixtureTool.dll"),
      "verify",
      path.join(destination, "fixtures"),
    ],
    { encoding: "utf8", windowsHide: true },
  );
  fs.writeFileSync(
    `${logPath}.fixtures.log`,
    `${verification.stdout ?? ""}${verification.stderr ?? ""}`,
  );
  if (verification.status !== 0)
    throw (
      verification.error ?? new Error(`Fixture verification failed; see ${logPath}.fixtures.log`)
    );
  // Hash every copied asset, including fixture bytes and nested dependency resources.
  const files = inventory(destination).map((name) => ({
    name,
    sha256: hash(fs.readFileSync(path.join(destination, name))),
  }));
  if (sourceIdentity(checkout, source.sdk).digest !== source.digest)
    throw new Error(
      "Sources changed while snapshotting the build; discard this incomplete capture and retry.",
    );
  const manifest = {
    schemaVersion: 1,
    createdUtc: new Date().toISOString(),
    checkout,
    source,
    fixtureVerification: "passed",
    files,
  };
  fs.writeFileSync(
    path.join(destination, "bundle.json"),
    JSON.stringify(manifest, null, 2) + "\n",
    { flag: "wx" },
  );
  validateBundle(destination);
  return manifest;
}
