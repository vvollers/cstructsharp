import { describe, expect, it } from "vitest";
import { supportedExtensions } from "file-type";
import {
  rawFileSchema,
  schemaCatalog,
  schemaForFile,
  detectorExtensions,
  sampleExamples,
} from "./schema-catalog";
import { formatLayout } from "./format-layout";

describe("standalone schema catalog", () => {
  it("lists each extension once and attaches samples through their declared extension", () => {
    const extensions = schemaCatalog.map((example) => example.extension);
    expect(new Set(extensions).size).toBe(extensions.length);
    expect([...extensions].sort()).toEqual([...detectorExtensions, "dll"].sort());
    expect(new Set(schemaCatalog.map((example) => example.id)).size).toBe(schemaCatalog.length);

    for (const sample of sampleExamples) {
      expect(schemaCatalog.find((example) => example.extension === sample.extension)).toBe(sample);
      expect(sample.schemaOnly).toBeUndefined();
      expect(sample.description).not.toBe("");
    }
    for (const example of schemaCatalog.filter((example) => example.schemaOnly)) {
      expect(example.definition).toBe(schemaForFile(example.extension!).definition);
      expect(example.binaryHex).toBe("");
    }
  });

  it("resolves the registered PE alias and rejects unknown extensions", () => {
    expect(schemaForFile("dll")).toEqual(schemaForFile("exe"));
    expect(() => schemaForFile("not-a-format")).toThrow("No schema registered");
  });

  it("covers every detector extension with a formatted standalone definition", () => {
    expect([...detectorExtensions].sort()).toEqual([...supportedExtensions].sort());
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

describe("sample examples", () => {
  it("has a unique id for every entry", () => {
    const ids = sampleExamples.map((format) => format.id);
    expect(new Set(ids).size).toBe(ids.length);
  });

  it("gives every entry a non-empty definition, root type, and documentation summary", () => {
    for (const format of sampleExamples) {
      expect(format.definition.trim().length).toBeGreaterThan(0);
      expect(format.rootType.trim().length).toBeGreaterThan(0);
      expect(format.documentation.summary.trim().length).toBeGreaterThan(0);
      expect(format.sourceFixture.trim().length).toBeGreaterThan(0);
    }
  });

  it("gives every entry a well-formed space-separated hex byte string", () => {
    for (const format of sampleExamples) {
      const bytes = format.binaryHex.trim().split(/\s+/);
      expect(bytes.length).toBeGreaterThan(0);
      for (const byte of bytes) {
        expect(byte).toMatch(/^[0-9a-f]{2}$/);
      }
    }
  });

  it("declares the pe-exe and pe-dll examples with the same definition", () => {
    const exe = sampleExamples.find((format) => format.id === "pe-exe");
    const dll = sampleExamples.find((format) => format.id === "pe-dll");
    expect(exe?.definition).toBe(dll?.definition);
    expect(exe?.binaryHex).not.toBe(dll?.binaryHex);
  });
});
