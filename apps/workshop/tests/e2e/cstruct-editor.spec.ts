import { expect, test } from "@playwright/test";

test("the layout editor offers the binary metadata types", async ({ page }) => {
  await page.goto("/#lesson=integers-24");
  await expect(page.locator(".status-badge")).toContainText("Ready");
  const editor = page.getByTestId("definition-input").getByRole("textbox");
  for (const [prefix, type] of [
    ["uleb128_", "uleb128_64"],
    ["utf16", "utf16le"],
    ["fixed2", "fixed2_30"],
    ["gui", "guid"],
    ["uint2", "uint24"],
    ["cp4", "cp437"],
  ]) {
    await editor.press("Control+A");
    await page.keyboard.insertText(`struct root { ${prefix}`);
    await editor.press("Control+Space");
    await expect(page.locator(".suggest-widget.visible")).toBeVisible();
    await expect(
      page
        .locator(".suggest-widget .label-name")
        .filter({ hasText: new RegExp(`^${type}$`) })
        .first(),
    ).toBeVisible();
    await editor.press("Escape");
  }
});
