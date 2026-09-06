import { describe, expect, it } from "vitest";
import { formatLayout } from "./format-layout";

describe("formatLayout", () => {
  it("indents nested declarations and keeps expressions intact", () => {
    expect(
      formatLayout("struct root { struct { uint8 value; } item; uint8 data[(2 + 3)]; };"),
    ).toBe(
      "struct root {\n    struct {\n        uint8 value;\n    } item;\n    uint8 data[(2 + 3)];\n};",
    );
  });
  it("preserves comment and string contents and preprocessor line boundaries", () => {
    const input =
      "#define COUNT 2\nstruct root { // a { comment;\n char text[COUNT]; /* keep }; */ char other[4]; };";
    const result = formatLayout(input);
    expect(result).toContain("#define COUNT 2\n");
    expect(result).toContain("// a { comment;\n");
    expect(result).toContain("/* keep }; */");
    expect(formatLayout('char value[sizeof("a;{b}")];')).toBe('char value[sizeof("a;{b}")];');
    expect(formatLayout(result)).toBe(result);
  });
});
