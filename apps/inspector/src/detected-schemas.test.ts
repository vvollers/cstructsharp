import { describe, expect, it } from "vitest";
import { supportedExtensions } from "file-type";
import { rawFileSchema, schemaForFile, schemaProfiles } from "./detected-schemas";

describe("detected schema catalog", () => {
  it("explicitly covers every extension in the installed detector", () => {
    expect(Object.keys(schemaProfiles).sort()).toEqual([...supportedExtensions].sort());
    for (const ext of supportedExtensions) {
      const schema = schemaForFile(ext, new Uint8Array(64));
      expect(schema.definition).toContain("struct root");
      expect(schema.documentation.summary.trim()).not.toBe("");
    }
  });

  it("selects TIFF byte order and BigTIFF pointer width from bytes", () => {
    const classic = schemaForFile("tif", Uint8Array.of(0x4d, 0x4d, 0, 42, 0, 0, 0, 8));
    expect(classic.parserOptions.littleEndian).toBe(false);
    expect(classic.parserOptions.pointerSize).toBe(4);
    const big = schemaForFile("tif", Uint8Array.of(0x49, 0x49, 43, 0, 8, 0, 0, 0));
    expect(big.parserOptions.pointerSize).toBe(8);
    expect(big.definition).toContain("uint64 entry_count");
  });

  it("handles empty ZIP directories instead of applying a local-entry layout", () => {
    const schema = schemaForFile("zip", Uint8Array.of(0x50, 0x4b, 5, 6));
    expect(schema.definition).toContain("total_entries");
    expect(schema.definition).not.toContain("version_needed");
  });

  it("does not mistake a non-JFIF JPEG for the JFIF example", () => {
    const schema = schemaForFile("jpg", Uint8Array.of(0xff, 0xd8, 0xff, 0xe1, 0, 8));
    expect(schema.definition).toContain("segment_data");
    expect(schema.definition).not.toContain("jfif_header");
  });

  it("bounds unknown/prefix layouts to available bytes", () => {
    expect(rawFileSchema(new Uint8Array(3)).definition).toContain("prefix[3]");
    expect(rawFileSchema(new Uint8Array(65536)).definition).toContain("prefix[256]");
  });
});
