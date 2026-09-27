import { describe, expect, it } from "vitest";

import { errorRecoveryHint } from "./error-hints";

const context = { limits: "Safety limits", fallback: "Review the details below." };

describe("errorRecoveryHint", () => {
  it("gives code-specific advice that names the app's limits", () => {
    expect(errorRecoveryHint("read-budget", context)).toContain("Safety limits");
    expect(errorRecoveryHint("invalid-path", context)).toContain("letter case");
  });

  it("falls back to the app's advice for other or missing codes", () => {
    expect(errorRecoveryHint("something-else", context)).toBe(context.fallback);
    expect(errorRecoveryHint(undefined, context)).toBe(context.fallback);
  });
});
