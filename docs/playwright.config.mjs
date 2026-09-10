import { defineConfig, devices } from "@playwright/test";

const port = Number(process.env.DOCS_TEST_PORT ?? 4173);
if (!Number.isInteger(port) || port < 1 || port > 65535) throw new Error("Invalid DOCS_TEST_PORT.");

export default defineConfig({
  testDir: "./browser-tests",
  fullyParallel: false,
  forbidOnly: true,
  retries: 0,
  workers: 1,
  reporter: [["line"]],
  outputDir: "test-results",
  use: {
    baseURL: `http://127.0.0.1:${port}/_site/`,
    permissions: ["clipboard-read", "clipboard-write"],
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
  webServer: {
    command: `dotnet tool run docfx serve . --hostname 127.0.0.1 --port ${port}`,
    url: `http://127.0.0.1:${port}/_site/index.html`,
    reuseExistingServer: false,
    timeout: 30_000,
    stdout: "pipe",
    stderr: "pipe",
  },
  projects: [
    {
      name: "chromium",
      use: {
        ...devices["Desktop Chrome"],
      },
    },
  ],
});
