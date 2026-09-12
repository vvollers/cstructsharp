import { expect, test } from "@playwright/test";
import { schemaForFile } from "../../src/detected-schemas";
import type { RawWasmAdapter } from "../../src/wasm/cstruct-contract";

test("expanded layouts decode bitfields, records, dimensions and mixed-width pointers", async ({
  page,
}) => {
  await page.goto("/");
  await expect(page.locator(".status-badge")).toContainText("Ready");
  const flac = Buffer.alloc(42);
  flac.write("fLaC");
  flac[4] = 128;
  flac[7] = 34;
  flac.writeUIntBE(0xabcdef, 12, 3);
  flac.writeBigUInt64BE((48000n << 44n) | (1n << 41n) | (23n << 36n) | 123456n, 18);
  const stl = Buffer.alloc(134);
  stl.writeUInt32LE(1, 80);
  stl.writeFloatLE(1.5, 84);
  stl.writeFloatLE(2.5, 96);
  const zip = Buffer.alloc(33);
  zip.writeUInt32LE(0x04034b50);
  zip.writeUInt16LE(0x800, 6);
  zip.writeUInt32LE(2, 18);
  zip.writeUInt16LE(1, 26);
  zip[30] = 97;
  zip[31] = 11;
  zip[32] = 22;
  const glb = Buffer.alloc(24);
  glb.write("glTF");
  glb.writeUInt32LE(2, 4);
  glb.writeUInt32LE(24, 8);
  glb.writeUInt32LE(4, 12);
  glb.write("JSON", 16);
  glb.write("{}  ", 20);
  const elf = Buffer.alloc(120);
  elf[4] = 2;
  elf[5] = 1;
  elf.writeBigUInt64LE(64n, 32);
  elf.writeUInt16LE(56, 54);
  elf.writeUInt16LE(1, 56);
  elf.writeUInt32LE(1, 64);
  elf.writeBigUInt64LE(4096n, 104);
  const wav = Buffer.alloc(46);
  wav.write("RIFF");
  wav.writeUInt32LE(38, 4);
  wav.write("WAVEfmt ", 8);
  wav.writeUInt32LE(16, 16);
  wav.writeUInt16LE(1, 20);
  wav.writeUInt16LE(2, 22);
  wav.writeUInt32LE(44100, 24);
  wav.write("data", 36);
  wav.writeUInt32LE(2, 40);
  const png = Buffer.alloc(61);
  png.writeUInt32BE(13, 8);
  png.write("IHDR", 12);
  png.writeUInt32BE(1, 16);
  png.writeUInt32BE(1, 20);
  png.writeUInt32BE(4, 33);
  png.write("gAMA", 37);
  png.writeUInt32BE(45455, 41);
  png.write("IEND", 53);
  const font = Buffer.alloc(82);
  font.writeUInt16BE(1, 4);
  font.write("head", 12);
  font.writeUInt32BE(28, 20);
  font.writeUInt32BE(54, 24);
  font.writeInt32BE(98304, 32);
  font.writeUInt16BE(2048, 46);
  font.writeInt16BE(-120, 64);
  const wasmBytes = Buffer.from([0, 97, 115, 109, 1, 0, 0, 0, 1, 1, 0]);
  const zst = Buffer.from([0x28, 0xb5, 0x2f, 0xfd, 0x60, 0x2c, 1, 1, 0, 0]);
  const webp = Buffer.alloc(30);
  webp.write("RIFF");
  webp.writeUInt32LE(22, 4);
  webp.write("WEBPVP8X", 8);
  webp.writeUInt32LE(10, 16);
  webp.writeUIntLE(0x123456, 24, 3);
  webp.writeUIntLE(0xabcdef, 27, 3);
  const voc = Buffer.alloc(33);
  voc.write("Creative Voice File");
  voc.writeUInt16LE(26, 20);
  voc[26] = 2;
  voc[27] = 3;
  voc.set([10, 20, 30], 30);
  const cases = [
    {
      ext: "icc",
      bytes: (() => {
        const profile = Buffer.alloc(132);
        profile.writeInt32BE(65536, 68);
        profile.writeInt32BE(32768, 72);
        profile.writeInt32BE(-16384, 76);
        return profile;
      })(),
      expected: ['"illuminant_xyz":[1,0.5,-0.25]'],
    },
    {
      ext: "lnk",
      bytes: (() => {
        const link = Buffer.alloc(76);
        Buffer.from("0114020000000000c000000000000046", "hex").copy(link, 4);
        return link;
      })(),
      expected: ['"class_id":"00021401-0000-0000-c000-000000000046"'],
    },
    {
      ext: "webp",
      bytes: webp,
      expected: ['"width_minus_one_le":1193046', '"height_minus_one_le":11259375'],
    },
    { ext: "voc", bytes: voc, expected: ['"length":3', '"payload":[10,20,30]'] },
    {
      ext: "ttf",
      bytes: font,
      expected: ['"revision":1.5', '"units_per_em":2048', '"x_min":-120', '"Address":28'],
    },
    { ext: "wasm", bytes: wasmBytes, expected: ['"Name":"Type"', '"entry_count":0'] },
    { ext: "zst", bytes: zst, expected: ['"single_segment":1', '"content_size_minus_256":300'] },
    {
      ext: "flac",
      bytes: flac,
      expected: [
        '"sample_rate":48000',
        '"minimum_frame_size_be":11259375',
        '"metadata_length_be":34',
        '"total_samples":123456',
        '"channels_minus_one":1',
        '"bits_per_sample_minus_one":23',
      ],
    },
    { ext: "stl", bytes: stl, expected: ['"normal":[1.5,0,0]', '"vertices":[[2.5,0,0]'] },
    { ext: "zip", bytes: zip, expected: ['"utf8_names":1', '"compressed_payload":[11,22]'] },
    { ext: "glb", bytes: glb, expected: ['"Name":"Json"', '"json_data":"{}  "'] },
    { ext: "elf", bytes: elf, expected: ['"Address":64', '"memory_size":4096'] },
    { ext: "wav", bytes: wav, expected: ['"sample_rate":44100', '"Name":"Pcm"', '"Name":"Data"'] },
    { ext: "png", bytes: png, expected: ['"gamma_times_100000":45455', '"Name":"IEND"'] },
  ];
  for (const { ext, bytes, expected } of cases) {
    const schema = schemaForFile(ext, bytes);
    const result = await page.evaluate(
      ({ schema, bytes }) => {
        const wasm = (window as unknown as { CStructSharpWasm: RawWasmAdapter }).CStructSharpWasm;
        return JSON.parse(
          wasm.parseWithDebug(schema.definition, new Uint8Array(bytes), {
            ...schema.parserOptions,
            rootTypeName: "root",
            maxArrayElements: 1024,
          }),
        );
      },
      { schema, bytes: [...bytes] },
    );
    expect(result.Success, `${ext}: ${JSON.stringify(result.Error)}`).toBe(true);
    const json = JSON.stringify(JSON.parse(result.Data));
    for (const value of expected) expect(json, ext).toContain(value);
  }
});

test("RIFF native variants reselect metadata from bytes and preserve unknown chunks and padding", async ({
  page,
}) => {
  await page.goto("/");
  await expect(page.locator(".status-badge")).toContainText("Ready");
  const bytes = Buffer.alloc(78);
  bytes.write("RIFF");
  bytes.writeUInt32LE(70, 4);
  bytes.write("WAVEfmt ", 8);
  bytes.writeUInt32LE(40, 16);
  bytes.writeUInt16LE(65534, 20);
  bytes.writeUInt16LE(2, 22);
  bytes.writeUInt32LE(48000, 24);
  bytes.writeUInt16LE(22, 36);
  bytes.writeUInt16LE(24, 38);
  Buffer.from("0100000000001000800000aa00389b71", "hex").copy(bytes, 44);
  bytes.write("JUNK", 60);
  bytes.writeUInt32LE(1, 64);
  bytes[68] = 42;
  bytes[69] = 123;
  bytes.write("data", 70);
  const schema = schemaForFile("wav", bytes);
  for (const variant of ["extensible", "pcm", "invalid-extension", "unknown"] as const) {
    const input = Buffer.from(bytes);
    if (variant === "pcm") input.writeUInt16LE(1, 20);
    if (variant === "invalid-extension") input.writeUInt16LE(21, 36);
    if (variant === "unknown") input.write("JUNK", 12);
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
      { schema, bytes: [...input] },
    );
    expect(result.parsed.Success, JSON.stringify(result.parsed.Error)).toBe(true);
    expect(result.encoded).toEqual([...input]);
    const root = JSON.parse(result.parsed.Data).root.header;
    expect(root.chunk_1.payload).toEqual([42]);
    expect(root.chunk_1.padding).toEqual([123]);
    if (variant === "unknown") {
      expect(root.chunk_0.wave).toBeUndefined();
      expect(root.chunk_0.payload).toHaveLength(40);
    } else if (variant === "extensible") {
      expect(root.chunk_0.wave.subformat_guid).toBe("00000001-0000-0010-8000-00aa00389b71");
      expect(result.parsed.DebugData).toContainEqual(
        expect.objectContaining({
          DebugStackString: "root.header.chunk_0.wave.subformat_guid",
          CurPos: 44,
          EndPos: 60,
        }),
      );
    } else {
      expect(root.chunk_0.wave.subformat_guid).toBeUndefined();
      expect(root.chunk_0.wave.extension_payload).toHaveLength(22);
    }
  }
});

test("PNG native tags and lengths select metadata without regenerating the schema", async ({
  page,
}) => {
  await page.goto("/");
  await expect(page.locator(".status-badge")).toContainText("Ready");
  const png = (tag: string, payload: Buffer) => {
    const bytes = Buffer.alloc(57 + payload.length);
    bytes.writeUInt32BE(13, 8);
    bytes.write("IHDR", 12);
    bytes.writeUInt32BE(payload.length, 33);
    bytes.write(tag, 37);
    payload.copy(bytes, 41);
    bytes.write("IEND", 49 + payload.length);
    return bytes;
  };
  const schema = schemaForFile("png", png("gAMA", Buffer.from([0, 0, 177, 143])));
  for (const [tag, payload, member] of [
    ["gAMA", Buffer.from([0, 0, 177, 143]), "gAMA"],
    ["gAMA", Buffer.from([1, 2, 3]), "invalid_gAMA"],
    ["PLTE", Buffer.from([1, 2, 3, 4, 5, 6]), "colors"],
    ["PLTE", Buffer.from([1, 2]), "invalid_palette"],
    ["tEXt", Buffer.from("k\0é", "latin1"), "text"],
    ["IDAT", Buffer.from([255, 0, 128]), "payload"],
  ] as const) {
    const bytes = png(tag, payload);
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
    const chunk = JSON.parse(result.parsed.Data).root.header.chunk_0;
    expect(Object.keys(chunk)).toEqual(["length", "type", member, "crc32"]);
    if (tag === "gAMA" && payload.length === 4) {
      expect(chunk.gAMA.gamma_times_100000).toBe(45455);
      expect(result.parsed.DebugData).toContainEqual(
        expect.objectContaining({
          DebugStackString: "root.header.chunk_0.gAMA.gamma_times_100000",
          CurPos: 41,
          EndPos: 45,
        }),
      );
    }
    if (member === "colors")
      expect(chunk.colors).toEqual([
        { red: 1, green: 2, blue: 3 },
        { red: 4, green: 5, blue: 6 },
      ]);
    if (member === "text") expect(chunk.text).toBe("k\0é");
  }
});

test("PNG international text uses runtime compression flags and bounded UTF-8", async ({
  page,
}) => {
  await page.goto("/");
  await expect(page.locator(".status-badge")).toContainText("Ready");
  const payload = Buffer.from("Title\0\0\0en\0Titre\0€", "utf8");
  const bytes = Buffer.alloc(57 + payload.length);
  bytes.writeUInt32BE(13, 8);
  bytes.write("IHDR", 12);
  bytes.writeUInt32BE(payload.length, 33);
  bytes.write("iTXt", 37);
  payload.copy(bytes, 41);
  bytes.write("IEND", 49 + payload.length);
  const schema = schemaForFile("png", bytes);
  for (const compressed of [false, true]) {
    const input = Buffer.from(bytes);
    if (compressed) {
      input[47] = 1;
      input.fill(255, 41 + payload.length - 3, 41 + payload.length);
    }
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
      { schema, bytes: [...input] },
    );
    expect(result.parsed.Success, JSON.stringify(result.parsed.Error)).toBe(true);
    expect(result.encoded).toEqual([...input]);
    const text = JSON.parse(result.parsed.Data).root.header.chunk_0.international;
    expect(text.keyword).toBe("Title");
    expect(text.translated_keyword).toBe("Titre");
    if (compressed) {
      expect(text.compressed_text).toEqual([255, 255, 255]);
      expect(text.text).toBeUndefined();
    } else {
      expect(text.text).toBe("€");
      expect(text.compressed_text).toBeUndefined();
    }
  }
});

test("WebAssembly sections expose LEB128 counts, indexes and custom UTF-8 names", async ({
  page,
}) => {
  await page.goto("/");
  await expect(page.locator(".status-badge")).toContainText("Ready");
  const bytes = Buffer.from([
    0, 97, 115, 109, 1, 0, 0, 0, 3, 4, 2, 0, 172, 2, 8, 2, 172, 2, 12, 1, 2, 0, 5, 3, 226, 130, 172,
    255, 10, 2, 1, 0,
  ]);
  const schema = schemaForFile("wasm", bytes);
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
  const root = JSON.parse(result.parsed.Data).root.header;
  expect(root.section_0).toMatchObject({ function_count: 2, type_indices: [0, 300] });
  expect(root.section_1.start_function_index).toBe(300);
  expect(root.section_2.data_segment_count).toBe(2);
  expect(root.section_3).toMatchObject({ name: "€", custom_data: [255] });
  expect(root.section_4).toMatchObject({ entry_count: 1, entries_payload: [0] });
  expect(result.parsed.DebugData).toContainEqual(
    expect.objectContaining({
      DebugStackString: "root.header.section_0.type_indices",
      CurPos: 12,
      EndPos: 14,
    }),
  );
});

test("WebAssembly malformed metadata cannot borrow the next section during preview expansion", async ({
  page,
}) => {
  await page.goto("/");
  await expect(page.locator(".status-badge")).toContainText("Ready");
  for (const payload of [
    [1, 128],
    [128, 128, 128, 128, 16],
    [129, 8],
  ]) {
    const bytes = [0, 97, 115, 109, 1, 0, 0, 0, 3, payload.length, ...payload, 12, 1, 3];
    const schema = schemaForFile("wasm", new Uint8Array(bytes));
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
      { schema, bytes },
    );
    expect(result.Success, JSON.stringify(result.Error)).toBe(true);
    const root = JSON.parse(result.Data).root.header;
    expect(root.section_0.payload).toEqual(payload);
    expect(root.section_0.function_count).toBeUndefined();
    expect(root.section_1.data_segment_count).toBe(3);
  }
});

test("movie headers decode versioned times and mixed-scale fixed-point matrices", async ({
  page,
}) => {
  await page.goto("/");
  await expect(page.locator(".status-badge")).toContainText("Ready");
  const movie = (version: number) => {
    const wide = version === 1;
    const bytes = Buffer.alloc(wide ? 128 : 116);
    bytes.writeUInt32BE(bytes.length);
    bytes.write("moov", 4);
    bytes.writeUInt32BE(bytes.length - 8, 8);
    bytes.write("mvhd", 12);
    bytes[16] = version;
    if (wide) {
      bytes.writeBigUInt64BE(4294967297n, 20);
      bytes.writeUInt32BE(1000, 36);
      bytes.writeBigUInt64BE(4294967298n, 40);
    } else {
      bytes.writeUInt32BE(123, 20);
      bytes.writeUInt32BE(1000, 28);
      bytes.writeUInt32BE(4242, 32);
    }
    const values = wide ? 48 : 36;
    bytes.writeInt32BE(98304, values);
    bytes.writeInt16BE(128, values + 4);
    const matrix = values + 16;
    const raw = [65536, -32768, 268435456, 0, 65536, 0, 655360, -131072, 1073741824];
    raw.forEach((value, index) => bytes.writeInt32BE(value, matrix + index * 4));
    return bytes;
  };
  const schema = schemaForFile("mov", movie(0));
  for (const version of [0, 1, 2]) {
    const bytes = movie(version);
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
    const header = JSON.parse(result.parsed.Data).root.header.box_0.child_0;
    if (version === 2) {
      expect(header.unknown_version).toHaveLength(96);
      continue;
    }
    expect(header[`version_${version}`].creation_time).toBe(version === 1 ? 4294967297 : 123);
    expect(header[`version_${version}`].duration).toBe(version === 1 ? 4294967298 : 4242);
    expect(header[`version_${version}`].time_scale).toBe(1000);
    const values = header[`version_${version}`].values;
    expect(values.preferred_rate).toBe(1.5);
    expect(values.preferred_volume_q8).toBe(128);
    expect(values.matrix).toEqual([
      { a: 1, b: -0.5, perspective: 0.25 },
      { a: 0, b: 1, perspective: 0 },
      { a: 10, b: -2, perspective: 1 },
    ]);
    const start = version === 1 ? 72 : 60;
    expect(result.parsed.DebugData).toContainEqual(
      expect.objectContaining({
        DebugStackString: `root.header.box_0.child_0.version_${version}.values.matrix[0].perspective`,
        CurPos: start,
        EndPos: start + 4,
      }),
    );
  }
});

test("BMP calibration uses fixed-point values only for calibrated RGB", async ({ page }) => {
  await page.goto("/");
  await expect(page.locator(".status-badge")).toContainText("Ready");
  for (const size of [108, 124]) {
    const bytes = Buffer.alloc(14 + size);
    bytes.write("BM");
    bytes.writeUInt32LE(bytes.length, 2);
    bytes.writeUInt32LE(size, 14);
    bytes.writeInt32LE(536870912, 74);
    bytes.writeInt32LE(-268435456, 78);
    bytes.writeUInt32LE(98304, 110);
    bytes.writeUInt32LE(131072, 114);
    bytes.writeUInt32LE(163840, 118);
    const schema = schemaForFile("bmp", bytes);
    for (const calibrated of [true, false]) {
      const input = Buffer.from(bytes);
      if (!calibrated) input.writeUInt32LE(0x73524742, 70);
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
        { schema, bytes: [...input] },
      );
      expect(result.parsed.Success, JSON.stringify(result.parsed.Error)).toBe(true);
      expect(result.encoded).toEqual([...input]);
      const root = JSON.parse(result.parsed.Data).root.header;
      if (calibrated) {
        expect(root.endpoints_xyz[0]).toEqual([0.5, -0.25, 0]);
        expect([root.gamma_red, root.gamma_green, root.gamma_blue]).toEqual([1.5, 2, 2.5]);
        expect(result.parsed.DebugData).toContainEqual(
          expect.objectContaining({
            DebugStackString: "root.header.gamma_red",
            CurPos: 110,
            EndPos: 114,
          }),
        );
      } else {
        expect(root.endpoints_xyz).toBeUndefined();
        expect(root.unused_color_calibration).toEqual([...input.subarray(74, 122)]);
      }
    }
  }
});

test("Palm record IDs use three-byte big-endian integers with exact array strides", async ({
  page,
}) => {
  await page.goto("/");
  await expect(page.locator(".status-badge")).toContainText("Ready");
  const bytes = Buffer.alloc(94);
  bytes.writeUInt16BE(2, 76);
  bytes.writeUInt32BE(100, 78);
  bytes[82] = 64;
  bytes.writeUIntBE(0x123456, 83, 3);
  bytes.writeUInt32BE(200, 86);
  bytes[90] = 128;
  bytes.writeUIntBE(0xffffff, 91, 3);
  const schema = schemaForFile("mobi", bytes);
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
  expect(JSON.parse(result.parsed.Data).root.header.records).toEqual([
    { offset: 100, attributes: 64, unique_id: 0x123456 },
    { offset: 200, attributes: 128, unique_id: 0xffffff },
  ]);
  expect(result.encoded).toEqual([...bytes]);
  expect(result.parsed.DebugData).toContainEqual(
    expect.objectContaining({
      DebugStackString: "root.header.records[1].unique_id",
      CurPos: 91,
      EndPos: 94,
    }),
  );
});
