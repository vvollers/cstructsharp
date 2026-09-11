import { expect, it } from "vitest";
import { editBlob, searchBlob } from "./blob-hex";

it("edits a file range without losing surrounding bytes", async () => {
  const source = new Blob([new Uint8Array([1, 2, 3, 4])]);
  const edited = editBlob(source, { kind: "overwrite-byte", index: 2, value: 42, column: "hex" });
  expect([...new Uint8Array(await edited.arrayBuffer())]).toEqual([1, 2, 42, 4]);
  expect([...new Uint8Array(await source.arrayBuffer())]).toEqual([1, 2, 3, 4]);
});

it("searches across page boundaries and wraps in either direction", async () => {
  const bytes = new Uint8Array(65540);
  bytes.set([42, 43], 2);
  bytes.set([42, 43], 65535);
  const source = new Blob([bytes]);
  const request = {
    query: new Uint8Array([42, 43]),
    mode: "hex" as const,
    direction: "next" as const,
    from: 3,
    wrap: true,
  };
  expect(await searchBlob(source, request)).toEqual({
    total: 2,
    hit: { start: 65535, end: 65536 },
    activeOrdinal: 2,
  });
  expect((await searchBlob(source, { ...request, from: 65539 })).hit?.start).toBe(2);
  expect(
    (await searchBlob(source, { ...request, from: 0, direction: "previous" })).hit?.start,
  ).toBe(65535);
});
