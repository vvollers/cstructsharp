import { expect, test } from "@playwright/test";
import { supportedExtensions } from "file-type";

test("all detector types are listed and the filter searches extensions and names", async ({
  page,
}) => {
  await page.goto("/");
  const list = page.getByRole("navigation", { name: "Binary format examples" }).locator("ul");
  await expect(list.getByRole("button")).toHaveCount(185);
  const labels = await list.locator(".example-title").allTextContents();
  for (const extension of supportedExtensions) expect(labels).toContain(extension.toUpperCase());
  const filter = page.getByRole("searchbox", { name: "Filter file types" });
  await filter.fill(" .SQLITE ");
  await expect(list.getByRole("button")).toHaveCount(1);
  await expect(list).toContainText("SQLITE");
  await filter.fill("TIFF");
  await expect(list).toContainText("DNG");
  await filter.fill("no-such-file-type");
  await expect(page.getByText("No matching file types.")).toBeVisible();
  await page.getByRole("button", { name: "Clear file type filter" }).click();
  await expect(list.getByRole("button")).toHaveCount(185);
});

test("a catalog schema can be selected before loading a file and preserves that file on selection", async ({
  page,
}) => {
  await page.goto("/");
  await expect(page.locator(".status-badge")).toContainText("Ready");
  await page.getByRole("searchbox", { name: "Filter file types" }).fill("sqlite");
  await page.getByTestId("example-schema-sqlite").click();
  await expect(page.locator(".file-name")).toContainText("Load a file");
  const bytes = Buffer.alloc(100);
  bytes.write("SQLite format 3\0");
  bytes.writeUInt16BE(4096, 16);
  const [chooser] = await Promise.all([
    page.waitForEvent("filechooser"),
    page.getByRole("button", { name: "Load file", exact: true }).click(),
  ]);
  await chooser.setFiles({
    name: "database.sqlite",
    mimeType: "application/octet-stream",
    buffer: bytes,
  });
  await page.getByRole("button", { name: /^Run$/ }).click();
  await expect(page.locator(".result-status")).toHaveText("Parse completed");
  await expect(page.getByTestId("result-json")).toContainText("4096");
  await page.getByRole("searchbox", { name: "Filter file types" }).fill("wasm");
  await page.getByTestId("example-schema-wasm").click();
  await expect(page.locator(".file-name")).toHaveText("database.sqlite");
  await expect(page.locator(".byte-count")).toContainText("100 bytes");
});
