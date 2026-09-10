import test from "node:test";
import assert from "node:assert/strict";
import crypto from "node:crypto";
import { registryStatus, validatePackageInfo } from "./npm-release.mjs";

const bytes = Buffer.from("verified tarball");
const info = {
  name: "cstructsharp",
  version: "1.2.3",
  filename: "cstructsharp-1.2.3.tgz",
  integrity: `sha512-${crypto.createHash("sha512").update(bytes).digest("base64")}`,
  sha256: crypto.createHash("sha256").update(bytes).digest("hex"),
};
test("recovery skips only identical immutable npm versions", async () => {
  assert.equal(
    await registryStatus(info, async () => new Response("", { status: 404 })),
    "missing",
  );
  assert.equal(
    await registryStatus(info, async () =>
      Response.json({
        name: info.name,
        version: info.version,
        dist: { integrity: info.integrity },
      }),
    ),
    "identical",
  );
  await assert.rejects(
    registryStatus(info, async () =>
      Response.json({ name: info.name, version: info.version, dist: { integrity: "different" } }),
    ),
    /different integrity/,
  );
});
test("authentication, server, malformed responses and network failures never mean missing", async () => {
  for (const status of [401, 403, 429, 500])
    await assert.rejects(
      registryStatus(info, async () => new Response("", { status })),
      /lookup failed/,
    );
  await assert.rejects(
    registryStatus(info, async () => {
      throw Error("network unavailable");
    }),
    /network unavailable/,
  );
  await assert.rejects(registryStatus(info, async () => new Response("bad json")));
});
test("package integrity and path identity are checked before publication", () => {
  validatePackageInfo(info, bytes);
  assert.throws(() => validatePackageInfo(info, Buffer.from("tampered")));
  assert.throws(() => validatePackageInfo({ ...info, filename: "../other.tgz" }, bytes));
  assert.throws(() => validatePackageInfo({ ...info, name: "other" }, bytes));
});
