import { describe, expect, it } from "vitest";
import { formats } from "./formats";

describe("formats catalog", () => {
  it("has a unique id for every entry", () => {
    const ids = formats.map((format) => format.id);
    expect(new Set(ids).size).toBe(ids.length);
  });

  it("gives every entry a non-empty definition, root type, and documentation summary", () => {
    for (const format of formats) {
      expect(format.definition.trim().length).toBeGreaterThan(0);
      expect(format.rootType.trim().length).toBeGreaterThan(0);
      expect(format.documentation.summary.trim().length).toBeGreaterThan(0);
      expect(format.sourceFixture.trim().length).toBeGreaterThan(0);
    }
  });

  it("gives every entry a well-formed space-separated hex byte string", () => {
    for (const format of formats) {
      const bytes = format.binaryHex.trim().split(/\s+/);
      expect(bytes.length).toBeGreaterThan(0);
      for (const byte of bytes) {
        expect(byte).toMatch(/^[0-9a-f]{2}$/);
      }
    }
  });

  it("declares the pe-exe and pe-dll examples with the same definition", () => {
    const exe = formats.find((format) => format.id === "pe-exe");
    const dll = formats.find((format) => format.id === "pe-dll");
    expect(exe?.definition).toBe(dll?.definition);
    expect(exe?.binaryHex).not.toBe(dll?.binaryHex);
  });
});
