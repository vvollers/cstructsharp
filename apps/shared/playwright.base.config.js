/**
 * The Playwright configuration both apps' browser tests build on: tests under `tests/e2e` against the production
 * build served by `vite preview` on a fixed local port. Each app's `playwright.config.ts` passes its own port, so
 * both suites can run at the same time:
 *
 *   import { appPlaywrightConfig } from "@cstructsharp/app-shared/playwright-config";
 *   export default appPlaywrightConfig({ port: 4173 });
 *
 * CSTRUCT_BROWSERS selects the engines (comma-separated: chromium, firefox, webkit); PR CI stays Chromium-only and
 * the release workflow adds the Firefox and WebKit smoke.
 */
import { defineConfig, devices } from "@playwright/test";

const engines = {
  chromium: devices["Desktop Chrome"],
  firefox: devices["Desktop Firefox"],
  webkit: devices["Desktop Safari"],
};

/**
 * Builds one app's Playwright configuration.
 * @param {{ port: number }} options The local port `vite preview` serves the app on.
 * @returns {import("@playwright/test").PlaywrightTestConfig} The configuration to export from `playwright.config.ts`.
 */
export function appPlaywrightConfig({ port }) {
  const browsers = (process.env.CSTRUCT_BROWSERS ?? "chromium")
    .split(",")
    .map((name) => name.trim());
  const url = `http://127.0.0.1:${port}`;
  return defineConfig({
    testDir: "./tests/e2e",
    projects: browsers.map((name) => ({ name, use: engines[name] })),
    timeout: 60_000,
    expect: {
      timeout: 10_000,
    },
    use: {
      baseURL: url,
      headless: true,
    },
    webServer: {
      command: `npm run preview -- --host 127.0.0.1 --port ${port}`,
      url,
      // Locally, reuse an already running preview; CI always starts a fresh one.
      reuseExistingServer: !process.env.CI,
      timeout: 120_000,
    },
  });
}
