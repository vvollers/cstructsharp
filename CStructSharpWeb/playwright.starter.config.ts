import { defineConfig } from "@playwright/test";

export default defineConfig({
  testDir: "./tests/starter",
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
