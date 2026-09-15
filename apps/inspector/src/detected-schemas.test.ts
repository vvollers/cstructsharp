import { describe, expect, it } from "vitest";
import { supportedExtensions } from "file-type";
import { rawFileSchema, schemaCatalog, schemaForFile, schemaProfiles } from "./detected-schemas";
import { formatLayout } from "./format-layout";

describe("standalone schema catalog", () => {
  it("covers every detector extension with a formatted standalone definition", () => {
    expect(Object.keys(schemaProfiles).sort()).toEqual([...supportedExtensions].sort());
    for (const ext of supportedExtensions) {
      const schema = schemaForFile(ext);
      expect(schema.definition).toContain("struct root");
      expect(schema.definition).toBe(formatLayout(schema.definition));
      expect(schema.definition).not.toContain("#define");
      expect(schema.documentation.summary.trim()).not.toBe("");
    }
    for (const sample of schemaCatalog) {
      expect(formatLayout(formatLayout(sample.definition))).toBe(formatLayout(sample.definition));
    }
  });
  it("keeps variant selection in native conditions with fixed parser options", () => {
    const tiff = schemaForFile("tif");
    expect(tiff.parserOptions.pointerSize).toBe(4);
    expect(tiff.definition).toContain("if (version == 42)");
    expect(tiff.definition).toContain("if (version == 43)");
    expect(schemaForFile("zip").definition).toContain("switch (signature)");
    expect(schemaForFile("exe").definition).toContain("optional_magic == 0x20b");
    expect(schemaForFile("elf").definition).toContain("if (byte_order == 1)");
    expect(schemaForFile("jpg").definition).toContain("switch (marker)");
  });
  it("has no binary-input parameter, including the unknown-file fallback", () => {
    expect(schemaForFile.length).toBe(1);
    expect(rawFileSchema.length).toBe(0);
    expect(rawFileSchema().definition).toContain("prefix[1]");
  });
});
