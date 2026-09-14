import { expect, test } from "@playwright/test";
import { readFile } from "node:fs/promises";
import { generateExample } from "../../src/generate-example";
import { lessons } from "../../src/lessons";

test("generation uses edited inputs, opens either language, and downloads the code", async ({
  page,
}) => {
  await page.goto("/#lesson=header-update");
  await expect(page.locator(".status-badge")).toContainText("Ready", { timeout: 60000 });
  await page.getByTestId("json-input").fill("7");
  await page.getByTestId("definition-input").getByRole("textbox").press("Control+A");
  await page.keyboard.insertText(
    "// Edited layout\nstruct header { uint16 kind; uint32 length; };",
  );
  await page.getByTestId("binary-input").locator(".vuehex-byte").first().click();
  await page.keyboard.press("0");
  await page.keyboard.press("a");
  await page.getByRole("button", { name: "Workbench settings", exact: true }).click();
  await page.getByTestId("endian-select").selectOption("big");
  await page.getByRole("button", { name: "Done", exact: true }).click();
  await page.getByRole("button", { name: "Generate C#", exact: true }).click();
  const modal = page.getByRole("dialog", { name: "Generated example" });
  await expect(modal).toBeVisible();
  await expect(modal.getByRole("tab", { name: "C#", exact: true })).toHaveAttribute(
    "aria-selected",
    "true",
  );
  const download = page.waitForEvent("download");
  await modal.getByRole("button", { name: "Download", exact: true }).click();
  const content = await readFile((await (await download).path())!, "utf8");
  expect(content).toContain("object? value = 7L");
  expect(content).toContain('"header.kind"');
  expect(content).toContain("00 07 06 00 00 00");
  expect(content).toContain("// Edited layout");
  expect(content).toContain('Convert.FromHexString("0a0006000000")');
  expect(content).toContain("isLittleEndian: false");
  await modal.getByRole("tab", { name: "JavaScript", exact: true }).click();
  await expect(modal.locator(".code-panel .view-lines")).toContainText("const definition");
  await page.screenshot({ path: "artifacts/generated-code-dialog.png" });
  await modal.getByRole("button", { name: "Close generated code" }).click();
  await expect(page.getByRole("button", { name: "Generate C#", exact: true })).toBeFocused();
  await page.getByRole("button", { name: "Generate JavaScript", exact: true }).click();
  await expect(modal.getByRole("tab", { name: "JavaScript", exact: true })).toHaveAttribute(
    "aria-selected",
    "true",
  );
  await page.setViewportSize({ width: 390, height: 844 });
  expect(await modal.evaluate((el) => el.scrollWidth <= el.clientWidth)).toBe(true);
});

test("generated JavaScript runs every lesson through the public WASM wrapper", async ({ page }) => {
  const publicWrapper = await readFile("../../src/CStructSharp.Wasm/cstructsharp-wasm.js", "utf8");
  const publicOperations = await readFile(
    "../../src/CStructSharp.Wasm/cstructsharp-api.js",
    "utf8",
  );
  await page.route("**/cstructsharp-api.js", (route) =>
    route.fulfill({ contentType: "text/javascript", body: publicOperations }),
  );
  await page.route("**/generated-public-api.js", (route) =>
    route.fulfill({ contentType: "text/javascript", body: publicWrapper }),
  );
  await page.goto("/");
  await expect(page.locator(".status-badge")).toContainText("Ready", { timeout: 60000 });
  for (const lesson of lessons) {
    const preset = lesson.operations[lesson.operation]!;
    const source = generateExample({
      operation: lesson.operation,
      definition: lesson.definition!,
      binaryHex: lesson.binaryHex!,
      jsonValue: preset.json ?? "{}",
      path: preset.path ?? "",
      options: { rootTypeName: lesson.rootType, ...lesson.parserOptions, ...lesson.options },
    }).javascript;
    const outcome = await page.evaluate(async (code) => {
      const api = await import(/* @vite-ignore */ `${location.origin}/generated-public-api.js`);
      const logs: unknown[] = [];
      const errors: unknown[] = [];
      const AsyncFunction = Object.getPrototypeOf(async function () {}).constructor;
      await new AsyncFunction(
        "parseWithDebug",
        "serialize",
        "update",
        "console",
        code.replace(/^import .*;$/m, ""),
      )(api.parseWithDebug, api.serialize, api.update, {
        log: (...args: unknown[]) => logs.push(args),
        error: (...args: unknown[]) => errors.push(args),
      });
      return { logs, errors };
    }, source);
    if (preset.expected.error)
      expect(outcome.errors[0], lesson.id).toContain(preset.expected.error);
    else {
      expect(outcome.errors, lesson.id).toEqual([]);
      expect(outcome.logs[0], lesson.id).toEqual([preset.expected.data ?? preset.expected.hex]);
    }
  }
});
