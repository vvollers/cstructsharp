import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import test from "node:test";
import { runtimeConfigName, validateWasmPublication } from "./wasm-publication.mjs";

function createFixture() {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "cstructsharp-wasm-manifest-"));
  const framework = path.join(root, "_framework");
  fs.mkdirSync(framework);
  fs.writeFileSync(
    path.join(root, "main.js"),
    'import { dotnet } from "./_framework/dotnet.js";\nimport { ready } from "./bootstrap.js";\n',
  );
  fs.writeFileSync(path.join(root, "bootstrap.js"), "export const ready = true;\n");
  fs.writeFileSync(path.join(root, runtimeConfigName), '{"runtimeOptions":{}}\n');
  fs.writeFileSync(
    path.join(framework, "dotnet.boot.js"),
    'export const config = /*json-start*/{"resources":{"jsModuleRuntime":[{"name":"dotnet.runtime.js"}],"assembly":[{"name":"CStructSharp.wasm"}]}}/*json-end*/;\n',
  );
  fs.writeFileSync(path.join(framework, "dotnet.js"), "export const dotnet = {};\n");
  fs.writeFileSync(path.join(framework, "dotnet.runtime.js"), "export const runtime = {};\n");
  fs.writeFileSync(path.join(framework, "CStructSharp.wasm"), Buffer.from([0, 97, 115, 109]));
  return root;
}

test("manifest accepts only root entrypoints and boot-referenced framework assets", (context) => {
  const root = createFixture();
  context.after(() => fs.rmSync(root, { recursive: true, force: true }));

  const manifest = validateWasmPublication(root);

  assert.deepEqual(
    manifest.files.map((entry) => entry.path),
    [
      "_framework/CStructSharp.wasm",
      "_framework/dotnet.boot.js",
      "_framework/dotnet.js",
      "_framework/dotnet.runtime.js",
      "bootstrap.js",
      "CStructSharpWeb.Wasm.runtimeconfig.json",
      "main.js",
    ],
  );
});

test("manifest rejects stale and non-deployable files", (context) => {
  const root = createFixture();
  context.after(() => fs.rmSync(root, { recursive: true, force: true }));
  fs.writeFileSync(path.join(root, "CStructSharp.dll"), "stale");
  fs.writeFileSync(path.join(root, "_framework", "dotnet.js.map"), "stale");

  assert.throws(
    () => validateWasmPublication(root),
    /Unreferenced publication root file: CStructSharp\.dll[\s\S]*Unreferenced _framework boot resource: dotnet\.js\.map/,
  );
});

test("manifest rejects a missing boot resource", (context) => {
  const root = createFixture();
  context.after(() => fs.rmSync(root, { recursive: true, force: true }));
  fs.rmSync(path.join(root, "_framework", "CStructSharp.wasm"));

  assert.throws(
    () => validateWasmPublication(root),
    /Missing _framework boot resource: CStructSharp\.wasm/,
  );
});

test("manifest enforces the raw production payload budget", (context) => {
  const root = createFixture();
  context.after(() => fs.rmSync(root, { recursive: true, force: true }));

  assert.throws(
    () => validateWasmPublication(root, { maxRawBytes: 1 }),
    /above the 1-byte raw payload limit/,
  );
});
