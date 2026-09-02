import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";

if (process.argv.length !== 4) {
  throw new Error("Usage: node compare-wasm-manifests.mjs <first manifest> <second manifest>");
}

const [firstPath, secondPath] = process.argv.slice(2).map((value) => path.resolve(value));
const first = JSON.parse(fs.readFileSync(firstPath, "utf8"));
const second = JSON.parse(fs.readFileSync(secondPath, "utf8"));
assert.deepEqual(
  second,
  first,
  `WASM publications differ between '${firstPath}' and '${secondPath}'.`,
);
console.log(
  `Cross-platform WASM manifests match exactly (${first.totals.files} files, ${first.totals.bytes} bytes).`,
);
