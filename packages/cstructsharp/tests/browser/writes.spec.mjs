/**
 * The write paths of the trimmed WebAssembly runtime, through the public API: conditional members, identifiers,
 * fixed-point and variable-length integers, and bounded text encodings survive serialize and update. The runtime is
 * published with full trimming, so a write path only the managed tests exercise could otherwise be removed from it.
 */
import { expect, test } from "@playwright/test";

test.beforeEach(async ({ page }) => {
  await page.goto("/tools/binary/bridge.html");
  await expect(page.locator("body")).toHaveAttribute("data-ready", "true", { timeout: 60_000 });
});

test("conditional members: update reachability, anonymous inactive fields and outer discriminators", async ({ page }) => {
  const result = await page.evaluate(async () => {
    const { api, bytes, hex } = window.bridge;
    const options = { root: "root", aligned: false };
    const unrelated = "struct unused { uint8 tag; if (tag) { uint16 payload; } }; struct root { uint8 value; utf8 trailing[1]; };";
    const promoted = "struct root { uint8 tag; if (tag) { struct { struct { uint8 value; }; }; } uint8 tail; };";
    const nested =
      "struct entry { uint8 tag; switch(tag) { case 1: { struct { uint8 tag; } child; uint8 value; } default: { uint16 other; } } if(tag == 1) { uint8 trailer; } }; struct root { entry items[2]; };";
    const nestedBytes = bytes("01 00 2a 58 00 34 12");

    const inactive = await api.serialize(promoted, { tag: 0, value: 42, tail: 99 }, options);
    const encoded = await api.serialize(promoted, { tag: 1, value: 42, tail: 99 }, options);
    const nestedParsed = await api.parseWithDebug(nested, nestedBytes, options);
    const nestedEncoded = await api.serialize(nested, nestedParsed.data, options);
    const nestedChanged = await api.update(nested, nestedBytes, "root.items[0].child.tag", 2, options);
    return {
      updated: hex((await api.update(unrelated, bytes("01 ff"), "root.value", 42, options)).data),
      inactiveCode: inactive.error?.code,
      encoded: hex(encoded.data),
      reparsed: (await api.parseWithDebug(promoted, encoded.data, options)).data,
      nestedParsed,
      nestedEncoded: hex(nestedEncoded.data),
      nestedUpdated: (await api.parseWithDebug(nested, nestedChanged.data, options)).data,
    };
  });

  expect(result.updated).toBe("2a ff");
  // With tag 0 the member value is inactive, so a value for it cannot be written.
  expect(result.inactiveCode).toBe("write-failed");
  expect(result.encoded).toBe("01 2a 63");
  expect(result.reparsed).toEqual({ tag: 1, value: 42, tail: 99 });
  expect(result.nestedParsed.success).toBe(true);
  expect(result.nestedEncoded).toBe("01 00 2a 58 00 34 12");
  expect(result.nestedUpdated.items).toEqual([
    { tag: 1, child: { tag: 2 }, value: 42, trailer: 88 },
    { tag: 0, other: 4660 },
  ]);
  expect(result.nestedParsed.debug).toContainEqual(expect.objectContaining({ path: "root.items[0].trailer", start: 3, end: 4 }));
});

test("identifiers and fixed-point values round-trip", async ({ page }) => {
  const result = await page.evaluate(async () => {
    const { api } = window.bridge;
    const definition =
      "typedef fixed16_16> revision_type; struct root { uuid network; guid windows; revision_type revision; ufixed8_8< volume; fixed2_30< matrix; };";
    const options = { root: "root", aligned: false };
    const id = "00112233-4455-6677-8899-aabbccddeeff";
    const value = { network: id, windows: id, revision: -1.5, volume: 0.5, matrix: -1.25 };
    const written = await api.serialize(definition, value, options);
    const updated = await api.update(definition, written.data, "root.windows", "00000000-0000-0000-0000-000000000000", options);
    return {
      value,
      bytes: Array.from(written.data),
      parsed: await api.parseWithDebug(definition, written.data, options),
      updated: (await api.parseWithDebug(definition, updated.data, options)).data,
    };
  });

  expect(result.parsed.data).toEqual(result.value);
  // A UUID stores network order; a Windows GUID reverses its first three integer fields.
  expect(result.bytes.slice(0, 8)).toEqual([0, 17, 34, 51, 68, 85, 102, 119]);
  expect(result.bytes.slice(16, 24)).toEqual([51, 34, 17, 0, 85, 68, 119, 102]);
  expect(result.updated.windows).toBe("00000000-0000-0000-0000-000000000000");
  expect(result.parsed.debug).toContainEqual(expect.objectContaining({ path: "root.windows", start: 16, end: 32 }));
});

test("LEB128 keeps 64-bit values exact and rejects an update that changes the encoded width", async ({ page }) => {
  const result = await page.evaluate(async () => {
    const { api } = window.bridge;
    const definition = "struct root { uleb128_64 unsigned_value; sleb128_64 signed_value; uint8 tail; };";
    const options = { root: "root", aligned: false };
    const value = { unsigned_value: "18446744073709551615", signed_value: "-9223372036854775808", tail: 99 };
    const written = await api.serialize(definition, value, options);
    const changed = await api.update(definition, written.data, "root.unsigned_value", "18446744073709551614", options);
    const narrower = await api.update(definition, written.data, "root.unsigned_value", 1, options);
    return {
      value,
      length: written.data.length,
      parsed: await api.parseWithDebug(definition, written.data, options),
      changed: (await api.parseWithDebug(definition, changed.data, options)).data,
      narrowerCode: narrower.error?.code,
    };
  });

  expect(result.parsed.data).toEqual(result.value);
  expect(result.length).toBe(21);
  expect(result.changed.unsigned_value).toBe("18446744073709551614");
  expect(result.narrowerCode).toBe("write-failed");
  expect(result.parsed.debug).toContainEqual(expect.objectContaining({ path: "root.signed_value", start: 10, end: 20 }));
});

test("bounded legacy and UTF-16 encodings survive the writer", async ({ page }) => {
  const results = await page.evaluate(async () => {
    const { api, hex } = window.bridge;
    const cases = [
      { type: "cp437", text: "é─", bytes: [0x82, 0xc4] },
      { type: "latin1", text: "éÿ", bytes: [0xe9, 0xff] },
      { type: "utf16le", text: "🌍", bytes: [0x3c, 0xd8, 0x0d, 0xdf] },
      { type: "utf16be", text: "🌍", bytes: [0xd8, 0x3c, 0xdf, 0x0d] },
    ];
    const out = [];
    for (const { type, text, bytes } of cases) {
      const definition = `struct root { ${type} text[${bytes.length}]; uint8 tail; };`;
      const options = { root: "root", aligned: false };
      const data = new Uint8Array([...bytes, 99]);
      out.push({
        text,
        expectedWritten: hex(data),
        expectedCleared: hex(new Uint8Array([...bytes.map(() => 0), 99])),
        parsed: (await api.parseWithDebug(definition, data, options)).data,
        written: hex((await api.serialize(definition, { text, tail: 99 }, options)).data),
        cleared: hex((await api.update(definition, data, "root.text", "", options)).data),
      });
    }
    return out;
  });

  for (const result of results) {
    expect(result.parsed).toEqual({ text: result.text, tail: 99 });
    expect(result.written).toBe(result.expectedWritten);
    expect(result.cleared).toBe(result.expectedCleared);
  }
});

test("bounded UTF-8 parses, serializes and updates, and rejects a cut character", async ({ page }) => {
  const result = await page.evaluate(async () => {
    const { api, hex } = window.bridge;
    const definition = "struct root { uint8 length; utf8 name[length]; uint8 tail; };";
    const options = { root: "root", aligned: false };
    const bytes = new Uint8Array([6, ...new TextEncoder().encode("é🌍"), 99]);
    const updated = await api.update(definition, bytes, "root.name", "你好", options);
    return {
      parsed: await api.parseWithDebug(definition, bytes, options),
      serialized: hex((await api.serialize(definition, { length: 6, name: "é🌍", tail: 99 }, options)).data),
      updated: (await api.parseWithDebug(definition, updated.data, options)).data,
      cut: await api.parseWithDebug("struct root { utf8 name[1]; uint8 tail; };", new Uint8Array([0xc3, 0xa9]), options),
    };
  });

  expect(result.parsed.data).toEqual({ length: 6, name: "é🌍", tail: 99 });
  expect(result.serialized).toBe("06 c3 a9 f0 9f 8c 8d 63");
  expect(result.updated).toEqual({ length: 6, name: "你好", tail: 99 });
  expect(result.parsed.debug).toContainEqual(expect.objectContaining({ path: "root.name", start: 1, end: 7 }));
  expect(result.cut.error.code).toBe("read-failed");
});
