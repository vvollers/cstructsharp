/**
 * Stress test of the browser bridge's result transport with parse results beyond 2^27 characters, the length at which
 * a result returned as a .NET string fails to decode in Node.js. It checks that such a result arrives intact on the
 * calling thread (the raw adapter's ParseBytes path) and through the source worker, that a result longer than the
 * bridge's 536,870,888-byte maximum is a read-budget failure envelope instead of a runtime crash, and that several
 * large parses in one process succeed or report resource-exhausted. It runs inside an installed consumer of the
 * packed tarball (see test-npm-package.mjs), needs a few gigabytes of memory and a few minutes, and is therefore
 * skipped unless CSTRUCTSHARP_LARGE_OUTPUT_STRESS=1. Each scenario runs in its own process, so a WebAssembly memory
 * that one scenario grew (it never shrinks) cannot affect another; the sequential scenario runs its parses in one
 * process on purpose.
 *
 *   CSTRUCTSHARP_LARGE_OUTPUT_STRESS=1 node tools/packaging/test-large-output.mjs <consumer directory>
 */
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { parseArguments } from "../lib/tooling.mjs";

/** The longest text the .NET runtime's UTF-16 string decoding returns in Node.js: 2^27 characters. */
const STRING_MARSHAL_LIMIT = 2 ** 27;
/** The bridge's maximum result length in bytes (InteropLimits.MaximumResultLength, V8's string limit). */
const MAXIMUM_RESULT_LENGTH = 536_870_888;

// A debug parse of one-byte records writes about 98 characters per input byte, so 1.5 MiB exceeds 2^27 characters
// while staying within the 4 MiB the calling thread's ParseBytes accepts.
const recordCount = 1.5 * 1024 * 1024;
const records = "struct rec { uint8 a; }; struct file { rec items[EOF]; };";
const recordOptions = { root: "file", maxArrayElements: recordCount };

const {
  _: [consumer],
  scenario,
} = parseArguments(process.argv.slice(2), { scenario: "string" }, { positionals: true });
if (process.env.CSTRUCTSHARP_LARGE_OUTPUT_STRESS !== "1") {
  console.log("Large-output stress test skipped; set CSTRUCTSHARP_LARGE_OUTPUT_STRESS=1 to run it.");
  process.exit(0);
}
assert.ok(consumer, "Pass the consumer directory that has cstructsharp installed.");

/** The scenarios, each run in a fresh process with the installed package's Node entry point. */
const scenarios = {
  /**
   * The calling thread: the raw adapter returns the envelope text the public API parses.
   * @param {object} api The installed package.
   */
  async callingThread(api) {
    const adapter = await api.loadCStructSharpWasm();
    const text = adapter.parseWithDebug(records, recordBytes(), recordOptions);
    assert.ok(text.length > STRING_MARSHAL_LIMIT, `the result has only ${text.length} characters`);
    assertRecords(JSON.parse(text));
    console.log(`Calling thread: ${text.length} characters arrived intact.`);
  },

  /**
   * The source worker: a byte input with a signal always goes there (without one, a debug parse of up to 4 MiB runs
   * on the calling thread), and the reply carries the envelope bytes. The result is larger than the worker recycling
   * threshold, so the recycling counter proves that the worker ran it.
   * @param {object} api The installed package.
   */
  async worker(api) {
    const recycling = await workerRecycling();
    const recycledBefore = recycling.recycled;
    assertRecords(await api.parseWithDebug(records, recordBytes(), { ...recordOptions, signal: new AbortController().signal }));
    assert.equal(recycling.recycled, recycledBefore + 1, "the parse ran in a worker, which was replaced afterwards");
    console.log("Worker: the same result arrived intact.");
  },

  /**
   * Several large parses in one process: on the calling thread, in the shared worker, and twice through one compiled
   * layout's worker. Each runtime keeps the memory it grew to, so each parse must succeed or report
   * resource-exhausted, never operation-failed or a crash. The worker is replaced after each large result, so the
   * compiled layout must still parse a small input afterwards.
   * @param {object} api The installed package.
   */
  async sequential(api) {
    const signal = new AbortController().signal;
    const recycling = await workerRecycling();
    assertRecordsOrExhausted(await api.parseWithDebug(records, recordBytes(), recordOptions), "calling thread");
    assert.equal(recycling.recycled, 0, "the parse without a signal ran on the calling thread");
    assertRecordsOrExhausted(await api.parseWithDebug(records, recordBytes(), { ...recordOptions, signal }), "shared worker");
    assert.equal(recycling.recycled, 1, "the parse with a signal ran in the shared worker, which was replaced");
    const compiled = await api.compile(records, { root: "file" });
    try {
      for (const round of [1, 2]) {
        assertRecordsOrExhausted(
          await compiled.parseWithDebug(recordBytes(), { maxArrayElements: recordCount, signal }),
          `compiled layout, parse ${round}`,
        );
      }
      assert.equal(recycling.recycled, 3, "each large compiled parse replaced the layout's worker");
      const small = await compiled.parse(new Uint8Array([7]), { signal });
      assert.equal(small.success, true, small.error?.message);
      assert.deepEqual(small.data.items, [{ a: 7 }]);
    } finally {
      await compiled.dispose();
    }
    console.log("Sequential: every large parse in one process completed.");
  },

  /**
   * The limit: 1 KiB text records escape each zero byte as \u0000 (six characters), so 96 MiB of input would need
   * about 604 million characters. The bridge stops at its maximum and reports a read-budget failure instead.
   * @param {object} api The installed package.
   */
  async limit(api) {
    const count = 96 * 1024;
    const oversized = await api.parse("struct rec { char s[1024]; }; struct file { rec items[EOF]; };", new Uint8Array(count * 1024), {
      root: "file",
      maxArrayElements: count,
      maxTotalBytesRead: 256 * 1024 * 1024,
    });
    assert.equal(oversized.success, false);
    assert.equal(oversized.data, null);
    assert.equal(oversized.error.code, "read-budget", oversized.error.message);
    assert.match(oversized.error.message, new RegExp(`exceeds ${MAXIMUM_RESULT_LENGTH} bytes`));
    console.log(`Oversized result: ${oversized.error.code}: ${oversized.error.message}`);
  },
};

/** The record layout's input: record i holds i mod 256, so the last element identifies the end of the data. */
function recordBytes() {
  return new Uint8Array(recordCount).map((_, index) => index & 0xff);
}

/**
 * Checks a debug parse envelope of the record layout: every element and at least one debug range per value.
 * @param {object} envelope The parsed envelope.
 */
function assertRecords(envelope) {
  assert.equal(envelope.success, true, envelope.error?.message);
  assert.equal(envelope.data.items.length, recordCount);
  assert.deepEqual(envelope.data.items.at(-1), { a: (recordCount - 1) & 0xff });
  assert.ok(envelope.debug.length >= recordCount);
}

/**
 * Loads the installed runtime's worker recycling state, the same module instance the package's public API uses, so a
 * scenario can tell from its `recycled` counter which large parses ran in a worker.
 * @returns {Promise<{resultBytes: number, recycled: number}>} The runtime's `workerRecycling` object.
 */
async function workerRecycling() {
  const url = pathToFileURL(path.join(consumer, "node_modules", "cstructsharp", "runtime", "large-source.js")).href;
  return (await import(url)).workerRecycling;
}

/**
 * Checks a debug parse envelope of the record layout, or accepts the categorized out-of-memory failure, which a
 * process that already holds grown runtimes may report instead.
 * @param {object} envelope The parsed envelope.
 * @param {string} label Names the parse in the log and in assertion messages.
 */
function assertRecordsOrExhausted(envelope, label) {
  if (!envelope.success && envelope.error.code === "resource-exhausted") {
    console.log(`${label}: resource-exhausted: ${envelope.error.message}`);
    return;
  }
  assert.equal(envelope.success, true, `${label}: ${envelope.error?.code}: ${envelope.error?.message}`);
  assertRecords(envelope);
  console.log(`${label}: the result arrived intact.`);
}

if (scenario) {
  assert.ok(Object.hasOwn(scenarios, scenario), `Unknown scenario: ${scenario}`);
  await scenarios[scenario](await import(pathToFileURL(path.join(consumer, "node_modules", "cstructsharp", "node.js")).href));
  // The shared worker would otherwise keep the process alive for its idle timeout.
  process.exit(0);
}
for (const name of Object.keys(scenarios)) {
  const child = spawnSync(process.execPath, [fileURLToPath(import.meta.url), consumer, "--scenario", name], {
    stdio: "inherit",
    env: process.env,
  });
  assert.equal(child.status, 0, `Large-output scenario ${name} failed.`);
}
console.log("Large-output stress test passed.");
