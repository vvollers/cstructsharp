import { expect, test } from "@playwright/test";
import type { RawWasmAdapter } from "../../../../src/CStructSharp.Wasm/cstructsharp-wasm.js";

test("compiled browser layouts retain decisions per read and recover after cancellation", async ({
  page,
}) => {
  await page.goto("/");
  await expect(page.locator(".status-badge")).toContainText("Ready", { timeout: 60_000 });
  const results = await page.evaluate(async () => {
    const api = (window as unknown as { CStructSharpWasm: RawWasmAdapter }).CStructSharpWasm;
    const layout = await api.compile(
      "struct root { uint8 tag; switch (tag) { case 1: { uint32 value; } case 2: { uint16 small; } default: { uint8 fallback; } } };",
    );
    const values = [
      new Uint8Array([1, 42, 0, 0, 0]),
      new Uint8Array([2, 19, 0]),
      new Uint8Array([3, 7]),
    ];
    try {
      const parsed = await Promise.all(values.map((bytes) => layout.parse(new Blob([bytes]))));
      const debug = await layout.parseWithDebug(values[0]!);
      const limit = await layout.parse(values[0]!, { maxTotalBytesRead: 1 });
      const cancelled = new AbortController();
      cancelled.abort();
      let abortName = "";
      try {
        await layout.parse(values[0]!, { signal: cancelled.signal });
      } catch (error) {
        abortName = (error as Error).name;
      }
      const recovered = await layout.parse(values[1]!);
      await layout.dispose();
      let disposed = false;
      try {
        await layout.parse(values[0]!);
      } catch {
        disposed = true;
      }
      return { parsed, debug, limit, abortName, recovered, disposed };
    } finally {
      await layout.dispose();
    }
  });
  expect(results.parsed.map((result) => JSON.parse(result.Data!))).toEqual([
    { root: { tag: 1, value: 42 } },
    { root: { tag: 2, small: 19 } },
    { root: { tag: 3, fallback: 7 } },
  ]);
  expect(results.debug.DebugData.map((item) => item.DebugStackString)).toContain("root.value");
  expect(results.limit.Success).toBe(false);
  expect(results.abortName).toBe("AbortError");
  expect(results.recovered.Success).toBe(true);
  expect(results.disposed).toBe(true);
});
