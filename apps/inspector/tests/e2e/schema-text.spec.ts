import { expect, test } from "@playwright/test";
import { schemaForFile } from "../../src/detected-schemas";
import type { RawWasmAdapter } from "../../src/wasm/cstruct-contract";

test("bounded legacy and UTF-16 encodings survive the real WASM writer", async ({ page }) => {
  await page.goto("/");
  await expect(page.locator(".status-badge")).toContainText("Ready");
  const results = await page.evaluate(() => {
    const wasm = (window as unknown as { CStructSharpWasm: RawWasmAdapter }).CStructSharpWasm;
    return [
      { type: "cp437", text: "é─", bytes: [0x82, 0xc4] },
      { type: "latin1", text: "éÿ", bytes: [0xe9, 0xff] },
      { type: "utf16le", text: "🌍", bytes: [0x3c, 0xd8, 0x0d, 0xdf] },
      { type: "utf16be", text: "🌍", bytes: [0xd8, 0x3c, 0xdf, 0x0d] },
    ].map(({ type, text, bytes }) => {
      const definition = `struct root { ${type} text[${bytes.length}]; uint8 tail; };`;
      const options = { rootTypeName: "root", aligned: false };
      const data = new Uint8Array([...bytes, 99]);
      const parsed = JSON.parse(wasm.parseWithDebug(definition, data, options));
      const written = [...wasm.serialize(definition, JSON.stringify({ text, tail: 99 }), options)];
      const updated = [
        ...wasm.updateStream(definition, data, "root.text", JSON.stringify(""), options),
      ];
      return { parsed, written, updated, text, bytes };
    });
  });
  for (const result of results) {
    expect(result.parsed.Success).toBe(true);
    expect(JSON.parse(result.parsed.Data).root).toEqual({ text: result.text, tail: 99 });
    expect(result.written).toEqual([...result.bytes, 99]);
    expect(result.updated).toEqual([...result.bytes.map(() => 0), 99]);
  }
});

test("bounded UTF-8 works through the WASM parse, serialize and update bridge", async ({
  page,
}) => {
  await page.goto("/");
  await expect(page.locator(".status-badge")).toContainText("Ready");
  const result = await page.evaluate(() => {
    const wasm = (window as unknown as { CStructSharpWasm: RawWasmAdapter }).CStructSharpWasm;
    const schema = "struct root { uint8 length; utf8 name[length]; uint8 tail; };";
    const options = { rootTypeName: "root", aligned: false };
    const bytes = new Uint8Array([6, ...new TextEncoder().encode("é🌍"), 99]);
    const parsed = JSON.parse(wasm.parseWithDebug(schema, bytes, options));
    const serialized = wasm.serialize(
      schema,
      JSON.stringify({ length: 6, name: "é🌍", tail: 99 }),
      options,
    );
    const updated = wasm.updateStream(schema, bytes, "root.name", JSON.stringify("你好"), options);
    const invalid = JSON.parse(
      wasm.parseWithDebug(
        "struct root { utf8 name[1]; uint8 tail; };",
        new Uint8Array([0xc3, 0xa9]),
        options,
      ),
    );
    return {
      parsed,
      serialized: [...serialized],
      updated: JSON.parse(wasm.parseWithDebug(schema, updated, options)),
      invalid,
    };
  });
  expect(result.parsed.Success).toBe(true);
  expect(JSON.parse(result.parsed.Data).root).toEqual({ length: 6, name: "é🌍", tail: 99 });
  expect(result.serialized).toEqual([6, 0xc3, 0xa9, 0xf0, 0x9f, 0x8c, 0x8d, 99]);
  expect(result.updated.Success).toBe(true);
  expect(JSON.parse(result.updated.Data).root).toEqual({ length: 6, name: "你好", tail: 99 });
  expect(result.parsed.DebugData).toContainEqual(
    expect.objectContaining({ DebugStackString: "root.name", CurPos: 1, EndPos: 7 }),
  );
  expect(result.invalid.Error.Code).toBe("read-failed");
});

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
      expected: { entries: [{ filename_cp437: "a.txt", extra: [255], compressed_payload: [128] }] },
      field: "root.header.entries[0].filename_cp437",
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
      expected: { chunk_0: { text: "k\0é!" } },
      field: "root.header.chunk_0.text",
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
            rootTypeName: "root",
          }),
        );
      },
      { schema: schemaForFile(ext, bytes), bytes: [...bytes] },
    );
    expect(result.Success, `${ext}: ${JSON.stringify(result.Error)}`).toBe(true);
    expect(JSON.parse(result.Data).root.header, ext).toMatchObject(expected);
    const ranges = result.DebugData.filter(
      (entry: { DebugStackString: string }) => entry.DebugStackString === field,
    );
    expect(Math.min(...ranges.map((entry: { CurPos: number }) => entry.CurPos)), field).toBe(start);
    expect(Math.max(...ranges.map((entry: { EndPos: number }) => entry.EndPos)), field).toBe(end);
  }
});

test("Ogg second-page comment packets decode bounded UTF-8 metadata", async ({ page }) => {
  await page.goto("/");
  await expect(page.locator(".status-badge")).toContainText("Ready");
  const oggPage = (payload: Buffer, sequence: number) => {
    const bytes = Buffer.alloc(28 + payload.length);
    bytes.write("OggS");
    bytes.writeUInt32LE(42, 14);
    bytes.writeUInt32LE(sequence, 18);
    bytes[26] = 1;
    bytes[27] = payload.length;
    payload.copy(bytes, 28);
    return bytes;
  };
  for (const opus of [false, true]) {
    const identification = Buffer.alloc(opus ? 19 : 30);
    if (opus) identification.write("OpusHead");
    else {
      identification[0] = 1;
      identification.write("vorbis", 1);
    }
    const prefix = Buffer.from(opus ? "OpusTags" : "\x03vorbis");
    const vendor = Buffer.from("工具", "utf8"),
      comment = Buffer.from("TITLE=é🌍", "utf8");
    const content = Buffer.alloc(
      prefix.length + 4 + vendor.length + 4 + 4 + comment.length + (opus ? 0 : 1),
    );
    prefix.copy(content);
    content.writeUInt32LE(vendor.length, prefix.length);
    vendor.copy(content, prefix.length + 4);
    const countAt = prefix.length + 4 + vendor.length;
    content.writeUInt32LE(1, countAt);
    content.writeUInt32LE(comment.length, countAt + 4);
    comment.copy(content, countAt + 8);
    if (!opus) content[content.length - 1] = 1;
    const bytes = Buffer.concat([oggPage(identification, 0), oggPage(content, 1)]);
    const secondPage = 28 + identification.length;
    for (const invalid of ["continued", "different-stream", "oversized-vendor"] as const) {
      const altered = Buffer.from(bytes);
      if (invalid === "continued") altered[secondPage + 5] = 1;
      if (invalid === "different-stream") altered.writeUInt32LE(43, secondPage + 14);
      if (invalid === "oversized-vendor")
        altered.writeUInt32LE(0xffffffff, secondPage + 28 + prefix.length);
      expect(schemaForFile(opus ? "opus" : "ogg", altered).definition, invalid).not.toContain(
        "ogg_comment_header",
      );
    }
    const schema = schemaForFile(opus ? "opus" : "ogg", bytes);
    const result = await page.evaluate(
      ({ schema, bytes }) => {
        const wasm = (window as unknown as { CStructSharpWasm: RawWasmAdapter }).CStructSharpWasm;
        const options = { ...schema.parserOptions, rootTypeName: "root" };
        const parsed = JSON.parse(
          wasm.parseWithDebug(schema.definition, new Uint8Array(bytes), options),
        );
        return {
          parsed,
          encoded: parsed.Success
            ? [
                ...wasm.serialize(
                  schema.definition,
                  JSON.stringify(JSON.parse(parsed.Data).root),
                  options,
                ),
              ]
            : [],
        };
      },
      { schema, bytes: [...bytes] },
    );
    expect(result.parsed.Success, JSON.stringify(result.parsed.Error)).toBe(true);
    expect(result.encoded).toEqual([...bytes]);
    expect(JSON.parse(result.parsed.Data).root.header.comment_header).toMatchObject({
      vendor: "工具",
      comment_count: 1,
      comments: [{ text: "TITLE=é🌍" }],
    });
    const start = 28 + identification.length + 28 + prefix.length + 4;
    expect(result.parsed.DebugData).toContainEqual(
      expect.objectContaining({
        DebugStackString: "root.header.comment_header.vendor",
        CurPos: start,
        EndPos: start + vendor.length,
      }),
    );
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
    const schema = schemaForFile("fbx", bytes);
    const result = await page.evaluate(
      ({ schema, bytes }) => {
        const wasm = (window as unknown as { CStructSharpWasm: RawWasmAdapter }).CStructSharpWasm;
        const options = { ...schema.parserOptions, rootTypeName: "root" };
        const parsed = JSON.parse(
          wasm.parseWithDebug(schema.definition, new Uint8Array(bytes), options),
        );
        return {
          parsed,
          encoded: parsed.Success
            ? [
                ...wasm.serialize(
                  schema.definition,
                  JSON.stringify(JSON.parse(parsed.Data).root),
                  options,
                ),
              ]
            : [],
        };
      },
      { schema, bytes: [...bytes] },
    );
    expect(result.parsed.Success, JSON.stringify(result.parsed.Error)).toBe(true);
    expect(JSON.parse(result.parsed.Data).root.header.first_node.name).toBe("A\0ÿP");
    expect(result.encoded).toEqual([...bytes]);
    const spans = result.parsed.DebugData.filter(
      (entry: { DebugStackString: string }) =>
        entry.DebugStackString === "root.header.first_node.name",
    );
    expect(
      spans.map((entry: { CurPos: number; EndPos: number }) => [entry.CurPos, entry.EndPos]),
    ).toEqual(Array.from({ length: 4 }, (_, index) => [start + index, start + index + 1]));
  }
});
