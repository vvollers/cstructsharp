import { expect, test } from "@playwright/test";
import { schemaForFile } from "../../src/schema-catalog";
import type { RawWasmAdapter } from "@cstructsharp/app-shared/wasm/contract";

test("PE and GLB schemas select alternatives from the loaded bytes", async ({ page }) => {
  await page.goto("/");
  await expect(page.locator(".status-badge")).toContainText("Ready");
  /**
   * Builds a minimal PE file whose optional header magic selects PE32 or PE32+ (64-bit).
   * @param wide Whether to build the PE32+ variant.
   */
  const pe = (wide: boolean) => {
    const size = wide ? 112 : 96;
    const bytes = Buffer.alloc(88 + size);
    bytes.write("MZ");
    bytes.writeUInt32LE(64, 60);
    bytes.write("PE\0\0", 64);
    bytes.writeUInt16LE(size, 84);
    bytes.writeUInt16LE(wide ? 0x20b : 0x10b, 88);
    return bytes;
  };
  /**
   * Builds a minimal GLB file whose first chunk is a JSON or a binary chunk.
   * @param json Whether the first chunk type is JSON.
   */
  const glb = (json: boolean) => {
    const bytes = Buffer.alloc(24);
    bytes.write("glTF");
    bytes.writeUInt32LE(2, 4);
    bytes.writeUInt32LE(24, 8);
    bytes.writeUInt32LE(4, 12);
    bytes.writeUInt32LE(json ? 0x4e4f534a : 0x004e4942, 16);
    bytes.write("{}  ", 20);
    return bytes;
  };
  for (const [ext, first, second, firstName, secondName] of [
    ["exe", pe(false), pe(true), "pe32", "pe64"],
    ["glb", glb(true), glb(false), "json_data", "binary_data"],
  ] as const) {
    const schema = schemaForFile(ext);
    for (const [bytes, active, inactive] of [
      [first, firstName, secondName],
      [second, secondName, firstName],
    ] as const) {
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
        { schema, bytes: [...bytes] },
      );
      expect(result.success, JSON.stringify(result.error)).toBe(true);
      expect(JSON.stringify(result.data)).toContain(`"${active}"`);
      expect(JSON.stringify(result.data)).not.toContain(`"${inactive}"`);
    }
  }
});

test("one CRX schema selects versioned headers from runtime bytes", async ({ page }) => {
  await page.goto("/");
  await expect(page.locator(".status-badge")).toContainText("Ready");
  const v2 = Buffer.alloc(19);
  v2.write("Cr24");
  v2.writeUInt32LE(2, 4);
  v2.writeUInt32LE(1, 8);
  v2.writeUInt32LE(2, 12);
  v2.set([11, 22, 33], 16);
  const v3 = Buffer.alloc(14);
  v3.write("Cr24");
  v3.writeUInt32LE(3, 4);
  v3.writeUInt32LE(2, 8);
  v3.set([44, 55], 12);
  const schema = schemaForFile("crx");
  for (const bytes of [v2, v3]) {
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
      { schema, bytes: [...bytes] },
    );
    expect(result.success).toBe(true);
    const header = result.data.header;
    if (bytes === v2) expect(header.signature_bytes).toEqual([22, 33]);
    else expect(header.signed_header).toEqual([44, 55]);
  }
});

test("ZIP selects each entry's own text encoding with native conditions", async ({ page }) => {
  await page.goto("/");
  await expect(page.locator(".status-badge")).toContainText("Ready");
  /**
   * Builds a ZIP local file header whose name is UTF-8 (flag bit 11 set) or code page 437.
   * @param utf8 Whether the entry name uses UTF-8.
   */
  const entry = (utf8: boolean) => {
    const name = utf8 ? Buffer.from("é.txt") : Buffer.from([0x82, 46, 116, 120, 116]);
    const bytes = Buffer.alloc(30 + name.length);
    bytes.writeUInt32LE(0x04034b50);
    bytes.writeUInt16LE(utf8 ? 0x800 : 0, 6);
    bytes.writeUInt16LE(name.length, 26);
    name.copy(bytes, 30);
    return bytes;
  };
  for (const utf8 of [true, false]) {
    const bytes = entry(utf8);
    const schema = schemaForFile("zip");
    expect(schema.definition).toContain("if (utf8_names)");
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
      { schema, bytes: [...bytes] },
    );
    expect(result.success).toBe(true);
    const local = result.data.header.local;
    expect(local[utf8 ? "filename_utf8" : "filename_cp437"]).toBe("é.txt");
    expect(local).not.toHaveProperty(utf8 ? "filename_cp437" : "filename_utf8");
  }
});

test("PNG international text stays opaque without external separator scans", async ({ page }) => {
  await page.goto("/");
  await expect(page.locator(".status-badge")).toContainText("Ready");
  const payload = Buffer.concat([Buffer.from("Title\0\0\0nl\0"), Buffer.from("标题\0你好🌍")]);
  for (const compressed of [false, true]) {
    const bytes = Buffer.alloc(33 + 12 + payload.length + 12);
    bytes.writeUInt32BE(13, 8);
    bytes.write("IHDR", 12);
    bytes.writeUInt32BE(payload.length, 33);
    bytes.write("iTXt", 37);
    payload.copy(bytes, 41);
    if (compressed) bytes[47] = 1;
    bytes.write("IEND", bytes.length - 8);
    const schema = schemaForFile("png");
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
      { schema, bytes: [...bytes] },
    );
    expect(result.success).toBe(true);
    expect(result.data.header.chunk_1.payload).toEqual([
      ...bytes.subarray(41, 41 + payload.length),
    ]);
  }
});
