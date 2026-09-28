/**
 * Browser checks of the npm package's standalone bundle: the starter page and the bridge contract, both served from
 * the extracted archive under a nested URL. tools/packaging/test-onboarding-browser.mjs stages the host in
 * artifacts/onboarding-host and runs this config. CSTRUCT_BROWSERS selects the engines (comma-separated: chromium,
 * firefox, webkit); pull requests run Chromium only and the release workflow adds the Firefox and WebKit smoke.
 */
import path from "node:path";
import { fileURLToPath } from "node:url";
import { defineConfig, devices } from "@playwright/test";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../../..");
const browsers = (process.env.CSTRUCT_BROWSERS ?? "chromium").split(",").map((name) => name.trim());
const engines = {
  chromium: devices["Desktop Chrome"],
  firefox: devices["Desktop Firefox"],
  webkit: devices["Desktop Safari"],
};

export default defineConfig({
  testDir: ".",
  testMatch: "*.spec.mjs",
  outputDir: path.join(repositoryRoot, "artifacts/package-browser-results"),
  projects: browsers.map((name) => ({ name, use: engines[name] })),
  timeout: 60_000,
  expect: { timeout: 15_000 },
  use: { baseURL: "http://127.0.0.1:4184", headless: true },
  webServer: {
    command: `node "${path.join(repositoryRoot, "artifacts/onboarding-host/serve.mjs")}" 4184`,
    url: "http://127.0.0.1:4184/tools/binary/starter/",
    reuseExistingServer: false,
    timeout: 30_000,
  },
});
