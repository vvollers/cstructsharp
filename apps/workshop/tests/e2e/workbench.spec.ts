import { expect, test } from "@playwright/test";
import { formatLayout } from "../../src/format-layout";
import webBudgetPolicy from "../../../../contracts/performance/web-rc1.json" with { type: "json" };
import testManifest from "../../src/generated/test-demos.json" with { type: "json" };

test.beforeEach(async ({ page }) => {
  await page.goto("/");
  await expect(page.locator(".status-badge")).toContainText("Ready", {
    timeout: 60_000,
  });
});

test("parsed JSON highlights values and hexadecimal comments and stays read-only", async ({
  page,
}) => {
  await page.getByRole("button", { name: "Run parse", exact: true }).click();
  const output = page.getByTestId("parsed-json");
  const lines = output.locator(".view-lines");
  await expect(lines).toContainText("// 0x");
  await expect(async () => {
    const colors = await output
      .locator(".view-line span[class^='mtk']")
      .evaluateAll((elements) => new Set(elements.map((el) => getComputedStyle(el).color)).size);
    expect(colors).toBeGreaterThan(2);
  }).toPass();
  const original = await lines.textContent();
  await output.getByRole("textbox", { name: "Parsed JSON", exact: true }).press("Control+A");
  await page.keyboard.insertText("replacement");
  await expect(lines).toHaveText(original!);
  await expect(output.locator(".squiggly-error")).toHaveCount(0);
});

test("Monaco highlights C, starts formatted, and supports formatting and reset", async ({
  page,
}) => {
  const source = page.getByTestId("definition-input");
  const lines = source.locator(".view-lines .view-line");
  await expect(lines).toHaveCount(4);
  await expect(lines.nth(1)).toContainText(/\s{4}uint16/);
  await expect
    .poll(async () =>
      source
        .locator(".view-line span[class^='mtk']")
        .evaluateAll((elements) => new Set(elements.map((el) => getComputedStyle(el).color)).size),
    )
    .toBeGreaterThan(1);
  await source.getByRole("textbox").press("Control+A");
  await page.keyboard.insertText("struct header { uint16 kind; uint32 length; };");
  await source.getByRole("textbox").press("Shift+Alt+F");
  await expect(lines).toHaveCount(4);
  await page.getByRole("button", { name: "Run parse", exact: true }).click();
  await expect(page.getByText("Matches the expected result.")).toBeVisible();
  await page.getByRole("button", { name: "Reset example", exact: true }).click();
  await expect(lines).toHaveCount(4);
  await source.screenshot({ path: "artifacts/monaco-layout-editor.png" });
});

test("the editable workbench parses, serializes, and updates without reloading", async ({
  page,
}) => {
  const definition = "struct root { byte value; };";
  await page.getByTestId("definition-input").getByRole("textbox").press("Control+A");
  await page.keyboard.insertText(definition);
  const binaryEditor = page.getByTestId("binary-input");
  await binaryEditor.locator(".vuehex-byte").first().click();
  await page.keyboard.press("2");
  await page.keyboard.press("a");
  await page.getByRole("button", { name: "Workbench settings", exact: true }).click();
  await page.locator("#root-type").fill("root");
  await page.getByRole("button", { name: "Done", exact: true }).click();
  await page.getByRole("button", { name: "Run parse" }).click();
  await expect(page.getByText("parse completed")).toBeVisible();
  await expect(page.getByTestId("parsed-json").locator(".view-lines")).toContainText('"value": 42');

  await page.goto("/#lesson=header-serialize");
  await page.getByTestId("definition-input").getByRole("textbox").press("Control+A");
  await page.keyboard.insertText(definition);
  await page.getByRole("button", { name: "Workbench settings", exact: true }).click();
  await page.locator("#root-type").fill("root");
  await page.getByRole("button", { name: "Done", exact: true }).click();
  await page.getByTestId("json-input").fill('{"value":165}');
  await page.getByRole("button", { name: "Run serialize" }).click();
  await expect(page.getByText("serialize completed")).toBeVisible();
  await expect(page.getByTestId("parsed-json").locator(".view-lines")).toHaveCount(0);
  await expect(page.getByTestId("binary-editor").locator(".vuehex-byte").first()).toHaveText("a5");

  await page.goto("/#lesson=header-update");
  await page.getByTestId("definition-input").getByRole("textbox").press("Control+A");
  await page.keyboard.insertText(definition);
  await page.getByRole("button", { name: "Workbench settings", exact: true }).click();
  await page.locator("#root-type").fill("root");
  await page.getByRole("button", { name: "Done", exact: true }).click();
  await binaryEditor.locator(".vuehex-byte").first().click();
  await page.keyboard.press("Control+A");
  await page.keyboard.press("0");
  await page.keyboard.press("0");
  await page.getByTestId("path-input").fill("root.value");
  await page.getByTestId("json-input").fill("42");
  await page.getByRole("button", { name: "Run update" }).click();
  await expect(page.getByText("update completed")).toBeVisible();
  await expect(page.getByTestId("parsed-json").locator(".view-lines")).toHaveCount(0);
  await expect(page.getByTestId("binary-editor").locator(".vuehex-byte").first()).toHaveText("2a");
});

test("the optimized local production build reaches managed readiness within its startup budget", async ({
  page,
}) => {
  const elapsed = await page.evaluate(() => performance.now());
  const managedResources = await page.evaluate(
    () =>
      performance
        .getEntriesByType("resource")
        .map((entry) => entry.name)
        .filter((name) => name.includes("/wasm/")).length,
  );

  console.log(
    `Browser startup evidence: managed Ready at ${elapsed.toFixed(2)} ms with ${managedResources} WASM resources.`,
  );
  expect(elapsed).toBeLessThan(webBudgetPolicy.maximums.startupReadyMilliseconds);
  expect(managedResources).toBeGreaterThan(0);
});

test("the explorer displays source explanations for runnable and parameterized tests", async ({
  page,
}) => {
  await page.goto("/#test=PathAccess.ParseStream_Path_StringInNestedArray_IsExpected_V2");
  const sourceLink = page.getByRole("link", { name: "View source on GitHub" });
  const runnableSource = testManifest.tests.find(
    (entry) => entry.id === "PathAccess.ParseStream_Path_StringInNestedArray_IsExpected_V2",
  )!;
  await expect(sourceLink).toHaveAttribute("href", runnableSource.sourceUrl);
  await expect(sourceLink).toHaveAttribute("target", "_blank");
  const explanation = page.locator(".example-explanation");
  await expect(explanation).toContainText(
    "Each inner record contains exactly four character bytes.",
  );
  await expect(explanation).toContainText("retained zero character and still has length 4");
  await page.getByRole("button", { name: "Run parse", exact: true }).click();
  await expect(page.getByText("parse completed")).toBeVisible();
  await page.goto("/#test=WriteBudgetTests.TerminatedStrings_EnforceExactEncodedByteBudget");
  await expect(page.locator(".example-context h2")).toHaveText(
    "Terminated strings enforce exact encoded byte budget",
  );
  await expect(explanation).toContainText("UTF-8 é also needs three");
  await expect(explanation).not.toContainText("parameterized test uses data rows");
  await expect(explanation).not.toContainText("available as a reference");
  const parameterizedSource = testManifest.tests.find(
    (entry) => entry.id === "WriteBudgetTests.TerminatedStrings_EnforceExactEncodedByteBudget",
  )!;
  await expect(sourceLink).toHaveAttribute("href", parameterizedSource.sourceUrl);
  await expect(page.getByRole("button", { name: "Run parse", exact: true })).toHaveCount(0);
});

test("every runnable generated demo parses through the WebAssembly bridge", async ({ page }) => {
  const runnableTests = testManifest.tests.filter(
    (
      entry,
    ): entry is (typeof testManifest.tests)[number] & {
      binaryHex: string;
      definition: string;
    } => entry.runnable && "binaryHex" in entry && "definition" in entry,
  );

  const failures = await page.evaluate(
    (entries) => {
      return entries.flatMap((entry) => {
        const compactHex = entry.binaryHex.replace(/\s/g, "");
        const binary = Uint8Array.from(
          compactHex.match(/.{2}/g)?.map((pair) => Number.parseInt(pair, 16)) ?? [],
        );
        const result = JSON.parse(
          window.CStructSharpWasm!.parseWithDebug(entry.definition, binary, {
            rootTypeName: entry.rootType,
            aligned: entry.parserOptions.aligned,
            littleEndian: entry.parserOptions.littleEndian,
            pointerSize: entry.parserOptions.pointerSize,
          }),
        ) as { Error: { Message: string } | null; Success: boolean };

        return result.Success ? [] : [`${entry.id}: ${result.Error?.Message ?? "unknown error"}`];
      });
    },
    runnableTests.flatMap((entry) => [
      entry,
      { ...entry, id: `${entry.id} (formatted)`, definition: formatLayout(entry.definition) },
    ]),
  );

  expect(failures).toEqual([]);
});
