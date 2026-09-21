import { expect, test } from "@playwright/test";

// Narrow screens receive actionable guidance instead of an apparently usable but clipped workspace.
test("narrow windows show desktop guidance and preserve the session on resize", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await page.goto("/");
  await expect(page.locator(".status-badge")).toContainText("Ready", { timeout: 60_000 });
  await expect(page.getByTestId("temporary-edits-notice")).toContainText("Edits are temporary");
  await expect(page.getByTestId("temporary-edits-notice")).toContainText(
    "downloading edited files is not available",
  );
  await page.getByRole("button", { name: /^Run$/ }).click();
  await expect(page.getByTestId("result-json")).toBeVisible();

  for (const width of [390, 768, 1199]) {
    await page.setViewportSize({ width, height: 900 });
    await expect(page.getByTestId("minimum-width-notice")).toBeVisible();
    await expect(page.getByTestId("minimum-width-notice")).toContainText("1200 CSS pixels");
    await expect(page.getByRole("link", { name: "Read the documentation" })).toBeVisible();
    await expect(page.locator(".workspace")).toBeHidden();
    await expect(page.locator(".top-bar")).toBeHidden();
    // The guidance itself fits the viewport; hidden docks must not cause horizontal page overflow.
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(
      width,
    );
  }

  await page.setViewportSize({ width: 1200, height: 900 });
  await expect(page.getByTestId("minimum-width-notice")).toBeHidden();
  await expect(page.getByTestId("result-json")).toBeVisible();
  await expect(page.getByTestId("example-bmp")).toHaveAttribute("aria-current", "true");
});
