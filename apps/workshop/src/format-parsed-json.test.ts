import { describe, expect, it } from "vitest";
import { formatParsedJson } from "./format-parsed-json";

describe("parsed JSON hexadecimal comments", () => {
  it("annotates nested integer properties and array elements with commas before comments", () => {
    expect(formatParsedJson({ c: 16, nested: { values: [0, -42, 255] } })).toBe(`{
  "c": 16, // 0x10
  "nested": {
    "values": [
      0, // 0x0
      -42, // -0x2A
      255 // 0xFF
    ]
  }
}`);
  });

  it("preserves text, escaped keys, decimal fractions, booleans, and null", () => {
    const input = {
      text: '16, "c": 42',
      wide: "18446744073709551615",
      fraction: 1.5,
      enabled: true,
      empty: null,
    };
    expect(formatParsedJson(input)).toBe(JSON.stringify(input, null, 2));
    expect(formatParsedJson({ 'a"b': 16 })).toContain('"a\\"b": 16 // 0x10');
    expect(formatParsedJson("16")).toBe('"16"');
    expect(formatParsedJson(16)).toBe("16 // 0x10");
  });
});
