// @vitest-environment node
import fs from "node:fs";
import { describe, expect, it } from "vitest";

import { formatBytes, OPTION_DEFAULTS } from "./options";

/** The public package declarations, which document the read-limit defaults users see. */
const declarations = fs.readFileSync(
  new URL("../../../packages/cstructsharp/index.d.ts", import.meta.url),
  "utf8",
);

describe("option defaults", () => {
  it("match the read limits the package declarations document", () => {
    expect(declarations).toMatch(/Default: 1,000,000\.[^\n]*\n\s*maxArrayElements\?/);
    expect(OPTION_DEFAULTS.maxArrayElements).toBe(1_000_000);
    expect(declarations).toMatch(/Default: 16 MiB\.[^\n]*\n\s*maxStringBytes\?/);
    expect(OPTION_DEFAULTS.maxStringBytes).toBe(16 * 1_048_576);
    expect(declarations).toMatch(/Default: 64 MiB\.[^\n]*\n\s*maxTotalBytesRead\?/);
    expect(OPTION_DEFAULTS.maxTotalBytesRead).toBe(64 * 1_048_576);
  });

  it("formats whole mebibytes and other byte counts", () => {
    expect(formatBytes(OPTION_DEFAULTS.maxStringBytes)).toBe("16 MiB");
    expect(formatBytes(3)).toBe("3 B");
    expect(formatBytes(1_048_577)).toBe((1_048_577).toLocaleString() + " B");
  });
});
