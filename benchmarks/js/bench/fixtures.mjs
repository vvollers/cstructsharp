// Fixture access shared by Node scripts (file system) and mirrored by the browser page (fetch).
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

export const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../..");
export const fixtureRoot = path.join(repositoryRoot, "benchmarks/fixtures");

/** Same generator as benchmarks/fixtures/generate-fixtures.mjs and CStructSharp.FixtureTool.FixtureLoader. */
export function xorshiftBytes(seed, size) {
  let x = seed >>> 0 || 1;
  const bytes = new Uint8Array(size);
  for (let i = 0; i < size; i++) {
    x ^= x << 13; x >>>= 0;
    x ^= x >>> 17;
    x ^= x << 5; x >>>= 0;
    bytes[i] = x & 0xff;
  }
  return bytes;
}

export function materializeBytes(document, readFile) {
  const spec = document.bytes;
  if (!spec) return new Uint8Array(0);
  if (spec.kind === "hex") return Uint8Array.from(Buffer.from(spec.hex, "hex"));
  if (spec.kind === "file") return readFile(spec.file);
  if (spec.kind === "xorshift") return xorshiftBytes(spec.seed, spec.size);
  throw new Error(`Unknown byte generator ${spec.kind}`);
}

const cache = new Map();
export function loadFixture(id) {
  if (cache.has(id)) return cache.get(id);
  const document = JSON.parse(fs.readFileSync(path.join(fixtureRoot, "cases", `${id}.json`), "utf8"));
  const bytes = materializeBytes(document, (file) => new Uint8Array(fs.readFileSync(path.join(fixtureRoot, file))));
  const fixture = { document, bytes };
  cache.set(id, fixture);
  return fixture;
}

export function manifest() {
  return JSON.parse(fs.readFileSync(path.join(fixtureRoot, "manifest.json"), "utf8"));
}
