import { describe, expect, it } from "vitest";
import {
  computeFieldGroups,
  debugEntryJsonPath,
  findDebugEntryIndexByOffset,
  findDebugEntryIndicesByPath,
  tokenizePath,
} from "./debug-path";
import type { DebugItem } from "@cstructsharp/app-shared/wasm/contract";

/**
 * Creates a one-byte `uint8` debug item at offset 0 with the given fields replaced.
 * @param overrides The fields to set.
 */
function debugItem(overrides: Partial<DebugItem>): DebugItem {
  return {
    start: 0,
    end: 1,
    path: "root",
    type: "uint8",
    value: "0",
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
  it("maps pointer target fields separately from address storage, preserving real value fields", () => {
    /** Builds a dereferenced pointer value at address 32 that holds the given target value. */
    const pointer = (value: unknown) => ({
      kind: "pointer",
      address: 32,
      depth: 1,
      dereferenced: true,
      value,
    });
    const result = {
      root: {
        ptr: pointer({ myfield: 7, value: 9, nested: pointer({ leaf: 11 }) }),
        value: { leaf: 12 },
      },
    };
    // The parser names each followed target with a value step, so its paths walk the JSON result.
    const entries = [
      debugItem({ path: "root.ptr.value.myfield", start: 32, end: 36 }),
      debugItem({ path: "root.ptr.value.value", start: 36, end: 40 }),
      debugItem({ path: "root.ptr.value.nested.value.leaf", start: 48, end: 52 }),
      debugItem({ path: "root.ptr.value.nested", start: 40, end: 44 }),
      debugItem({ path: "root.ptr", start: 0, end: 4 }),
      debugItem({ path: "root.value.leaf", start: 4, end: 8 }),
    ];
    expect(
      findDebugEntryIndicesByPath(entries, ["root", "ptr", "value", "myfield"], result),
    ).toEqual([0]);
    expect(findDebugEntryIndicesByPath(entries, ["root", "ptr", "value", "value"], result)).toEqual(
      [1],
    );
    expect(findDebugEntryIndicesByPath(entries, ["root", "ptr", "address"], result)).toEqual([4]);
    expect(findDebugEntryIndicesByPath(entries, ["root", "ptr", "value"], result)).toEqual([
      0, 1, 2, 3,
    ]);
    expect(findDebugEntryIndicesByPath(entries, ["root", "value", "leaf"], result)).toEqual([5]);
    expect(debugEntryJsonPath(entries[2]!, result)).toEqual([
      "root",
      "ptr",
      "value",
      "nested",
      "value",
      "leaf",
    ]);
    expect(debugEntryJsonPath(entries[3]!, result)).toEqual([
      "root",
      "ptr",
      "value",
      "nested",
      "address",
    ]);
  });

  it("maps pointer chains, scalar targets and pointers within struct arrays", () => {
    const result = {
      root: {
        entries: [
          {
            ptr: {
              kind: "pointer",
              address: 8,
              depth: 2,
              dereferenced: true,
              value: {
                kind: "pointer",
                address: 16,
                depth: 1,
                dereferenced: true,
                value: { leaf: 5 },
              },
            },
          },
        ],
        bytep: { kind: "pointer", address: 24, depth: 1, dereferenced: true, value: 51 },
      },
    };
    expect(
      debugEntryJsonPath(debugItem({ path: "root.entries[0].ptr.value.value.leaf" }), result),
    ).toEqual(["root", "entries", "0", "ptr", "value", "value", "leaf"]);
    // The intermediate pointer's own record covers its stored address.
    expect(debugEntryJsonPath(debugItem({ path: "root.entries[0].ptr.value" }), result)).toEqual([
      "root",
      "entries",
      "0",
      "ptr",
      "value",
      "address",
    ]);
    // A scalar target and the pointer's storage map to different JSON properties.
    expect(debugEntryJsonPath(debugItem({ path: "root.bytep.value" }), result)).toEqual([
      "root",
      "bytep",
      "value",
    ]);
    expect(debugEntryJsonPath(debugItem({ path: "root.bytep" }), result)).toEqual([
      "root",
      "bytep",
      "address",
    ]);
  });

  it("maps each element of a scalar pointer array and its target separately", () => {
    const result = {
      root: {
        bytes: [
          { kind: "pointer", address: 13, depth: 1, dereferenced: true, value: 2 },
          { kind: "pointer", address: 18, depth: 1, dereferenced: true, value: 85 },
        ],
      },
    };
    // The parser indexes every pointer-array element, so its storage and its target each map to one JSON property.
    expect(debugEntryJsonPath(debugItem({ path: "root.bytes[1]" }), result)).toEqual([
      "root",
      "bytes",
      "1",
      "address",
    ]);
    expect(debugEntryJsonPath(debugItem({ path: "root.bytes[1].value" }), result)).toEqual([
      "root",
      "bytes",
      "1",
      "value",
    ]);
    const entries = [
      debugItem({ path: "root.bytes[0]", start: 0, end: 1 }),
      debugItem({ path: "root.bytes[1]", start: 1, end: 2 }),
      debugItem({ path: "root.bytes[0].value", start: 13, end: 14 }),
      debugItem({ path: "root.bytes[1].value", start: 18, end: 19 }),
    ];
    expect(findDebugEntryIndicesByPath(entries, ["root", "bytes", "1", "value"], result)).toEqual([
      3,
    ]);
    expect(findDebugEntryIndicesByPath(entries, ["root", "bytes", "1"], result)).toEqual([1, 3]);
  });

  it("maps the elements of a counted pointer target through the pointer's value list", () => {
    const result = {
      root: {
        nodes: {
          kind: "pointer",
          address: 8,
          depth: 1,
          dereferenced: true,
          value: [{ a: 1 }, { a: 2 }],
        },
      },
    };
    expect(debugEntryJsonPath(debugItem({ path: "root.nodes.value[1].a" }), result)).toEqual([
      "root",
      "nodes",
      "value",
      "1",
      "a",
    ]);
  });

  const debugData = [
    debugItem({ path: "root.file_header.signature", start: 0, end: 2 }),
    debugItem({ path: "root.info_header.bits_per_pixel", start: 28, end: 30 }),
    debugItem({ path: "root.entries[0].width", start: 22, end: 23 }),
    debugItem({ path: "root.entries[1].width", start: 30, end: 31 }),
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
      debugItem({ path: "root.dos.e_res", start: 28, end: 30 }),
      debugItem({ path: "root.dos.e_res", start: 30, end: 32 }),
      debugItem({ path: "root.dos.e_res", start: 32, end: 34 }),
    ];
    expect(findDebugEntryIndicesByPath(scalarArrayData, ["root", "dos", "e_res"])).toEqual([
      0, 1, 2,
    ]);
  });

  it("still returns the full scalar-array set when the clicked path names a specific index it cannot represent", () => {
    const scalarArrayData = [
      debugItem({ path: "root.dos.e_res", start: 28, end: 30 }),
      debugItem({ path: "root.dos.e_res", start: 30, end: 32 }),
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
    debugItem({ path: "root.file_header.signature", start: 0, end: 2 }),
    debugItem({ path: "root.info_header.bits_per_pixel", start: 28, end: 30 }),
  ];

  it("finds the index of the entry covering an offset", () => {
    expect(findDebugEntryIndexByOffset(debugData, 29)).toBe(1);
  });

  it("treats start as inclusive and end as exclusive", () => {
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
      debugItem({ path: "root.entries[0].width" }),
      debugItem({ path: "root.entries[0].height" }),
      debugItem({ path: "root.entries[1].width" }),
      debugItem({ path: "root.entries[1].height" }),
    ];
    expect(computeFieldGroups(debugData)).toEqual([0, 0, 0, 0]);
  });

  it("keeps non-array leaf fields in their own individual groups", () => {
    const debugData = [
      debugItem({ path: "root.file_header.signature" }),
      debugItem({ path: "root.file_header.file_size" }),
      debugItem({ path: "root.info_header.width" }),
    ];
    expect(computeFieldGroups(debugData)).toEqual([0, 1, 2]);
  });

  it("assigns a new group per distinct array field, in order of first appearance", () => {
    const debugData = [
      debugItem({ path: "root.a[0]" }),
      debugItem({ path: "root.b.leaf" }),
      debugItem({ path: "root.a[1]" }),
      debugItem({ path: "root.b2.leaf" }),
    ];
    expect(computeFieldGroups(debugData)).toEqual([0, 1, 0, 2]);
  });

  it("collapses a multidimensional array to one group regardless of dimension count", () => {
    const debugData = [
      debugItem({ path: "root.matrix[0][0]" }),
      debugItem({ path: "root.matrix[2][3]" }),
    ];
    expect(computeFieldGroups(debugData)).toEqual([0, 0]);
  });

  it("returns an empty array for no debug data", () => {
    expect(computeFieldGroups([])).toEqual([]);
  });
});
