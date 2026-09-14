import assert from "node:assert/strict";
import fs from "node:fs/promises";
import { Readable } from "node:stream";
import test from "node:test";
import { prepareSource, removeTemporaryEntry } from "./large-source.js";

test("temporary storage cleanup waits for transient browser locks", async () => {
  let attempts = 0;
  await removeTemporaryEntry(
    {
      async removeEntry(name) {
        assert.equal(name, "owned-source");
        if (++attempts < 3)
          throw new DOMException(
            "Writer is closing",
            "NoModificationAllowedError",
          );
      },
    },
    "owned-source",
  );
  assert.equal(attempts, 3);
});

test("temporary storage cleanup is idempotent but reports persistent failures", async () => {
  await removeTemporaryEntry(
    {
      async removeEntry() {
        throw new DOMException("Gone", "NotFoundError");
      },
    },
    "owned-source",
  );
  for (const name of ["NotAllowedError", "NoModificationAllowedError"]) {
    let attempts = 0;
    const error = new DOMException("Storage failure", name);
    await assert.rejects(
      removeTemporaryEntry(
        {
          async removeEntry() {
            attempts++;
            throw error;
          },
        },
        "owned-source",
      ),
      (actual) => actual === error,
    );
    assert.equal(attempts, name === "NotAllowedError" ? 1 : 6);
  }
});

test("byte views snapshot exactly their range without detaching the caller", async () => {
  const original = new Uint8Array([9, 42, 0, 8]);
  const source = await prepareSource(new DataView(original.buffer, 1, 2));
  original[1] = 77;
  assert.deepEqual([...source.descriptor.bytes], [42, 0]);
  assert.equal(original.byteLength, 4);
  await source.dispose();
});

test("Node streams spool binary chunks, retain seekable bytes, and clean up", async () => {
  const source = await prepareSource(
    Readable.from([Buffer.from([1, 2]), Buffer.from([3, 4])]),
  );
  assert.equal(source.descriptor.size, 4);
  assert.deepEqual(
    [...(await fs.readFile(source.descriptor.path))],
    [1, 2, 3, 4],
  );
  await source.dispose();
  await assert.rejects(fs.stat(source.descriptor.path), { code: "ENOENT" });
});

test("one-pass inputs reject text chunks and enforce staging limits", async () => {
  await assert.rejects(prepareSource(["text"]), /Binary chunks/);
  await assert.rejects(
    prepareSource([new Uint8Array(2)], { maxSpoolBytes: 1 }),
    /maxSpoolBytes/,
  );
  await assert.rejects(
    prepareSource(new Response("bad", { status: 500 })),
    /HTTP 500/,
  );
});

test("pre-aborted inputs do not consume an iterator", async () => {
  let read = false;
  async function* input() {
    read = true;
    yield new Uint8Array(1);
  }
  await assert.rejects(
    prepareSource(input(), { signal: AbortSignal.abort() }),
    { name: "AbortError" },
  );
  assert.equal(read, false);
});
