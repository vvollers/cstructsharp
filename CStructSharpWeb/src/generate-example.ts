import type { WorkbenchRequest } from "./components/OperationWorkbench.vue";
import type { InteropResult } from "./wasm/cstruct-contract";
import { hexToBytes } from "./wasm/cstruct-wasm";

const csString = (value: string) => JSON.stringify(value);
const csText = (value: string) => '@"' + value.replace(/"/g, '""') + '"';
const jsText = (value: string) =>
  "`" + value.replace(/\\/g, "\\\\").replace(/`/g, "\\`").replace(/\$\{/g, "\\${") + "`";
const comment = (value: string) =>
  value
    .split(/\r?\n/)
    .map((line) => `// ${line}`)
    .join("\n");

// Defaults shared by the managed APIs and the public browser wrapper.
const defaults: Record<string, unknown> = {
  rootTypeName: null,
  pointerSize: 8,
  aligned: false,
  littleEndian: true,
  addressingMode: "Absolute",
  origin: 0,
  bindingMode: "PublicReadable",
  dereferencePointers: true,
  maxPointerDepth: 64,
  maxPointerTargetBytes: null,
  maxArrayElements: 1000000,
  maxStringBytes: 16777216,
  maxNestingDepth: 256,
  maxTotalBytesRead: 67108864,
  maxTotalBytesWritten: 67108864,
  allowPointerDereference: true,
  requireExistingPointerTarget: true,
  clearUnionStorage: true,
  maxTraversalPointerDepth: 64,
  maxTraversalPointerTargetBytes: null,
  maxTraversalStringBytes: 16777216,
  maxTraversalBytesRead: 67108864,
  maxTraversalNestingDepth: 256,
};
const readOnlyOptions = new Set([
  "dereferencePointers",
  "maxPointerDepth",
  "maxPointerTargetBytes",
  "maxTotalBytesRead",
]);
const updateOnlyOptions = new Set([
  "allowPointerDereference",
  "requireExistingPointerTarget",
  "clearUnionStorage",
  "maxTraversalPointerDepth",
  "maxTraversalPointerTargetBytes",
  "maxTraversalStringBytes",
  "maxTraversalBytesRead",
  "maxTraversalNestingDepth",
]);
const layoutOptions = new Set(["rootTypeName", "pointerSize", "aligned", "littleEndian"]);

function nonDefaultOptions(request: WorkbenchRequest): Record<string, unknown> {
  return Object.fromEntries(
    Object.entries(request.options).filter(([key, value]) => {
      if (request.operation !== "parse" && readOnlyOptions.has(key)) return false;
      if (request.operation !== "update" && updateOnlyOptions.has(key)) return false;
      if (
        request.operation === "parse" &&
        (key === "bindingMode" || key === "maxTotalBytesWritten")
      )
        return false;
      if (value == null) return false;
      if (key === "origin") {
        try {
          return BigInt(value) !== 0n;
        } catch {
          return true;
        }
      }
      if (key === "rootTypeName" && typeof value === "string" && !value.trim()) return false;
      return value !== defaults[key];
    }),
  );
}

function csOption(key: string, value: unknown): string {
  if (key === "addressingMode") return `PointerAddressingMode.${value}`;
  if (key === "bindingMode") return `PocoBindingMode.${value}`;
  if (key === "origin")
    return `long.Parse(${csString(String(value))}, System.Globalization.CultureInfo.InvariantCulture)`;
  if (typeof value === "boolean") return String(value);
  return `${value}${/Bytes/.test(key) ? "L" : ""}`;
}

function csValue(value: unknown, indent = 4): string {
  const space = " ".repeat(indent);
  if (value === null) return "null";
  if (typeof value === "string") return csString(value);
  if (typeof value === "boolean") return String(value);
  if (typeof value === "number") {
    const text = JSON.stringify(value);
    if (/^-?\d+$/.test(text)) {
      const integer = BigInt(text);
      if (integer >= -9223372036854775808n && integer <= 9223372036854775807n) return `${text}L`;
      if (integer >= 0n && integer <= 18446744073709551615n) return `${text}UL`;
    }
    return `${text}d`;
  }
  if (Array.isArray(value))
    return `new object?[] { ${value.map((v) => csValue(v, indent)).join(", ")} }`;
  const obj = value as Record<string, unknown>;
  if (obj.$kind === "union") {
    const name = csString(String(obj.Union));
    const raw =
      obj.RawStorage == null
        ? null
        : `UnionValue.FromRaw(${name}, Convert.FromBase64String(${csString(String(obj.RawStorage))}))`;
    if (typeof obj.SelectedMember === "string") {
      const member = csString(obj.SelectedMember);
      const selected = csValue(
        (obj.Members as Record<string, unknown>)[obj.SelectedMember],
        indent,
      );
      return raw
        ? `${raw}.WithSelectedMember(${member}, ${selected})`
        : `UnionValue.FromMember(${name}, ${member}, ${selected})`;
    }
    if (raw) return raw;
    throw new Error("A union needs raw storage or a selected member before code can be generated.");
  }
  return `new Dictionary<string, object?>\n${space}{\n${Object.entries(obj)
    .map(([key, v]) => `${space}    [${csString(key)}] = ${csValue(v, indent + 4)},`)
    .join("\n")}\n${space}}`;
}

export function generateExample(
  request: WorkbenchRequest,
  observed?: InteropResult,
): { csharp: string; javascript: string } {
  const { operation, definition, options: o } = request;
  const bytes = operation === "serialize" ? [] : Array.from(hexToBytes(request.binaryHex));
  const hex = bytes.map((b) => b.toString(16).padStart(2, "0")).join("");
  const value = operation === "parse" ? null : JSON.parse(request.jsonValue);
  const expected = !observed
    ? "Run this example to inspect the result for these inputs."
    : !observed.Success
      ? `These inputs currently fail in the browser: ${observed.Error?.Code}.\nExpected: the error handler reports the invalid input or exceeded limit.`
      : operation === "parse"
        ? `Observed browser values (C# uses its native value types without the outer root wrapper):\n${observed.Data}`
        : `Expected output bytes (hex): ${Array.from(atob(observed.Data!), (c) => c.charCodeAt(0).toString(16).padStart(2, "0")).join(" ")}`;
  const options = nonDefaultOptions(request);
  const policy = Object.entries(options)
    .filter(([key]) => !layoutOptions.has(key))
    .map(
      ([key, value]) =>
        `        ${key[0]!.toUpperCase()}${key.slice(1)} = ${csOption(key, value)},`,
    )
    .join("\n");
  const constructorOptions = Object.entries(options)
    .filter(([key]) => layoutOptions.has(key) && key !== "rootTypeName")
    .map(([key, value]) => `, ${key === "littleEndian" ? "isLittleEndian" : key}: ${value}`)
    .join("");
  const csOptionsArgument = policy ? ", options: options" : "";
  const jsOptionsArgument = Object.keys(options).length ? ", options" : "";
  const root = o.rootTypeName
    ? csString(o.rootTypeName)
    : "layout.CStructElements.First(entry => entry.Value is CStructSharp.Structure.Struct).Key";
  const csharp = `// Create a .NET 10 console project and install the CStructSharp package.
// Replace Program.cs with this code. These are the inputs captured when you clicked Generate.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using CStructSharp;

try
{
    // The C-like declaration describes byte positions, widths, and field names.
    string definition = ${csText(definition)};
    var layout = new CStruct(definition${constructorOptions});
${
  operation !== "serialize"
    ? `
    // Hexadecimal pairs represent the current binary input; each pair is one byte.
    byte[] bytes = Convert.FromHexString(${csString(hex)});
`
    : ""
}
${
  policy
    ? `    // Only settings that differ from the defaults need to be supplied.
    var options = new ${operation === "parse" ? "ReadOptions" : operation === "serialize" ? "WriteOptions" : "UpdateOptions"}
    {
${policy}
    };`
    : "    // Default operation settings already match the workbench."
}
${
  operation !== "parse"
    ? `
    // Field names match the layout. Large integer strings preserve their exact digits.
    object? value = ${csValue(value)};
`
    : ""
}
${comment(expected)
  .split("\n")
  .map((line) => "    " + line)
  .join("\n")}
${
  operation === "parse"
    ? `    // AsSpan selects the memory read overload without copying the input bytes.
    object result = layout.Parse(bytes.AsSpan(), ${root}${csOptionsArgument});
    Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));`
    : operation === "serialize"
      ? `    // Create a new byte array from the selected root and application values.
    byte[] output = layout.Serialize(${root}, value!${csOptionsArgument});
    Console.WriteLine(Convert.ToHexString(output));`
      : `    // Change only the selected path in an existing, seekable byte buffer.
    using var stream = new MemoryStream(bytes);
    layout.UpdateStream(stream, ${csString(request.path)}, value!${csOptionsArgument});
    Console.WriteLine(Convert.ToHexString(stream.ToArray()));`
}
}
catch (CStructException error)
{
    // Layout, path, and data errors include a stable code and their location.
    Console.Error.WriteLine($"{error.Code}: {error.Message} (path {error.Path}, offset {error.Offset})");
}
catch (Exception error)
{
    // For example, an invalid option or malformed hexadecimal input.
    Console.Error.WriteLine(error.Message);
}
`;
  const api = operation === "parse" ? "parseWithDebug" : operation;
  const javascript = `// Save as app.js beside cstructsharp-wasm.js and the complete WASM bundle.
// Load it with <script type="module" src="./app.js"></script> on a page served over HTTP.
import { ${api} } from "./cstructsharp-wasm.js";

// These are the live inputs captured when you clicked Generate.
const definition = ${jsText(definition)};
${operation === "serialize" ? "" : `const bytes = new Uint8Array([${bytes.map((b) => "0x" + b.toString(16).padStart(2, "0")).join(", ")}]);\n`}
${
  jsOptionsArgument
    ? `// Only settings that differ from the defaults need to be supplied.
const options = ${JSON.stringify(options, (_key, value) => (typeof value === "bigint" ? value.toString() : value), 4)};`
    : "// Default settings already match the workbench; no options object is needed."
}
${
  operation === "parse"
    ? ""
    : `
// JSON field names match the layout; keep large integer values as quoted decimal strings.
const value = JSON.parse(${JSON.stringify(request.jsonValue)});
`
}
try {
    // The public async wrapper loads the runtime before performing the operation.
    const result = await ${api}(definition, ${operation === "parse" ? "bytes" : operation === "serialize" ? "value" : `bytes, ${JSON.stringify(request.path)}, value`}${jsOptionsArgument});
    if (!result.Success) {
        // Operation errors are reported in the result, rather than thrown.
        console.error(result.Error.Code, result.Error.Message, result.Error.Path, result.Error.Offset);
    } else {
${comment(expected)
  .split("\n")
  .map((line) => "        " + line)
  .join("\n")}
${
  operation === "parse"
    ? `        // Data contains JSON text, including a root wrapper and exact large-integer strings.
        console.log(JSON.parse(result.Data));`
    : `        // Data is already a Uint8Array, ready to save, send, or read again.
        const output = result.Data;
        console.log(Array.from(output, b => b.toString(16).padStart(2, "0")).join(" "));`
}
    }
} catch (error) {
    // Loading failures and JavaScript input errors reach this handler.
    console.error(error);
}
`;
  return { csharp, javascript };
}
