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

test("clicking a parsed JSON field highlights its bytes, and clicking a highlighted byte selects it back", async ({
  page,
}) => {
  await page.getByRole("button", { name: /^Run$/ }).click();
  await expect(page.locator('[data-testid="result-json"]')).toBeVisible({ timeout: 10_000 });

  await page
    .locator('[data-testid="result-json"] .jse-key', { hasText: "bits_per_pixel" })
    .first()
    .click();
  const highlighted = page.locator('[data-testid="binary-panel-hex"] .field-highlight');
  await expect(highlighted.first()).toBeVisible();

  await highlighted.first().click();
  await expect(page.locator('[data-testid="result-json"] .jse-selected-value')).toBeVisible();
});
