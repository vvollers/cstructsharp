import { describe, expect, it } from "vitest";
import {
  computeFieldGroups,
  findDebugEntryIndexByOffset,
  findDebugEntryIndicesByPath,
  tokenizePath,
} from "./debug-path";
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

describe("findDebugEntryIndicesByPath", () => {
  const debugData = [
    debugItem({ DebugStackString: "root.file_header.signature", CurPos: 0, EndPos: 2 }),
    debugItem({ DebugStackString: "root.info_header.bits_per_pixel", CurPos: 28, EndPos: 30 }),
    debugItem({ DebugStackString: "root.entries[0].width", CurPos: 22, EndPos: 23 }),
    debugItem({ DebugStackString: "root.entries[1].width", CurPos: 30, EndPos: 31 }),
  ];

  it("finds the single entry whose tokenized path exactly matches a leaf", () => {
    expect(
      findDebugEntryIndicesByPath(debugData, ["root", "info_header", "bits_per_pixel"]),
    ).toEqual([1]);
  });

  it("matches a specific array-indexed leaf path", () => {
    expect(findDebugEntryIndicesByPath(debugData, ["root", "entries", "1", "width"])).toEqual([3]);
  });

  it("returns every descendant leaf when the clicked path is their shared container (a struct array)", () => {
    expect(findDebugEntryIndicesByPath(debugData, ["root", "entries"])).toEqual([2, 3]);
  });

  it("returns every entry sharing an identical un-indexed path (a scalar array quirk)", () => {
    const scalarArrayData = [
      debugItem({ DebugStackString: "root.dos.e_res", CurPos: 28, EndPos: 30 }),
      debugItem({ DebugStackString: "root.dos.e_res", CurPos: 30, EndPos: 32 }),
      debugItem({ DebugStackString: "root.dos.e_res", CurPos: 32, EndPos: 34 }),
    ];
    expect(findDebugEntryIndicesByPath(scalarArrayData, ["root", "dos", "e_res"])).toEqual([
      0, 1, 2,
    ]);
  });

  it("still returns the full scalar-array set when the clicked path names a specific index it cannot represent", () => {
    const scalarArrayData = [
      debugItem({ DebugStackString: "root.dos.e_res", CurPos: 28, EndPos: 30 }),
      debugItem({ DebugStackString: "root.dos.e_res", CurPos: 30, EndPos: 32 }),
    ];
    expect(findDebugEntryIndicesByPath(scalarArrayData, ["root", "dos", "e_res", "1"])).toEqual([
      0, 1,
    ]);
  });

  it("returns an empty array when no entry is related to the path", () => {
    expect(findDebugEntryIndicesByPath(debugData, ["root", "does_not_exist"])).toEqual([]);
  });

  it("returns an empty array for an empty debug data list", () => {
    expect(findDebugEntryIndicesByPath([], ["root"])).toEqual([]);
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

describe("computeFieldGroups", () => {
  it("puts every element of an array of structs into one shared group", () => {
    const debugData = [
      debugItem({ DebugStackString: "root.entries[0].width" }),
      debugItem({ DebugStackString: "root.entries[0].height" }),
      debugItem({ DebugStackString: "root.entries[1].width" }),
      debugItem({ DebugStackString: "root.entries[1].height" }),
    ];
    expect(computeFieldGroups(debugData)).toEqual([0, 0, 0, 0]);
  });

  it("keeps non-array leaf fields in their own individual groups", () => {
    const debugData = [
      debugItem({ DebugStackString: "root.file_header.signature" }),
      debugItem({ DebugStackString: "root.file_header.file_size" }),
      debugItem({ DebugStackString: "root.info_header.width" }),
    ];
    expect(computeFieldGroups(debugData)).toEqual([0, 1, 2]);
  });

  it("assigns a new group per distinct array field, in order of first appearance", () => {
    const debugData = [
      debugItem({ DebugStackString: "root.a[0]" }),
      debugItem({ DebugStackString: "root.b.leaf" }),
      debugItem({ DebugStackString: "root.a[1]" }),
      debugItem({ DebugStackString: "root.b2.leaf" }),
    ];
    expect(computeFieldGroups(debugData)).toEqual([0, 1, 0, 2]);
  });

  it("collapses a multidimensional array to one group regardless of dimension count", () => {
    const debugData = [
      debugItem({ DebugStackString: "root.matrix[0][0]" }),
      debugItem({ DebugStackString: "root.matrix[2][3]" }),
    ];
    expect(computeFieldGroups(debugData)).toEqual([0, 0]);
  });

  it("returns an empty array for no debug data", () => {
    expect(computeFieldGroups([])).toEqual([]);
  });
});
