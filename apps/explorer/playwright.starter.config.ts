import { defineConfig, devices } from "@playwright/test";
// CSTRUCT_BROWSERS selects the engines (comma-separated: chromium, firefox, webkit); PR CI stays Chromium-only and
// the release workflow adds the Firefox and WebKit smoke.
const browsers = (process.env.CSTRUCT_BROWSERS ?? "chromium").split(",").map((name) => name.trim());
const engines = {
  chromium: devices["Desktop Chrome"],
  firefox: devices["Desktop Firefox"],
  webkit: devices["Desktop Safari"],
};

export default defineConfig({
  testDir: "./tests/starter",
  projects: browsers.map((name) => ({ name, use: engines[name as keyof typeof engines] })),
  timeout: 60_000,
  expect: { timeout: 15_000 },
  use: { baseURL: "http://127.0.0.1:4184", headless: true },
  webServer: {
    command: "node artifacts/onboarding-host/serve.mjs 4184",
    url: "http://127.0.0.1:4184/tools/binary/starter/",
    reuseExistingServer: false,
    timeout: 30_000,
  },
});
