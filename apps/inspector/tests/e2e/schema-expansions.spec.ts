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
  font.writeUInt16BE(2048, 46);
  font.writeInt16BE(-120, 64);
  const wasmBytes = Buffer.from([0, 97, 115, 109, 1, 0, 0, 0, 1, 1, 0]);
  const zst = Buffer.from([0x28, 0xb5, 0x2f, 0xfd, 0x60, 0x2c, 1, 1, 0, 0]);
  const cases = [
    { ext: "ttf", bytes: font, expected: ['"units_per_em":2048', '"x_min":-120', '"Address":28'] },
    { ext: "wasm", bytes: wasmBytes, expected: ['"Name":"Type"', '"payload":[0]'] },
    { ext: "zst", bytes: zst, expected: ['"single_segment":1', '"content_size_minus_256":300'] },
    {
      ext: "flac",
      bytes: flac,
      expected: [
        '"sample_rate":48000',
        '"total_samples":123456',
        '"channels_minus_one":1',
        '"bits_per_sample_minus_one":23',
      ],
    },
    { ext: "stl", bytes: stl, expected: ['"normal":[1.5,0,0]', '"vertices":[[2.5,0,0]'] },
    { ext: "zip", bytes: zip, expected: ['"utf8_names":1', '"compressed_payload":[11,22]'] },
    { ext: "glb", bytes: glb, expected: ['"Name":"Json"', '"data":"{}  "'] },
    { ext: "elf", bytes: elf, expected: ['"Address":64', '"memory_size":4096'] },
    { ext: "wav", bytes: wav, expected: ['"sample_rate":44100', '"Name":"Pcm"', '"type":"data"'] },
    { ext: "png", bytes: png, expected: ['"gamma_times_100000":45455', '"type":"IEND"'] },
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
