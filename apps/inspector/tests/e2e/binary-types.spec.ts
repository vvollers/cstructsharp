import { expect, test } from "@playwright/test";
import { schemaForFile } from "../../src/schema-catalog";
import type { RawWasmAdapter } from "../../src/wasm/cstruct-contract";

test("conditional update reachability and anonymous inactive fields survive WASM trimming", async ({
  page,
}) => {
  await page.goto("/");
  await expect(page.locator(".status-badge")).toContainText("Ready");
  const result = await page.evaluate(() => {
    const wasm = (window as unknown as { CStructSharpWasm: RawWasmAdapter }).CStructSharpWasm;
    const options = { root: "root", aligned: false };
    const unrelated =
      "struct unused { uint8 tag; if (tag) { uint16 payload; } }; struct root { uint8 value; utf8 trailing[1]; };";
    const updated = [
      ...wasm.updateStream(unrelated, new Uint8Array([1, 255]), "root.value", "42", options),
    ];
    const promoted =
      "struct root { uint8 tag; if (tag) { struct { struct { uint8 value; }; }; } uint8 tail; };";
    let rejected = false;
    try {
      wasm.serialize(promoted, JSON.stringify({ tag: 0, value: 42, tail: 99 }), options);
    } catch {
      rejected = true;
    }
    const encoded = [
      ...wasm.serialize(promoted, JSON.stringify({ tag: 1, value: 42, tail: 99 }), options),
    ];
    const parsed = JSON.parse(wasm.parseWithDebug(promoted, new Uint8Array(encoded), options));
    return { updated, rejected, encoded, parsed };
  });
  expect(result.updated).toEqual([42, 255]);
  expect(result.rejected).toBe(true);
  expect(result.encoded).toEqual([1, 42, 99]);
  expect(result.parsed.success).toBe(true);
  expect(result.parsed.data).toEqual({ tag: 1, value: 42, tail: 99 });
});

test("conditional groups retain outer discriminators across nested fields and array elements", async ({
  page,
}) => {
  await page.goto("/");
  await expect(page.locator(".status-badge")).toContainText("Ready");
  const result = await page.evaluate(() => {
    const wasm = (window as unknown as { CStructSharpWasm: RawWasmAdapter }).CStructSharpWasm;
    const definition =
      "struct entry { uint8 tag; switch(tag) { case 1: { struct { uint8 tag; } child; uint8 value; } default: { uint16 other; } } if(tag == 1) { uint8 trailer; } }; struct root { entry items[2]; };";
    const options = { root: "root", aligned: false };
    const bytes = new Uint8Array([1, 0, 42, 88, 0, 52, 18]);
    const parsed = JSON.parse(wasm.parseWithDebug(definition, bytes, options));
    const encoded = wasm.serialize(definition, JSON.stringify(parsed.data), options);
    const changed = wasm.updateStream(definition, bytes, "root.items[0].child.tag", "2", options);
    return {
      parsed,
      encoded: [...encoded],
      updated: JSON.parse(wasm.parseWithDebug(definition, changed, options)),
    };
  });
  expect(result.parsed.success).toBe(true);
  expect(result.encoded).toEqual([1, 0, 42, 88, 0, 52, 18]);
  expect(result.updated.data.items).toEqual([
    { tag: 1, child: { tag: 2 }, value: 42, trailer: 88 },
    { tag: 0, other: 4660 },
  ]);
  expect(result.parsed.debug).toContainEqual(
    expect.objectContaining({ path: "root.items[0].trailer", start: 3, end: 4 }),
  );
});

test("PE and GLB schemas select alternatives from the loaded bytes", async ({ page }) => {
  await page.goto("/");
  await expect(page.locator(".status-badge")).toContainText("Ready");
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

test("identifiers and fixed-point values round-trip through WASM", async ({ page }) => {
  await page.goto("/");
  await expect(page.locator(".status-badge")).toContainText("Ready");
  const result = await page.evaluate(() => {
    const wasm = (window as unknown as { CStructSharpWasm: RawWasmAdapter }).CStructSharpWasm;
    const definition =
      "typedef fixed16_16> revision_type; struct root { uuid network; guid windows; revision_type revision; ufixed8_8< volume; fixed2_30< matrix; };";
    const options = { root: "root", aligned: false };
    const id = "00112233-4455-6677-8899-aabbccddeeff";
    const value = { network: id, windows: id, revision: -1.5, volume: 0.5, matrix: -1.25 };
    const bytes = wasm.serialize(definition, JSON.stringify(value), options);
    const parsed = JSON.parse(wasm.parseWithDebug(definition, bytes, options));
    const updated = wasm.updateStream(
      definition,
      bytes,
      "root.windows",
      JSON.stringify("00000000-0000-0000-0000-000000000000"),
      options,
    );
    return {
      value,
      parsed,
      bytes: [...bytes],
      updated: JSON.parse(wasm.parseWithDebug(definition, updated, options)),
    };
  });
  expect(result.parsed.success).toBe(true);
  expect(result.parsed.data).toEqual(result.value);
  expect(result.bytes.slice(0, 8)).toEqual([0, 17, 34, 51, 68, 85, 102, 119]);
  expect(result.bytes.slice(16, 24)).toEqual([51, 34, 17, 0, 85, 68, 119, 102]);
  expect(result.updated.data.windows).toBe("00000000-0000-0000-0000-000000000000");
  expect(result.parsed.debug).toContainEqual(
    expect.objectContaining({ path: "root.windows", start: 16, end: 32 }),
  );
});

test("LEB128 preserves 64-bit values and rejects extent-changing updates in WASM", async ({
  page,
}) => {
  await page.goto("/");
  await expect(page.locator(".status-badge")).toContainText("Ready");
  const result = await page.evaluate(() => {
    const wasm = (window as unknown as { CStructSharpWasm: RawWasmAdapter }).CStructSharpWasm;
    const definition =
      "struct root { uleb128_64 unsigned_value; sleb128_64 signed_value; uint8 tail; };";
    const options = { root: "root", aligned: false };
    const value = {
      unsigned_value: "18446744073709551615",
      signed_value: "-9223372036854775808",
      tail: 99,
    };
    const bytes = wasm.serialize(definition, JSON.stringify(value), options);
    const parsed = JSON.parse(wasm.parseWithDebug(definition, bytes, options));
    const changed = wasm.updateStream(
      definition,
      bytes,
      "root.unsigned_value",
      JSON.stringify("18446744073709551614"),
      options,
    );
    let rejected = false;
    try {
      wasm.updateStream(definition, bytes, "root.unsigned_value", "1", options);
    } catch {
      rejected = true;
    }
    return {
      parsed,
      value,
      length: bytes.length,
      changed: JSON.parse(wasm.parseWithDebug(definition, changed, options)),
      rejected,
    };
  });
  expect(result.parsed.success).toBe(true);
  expect(result.parsed.data).toEqual(result.value);
  expect(result.length).toBe(21);
  expect(result.rejected).toBe(true);
  expect(result.changed.data.unsigned_value).toBe("18446744073709551614");
  expect(result.parsed.debug).toContainEqual(
    expect.objectContaining({ path: "root.signed_value", start: 10, end: 20 }),
  );
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
