import { readFile, readdir } from "node:fs/promises";
import path from "node:path";
import { env } from "node:process";
import { expect, test } from "@playwright/test";
import { fileTypeFromBuffer } from "file-type";
import { schemaForFile } from "../../src/detected-schemas";
import type { RawWasmAdapter } from "../../src/wasm/cstruct-contract";

// Optional compatibility audit against a local copy of file-type's upstream fixtures.
// No network access or third-party fixture redistribution is needed for normal CI.
test("upstream detection fixtures compile with their selected schemas", async ({
  page,
}, testInfo) => {
  const directory = env.CSTRUCT_FILE_TYPE_FIXTURES;
  test.skip(!directory, "Set CSTRUCT_FILE_TYPE_FIXTURES to an upstream fixture directory");
  test.setTimeout(120_000);
  await page.goto("/");
  await expect(page.locator(".status-badge")).toContainText("Ready");
  const results: { name: string; ext?: string; success?: boolean; error?: unknown }[] = [];
  for (const name of await readdir(directory!)) {
    const bytes = await readFile(path.join(directory!, name));
    let type;
    try {
      type = await fileTypeFromBuffer(bytes);
    } catch {
      continue;
    }
    if (!type) continue;
    const schema = schemaForFile(type.ext, bytes.subarray(0, 65536));
    const result = await page.evaluate(
      ({ schema, bytes }) => {
        const wasm = (window as unknown as { CStructSharpWasm: RawWasmAdapter }).CStructSharpWasm;
        return JSON.parse(
          wasm.parseWithDebug(schema.definition, new Uint8Array(bytes), {
            ...schema.parserOptions,
            rootTypeName: "root",
            maxArrayElements: 100000,
          }),
        );
      },
      { schema, bytes: [...bytes] },
    );
    results.push({ name, ext: type.ext, success: result.Success, error: result.Error });
    expect
      .soft(result.Error?.Code, `${name}: ${JSON.stringify(result.Error)}`)
      .not.toBe("invalid-layout");
  }
  await testInfo.attach("fixture-results", {
    body: JSON.stringify(results, null, 2),
    contentType: "application/json",
  });
  expect(results.length).toBeGreaterThan(0);
});
