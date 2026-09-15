import { expect, test } from "@playwright/test";
import { schemaForFile } from "../../src/schema-catalog";
import { inspectionFiles, inspectionVariants } from "../fixtures/inspection-files";
import type { RawWasmAdapter } from "../../src/wasm/cstruct-contract";

test.beforeEach(async ({ page }) => {
  await page.goto("/");
  await expect(page.locator(".status-badge")).toContainText("Ready");
});

test("common formats parse one standalone definition against the complete source", async ({
  page,
}) => {
  const files = inspectionFiles();
  for (const ext of ["png", "jpg", "zip", "exe", "elf", "pdf"]) {
    const schema = schemaForFile(ext);
    const result = await page.evaluate(
      async ({ schema, bytes }) => {
        const wasm = (window as unknown as { CStructSharpWasm: RawWasmAdapter }).CStructSharpWasm;
        return wasm.parseSource(
          schema.definition,
          new Blob([new Uint8Array(bytes)]),
          { ...schema.parserOptions, rootTypeName: "root" },
          true,
        );
      },
      { schema, bytes: [...files[ext]!] },
    );
    expect(result.Success, `${ext}: ${JSON.stringify(result.Error)}`).toBe(true);
    const header = result.Data.root.header;
    if (ext === "png") {
      expect(header.chunk_0.width).toBe(320);
      expect(header.chunk_2.gamma_times_100000).toBe(45455);
      expect(header.chunk_3.colors[0]).toEqual({ red: 10, green: 20, blue: 30 });
      expect(result.DebugData).toContainEqual(
        expect.objectContaining({
          DebugStackString: "root.header.chunk_2.gamma_times_100000",
          CurPos: 70053,
          EndPos: 70057,
        }),
      );
    }
    if (ext === "jpg") {
      expect(header.segment_0.coefficients).toHaveLength(64);
      expect(header.segment_2.progressive).toMatchObject({
        width: 320,
        height: 240,
        components: [{ horizontal_sampling: 2, vertical_sampling: 1 }],
      });
      expect(header.segment_3.marker).toBe(0xffda);
      expect(header).not.toHaveProperty("segment_4");
    }
    if (ext === "zip")
      expect(header.local).toMatchObject({ filename_cp437: "a", flags: { data_descriptor: 1 } });
    if (ext === "exe") {
      expect(header.pe.Address).toBe(66000);
      expect(header.pe.Value.pe32.directories).toHaveLength(16);
      expect(header.pe.Value.sections[0].raw_data).toMatchObject({
        Address: 70000,
        IsDereferenced: true,
      });
    }
    if (ext === "elf") expect(header.header64_le.section_headers_offset).toBe(66000);
    if (ext === "pdf") expect(header).toEqual({ signature: "%PDF-", version: "1.7" });
  }
});

test("the same definitions select PE32+, big-endian ELF and PDF header variants", async ({
  page,
}) => {
  for (const variant of inspectionVariants().filter((v) => v.ext !== "zip")) {
    const schema = schemaForFile(variant.ext);
    const result = await page.evaluate(
      async ({ schema, bytes }) => {
        const wasm = (window as unknown as { CStructSharpWasm: RawWasmAdapter }).CStructSharpWasm;
        return wasm.parseSource(
          schema.definition,
          new Blob([new Uint8Array(bytes)]),
          { ...schema.parserOptions, rootTypeName: "root" },
          false,
        );
      },
      { schema, bytes: [...variant.bytes] },
    );
    expect(result.Success, `${variant.name}: ${JSON.stringify(result.Error)}`).toBe(true);
    const header = result.Data.root.header;
    if (variant.ext === "exe") expect(header.pe.Value.pe64.image_base).toBe(5368709120);
    if (variant.ext === "elf") expect(header.header64_be.section_headers_offset).toBe(66000);
    if (variant.ext === "pdf") expect(header.version).toBe("1.7");
  }
});

test("PNG branches follow changed tags and lengths without regenerating the layout", async ({
  page,
}) => {
  const schema = schemaForFile("png");
  for (const valid of [true, false]) {
    const gamma = Buffer.alloc(8 + (valid ? 16 : 15) + 12);
    gamma.writeUInt32BE(valid ? 4 : 3, 8);
    gamma.write("gAMA", 12);
    if (valid) gamma.writeUInt32BE(45455, 16);
    gamma.write("IEND", gamma.length - 8);
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
      { schema, bytes: [...gamma] },
    );
    expect(result.Success, JSON.stringify(result.Error)).toBe(true);
    if (valid) expect(result.Data.root.header.chunk_0.gamma_times_100000).toBe(45455);
    else expect(result.Data.root.header.chunk_0.invalid_gamma).toHaveLength(3);
    expect(result.Data.root.header).not.toHaveProperty("chunk_2");
  }
});

test("binary STL reads every declared triangle instead of a preview cap", async ({ page }) => {
  const bytes = Buffer.alloc(84 + 129 * 50);
  bytes.writeUInt32LE(129, 80);
  bytes.writeFloatLE(3.5, 84 + 128 * 50);
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
    { schema: schemaForFile("stl"), bytes: [...bytes] },
  );
  expect(result.Success, JSON.stringify(result.Error)).toBe(true);
  expect(result.Data.root.header.triangles).toHaveLength(129);
  expect(result.Data.root.header.triangles[128].normal[0]).toBe(3.5);
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
    const schema = schemaForFile("bmp");
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
              ? [...wasm.serialize(schema.definition, JSON.stringify(parsed.Data.root), options)]
              : [],
          };
        },
        { schema, bytes: [...input] },
      );
      expect(result.parsed.Success, JSON.stringify(result.parsed.Error)).toBe(true);
      expect(result.encoded).toEqual([...input]);
      const root = result.parsed.Data.root.header;
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
  const schema = schemaForFile("mobi");
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
          ? [...wasm.serialize(schema.definition, JSON.stringify(parsed.Data.root), options)]
          : [],
      };
    },
    { schema, bytes: [...bytes] },
  );
  expect(result.parsed.Success, JSON.stringify(result.parsed.Error)).toBe(true);
  expect(result.parsed.Data.root.header.records).toEqual([
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
