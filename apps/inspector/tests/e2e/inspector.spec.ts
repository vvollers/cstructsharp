import { expect, test } from "@playwright/test";

test.beforeEach(async ({ page }) => {
  await page.goto("/");
  await expect(page.locator(".status-badge")).toContainText("Ready", { timeout: 60_000 });
});

test("loads with the first catalog example selected and ready to run", async ({ page }) => {
  await expect(page.locator('[data-testid="example-new"]')).toBeVisible();
  await expect(page.locator('[data-testid="example-bmp"]')).toHaveAttribute("aria-current", "true");
  await expect(page.getByRole("button", { name: /^Run$/ })).toBeEnabled();
});

test("every catalog example parses successfully and shows the expected root field", async ({
  page,
}) => {
  const expectations: Record<string, string> = {
    bmp: "file_header",
    wav: "fmt",
    zip: "file_name",
    png: "ihdr",
    jpg: "app0",
    "pe-exe": "dos",
    "pe-dll": "dos",
    ico: "entries",
    tar: "name",
  };

  for (const [id, expectedField] of Object.entries(expectations)) {
    await page.locator(`[data-testid="example-${id}"]`).click();
    await page.getByRole("button", { name: /^Run$/ }).click();
    await expect(
      page.locator('[data-testid="result-json"] .jse-key', { hasText: expectedField }).first(),
    ).toBeVisible({ timeout: 10_000 });
  }
});

test("PE dereferences its pointer to a real COFF header field", async ({ page }) => {
  await page.locator('[data-testid="example-pe-exe"]').click();
  await page.getByRole("button", { name: /^Run$/ }).click();
  await expect(page.locator('[data-testid="result-json"]')).toContainText("machine", {
    timeout: 10_000,
  });
});

test("pointer target fields highlight their bytes and hex clicks select the target field", async ({
  page,
}) => {
  await page.getByTestId("example-pe-exe").click();
  await page.getByRole("button", { name: /^Run$/ }).click();
  const machine = page
    .getByTestId("result-json")
    .locator(".jse-key")
    .filter({ hasText: /^machine$/ });
  await machine.click();
  const active = page.getByTestId("binary-panel-hex").locator("[data-hex-index].field-active");
  // The sample's PE header starts at 64; machine follows the four-byte signature.
  await expect(active).toHaveCount(2);
  await expect(active.nth(0)).toHaveAttribute("data-hex-index", "68");
  await expect(active.nth(1)).toHaveAttribute("data-hex-index", "69");
  await page
    .getByTestId("result-json")
    .locator(".jse-key")
    .filter({ hasText: /^e_magic$/ })
    .click();
  await page.getByTestId("binary-panel-hex").locator('[data-hex-index="68"]').click();
  await expect(active).toHaveCount(2);
  await expect(page.getByTestId("result-json").locator(".jse-selected-value")).toHaveAttribute(
    "data-path",
    "%2Froot%2Fdos%2Fe_lfanew%2FValue%2Fmachine",
  );
});

test('"New" clears the schema and binary data', async ({ page }) => {
  await page.locator('[data-testid="example-new"]').click();
  await expect(page.locator('[data-testid="definition-editor"]')).toContainText("struct root");
  await expect(page.locator(".byte-count")).toContainText("0 bytes");
});

test("schema settings dialog opens and closes", async ({ page }) => {
  await page.getByRole("button", { name: "Schema settings" }).click();
  await expect(page.getByRole("heading", { name: "Schema settings" })).toBeVisible();
  await page.getByRole("button", { name: "Close settings" }).click();
  await expect(page.getByRole("heading", { name: "Schema settings" })).toBeHidden();
});

test("loading a file replaces the binary data and shows its name", async ({ page }) => {
  const [chooser] = await Promise.all([
    page.waitForEvent("filechooser"),
    page.getByRole("button", { name: "Load file" }).click(),
  ]);
  await chooser.setFiles({
    name: "sample.bin",
    mimeType: "application/octet-stream",
    buffer: Buffer.from([0x01, 0x02, 0x03, 0x04]),
  });
  await expect(page.locator(".file-name")).toContainText("sample.bin");
  await expect(page.locator(".byte-count")).toContainText("4 bytes");
});

test("every parsed field is colorized in the hex view as soon as a parse succeeds", async ({
  page,
}) => {
  await page.getByRole("button", { name: /^Run$/ }).click();
  await expect(page.locator('[data-testid="result-json"]')).toBeVisible({ timeout: 10_000 });

  const colorized = page.locator('[data-testid="binary-panel-hex"] [class*="field-range-"]');
  await expect(colorized.first()).toBeVisible();
  expect(await colorized.count()).toBeGreaterThan(1);
});

test("clicking a parsed JSON field highlights its bytes, and clicking a highlighted byte selects it back", async ({
  page,
}) => {
  await page.getByRole("button", { name: /^Run$/ }).click();
  await expect(page.locator('[data-testid="result-json"]')).toBeVisible({ timeout: 10_000 });

  await page
    .locator('[data-testid="result-json"] .jse-key', { hasText: "bits_per_pixel" })
    .first()
    .click();
  const active = page.locator('[data-testid="binary-panel-hex"] .field-active');
  await expect(active.first()).toBeVisible();
  const dimmed = page.locator('[data-testid="binary-panel-hex"] .field-dim');
  await expect(dimmed.first()).toBeVisible();

  await active.first().click();
  await expect(page.locator('[data-testid="result-json"] .jse-selected-value')).toBeVisible();
});

test("the schema editor completes primitive types and previously declared type names", async ({
  page,
}) => {
  await page.locator('[data-testid="example-new"]').click();
  const editor = page.locator('[data-testid="definition-editor"] .monaco-editor').first();
  await editor.click();

  await page.keyboard.press("Control+End");
  await page.keyboard.press("Enter");
  await page.keyboard.type("uin");
  await expect(page.locator(".suggest-widget.visible")).toBeVisible();
  await expect(
    page.locator(".suggest-widget .monaco-list-row .label-name", { hasText: "uint32" }).first(),
  ).toBeVisible();
  await page.keyboard.press("Escape");

  await page.keyboard.press("Home");
  await page.keyboard.press("Shift+End");
  await page.keyboard.press("Delete");
  await page.keyboard.type("struct my_custom_header { uint8 flag; };");
  await page.keyboard.press("Enter");
  await page.keyboard.type("my_cus");
  await expect(
    page
      .locator(".suggest-widget .monaco-list-row .label-name", { hasText: "my_custom_header" })
      .first(),
  ).toBeVisible();
});

test("hovering a keyword or a declared type name in the schema editor shows its documentation", async ({
  page,
}) => {
  const editor = page.locator('[data-testid="definition-editor"] .monaco-editor').first();
  await editor.click();
  await page.keyboard.press("Control+F");
  await page.keyboard.type("bmp_compression compression");
  await page.keyboard.press("Escape");
  await page.keyboard.press("Home");
  await page.keyboard.press("ArrowRight");
  await page.keyboard.press("Control+K");
  await page.keyboard.press("Control+I");
  const hover = page.locator(".monaco-hover:not(.hidden) .monaco-hover-content");
  await expect(hover).toContainText("enum bmp_compression");
  await expect(hover).toContainText("Rgb, Rle8, Rle4, Bitfields");
});

test("struct-array fields select only their element's bytes in both directions", async ({
  page,
}) => {
  await page.getByTestId("example-ico").click();
  await page.getByRole("button", { name: /^Run$/ }).click();
  const result = page.getByTestId("result-json");
  const hex = page.getByTestId("binary-panel-hex");
  const active = hex.locator("[data-hex-index].field-active");
  for (const [index, offset] of [
    [0, 6],
    [1, 22],
  ]) {
    await result.locator(`[data-path="%2Froot%2Fentries%2F${index}%2Fwidth"] .jse-key`).click();
    await expect(active).toHaveCount(1);
    await expect(active).toHaveAttribute("data-hex-index", String(offset));
  }
  await hex.locator('[data-hex-index="6"]').click();
  await expect(result.locator(".jse-selected-value")).toHaveAttribute(
    "data-path",
    "%2Froot%2Fentries%2F0%2Fwidth",
  );
  await result
    .locator('[data-path="%2Froot%2Fentries"] > .jse-header-outer .jse-header')
    .first()
    .click();
  await expect(active).toHaveCount(32);
});

test("clicking a scalar array field activates every element, not just the first", async ({
  page,
}) => {
  // uint16 e_res[4] in the PE example: the managed debug output records every element under the exact
  // same un-indexed "root.dos.e_res" path (unlike a struct array's per-index paths), so this exercises a
  // distinct code path from the struct-array (ICO) and single-leaf (BMP bits_per_pixel) cases above.
  await page.locator('[data-testid="example-pe-exe"]').click();
  await page.getByRole("button", { name: /^Run$/ }).click();
  await expect(page.locator('[data-testid="result-json"]')).toBeVisible({ timeout: 10_000 });

  const eResNode = page.locator('[data-path="%2Froot%2Fdos%2Fe_res"]').first();
  await eResNode.locator("> .jse-header-outer .jse-header").first().click();

  // 4 elements x 2 bytes x 2 columns (hex + ascii) = 16 active cells; each element is one uint16 (2 bytes).
  await expect(page.locator('[data-testid="binary-panel-hex"] .field-active')).toHaveCount(16);
});
