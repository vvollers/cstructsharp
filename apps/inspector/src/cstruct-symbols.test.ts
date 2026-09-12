import { describe, expect, it } from "vitest";
import { collectSymbols } from "./cstruct-symbols";

describe("collectSymbols", () => {
  it("counts conditional declarations while keeping nested composites as single members", () => {
    const symbols = collectSymbols(`
      struct root {
        uint8 tag;
        if (tag) { utf8 label[3]; struct { uint8 a; uint8 b; } child; }
        else { switch (tag) { case 0: { guid id; } default: { uint24> number; } } }
        uint8 tail;
      };
      typedef struct { if (enabled) { uint8 one; } else { uint16 two; } } choice;
    `);
    expect(symbols.find((symbol) => symbol.name === "root")?.documentation).toBe(
      "6 fields declared across all branches; active fields depend on the data",
    );
    expect(symbols.find((symbol) => symbol.name === "choice")?.documentation).toContain(
      "2 fields declared across all branches",
    );
    expect(symbols.map((symbol) => symbol.name)).toEqual(["root", "choice"]);
  });

  it("finds a named struct and counts its top-level fields", () => {
    const symbols = collectSymbols(`
      struct bitmap_file_header {
          char signature[2];
          uint32 file_size;
          uint32 pixel_data_offset;
      };
    `);
    expect(symbols).toContainEqual({
      kind: "struct",
      name: "bitmap_file_header",
      detail: "struct bitmap_file_header",
      documentation: "3 fields",
    });
  });

  it("finds a union and does not confuse a tagged field reference with a declaration", () => {
    const symbols = collectSymbols(`
      union sample {
          uint32 as_uint32;
          float32 as_float32;
      };
      struct outer {
          union sample value;
      };
    `);
    const names = symbols.map((s) => s.name);
    expect(names).toContain("sample");
    expect(names).toContain("outer");
    // "union sample value;" inside outer must not itself be treated as a new declaration.
    expect(symbols.filter((s) => s.name === "sample")).toHaveLength(1);
  });

  it("finds an enum with explicit storage and lists its members", () => {
    const symbols = collectSymbols(`
      enum bmp_compression : uint32 {
          Rgb = 0,
          Rle8 = 1,
          Rle4 = 2,
      };
    `);
    expect(symbols).toContainEqual({
      kind: "enum",
      name: "bmp_compression",
      detail: "enum bmp_compression : uint32",
      documentation: "Values: Rgb, Rle8, Rle4",
    });
  });

  it("finds a typedef'd anonymous struct by its trailing alias name", () => {
    const symbols = collectSymbols(`
      typedef struct {
          uint16 width;
          uint16 height;
      } dimensions;
    `);
    const dimensions = symbols.find((s) => s.name === "dimensions");
    expect(dimensions?.kind).toBe("typedef");
    expect(dimensions?.documentation).toContain("2 field");
  });

  it("finds a plain typedef alias, including a pointer typedef", () => {
    const symbols = collectSymbols(`
      typedef uint32 offset_t;
      typedef uint8 *byte_ptr;
      typedef int24< little_delta;
      typedef fixed16_16> revision;
    `);
    expect(symbols.find((s) => s.name === "offset_t")?.detail).toBe("typedef uint32 offset_t");
    expect(symbols.find((s) => s.name === "byte_ptr")?.detail).toBe("typedef uint8* byte_ptr");
    expect(symbols.find((s) => s.name === "little_delta")?.detail).toBe(
      "typedef int24< little_delta",
    );
    expect(symbols.find((s) => s.name === "revision")?.detail).toBe("typedef fixed16_16> revision");
  });

  it("finds #define constants with their expression", () => {
    const symbols = collectSymbols("#define HEADER_SIZE 40\n");
    expect(symbols).toContainEqual({
      kind: "define",
      name: "HEADER_SIZE",
      detail: "#define HEADER_SIZE",
      documentation: "= 40",
    });
  });

  it("ignores struct-like text inside comments and strings", () => {
    const symbols = collectSymbols(`
      // struct fake_from_comment { uint8 a; };
      /* struct also_fake { uint8 a; }; */
      struct real_one {
          uint8 value;
      };
    `);
    const names = symbols.map((s) => s.name);
    expect(names).toEqual(["real_one"]);
  });

  it("does not duplicate a symbol declared more than once", () => {
    const symbols = collectSymbols(`
      struct dup { uint8 a; };
    `).concat(
      collectSymbols(`
      struct dup { uint8 a; };
      struct dup { uint8 a; };
    `),
    );
    expect(symbols.filter((s) => s.name === "dup").length).toBeGreaterThan(0);
    const fromSingleScan = collectSymbols(`
      struct dup { uint8 a; };
      struct dup { uint8 a; };
    `);
    expect(fromSingleScan.filter((s) => s.name === "dup")).toHaveLength(1);
  });
});
