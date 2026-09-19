import test from "node:test";
import assert from "node:assert/strict";
import crypto from "node:crypto";
import {
  awaitPublishedStatus,
  registryStatus,
  validatePackageInfo,
} from "./npm-release.mjs";

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
      Response.json({
        name: info.name,
        version: info.version,
        dist: { integrity: "different" },
      }),
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
  await assert.rejects(
    registryStatus(info, async () => new Response("bad json")),
  );
});
test("package integrity and path identity are checked before publication", () => {
  validatePackageInfo(info, bytes);
  assert.throws(() => validatePackageInfo(info, Buffer.from("tampered")));
  assert.throws(() =>
    validatePackageInfo({ ...info, filename: "../other.tgz" }, bytes),
  );
  assert.throws(() => validatePackageInfo({ ...info, name: "other" }, bytes));
});
test("a publication check waits for the registry to finish processing, but not for a different package", async () => {
  const published = Response.json({
    name: info.name,
    version: info.version,
    dist: { integrity: info.integrity },
  });
  let lookups = 0;
  const waits = [];
  const options = {
    timeoutMs: 100,
    intervalMs: 10,
    sleep: async (ms) => {
      waits.push(ms);
    },
    log: () => {},
  };
  assert.equal(
    await awaitPublishedStatus(info, {
      ...options,
      fetchImpl: async () =>
        ++lookups < 3 ? new Response("", { status: 404 }) : published,
    }),
    "identical",
  );
  assert.equal(lookups, 3);
  assert.deepEqual(waits, [10, 10]);

  let now = 0;
  const clock = { now: () => now };
  lookups = 0;
  assert.equal(
    await awaitPublishedStatus(info, {
      ...options,
      sleep: async (ms) => {
        now += ms;
      },
      fetchImpl: async () => {
        lookups++;
        return new Response("", { status: 404 });
      },
      clock,
    }),
    "missing",
  );
  assert.ok(lookups > 1 && lookups <= 12, `polled ${lookups} times`);

  await assert.rejects(
    awaitPublishedStatus(info, {
      ...options,
      fetchImpl: async () =>
        Response.json({
          name: info.name,
          version: info.version,
          dist: { integrity: "different" },
        }),
    }),
    /different integrity/,
  );
  await assert.rejects(
    awaitPublishedStatus(info, {
      ...options,
      fetchImpl: async () => new Response("", { status: 500 }),
    }),
    /lookup failed/,
  );
});
