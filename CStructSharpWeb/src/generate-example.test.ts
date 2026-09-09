import { describe, expect, it } from "vitest";
import { generateExample } from "./generate-example";
import type { WorkbenchRequest } from "./components/OperationWorkbench.vue";

const request: WorkbenchRequest = {
  operation: "parse",
  definition: "struct header { uint16 kind; };",
  binaryHex: "0200",
  jsonValue: "2",
  path: "header.kind",
  options: {
    rootTypeName: null,
    pointerSize: 8,
    aligned: false,
    littleEndian: true,
    addressingMode: "Absolute",
    origin: "0",
    dereferencePointers: true,
    maxArrayElements: 1000000,
    maxStringBytes: 16777216,
    maxTotalBytesRead: 67108864,
    maxTotalBytesWritten: 67108864,
    maxNestingDepth: 256,
    maxTraversalBytesRead: 67108864,
    maxTraversalNestingDepth: 256,
  },
};
describe("generated option defaults", () => {
  it.each(["parse", "serialize", "update"] as const)(
    "omits default settings entirely for %s",
    (operation) => {
      const source = generateExample({ ...request, operation });
      expect(source.csharp).toContain("new CStruct(definition);");
      expect(source.csharp).not.toContain("var options =");
      expect(source.csharp).not.toContain("options: options");
      expect(source.javascript).not.toContain("const options =");
      expect(source.javascript).not.toContain(", options)");
    },
  );
  it("preserves false booleans, nondefault layout choices, root names, and limits", () => {
    const source = generateExample({
      ...request,
      options: {
        ...request.options,
        rootTypeName: "header",
        littleEndian: false,
        aligned: true,
        pointerSize: 4,
        dereferencePointers: false,
        maxTotalBytesRead: 3,
        origin: "004",
        addressingMode: "Relative",
      },
    });
    expect(source.csharp).toContain("isLittleEndian: false");
    expect(source.csharp).toContain("DereferencePointers = false");
    expect(source.csharp).toContain("MaxTotalBytesRead = 3L");
    expect(source.csharp).not.toContain("MaxStringBytes =");
    expect(source.javascript).toContain('"rootTypeName": "header"');
    expect(source.javascript).toContain('"dereferencePointers": false');
    expect(source.javascript).not.toContain('"maxStringBytes"');
  });
  it("omits equivalent zero origins and options that do not apply to the operation", () => {
    const source = generateExample({
      ...request,
      operation: "update",
      options: {
        ...request.options,
        origin: 0n,
        dereferencePointers: false,
        maxTotalBytesRead: 3,
        maxTraversalBytesRead: 4,
      },
    });
    expect(source.csharp).toContain("DereferencePointers = false");
    expect(source.csharp).toContain("MaxTraversalBytesRead = 4L");
    expect(source.javascript).toContain('"dereferencePointers": false');
    expect(source.javascript).not.toContain('"origin"');
    expect(source.javascript).not.toContain('"maxTotalBytesRead"');
  });
  it("shares dereferencePointers between parse and update, but omits it for serialize", () => {
    const options = { ...request.options, dereferencePointers: false };
    const parsed = generateExample({ ...request, operation: "parse", options });
    const updated = generateExample({ ...request, operation: "update", options });
    const serialized = generateExample({ ...request, operation: "serialize", options });
    expect(parsed.csharp).toContain("DereferencePointers = false");
    expect(updated.csharp).toContain("DereferencePointers = false");
    expect(serialized.csharp).not.toContain("DereferencePointers");
  });
});
