import { expect, test } from "@playwright/test";
import { lessons } from "../../src/lessons";

test("the explanation panel is one column with direct failure recovery", async ({ page }) => {
  await page.goto("/#lesson=truncated");
  const panel = page.locator(".example-context");
  await expect(panel.locator("details")).toHaveCount(0);
  await expect(panel.getByRole("link")).toHaveCount(0);
  await expect(panel.locator(".example-explanation")).toContainText("Paste 02 00 06 00 00 00");
  await expect(panel).not.toContainText("Try it:");
  await expect(panel).not.toContainText("This lesson practices");
  const heading = await panel.getByRole("heading").boundingBox();
  const text = await panel.locator(".example-explanation").boundingBox();
  expect(text!.y).toBeGreaterThan(heading!.y + heading!.height);
  expect(text!.y - (heading!.y + heading!.height)).toBeLessThan(16);
  expect(text!.x).toBe(heading!.x);
  await panel.screenshot({ path: "artifacts/example-explanation-panel.png" });
});

test("status tooltips explain live settings with matching value colors", async ({ page }) => {
  await page.goto("/#lesson=header");
  await expect(page.locator(".status-badge")).toContainText("Ready", { timeout: 60_000 });
  const items = page.locator(".settings-summary .setting-item");
  await expect(items).toHaveCount(11);
  for (const item of await items.all()) {
    await item.hover();
    const tooltip = page.getByRole("tooltip");
    await expect(tooltip).toBeVisible();
    const color = await item.locator(".setting-value").evaluate((el) => getComputedStyle(el).color);
    await expect(tooltip.locator(".setting-value")).toHaveCSS("color", color);
    await expect(tooltip).not.toHaveText("");
    await page.keyboard.press("Escape");
    await expect(tooltip).toHaveCount(0);
  }
  await page.getByRole("button", { name: "Workbench settings", exact: true }).click();
  await page.getByTestId("endian-select").selectOption("big");
  await page.getByRole("button", { name: "Done", exact: true }).click();
  const order = items.filter({ has: page.locator(".setting-label", { hasText: "Order:" }) });
  await order.focus();
  await expect(page.getByRole("tooltip")).toContainText("Big endian");
  await expect(page.getByRole("tooltip")).toContainText("most significant byte comes first");
  await page.getByRole("tooltip").hover();
  await expect(page.getByRole("tooltip")).toBeVisible();
  await page.screenshot({ path: "artifacts/workbench-setting-tooltip.png" });
  await page.keyboard.press("Escape");
  await expect(page.getByRole("tooltip")).toHaveCount(0);
});

test("settings popup updates the summary, applies options, and resets with the lesson", async ({
  page,
}) => {
  await page.goto("/#lesson=header");
  await expect(page.locator(".status-badge")).toContainText("Ready", { timeout: 60_000 });
  const settings = page.getByRole("button", { name: "Workbench settings", exact: true });
  const dialog = page.getByRole("dialog", { name: "Workbench settings" });
  const summary = page.getByLabel("Current workbench settings");
  await expect(summary.getByRole("img", { name: "Enabled", exact: true })).toHaveCount(1);
  await expect(summary.getByRole("img", { name: "Disabled", exact: true })).toHaveCount(1);
  await expect(page.getByText("Read bytes (parse)", { exact: true })).toHaveCount(0);
  await expect(page.locator("#root-type")).not.toBeVisible();
  await expect(summary).toContainText(/Root:\s*header/);
  await settings.click();
  await expect(dialog).toBeVisible();
  await expect(dialog.locator("details")).toHaveCount(0);
  await expect(dialog.locator("#pointer-size")).toBeVisible();
  await expect(dialog.locator("#max-total")).toBeVisible();
  await page.getByTestId("endian-select").selectOption("big");
  await page.keyboard.press("Escape");
  await expect(dialog).not.toBeVisible();
  await expect(settings).toBeFocused();
  await expect(summary).toContainText("Big endian");
  await page.getByRole("button", { name: "Run parse", exact: true }).click();
  await expect(page.getByTestId("parsed-json").locator(".view-lines")).toContainText('"kind": 512');
  await page.getByRole("button", { name: "Reset example", exact: true }).click();
  await expect(summary).toContainText("Little endian");
  await settings.click();
  await dialog.locator("#max-total").fill("3");
  await dialog.getByRole("button", { name: "Done", exact: true }).click();
  await expect(summary).toContainText(/Total:\s*3 B/);
  await page.getByRole("button", { name: "Run parse", exact: true }).click();
  await expect(page.locator(".result-panel")).toContainText("read-budget");
  await page.getByRole("button", { name: "Reset example", exact: true }).click();
  await page.locator(".workbench").screenshot({ path: "artifacts/workbench-settings-header.png" });
  await page.setViewportSize({ width: 390, height: 844 });
  await settings.click();
  expect(await dialog.evaluate((element) => element.scrollWidth <= element.clientWidth)).toBe(true);
  await page.screenshot({ path: "artifacts/workbench-settings-mobile.png" });
  await dialog.getByRole("button", { name: "Done", exact: true }).click();
  await expect(settings).toBeFocused();
});

test("the byte-limit lesson explains rereads and recovers at the documented budget", async ({
  page,
}) => {
  await page.goto("/#lesson=limits");
  await expect(page.locator(".status-badge")).toContainText("Ready", { timeout: 60_000 });
  await expect(page.locator(".example-explanation")).toContainText("increase Total bytes to 12");
  const run = page.getByRole("button", { name: "Run parse", exact: true });
  await run.click();
  await expect(page.locator(".result-panel")).toContainText("read-budget");
  for (const budget of [6, 11, 12]) {
    await page.getByRole("button", { name: "Workbench settings", exact: true }).click();
    const dialog = page.getByRole("dialog", { name: "Workbench settings" });
    await dialog.locator("#max-total").fill(String(budget));
    await dialog.getByRole("button", { name: "Done", exact: true }).click();
    await run.click();
    if (budget < 12) {
      await expect(page.locator(".result-panel")).toContainText("read-budget");
    } else {
      await expect(page.getByTestId("parsed-json").locator(".view-lines")).toContainText(
        '"kind": 2',
      );
      await expect(page.getByTestId("parsed-json").locator(".view-lines")).toContainText(
        '"length": 6',
      );
      await expect(page.locator(".result-panel")).not.toContainText("read-budget");
    }
  }
});

test("each lesson fixes its operation and runs its starting inputs", async ({ page }) => {
  await page.goto("/");
  await expect(page.locator(".status-badge")).toContainText("Ready", { timeout: 60_000 });
  for (const lesson of lessons) {
    await page.goto(`/#lesson=${lesson.id}`);
    await expect(page.getByRole("heading", { name: lesson.title, exact: true })).toBeVisible();
    await expect(page.getByTestId("operation-select")).toHaveCount(0);
    await page.getByRole("button", { name: `Run ${lesson.operation}`, exact: true }).click();
    await expect(page.getByText("Matches the expected result."), lesson.id).toBeVisible();
  }
  await page.locator(".catalog > summary").click();
  if (!(await page.getByRole("button", { name: "All tests", exact: true }).isVisible()))
    await page.locator(".catalog > summary").click();
  await page.getByRole("button", { name: "All tests", exact: true }).click();
  await expect(page.getByTestId("operation-select")).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Run parse", exact: true })).toBeVisible();
});

test("all curated operation presets match real managed results", async ({ page }) => {
  await page.goto("/");
  await expect(page.locator(".status-badge")).toContainText("Ready", { timeout: 60_000 });
  const evidence = await page.evaluate(
    (catalog) =>
      catalog.flatMap((lesson) =>
        Object.entries(lesson.operations).map(([operation, preset]) => {
          const api = window.CStructSharpWasm!;
          if (!api.ready) throw new Error("Runtime unavailable");
          const options = {
            rootTypeName: lesson.rootType,
            ...lesson.parserOptions,
            ...lesson.options,
          };
          const base64 = btoa(
            String.fromCharCode(
              ...(lesson.binaryHex!.match(/[a-f\d]{2}/gi) ?? []).map((byte) => parseInt(byte, 16)),
            ),
          );
          const result = JSON.parse(
            operation === "parse"
              ? api.parseWithDebug(lesson.definition!, base64, options)
              : operation === "serialize"
                ? api.serializeToBase64(lesson.definition!, preset!.json!, options)
                : api.updateStreamToBase64(
                    lesson.definition!,
                    base64,
                    preset!.path!,
                    preset!.json!,
                    options,
                  ),
          );
          const actual = !result.Success
            ? { error: result.Error.Code }
            : operation === "parse"
              ? { data: JSON.parse(result.Data) }
              : {
                  hex: Array.from(atob(result.Data), (c) =>
                    c.charCodeAt(0).toString(16).padStart(2, "0"),
                  ).join(" "),
                };
          return { id: `${lesson.id}/${operation}`, actual, expected: preset!.expected };
        }),
      ),
    lessons,
  );
  for (const item of evidence) expect(item.actual, item.id).toEqual(item.expected);
});

test("a beginner can run all header actions, edit bytes, reset, and follow history", async ({
  page,
}) => {
  await page.goto("/#lesson=header");
  await expect(page.locator(".status-badge")).toContainText("Ready", { timeout: 60_000 });
  for (const operation of ["parse", "serialize", "update"]) {
    await page.goto(operation === "parse" ? "/#lesson=header" : `/#lesson=header-${operation}`);
    await expect(page.getByTestId("operation-select")).toHaveCount(0);
    await page.getByRole("button", { name: `Run ${operation}`, exact: true }).click();
    await expect(page.getByText("Matches the expected result.")).toBeVisible();
  }
  await page.goto("/#lesson=header");
  await page.getByRole("button", { name: "Run parse", exact: true }).click();
  await page.getByTestId("binary-input").locator(".vuehex-byte").first().click();
  await page.keyboard.press("0");
  await page.keyboard.press("3");
  await expect(page.getByText("Inputs changed. Run again to refresh the result.")).toBeVisible();
  await page.getByRole("button", { name: "Run parse", exact: true }).click();
  await expect(page.getByTestId("parsed-json").locator(".view-lines")).toContainText('"kind": 3');
  await page.getByRole("button", { name: "Reset example", exact: true }).click();
  await expect(page.getByTestId("binary-input").locator(".vuehex-byte").first()).toHaveText("02");
  await page.locator("#lesson-search").fill("string");
  await page.getByRole("button", { name: "Read fixed-capacity text", exact: true }).click();
  await expect(page).toHaveURL(/lesson=text/);
  await page.goBack();
  await expect(page).toHaveURL(/lesson=header/);
  await expect(page.getByRole("heading", { name: "Read a file header" })).toBeVisible();
  await page.screenshot({ path: "artifacts/onboarding-desktop.png", fullPage: true });
});

test("intentional errors can be repaired by pasting into VueHex", async ({ page }) => {
  await page.goto("/#lesson=truncated");
  await expect(page.locator(".status-badge")).toContainText("Ready", { timeout: 60_000 });
  await page.getByRole("button", { name: "Run parse", exact: true }).click();
  await expect(page.getByText("Matches the expected result.")).toBeVisible();
  await page.getByTestId("binary-input").locator(".vuehex-byte").first().click();
  await page.keyboard.press("Control+A");
  await page
    .getByTestId("binary-input")
    .locator(".vuehex")
    .evaluate((editor) => {
      const clipboardData = new DataTransfer();
      clipboardData.setData("text", "02 00 06 00 00 00");
      editor.dispatchEvent(new ClipboardEvent("paste", { clipboardData, bubbles: true }));
    });
  await page.getByRole("button", { name: "Run parse", exact: true }).click();
  await expect(page.getByText("parse completed", { exact: true })).toBeVisible();
  await page.setViewportSize({ width: 390, height: 844 });
  // Monaco resizes through ResizeObserver after the viewport change has been applied.
  await expect
    .poll(() => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth))
    .toBe(true);
  await page.screenshot({ path: "artifacts/onboarding-mobile.png", fullPage: true });
});
