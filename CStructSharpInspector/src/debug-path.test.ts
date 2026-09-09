import { describe, expect, it } from "vitest";
import { findDebugEntryByPath, tokenizePath } from "./debug-path";
import type { DebugDataItem } from "./wasm/cstruct-contract";

function debugItem(overrides: Partial<DebugDataItem>): DebugDataItem {
  return {
    CurPos: 0,
    EndPos: 1,
    DebugStackString: "root",
    Type: "uint8",
    Value: "0",
    Buffer: null,
    ...overrides,
  };
}

describe("tokenizePath", () => {
  it("splits dot-separated names into path segments", () => {
    expect(tokenizePath("root.file_header.signature")).toEqual([
      "root",
      "file_header",
      "signature",
    ]);
  });

  it("splits bracketed array indices into their own segments", () => {
    expect(tokenizePath("root.entries[1].width")).toEqual(["root", "entries", "1", "width"]);
  });

  it("returns a single segment for a bare root name", () => {
    expect(tokenizePath("root")).toEqual(["root"]);
  });

  it("returns an empty array for an empty string", () => {
    expect(tokenizePath("")).toEqual([]);
  });
});

describe("findDebugEntryByPath", () => {
  const debugData = [
    debugItem({ DebugStackString: "root.file_header.signature", CurPos: 0, EndPos: 2 }),
    debugItem({ DebugStackString: "root.info_header.bits_per_pixel", CurPos: 28, EndPos: 30 }),
    debugItem({ DebugStackString: "root.entries[1].width", CurPos: 22, EndPos: 23 }),
  ];

  it("finds the entry whose tokenized path exactly matches", () => {
    const entry = findDebugEntryByPath(debugData, ["root", "info_header", "bits_per_pixel"]);
    expect(entry?.CurPos).toBe(28);
    expect(entry?.EndPos).toBe(30);
  });

  it("matches an array-indexed path", () => {
    const entry = findDebugEntryByPath(debugData, ["root", "entries", "1", "width"]);
    expect(entry?.CurPos).toBe(22);
  });

  it("returns undefined when no entry matches", () => {
    expect(findDebugEntryByPath(debugData, ["root", "does_not_exist"])).toBeUndefined();
  });

  it("returns undefined for an empty debug data list", () => {
    expect(findDebugEntryByPath([], ["root"])).toBeUndefined();
  });
});
