/** Execute the authored landing example against an installed package. Used by test-npm-package.mjs. */
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { pathToFileURL } from "node:url";

/** Extract the copyable JavaScript body, decoding HTML entities without changing its API calls. */
export function extractLandingExample(html) {
  const match = html.match(/<pre id="snippet-js"><code>([\s\S]*?)<\/code><\/pre>/);
  assert.ok(match, "Landing page must contain the copyable JavaScript example");
  return match[1].replace(/<[^>]+>/g, "")
    .replaceAll("&lt;", "<").replaceAll("&gt;", ">")
    .replaceAll("&quot;", '"').replaceAll("&#39;", "'").replaceAll("&amp;", "&");
}

/** Run the exact snippet body with the shipped parse function, then exercise its malformed-input branch. */
export async function validateLandingExample(htmlPath, installedPackage) {
  const source = extractLandingExample(fs.readFileSync(htmlPath, "utf8"));
  const importLine = /^import \{ parse \} from ["']cstructsharp["'];/;
  assert.match(source, importLine, "Expected the public package import in the landing example");
  const { parse } = await import(pathToFileURL(path.join(installedPackage, "node.js")).href);
  // Obtain the async-function constructor to execute top-level await with explicit package/output bindings.
  const AsyncFunction = Object.getPrototypeOf(async function () {}).constructor;
  const execute = new AsyncFunction("parse", "console", source.replace(importLine, ""));
  const lines = [];
  // Capture the example's visible output, not a separately maintained approximation of the example.
  await execute(parse, {
    /** Captures one visible output line for comparison with the documented values. */
    log: (line) => lines.push(String(line)),
  });
  assert.deepEqual(lines, ["kind = 2", "length = 6"]);

  let expectedMessage;
  /** Keep the actual example's layout/options but supply a truncated input to its error branch. */
  async function parseTruncated(definition, bytes, options) {
    const result = await parse(definition, bytes.subarray(0, 1), options);
    assert.equal(result.success, false, "Truncated header must fail");
    expectedMessage = result.error.message;
    return result;
  }
  // Require the library's diagnostic; obsolete envelope access would instead throw a TypeError.
  await assert.rejects(() => execute(parseTruncated, console), (error) => {
    assert.equal(error.constructor, Error);
    assert.ok(expectedMessage);
    assert.equal(error.message, expectedMessage);
    return true;
  });
  console.log(`Verified exact landing example and malformed-input diagnostic: ${htmlPath}`);
}
