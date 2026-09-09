import { describe, expect, it } from "vitest";
import { findDebugEntryIndexByOffset, findDebugEntryIndexByPath, tokenizePath } from "./debug-path";
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

describe("findDebugEntryIndexByPath", () => {
  const debugData = [
    debugItem({ DebugStackString: "root.file_header.signature", CurPos: 0, EndPos: 2 }),
    debugItem({ DebugStackString: "root.info_header.bits_per_pixel", CurPos: 28, EndPos: 30 }),
    debugItem({ DebugStackString: "root.entries[1].width", CurPos: 22, EndPos: 23 }),
  ];

  it("finds the index of the entry whose tokenized path exactly matches", () => {
    expect(findDebugEntryIndexByPath(debugData, ["root", "info_header", "bits_per_pixel"])).toBe(1);
  });

  it("matches an array-indexed path", () => {
    expect(findDebugEntryIndexByPath(debugData, ["root", "entries", "1", "width"])).toBe(2);
  });

  it("returns -1 when no entry matches", () => {
    expect(findDebugEntryIndexByPath(debugData, ["root", "does_not_exist"])).toBe(-1);
  });

  it("returns -1 for an empty debug data list", () => {
    expect(findDebugEntryIndexByPath([], ["root"])).toBe(-1);
  });
});

describe("findDebugEntryIndexByOffset", () => {
  const debugData = [
    debugItem({ DebugStackString: "root.file_header.signature", CurPos: 0, EndPos: 2 }),
    debugItem({ DebugStackString: "root.info_header.bits_per_pixel", CurPos: 28, EndPos: 30 }),
  ];

  it("finds the index of the entry covering an offset", () => {
    expect(findDebugEntryIndexByOffset(debugData, 29)).toBe(1);
  });

  it("treats CurPos as inclusive and EndPos as exclusive", () => {
    expect(findDebugEntryIndexByOffset(debugData, 28)).toBe(1);
    expect(findDebugEntryIndexByOffset(debugData, 30)).toBe(-1);
  });

  it("returns -1 for an offset covered by no entry", () => {
    expect(findDebugEntryIndexByOffset(debugData, 10)).toBe(-1);
  });
});
