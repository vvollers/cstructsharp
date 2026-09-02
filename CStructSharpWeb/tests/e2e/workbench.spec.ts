import { expect, test } from "@playwright/test";
import webBudgetPolicy from "../../../CStructSharp.Docs/contracts/performance/web-rc1.json" with { type: "json" };
import testManifest from "../../src/generated/test-demos.json" with { type: "json" };

test.beforeEach(async ({ page }) => {
  await page.goto("/");
  await expect(page.locator(".status-badge")).toContainText("Ready", {
    timeout: 60_000,
  });
});

test("the editable workbench parses, serializes, and updates without reloading", async ({
  page,
}) => {
  const definition = "struct root { byte value; };";
  await page.getByTestId("definition-input").fill(definition);
  await page.getByTestId("binary-input").fill("2a");
  await page.locator("#root-type").fill("root");
  await page.getByRole("button", { name: "Run parse" }).click();
  await expect(page.getByText("parse completed")).toBeVisible();
  await expect(page.locator(".result-panel pre")).toContainText('"value": 42');

  await page.getByTestId("operation-select").selectOption("serialize");
  await page.getByTestId("json-input").fill('{"value":165}');
  await page.getByRole("button", { name: "Run serialize" }).click();
  await expect(page.getByText("serialize completed")).toBeVisible();
  await expect(page.locator(".result-panel pre")).toHaveText("pQ==");

  await page.getByTestId("operation-select").selectOption("update");
  await page.getByTestId("binary-input").fill("00");
  await page.getByTestId("path-input").fill("root.value");
  await page.getByTestId("json-input").fill("42");
  await page.getByRole("button", { name: "Run update" }).click();
  await expect(page.getByText("update completed")).toBeVisible();
  await expect(page.locator(".result-panel pre")).toHaveText("Kg==");
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

test("every runnable generated demo parses through the WebAssembly bridge", async ({ page }) => {
  const runnableTests = testManifest.tests.filter(
    (
      entry,
    ): entry is (typeof testManifest.tests)[number] & {
      binaryHex: string;
      definition: string;
    } => entry.runnable && "binaryHex" in entry && "definition" in entry,
  );

  const failures = await page.evaluate((entries) => {
    return entries.flatMap((entry) => {
      const compactHex = entry.binaryHex.replace(/\s/g, "");
      const binary = Uint8Array.from(
        compactHex.match(/.{2}/g)?.map((pair) => Number.parseInt(pair, 16)) ?? [],
      );
      const binaryBase64 = btoa(String.fromCharCode(...binary));
      const result = JSON.parse(
        window.CStructSharpWasm!.parseWithDebug(entry.definition, binaryBase64, {
          rootTypeName: entry.rootType,
          aligned: entry.parserOptions.aligned,
          littleEndian: entry.parserOptions.littleEndian,
          pointerSize: entry.parserOptions.pointerSize,
        }),
      ) as { Error: { Message: string } | null; Success: boolean };

      return result.Success ? [] : [`${entry.id}: ${result.Error?.Message ?? "unknown error"}`];
    });
  }, runnableTests);

  expect(failures).toEqual([]);
});
