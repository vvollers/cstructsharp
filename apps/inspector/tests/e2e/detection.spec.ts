import { expect, test } from "@playwright/test";
import { schemaForFile, schemaProfiles } from "../../src/detected-schemas";
import type { RawWasmAdapter } from "../../src/wasm/cstruct-contract";

test.beforeEach(async ({ page }) => {
  await page.goto("/");
  await expect(page.locator(".status-badge")).toContainText("Ready", { timeout: 60_000 });
});

test("detects content despite a misleading filename and applies the GIF palette schema", async ({
  page,
}) => {
  const gif = Buffer.from("47494638396101000100800000000000ffffff3b", "hex");
  const [chooser] = await Promise.all([
    page.waitForEvent("filechooser"),
    page.getByRole("button", { name: "Load & detect" }).click(),
  ]);
  await chooser.setFiles({ name: "picture.zip", mimeType: "application/zip", buffer: gif });
  await expect(page.locator(".detection-status")).toContainText("GIF detected");
  await expect(page.locator(".file-name")).toContainText("picture.zip");
  await page.getByRole("button", { name: /^Run$/ }).click();
  await expect(page.locator(".result-status")).toHaveText("Parse completed");
  await expect(page.getByTestId("result-json")).toContainText("global_palette");
});

test("loads an empty ZIP with its end-of-directory schema", async ({ page }) => {
  const zip = Buffer.alloc(22);
  zip.writeUInt32LE(0x06054b50);
  const [chooser] = await Promise.all([
    page.waitForEvent("filechooser"),
    page.getByRole("button", { name: "Load & detect" }).click(),
  ]);
  await chooser.setFiles({ name: "empty.zip", mimeType: "application/zip", buffer: zip });
  await expect(page.locator(".detection-status")).toContainText("ZIP detected");
  await page.getByRole("button", { name: /^Run$/ }).click();
  await expect(page.locator(".result-status")).toHaveText("Parse completed");
  await expect(page.getByTestId("result-json")).toContainText("total_entries");
});

test("unknown files get a raw schema and manual loading preserves the selected schema", async ({
  page,
}) => {
  const [chooser] = await Promise.all([
    page.waitForEvent("filechooser"),
    page.getByRole("button", { name: "Load & detect" }).click(),
  ]);
  await chooser.setFiles({
    name: "unknown.bin",
    mimeType: "application/octet-stream",
    buffer: Buffer.from([1, 2, 3]),
  });
  await expect(page.locator(".detection-status")).toContainText("not recognized");
  await page.getByRole("button", { name: /^Run$/ }).click();
  await expect(page.locator(".result-status")).toHaveText("Parse completed");
  await page.getByTestId("example-png").click();
  const [manual] = await Promise.all([
    page.waitForEvent("filechooser"),
    page.getByRole("button", { name: "Load file", exact: true }).click(),
  ]);
  await manual.setFiles({
    name: "manual.bin",
    mimeType: "application/octet-stream",
    buffer: Buffer.alloc(40),
  });
  await expect(page.getByTestId("example-png")).toHaveAttribute("aria-current", "true");
});

test("every registered schema compiles in the real WASM parser", async ({ page }) => {
  const cases = Object.keys(schemaProfiles).map((ext) => schemaForFile(ext, new Uint8Array(1024)));
  const failures = await page.evaluate((schemas) => {
    const wasm = (window as unknown as { CStructSharpWasm: RawWasmAdapter }).CStructSharpWasm;
    return schemas.flatMap((schema) => {
      const result = JSON.parse(
        wasm.parseWithDebug(schema.definition, new Uint8Array(65536), {
          ...schema.parserOptions,
          rootTypeName: "root",
          maxArrayElements: 1024,
        }),
      );
      return result.Error?.Code === "invalid-layout"
        ? [{ id: schema.id, error: result.Error }]
        : [];
    });
  }, cases);
  expect(failures).toEqual([]);
});

test("TIFF directory pointers and SQLite header fields decode actual values", async ({ page }) => {
  const tiff = Buffer.alloc(26);
  tiff.write("II");
  tiff.writeUInt16LE(42, 2);
  tiff.writeUInt32LE(8, 4);
  tiff.writeUInt16LE(1, 8);
  tiff.writeUInt16LE(256, 10);
  tiff.writeUInt16LE(4, 12);
  tiff.writeUInt32LE(1, 14);
  tiff.writeUInt32LE(640, 18);
  const sqlite = Buffer.alloc(100);
  sqlite.write("SQLite format 3\0");
  sqlite.writeUInt16BE(4096, 16);
  sqlite.writeUInt32BE(23, 28);
  for (const [ext, bytes, expected] of [
    ["tif", tiff, { value_or_offset: 640 }],
    ["sqlite", sqlite, { page_size: 4096, page_count: 23 }],
  ] as const) {
    const schema = schemaForFile(ext, bytes);
    const result = await page.evaluate(
      ({ schema, bytes }) => {
        const wasm = (window as unknown as { CStructSharpWasm: RawWasmAdapter }).CStructSharpWasm;
        return JSON.parse(
          wasm.parseWithDebug(schema.definition, new Uint8Array(bytes), {
            ...schema.parserOptions,
            rootTypeName: "root",
          }),
        );
      },
      { schema, bytes: [...bytes] },
    );
    expect(result.Success).toBe(true);
    const json = JSON.stringify(JSON.parse(result.Data));
    if (ext === "tif") expect(json).toContain('"Name":"ImageWidth","Value":256');
    for (const [key, value] of Object.entries(expected))
      expect(json).toContain(`"${key}":${value}`);
  }
});

test("selecting New schema cancels detection without overwriting the selection", async ({
  page,
}) => {
  let release!: () => void;
  let requested!: () => void;
  const gate = new Promise<void>((resolve) => {
    release = resolve;
  });
  const started = new Promise<void>((resolve) => {
    requested = resolve;
  });
  await page.route(/detection\.worker-.*\.js/, async (route) => {
    requested();
    await gate;
    await route.continue().catch(() => {});
  });
  const [chooser] = await Promise.all([
    page.waitForEvent("filechooser"),
    page.getByRole("button", { name: "Load & detect" }).click(),
  ]);
  await chooser.setFiles({
    name: "pending.gif",
    mimeType: "image/gif",
    buffer: Buffer.from("GIF89a"),
  });
  await started;
  await page.getByTestId("example-new").click();
  release();
  await expect(page.getByTestId("example-new")).toHaveAttribute("aria-current", "true");
  await expect(page.locator(".file-name")).toContainText("New schema");
  await expect(page.getByRole("button", { name: "Load & detect" })).toBeVisible();
});
