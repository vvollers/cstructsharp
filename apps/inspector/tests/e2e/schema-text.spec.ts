import { expect, test } from "@playwright/test";
import { schemaForFile } from "../../src/schema-catalog";
import type { RawWasmAdapter } from "@cstructsharp/app-shared/wasm/contract";

test("text fields produce strings while retaining their byte extents and binary neighbours", async ({
  page,
}) => {
  await page.goto("/");
  await expect(page.locator(".status-badge")).toContainText("Ready");
  const zip = Buffer.alloc(37);
  zip.writeUInt32LE(0x04034b50);
  zip.writeUInt32LE(1, 18);
  zip.writeUInt16LE(5, 26);
  zip.writeUInt16LE(1, 28);
  zip.write("a.txt", 30);
  zip[35] = 255;
  zip[36] = 128;
  const emptyZip = Buffer.alloc(26);
  emptyZip.writeUInt32LE(0x06054b50);
  emptyZip.writeUInt16LE(4, 20);
  emptyZip.write("note", 22);
  const asar = Buffer.alloc(18);
  asar.writeUInt32LE(2, 12);
  asar.write("{}", 16);
  const cpio = Buffer.alloc(32);
  cpio.writeUInt16LE(0x71c7);
  cpio.writeUInt16LE(6, 20);
  cpio.write("a.txt\0", 26);
  const hive = Buffer.alloc(112);
  hive.write("regf");
  hive.write("配置", 48, "utf16le");
  const dicom = Buffer.alloc(144);
  dicom.write("DICM", 128);
  dicom.writeUInt16LE(2, 132);
  dicom.write("UI", 136);
  dicom.writeUInt16LE(4, 138);
  dicom.write("1.2\0", 140);
  const dicomBinary = Buffer.alloc(146);
  dicomBinary.write("DICM", 128);
  dicomBinary.writeUInt16LE(2, 132);
  dicomBinary.write("OB", 136);
  dicomBinary.writeUInt32LE(2, 140);
  dicomBinary[145] = 255;
  const png = Buffer.alloc(61);
  png.writeUInt32BE(13, 8);
  png.write("IHDR", 12);
  png.writeUInt32BE(4, 33);
  png.write("tEXt", 37);
  png.write("k\0é!", 41, "latin1");
  png.write("IEND", 53);
  const cases = [
    {
      ext: "zip",
      bytes: zip,
      expected: { local: { filename_cp437: "a.txt", extra: [255] } },
      field: "root.header.local.filename_cp437",
      start: 30,
      end: 35,
    },
    {
      ext: "zip",
      bytes: emptyZip,
      expected: { comment: "note" },
      field: "root.header.comment",
      start: 22,
      end: 26,
    },
    {
      ext: "asar",
      bytes: asar,
      expected: { json: "{}" },
      field: "root.header.json",
      start: 16,
      end: 18,
    },
    {
      ext: "cpio",
      bytes: cpio,
      expected: { name: "a.txt\0" },
      field: "root.header.name",
      start: 26,
      end: 32,
    },
    {
      ext: "dat",
      bytes: hive,
      expected: { file_name_utf16le: "配置" + "\0".repeat(30) },
      field: "root.header.file_name_utf16le",
      start: 48,
      end: 112,
    },
    {
      ext: "dcm",
      bytes: dicom,
      expected: { short_value: { text: "1.2\0" } },
      field: "root.header.short_value.text",
      start: 140,
      end: 144,
    },
    {
      ext: "dcm",
      bytes: dicomBinary,
      expected: { long_value: { bytes: [0, 255] } },
      field: "root.header.long_value.bytes",
      start: 144,
      end: 146,
    },
    ...(["SV", "UV", "ZZ"] as const).map((vr) => {
      const bytes = Buffer.alloc(152);
      bytes.write("DICM", 128);
      bytes.writeUInt16LE(2, 132);
      bytes.write(vr, 136);
      bytes.writeUInt32LE(8, 140);
      bytes.writeUInt32LE(42, 144);
      const field = vr === "ZZ" ? "bytes" : `values_${vr}`;
      return {
        ext: "dcm",
        bytes,
        expected: { long_value: { [field]: vr === "ZZ" ? [42, 0, 0, 0, 0, 0, 0, 0] : [42] } },
        field: `root.header.long_value.${field}`,
        start: 144,
        end: 152,
      };
    }),
    {
      ext: "png",
      bytes: png,
      expected: { chunk_1: { keyword_and_text: "k\0é!" } },
      field: "root.header.chunk_1.keyword_and_text",
      start: 41,
      end: 45,
    },
  ];
  for (const { ext, bytes, expected, field, start, end } of cases) {
    const result = await page.evaluate(
      ({ schema, bytes }) => {
        const wasm = (window as unknown as { CStructSharpWasm: RawWasmAdapter }).CStructSharpWasm;
        return JSON.parse(
          wasm.parseWithDebug(schema.definition, new Uint8Array(bytes), {
            ...schema.parserOptions,
            root: "root",
          }),
        );
      },
      { schema: schemaForFile(ext), bytes: [...bytes] },
    );
    expect(result.success, `${ext}: ${JSON.stringify(result.error)}`).toBe(true);
    expect(result.data.header, ext).toMatchObject(expected);
    const ranges = result.debug.filter((entry: { path: string }) => entry.path === field);
    expect(Math.min(...ranges.map((entry: { start: number }) => entry.start)), field).toBe(start);
    expect(Math.max(...ranges.map((entry: { end: number }) => entry.end)), field).toBe(end);
  }
});

test("FBX node identifiers preserve raw code units across both header widths", async ({ page }) => {
  await page.goto("/");
  await expect(page.locator(".status-badge")).toContainText("Ready");
  for (const wide of [false, true]) {
    const start = 27 + (wide ? 25 : 13);
    const bytes = Buffer.alloc(start + 4);
    bytes.write("Kaydara FBX Binary  ");
    bytes[21] = 26;
    bytes.writeUInt32LE(wide ? 7500 : 7400, 23);
    if (wide) bytes.writeBigUInt64LE(BigInt(bytes.length), 27);
    else bytes.writeUInt32LE(bytes.length, 27);
    bytes[start - 1] = 4;
    bytes.set([65, 0, 255, 80], start);
    const schema = schemaForFile("fbx");
    const result = await page.evaluate(
      ({ schema, bytes }) => {
        const wasm = (window as unknown as { CStructSharpWasm: RawWasmAdapter }).CStructSharpWasm;
        const options = { ...schema.parserOptions, root: "root" };
        const parsed = JSON.parse(
          wasm.parseWithDebug(schema.definition, new Uint8Array(bytes), options),
        );
        return {
          parsed,
          encoded: parsed.success
            ? [...wasm.serialize(schema.definition, JSON.stringify(parsed.data), options)]
            : [],
        };
      },
      { schema, bytes: [...bytes] },
    );
    expect(result.parsed.success, JSON.stringify(result.parsed.error)).toBe(true);
    expect(result.parsed.data.header.first_node.name).toBe("A\0ÿP");
    expect(result.encoded).toEqual([...bytes]);
    const spans = result.parsed.debug.filter(
      (entry: { path: string }) => entry.path === "root.header.first_node.name",
    );
    expect(spans.map((entry: { start: number; end: number }) => [entry.start, entry.end])).toEqual(
      Array.from({ length: 4 }, (_, index) => [start + index, start + index + 1]),
    );
  }
});
