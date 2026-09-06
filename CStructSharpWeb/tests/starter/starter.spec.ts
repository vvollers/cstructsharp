import { expect, test } from "@playwright/test";

const starter = "/tools/binary/starter/";
test("the extracted package starter reads, writes, updates, and rereads under a nested URL", async ({
  page,
}) => {
  await page.goto(starter);
  await expect(page.locator("#status")).toHaveText("Ready");
  await page.locator("#run").click();
  await expect(page.locator("#output")).toHaveText(
    [
      'Read: {"header":{"kind":2,"length":6}}',
      "Created: 03 00 06 00 00 00",
      "Updated: 04 00 06 00 00 00",
      'Read again: {"header":{"kind":4,"length":6}}',
    ].join("\n"),
  );
});

test("the starter shows a useful loading failure", async ({ page }) => {
  await page.route("**/cstructsharp-wasm.js", (route) => route.abort());
  await page.goto(starter);
  await expect(page.locator("#status")).toHaveText("Could not load WebAssembly");
  await expect(page.locator("#output")).toContainText(
    "Serve the complete extracted bundle over HTTP",
  );
  await expect(page.locator("#run")).toBeDisabled();
});

test("the starter reports operation failures separately", async ({ page }) => {
  // Change only the input in the delivered application, preserving the real runtime and error handling.
  await page.route("**/starter/app.js", async (route) => {
    const response = await route.fetch();
    const script = (await response.text()).replace(
      "new Uint8Array([2, 0, 6, 0, 0, 0])",
      "new Uint8Array([2])",
    );
    await route.fulfill({ response, body: script });
  });
  await page.goto(starter);
  await expect(page.locator("#status")).toHaveText("Ready");
  await page.locator("#run").click();
  await expect(page.locator("#output")).toContainText("Operation failed: read-failed");
  await expect(page.locator("#status")).toHaveText("Ready");
});

test("the inspector maps fields, preserves other bytes, and downloads the changed file", async ({
  page,
}) => {
  await page.goto(`${starter}inspector.html`);
  await expect(page.locator("#status")).toHaveText("Ready");
  await page.locator("#fixture").click();
  await expect(page.locator("#bytes")).toHaveText("43 53 01 02 01 00 10 02 00 20");
  await page.locator("#fields button").first().click();
  await expect(page.locator("#selection")).toContainText("Offset");
  await page.locator("#patch").click();
  await expect(page.locator("#bytes")).toHaveText("43 53 01 02 01 00 10 02 00 a5");
  const downloaded = page.waitForEvent("download");
  await page.locator("#download").click();
  const download = await downloaded;
  const stream = await download.createReadStream();
  const chunks = [];
  for await (const chunk of stream!) chunks.push(chunk);
  expect(Buffer.concat(chunks)).toEqual(Buffer.from([0x43, 0x53, 1, 2, 1, 0, 0x10, 2, 0, 0xa5]));
  await page.locator("#file").setInputFiles({
    name: "truncated.bin",
    mimeType: "application/octet-stream",
    buffer: Buffer.from([0x43]),
  });
  await expect(page.locator("#result")).toContainText("ten-byte file only");
  await expect(page.locator("#download")).toBeHidden();
});
