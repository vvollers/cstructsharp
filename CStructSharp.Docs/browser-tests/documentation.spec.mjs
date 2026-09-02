import { test, expect } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";

const representativePages = [
  {
    path: "index.html",
    heading: "CStructSharp documentation",
  },
  {
    path: "guides/install-and-first-parse.html",
    heading: "Install and make a first parse",
  },
  {
    path: "language/grammar.html",
    heading: "Complete Portable grammar",
  },
  {
    path: "api/CStructSharp.CStruct.html",
    heading: "Class CStruct",
  },
  {
    path: "404.html",
    heading: "Documentation page not found",
  },
];

function captureBrowserErrors(page) {
  const errors = [];
  page.on("console", (message) => {
    if (message.type() === "error") {
      errors.push(`console: ${message.text()}`);
    }
  });
  page.on("pageerror", (error) => {
    errors.push(`page: ${error.message}`);
  });
  return errors;
}

test("representative templates render without serious accessibility or console errors", async ({ page }) => {
  const errors = captureBrowserErrors(page);

  for (const item of representativePages) {
    await page.goto(item.path);
    await expect(page.getByRole("heading", { level: 1, name: item.heading })).toBeVisible();
    await expect(page.locator("main")).toBeVisible();
    await expect(page.locator("link[rel='canonical']")).toHaveAttribute(
      "href",
      `https://vvollers.github.io/CStructSharp/${item.path}`,
    );

    const results = await new AxeBuilder({ page }).
      withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"]).
      analyze();
    const serious = results.violations.filter(
      (violation) => violation.impact === "serious" || violation.impact === "critical",
    );
    expect(serious, `${item.path}: ${JSON.stringify(serious, null, 2)}`).toEqual([]);
  }

  expect(errors).toEqual([]);
});

test("primary navigation and unified search reach conceptual and API content", async ({ page }) => {
  const errors = captureBrowserErrors(page);
  await page.goto("index.html");

  await page.getByRole("link", { name: "Layout language", exact: true }).click();
  await expect(page).toHaveURL(/\/language\/index\.html$/);
  await expect(page.getByRole("heading", { level: 1, name: "The Portable layout language" })).toBeVisible();

  const search = page.getByRole("searchbox", { name: "Search" });
  await expect(search).toBeEnabled();
  await search.fill("portable binary layout grammar");
  await expect(page.locator("#search-results")).toContainText("The Portable layout language");

  await search.fill("TryReadValue");
  await expect(page.locator("#search-results")).toContainText("CStruct");

  for (const item of [
    { query: "caller-owned output", title: "Use spans, memory, and buffer writers" },
    { query: "unknown enum", title: "Preserve exact enum values" },
    { query: "byte order padding", title: "Layout, alignment, and padding" },
    { query: "build test contribute", title: "Project documentation" },
  ]) {
    await search.fill(item.query);
    await expect(page.locator("#search-results"), item.query).toContainText(item.title);
  }
  expect(errors).toEqual([]);
});

test("theme, narrow navigation, and keyboard focus remain usable", async ({ page }) => {
  const errors = captureBrowserErrors(page);
  await page.goto("language/operation-matrix.html");

  const themeButton = page.getByRole("button", { name: "Change theme" });
  await expect(themeButton).toBeVisible();
  await themeButton.click();
  await page.getByRole("link", { name: /Dark/ }).click();
  await expect(page.locator("html")).toHaveAttribute("data-bs-theme", "dark");
  await expect.poll(() => page.evaluate(() => localStorage.getItem("theme"))).toBe("dark");
  await page.reload();
  await expect(page.locator("html")).toHaveAttribute("data-bs-theme", "dark");
  expect(await page.evaluate(() => navigator.serviceWorker.getRegistrations().then((items) => items.length))).toBe(0);

  await page.setViewportSize({ width: 390, height: 844 });
  const navigationToggle = page.getByRole("button", { name: "Toggle navigation" });
  await expect(navigationToggle).toBeVisible();
  await navigationToggle.click();
  await expect(navigationToggle).toHaveAttribute("aria-expanded", "true");
  const overflows = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
  expect(overflows).toBe(false);

  await page.goto("api/CStructSharp.CStruct.html");
  expect(await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth)).toBe(false);

  await page.goto("index.html");
  await page.keyboard.press("Tab");
  const focus = await page.evaluate(() => ({
    tag: document.activeElement?.tagName,
    text: document.activeElement?.textContent?.trim(),
    outline: getComputedStyle(document.activeElement).outlineStyle,
  }));
  expect(focus.tag).not.toBe("BODY");
  expect(focus.text).toContain("CStructSharp");
  expect(focus.outline).not.toBe("none");
  expect(errors).toEqual([]);
});

test("missing routes return 404 and the static Pages fallback is useful", async ({ page, request }) => {
  const errors = captureBrowserErrors(page);
  const missing = await request.get("definitely-not-a-document.html");
  expect(missing.status()).toBe(404);

  await page.goto("404.html");
  await expect(page.getByRole("heading", { level: 1, name: "Documentation page not found" })).toBeVisible();
  await page.getByRole("link", { name: "documentation home" }).click();
  await expect(page).toHaveURL(/\/index\.html$/);
  expect(errors).toEqual([]);
});

test("repository-subpath pages expose reviewed edit and source links", async ({ page }) => {
  await page.goto("guides/index.html");
  await expect(page).toHaveURL(/\/_site\/guides\/index\.html$/);
  await expect(page.getByRole("link", { name: "Edit this page" })).toHaveAttribute(
    "href",
    "https://github.com/vvollers/CStructSharp/edit/main/CStructSharp.Docs/guides/index.md",
  );

  await page.goto("api/CStructSharp.CStruct.html");
  await expect(page.getByRole("link", { name: /Edit this page|View source/ })).toHaveAttribute(
    "href",
    /https:\/\/github\.com\/vvollers\/CStructSharp\/blob\/(?:[0-9a-f]{40}|main)\/CStructSharp\/CStruct\.cs/,
  );
});

test("a first-time reader can reach a runnable example, language rules, release notes, and issue reporting", async ({
  page,
}) => {
  await page.goto("index.html");
  await page.getByRole("link", { name: "Install and make a first parse" }).click();
  await expect(page.getByRole("heading", { level: 1, name: "Install and make a first parse" })).toBeVisible();

  const copyButton = page.getByRole("button", { name: "Copy code" }).first();
  await expect(copyButton).toBeVisible();
  await copyButton.focus();
  await page.keyboard.press("Enter");
  await expect.poll(() => page.evaluate(() => navigator.clipboard.readText())).toContain("dotnet add package");

  const exampleCopyButton = page.getByRole("button", { name: "Copy code" }).nth(1);
  await exampleCopyButton.focus();
  await page.keyboard.press("Enter");
  await expect.poll(() => page.evaluate(() => navigator.clipboard.readText())).toContain("struct header");

  const languageLink = page.getByRole("link", { name: "layout-language tutorial" });
  await languageLink.focus();
  await page.keyboard.press("Enter");
  await expect(page.getByRole("heading", {
    level: 1,
    name: "Portable language tutorial",
  })).toBeVisible();

  await page.goto("index.html");
  await expect(page.getByRole("link", { name: "Release notes", exact: true })).toHaveAttribute(
    "href",
    "https://github.com/vvollers/CStructSharp/blob/main/CHANGELOG.md",
  );
  await expect(page.getByRole("link", { name: "report a documentation problem" })).toHaveAttribute(
    "href",
    /https:\/\/github\.com\/vvollers\/CStructSharp\/issues\/new\?/,
  );
});
