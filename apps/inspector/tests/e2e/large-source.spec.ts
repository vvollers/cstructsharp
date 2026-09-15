import { expect, test } from "@playwright/test";
import type { RawWasmAdapter } from "../../src/wasm/cstruct-contract";

test.beforeEach(async ({ page }) => {
  await page.goto("/");
  await expect(page.locator(".status-badge")).toContainText("Ready", { timeout: 60_000 });
});

test("the inspector follows pointers past 4 MiB in a loaded file", async ({ page }) => {
  await page.getByTestId("example-new").click();
  await page.getByTestId("definition-editor").locator(".monaco-editor").click();
  await page.keyboard.press("Control+A");
  await page.keyboard.insertText("struct root { item *ptr; }; struct item { uint32 number; };");
  const bytes = Buffer.alloc(8 * 1024 * 1024 + 4);
  bytes.writeBigUInt64LE(BigInt(bytes.length - 4));
  bytes.writeUInt32LE(42, bytes.length - 4);
  const [chooser] = await Promise.all([
    page.waitForEvent("filechooser"),
    page.getByRole("button", { name: "Load file" }).click(),
  ]);
  await chooser.setFiles({
    name: "pointer.bin",
    mimeType: "application/octet-stream",
    buffer: bytes,
  });
  await expect(page.locator(".byte-count")).toContainText("8,388,612 bytes");
  await page.getByRole("button", { name: /^Run$/ }).click();
  await expect(page.locator(".result-status")).toHaveText("Parse completed");
  await expect(page.getByTestId("result-json")).toContainText("42");
  await page
    .getByTestId("result-json")
    .locator(".jse-key")
    .filter({ hasText: /^number$/ })
    .click();
  await expect(page.locator('[data-hex-index="8388608"]')).toHaveText("2a");
  await page.getByRole("textbox", { name: "Go to byte" }).fill("0x800000");
  await page.getByRole("button", { name: "Go", exact: true }).click();
  await expect(page.locator('[data-hex-index="8388608"]')).toHaveText("2a");
});

test("decompression streams feed the source parser", async ({ page }) => {
  const result = await page.evaluate(async () => {
    const wasm = (window as unknown as { CStructSharpWasm: RawWasmAdapter }).CStructSharpWasm;
    const compressed = new Blob([new Uint8Array([42, 0, 0, 0])])
      .stream()
      .pipeThrough(new CompressionStream("gzip"));
    return wasm.parseSource(
      "struct root { uint32 value; };",
      compressed.pipeThrough(new DecompressionStream("gzip")),
      {},
      false,
    );
  });
  expect(result.Success).toBe(true);
  expect(result.Data).toEqual({ root: { value: 42 } });
});

test("browser sources preserve view boundaries and stage one-pass data", async ({ page }) => {
  const results = await page.evaluate(async () => {
    const wasm = (window as unknown as { CStructSharpWasm: RawWasmAdapter }).CStructSharpWasm;
    const bytes = new Uint8Array([99, 42, 0, 0, 0, 99]);
    const body = new Uint8Array([42, 0, 0, 0]);
    async function* chunks() {
      yield body.subarray(0, 1);
      yield body.subarray(1);
    }
    const inputs = [
      new Blob([body]),
      new File([body], "test.bin"),
      body.buffer,
      new DataView(bytes.buffer, 1, 4),
      bytes.subarray(1, 5),
      new Response(body),
      new Blob([body]).stream(),
      chunks(),
      [body.subarray(0, 2), body.subarray(2)],
      { getFile: async () => new File([body], "handle.bin") },
    ];
    const values = [];
    for (const input of inputs) {
      values.push(await wasm.parseSource("struct root { uint32 value; };", input, {}, false));
    }
    return values;
  });
  for (const result of results) {
    expect(result.Success).toBe(true);
    expect(result.Data).toEqual({ root: { value: 42 } });
    expect(result.DebugData).toEqual([]);
  }
});

test("pointers can reach beyond 4 GiB and read across a page boundary", async ({ page }) => {
  const result = await page.evaluate(async () => {
    const wasm = (window as unknown as { CStructSharpWasm: RawWasmAdapter }).CStructSharpWasm;
    const zero = new Blob([new Uint8Array(1024 * 1024)]);
    // Repeated immutable Blob parts make a large logical source without a 4 GiB JS allocation.
    const padding = new Blob(Array<Blob>(4096).fill(zero));
    const address = new Uint8Array(8);
    const target = 2 ** 32 + 65535;
    new DataView(address.buffer).setBigUint64(0, BigInt(target), true);
    const source = new Blob([
      address,
      padding,
      new Uint8Array(65535 - 8),
      new Uint8Array([42, 0, 0, 0]),
    ]);
    return wasm.parseSource(
      "struct root { uint32 *ptr; };",
      source,
      { maxTotalBytesRead: 64 },
      true,
    );
  });
  expect(result.Success).toBe(true);
  expect(result.Data.root.ptr).toMatchObject({
    Address: 2 ** 32 + 65535,
    Value: 42,
    IsDereferenced: true,
  });
});

test("staging failures and cancellation remove temporary files", async ({ page }) => {
  const results = await page.evaluate(async () => {
    const wasm = (window as unknown as { CStructSharpWasm: RawWasmAdapter }).CStructSharpWasm;
    const root = await navigator.storage.getDirectory();
    async function names() {
      const result: string[] = [];
      for await (const name of (root as unknown as { keys(): AsyncIterable<string> }).keys()) {
        if (name.startsWith("cstructsharp-source-")) result.push(name);
      }
      return result.sort();
    }
    const before = await names();
    const messages = [];
    for (const [source, options] of [
      [new Response(new Uint8Array(10)), { maxSpoolBytes: 1 }],
      [new Response("missing", { status: 404 }), {}],
      [
        new ReadableStream({
          start(controller) {
            controller.error(new Error("broken source"));
          },
        }),
        {},
      ],
      [
        new ReadableStream({
          pull() {
            return new Promise(() => {});
          },
        }),
        { signal: AbortSignal.timeout(100) },
      ],
    ] as const) {
      try {
        await wasm.parseSource("struct root { uint8 value; };", source, options, false);
      } catch (error) {
        messages.push((error as Error).name + ": " + (error as Error).message);
      }
    }
    return { before, after: await names(), messages };
  });
  expect(results.messages[0]).toContain("maxSpoolBytes");
  expect(results.messages[1]).toContain("HTTP 404");
  expect(results.messages[2]).toContain("broken source");
  expect(results.messages[3]).toContain("AbortError");
  expect(results.after).toEqual(results.before);
});

test("worker parses can be cancelled without blocking the page", async ({ page }) => {
  const result = await page.evaluate(async () => {
    const wasm = (window as unknown as { CStructSharpWasm: RawWasmAdapter }).CStructSharpWasm;
    const controller = new AbortController();
    let ticked = false;
    setTimeout(() => {
      ticked = true;
      controller.abort();
    }, 100);
    try {
      await wasm.parseSource(
        "struct root { uint8 values[1000000]; };",
        new Blob([new Uint8Array(1000000)]),
        { signal: controller.signal },
        true,
      );
      return { ticked, error: "not cancelled" };
    } catch (error) {
      return { ticked, error: (error as Error).name };
    }
  });
  expect(result).toEqual({ ticked: true, error: "AbortError" });
});

test("read budgets can exceed former browser caps while defaults still apply", async ({ page }) => {
  const result = await page.evaluate(async () => {
    const wasm = (window as unknown as { CStructSharpWasm: RawWasmAdapter }).CStructSharpWasm;
    const count = 1_000_001;
    const definition = `struct root { uint8 data[${count}]; };`;
    const source = new Blob([new Uint8Array(count)]);
    const defaults = await wasm.parseSource(definition, source, {}, false);
    const raised = await wasm.parseSource(
      definition,
      source,
      {
        maxArrayElements: count,
        maxTotalBytesRead: 128 * 1024 * 1024,
        maxStringBytes: 32 * 1024 * 1024,
        maxPointerTargetBytes: 128 * 1024 * 1024,
      },
      false,
    );
    const invalid = await wasm.parseSource(
      "struct root { uint8 value; };",
      new Blob([new Uint8Array(1)]),
      { maxTotalBytesRead: 2 ** 53 },
      false,
    );
    return {
      defaults: defaults.Error,
      success: raised.Success,
      error: raised.Error,
      length: raised.Data?.root.data.length,
      invalid: invalid.Error,
    };
  });
  expect(result.defaults?.Code).toBe("read-budget");
  expect(result.success, JSON.stringify(result.error)).toBe(true);
  expect(result.length).toBe(1_000_001);
  expect(result.invalid?.Code).toBe("invalid-input");
});

test("multiple linked targets beyond 4 GiB preserve addresses and the parent cursor", async ({
  page,
}) => {
  const result = await page.evaluate(async () => {
    const wasm = (window as unknown as { CStructSharpWasm: RawWasmAdapter }).CStructSharpWasm;
    const far = 2 ** 32 + 65535,
      near = 4096;
    const header = new Uint8Array(20),
      h = new DataView(header.buffer);
    h.setBigUint64(0, BigInt(far), true);
    h.setBigUint64(8, BigInt(near), true);
    h.setUint32(16, 99, true);
    const node = (value: number, next: number) => {
      const b = new Uint8Array(12),
        v = new DataView(b.buffer);
      v.setUint32(0, value, true);
      v.setBigUint64(4, BigInt(next), true);
      return b;
    };
    const megabyte = new Blob([new Uint8Array(1024 * 1024)]);
    const zeros = new Blob(Array<Blob>(4097).fill(megabyte));
    const source = new Blob([
      header,
      zeros.slice(0, near - header.length),
      node(22, 0),
      zeros.slice(0, far - near - 12),
      node(11, near),
    ]);
    return wasm.parseSource(
      "struct node { uint32 number; node *next; }; struct root { node *nodes[2]; uint32 tail; };",
      source,
      { rootTypeName: "root", pointerSize: 8, maxTotalBytesRead: 256 },
      true,
    );
  });
  expect(result.Success, JSON.stringify(result.Error)).toBe(true);
  expect(result.Data.root.nodes[0]).toMatchObject({
    Address: 2 ** 32 + 65535,
    Value: { number: 11, next: { Address: 4096, Value: { number: 22 } } },
  });
  expect(result.Data.root.nodes[1].Value.number).toBe(22);
  expect(result.Data.root.tail).toBe(99);
});
