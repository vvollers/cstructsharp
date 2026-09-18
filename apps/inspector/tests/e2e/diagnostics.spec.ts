import { expect, test } from "@playwright/test";
import type { RawWasmAdapter } from "../../src/wasm/cstruct-contract";

test.beforeEach(async ({ page }) => {
  await page.goto("/");
  await expect(page.locator(".status-badge")).toContainText("Ready", { timeout: 60_000 });
});

test("large ZIP files retain the full source and parse", async ({ page }) => {
  await page.getByTestId("example-zip").click();
  const bytes = Buffer.alloc(4 * 1024 * 1024 + 1);
  Buffer.from(
    "504b03041400000000000000000000000000000000000000000008000000746573742e747874",
    "hex",
  ).copy(bytes);
  const [chooser] = await Promise.all([
    page.waitForEvent("filechooser"),
    page.getByRole("button", { name: "Load file" }).click(),
  ]);
  await chooser.setFiles({ name: "large.zip", mimeType: "application/zip", buffer: bytes });
  await expect(page.locator(".file-name")).toContainText("large.zip");
  await expect(page.locator(".byte-count")).toContainText("4,194,305 bytes");
  await page.getByRole("button", { name: /^Run$/ }).click();
  await expect(page.locator(".result-status")).toHaveText("Parse completed");
  await expect(page.getByTestId("result-json")).toContainText("test.txt");
  await page.getByTestId("example-bmp").click();
  await expect(page.locator(".file-name")).toContainText("Sample data");
});

test("manual loading preserves the local ZIP example and returns the library read error", async ({
  page,
}) => {
  await page.getByTestId("example-zip").click();
  const [chooser] = await Promise.all([
    page.waitForEvent("filechooser"),
    page.getByRole("button", { name: "Load file" }).click(),
  ]);
  const bytes = Buffer.alloc(22);
  bytes.writeUInt32LE(0x06054b50);
  await chooser.setFiles({ name: "empty.zip", mimeType: "application/zip", buffer: bytes });
  await expect(page.locator(".file-name")).toContainText("empty.zip");
  await page.getByRole("button", { name: /^Run$/ }).click();
  // Contract v8 carries the library's own diagnostic verbatim, including the field and its declared type.
  await expect(page.locator(".result-status")).toContainText(
    "Not enough bytes: needed 4, available 0 (field 'uncompressed_size' (uint32), in 'root', offset 22).",
  );
});

test("the real bridge preserves actionable input and read diagnostics", async ({ page }) => {
  const results = await page.evaluate(() => {
    const wasm = (window as unknown as { CStructSharpWasm: RawWasmAdapter }).CStructSharpWasm;
    const parse = (definition: string, bytes: Uint8Array, options = {}) =>
      JSON.parse(wasm.parseWithDebug(definition, bytes, options));
    return {
      large: parse("struct root { uint8 value; };", new Uint8Array(4194305)),
      empty: parse("struct root { uint8 value; };", new Uint8Array()),
      truncated: parse("struct root { uint32 value; };", new Uint8Array(1)),
      encoding: parse("struct root { utf8_string_zero value; };", new Uint8Array([0xc3, 0x28, 0])),
      budget: parse("struct root { uint8 values[2]; };", new Uint8Array(2), {
        maxArrayElements: 1,
      }),
      option: parse("struct root { uint8 value; };", new Uint8Array(1), {
        maxArrayElements: 0,
      }),
    };
  });
  expect(results.large.error.message).toContain("4194305 bytes");
  expect(results.large.error.message).toContain("4194304 bytes (4 MiB)");
  expect(results.empty.error.message).toContain("No binary data");
  expect(results.truncated.error).toMatchObject({
    code: "read-failed",
    path: "root",
    offset: 1,
    member: "value",
    memberType: "uint32",
  });
  expect(results.truncated.error.message).toBe(
    "Not enough bytes: needed 4, available 1 (field 'value' (uint32), in 'root', offset 1).",
  );
  expect(results.encoding.error.message).toBe(
    "String field contains bytes that are invalid for its encoding (field 'value' (utf8_string_zero), in 'root', offset 3).",
  );
  expect(results.budget.error.message).toContain("MaxArrayElements");
  expect(results.option.error.message).toContain("between 1 and 2147483647; received 0");
});
