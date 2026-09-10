import assert from "node:assert/strict";
import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { root, npmArtifacts, npm, run } from "./npm-package-utils.mjs";
import { validateWasmPublication } from "./wasm-publication.mjs";

const info = JSON.parse(fs.readFileSync(path.join(npmArtifacts, "package-info.json"), "utf8"));
const tarball = path.join(npmArtifacts, info.filename);
assert.equal(
  `sha512-${crypto.createHash("sha512").update(fs.readFileSync(tarball)).digest("base64")}`,
  info.integrity,
);
const consumer = fs.mkdtempSync(path.join(npmArtifacts, "consumer space-"));
fs.writeFileSync(path.join(consumer, "package.json"), '{"private":true,"type":"module"}\n');
npm(
  [
    "install",
    tarball,
    "--ignore-scripts",
    "--offline",
    "--no-audit",
    "--no-fund",
    "--cache",
    path.join(root, ".npm"),
  ],
  { cwd: consumer },
);
const installed = path.join(consumer, "node_modules", "cstructsharp");
const pkg = JSON.parse(fs.readFileSync(path.join(installed, "package.json"), "utf8"));
assert.equal(pkg.name, "cstructsharp");
assert.equal(pkg.private, undefined);
assert.equal(pkg.version, info.version);
assert.equal(pkg.dependencies, undefined);
assert.equal(pkg.scripts, undefined);
assert.deepEqual(validateWasmPublication(path.join(installed, "runtime")), info.runtime);
const actualFiles = fs
  .readdirSync(installed, { recursive: true, withFileTypes: true })
  .filter((item) => item.isFile())
  .map((item) =>
    path.relative(installed, path.join(item.parentPath, item.name)).replaceAll("\\", "/"),
  )
  .sort();
assert.deepEqual(actualFiles, info.files.map((item) => item.path).sort());
const allowed = new Set([
  "package.json",
  "README.md",
  "LICENSE.txt",
  "THIRD-PARTY-NOTICES.txt",
  "index.d.ts",
  "vite.d.ts",
  "node.js",
  "browser.js",
  "cstructsharp-api.js",
  "runtime-loader.js",
  "assets.js",
  "copy-assets.js",
  "vite.js",
  "runtime-manifest.json",
  ...info.runtime.files.map((file) => `runtime/${file.path}`),
]);
assert.deepEqual(actualFiles, [...allowed].sort(), "Unexpected or missing tarball files");
assert.ok(
  fs
    .readFileSync(path.join(installed, "THIRD-PARTY-NOTICES.txt"), "utf8")
    .includes("Benjamin Hodgson"),
);
const env = { ...process.env, EXPECTED_VERSION: info.version };
fs.copyFileSync(
  new URL("./npm-consumer-check.mjs", import.meta.url),
  path.join(consumer, "check.mjs"),
);
console.log(
  run(process.execPath, [path.join(consumer, "check.mjs")], { cwd: root, env, timeout: 30000 }),
);
fs.writeFileSync(
  path.join(consumer, "check.cjs"),
  'import("cstructsharp").then(async api => console.log(await api.getVersion())).catch(error => { console.error(error); process.exitCode = 1; });',
);
assert.match(
  run(process.execPath, [path.join(consumer, "check.cjs")], { cwd: root, timeout: 30000 }),
  /CStructSharp WASM/,
);
fs.writeFileSync(
  path.join(consumer, "ssr.mjs"),
  'import assert from "node:assert/strict"; await import("cstructsharp"); const browser = await import("cstructsharp/browser"); await assert.rejects(browser.getVersion(), /requires a browser window/); if(globalThis.CStructSharpWasm) throw Error("Import initialized a global runtime"); console.log("SSR import passed");',
);
console.log(run(process.execPath, [path.join(consumer, "ssr.mjs")], { timeout: 10000 }));
fs.writeFileSync(
  path.join(consumer, "missing.mjs"),
  'import assert from "node:assert/strict"; import { getVersion } from "cstructsharp"; await assert.rejects(getVersion(), /Cannot find module|ENOENT/); console.log("Missing asset rejected without terminating the host");',
);
const boot = path.join(installed, "runtime", "_framework", "dotnet.js");
fs.renameSync(boot, `${boot}.test-backup`);
try {
  console.log(run(process.execPath, [path.join(consumer, "missing.mjs")], { timeout: 10000 }));
} finally {
  fs.renameSync(`${boot}.test-backup`, boot);
}
const assembly = path.join(installed, "runtime", "_framework", "CStructSharp.wasm");
const originalAssembly = fs.readFileSync(assembly);
fs.writeFileSync(
  path.join(consumer, "corrupt.mjs"),
  'import assert from "node:assert/strict"; import { getVersion } from "cstructsharp"; await assert.rejects(getVersion(), /integrity mismatch/); console.log("Corrupt assembly rejected without terminating the host");',
);
fs.writeFileSync(assembly, "corrupt");
try {
  console.log(run(process.execPath, [path.join(consumer, "corrupt.mjs")], { timeout: 10000 }));
} finally {
  fs.writeFileSync(assembly, originalAssembly);
}
const copied = path.join(consumer, "public", "static-runtime");
npm(["exec", "--offline", "--", "cstructsharp-copy", "--out", copied], { cwd: consumer });
assert.deepEqual(validateWasmPublication(copied), info.runtime);
assert.throws(
  () => npm(["exec", "--offline", "--", "cstructsharp-copy", "--out", copied], { cwd: consumer }),
  /already exists/,
);

// CI's cross-platform Node matrix needs no browser or frontend dependencies.
if (!process.argv.includes("--node-only")) {
  const tsc = path.join(root, "apps/workshop", "node_modules", "typescript", "bin", "tsc");
  const ts =
    'import { parseWithDebug, loadCStructSharpWasm, type Result } from "cstructsharp"; import { cstructsharp } from "cstructsharp/vite"; const result: Result<string,"parse"> = await parseWithDebug("struct x { uint8 a; };", new Uint8Array([1])); if (!result.Success) throw Error(result.Error.Message); console.log(result.Data); void cstructsharp; void loadCStructSharpWasm;';
  fs.writeFileSync(path.join(consumer, "types.mts"), ts);
  run(
    process.execPath,
    [
      tsc,
      "--strict",
      "--target",
      "es2022",
      "--module",
      "nodenext",
      "--moduleResolution",
      "nodenext",
      path.join(consumer, "types.mts"),
    ],
    { cwd: consumer },
  );
  run(process.execPath, [path.join(consumer, "types.mjs")], { timeout: 30000 });
  run(
    process.execPath,
    [
      tsc,
      "--strict",
      "--noEmit",
      "--target",
      "es2022",
      "--module",
      "esnext",
      "--moduleResolution",
      "bundler",
      path.join(consumer, "types.mts"),
    ],
    { cwd: consumer },
  );
  const { testBrowserConsumer } = await import("./test-npm-browser.mjs");
  await testBrowserConsumer(consumer, installed, info);
}
console.log(`Verified installed tarball ${info.filename} in ${consumer}`);
