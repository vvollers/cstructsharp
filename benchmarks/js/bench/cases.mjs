// Case definitions shared by the Node harness and the browser page. `env` supplies host-specific pieces:
//   env.loadFixture(id)      -> { document, bytes }      (Node: file system; browser: fetch)
//   env.managed              -> the CStructExports object from getAssemblyExports (benchmark-only exports included)
//   env.api                  -> the public API (createPublicApi(...)) from the bundle
//   env.runtime              -> the dotnet runtime (for setModuleImports)
//   env.copyCounter          -> { pages, pageBytes, bytesIn, bytesOut, jsonChars } mutable counters
//   env.host                 -> "node" | "browser"

const BOUNDARY_SIZES = [16, 4096, 1048576];

function fixtureOptions(document) {
  return {
    pointerSize: document.options.pointerSize,
    aligned: document.options.aligned,
    littleEndian: document.options.littleEndian,
    rootTypeName: document.root,
    ...(document.readOptions?.addressingMode ? { addressingMode: document.readOptions.addressingMode } : {}),
    ...(document.readOptions?.maxArrayElements ? { maxArrayElements: document.readOptions.maxArrayElements } : {}),
    ...(document.readOptions?.maxTotalBytesRead ? { maxTotalBytesRead: document.readOptions.maxTotalBytesRead } : {}),
    ...(document.readOptions?.maxStringBytes ? { maxStringBytes: document.readOptions.maxStringBytes } : {}),
    ...(document.readOptions?.maxPointerDepth ? { maxPointerDepth: document.readOptions.maxPointerDepth } : {}),
  };
}

const stringify = (value) => JSON.stringify(value, (_k, v) => (typeof v === "bigint" ? v.toString() : v));

/** A counting JS-backed source for the ParseSource/BenchPageReadAll exports (mirrors the worker's read function). */
function countingSource(env, bytes) {
  return {
    size: bytes.byteLength,
    read: (offset, count) => {
      env.copyCounter.pages++;
      env.copyCounter.pageBytes += count;
      return bytes.subarray(offset, offset + count);
    },
  };
}

export async function verifyFixture(env, id) {
  const { document, bytes } = await env.loadFixture(id);
  const options = fixtureOptions(document);
  const result = await env.api.parse(document.definition, bytes, options);
  if (!result.Success) throw new Error(`${id}: public parse failed: ${JSON.stringify(result.Error)}`);
  const parsed = result.Data;
  const actual = JSON.stringify(parsed[document.root]);
  if (document.expected !== null && document.expected !== undefined) {
    const expected = JSON.stringify(document.expected);
    if (actual !== expected) throw new Error(`${id}: JS result differs from the C# expected JSON`);
  } else if (document.expectedSha256) {
    // The digest is over the C# canonical text (Utf8JsonWriter default escaping), so re-serialize with the same
    // escaping before hashing; JSON.stringify would leave non-ASCII characters raw.
    const digest = await sha256Hex(env, canonicalJson(parsed[document.root]));
    if (digest !== document.expectedSha256) throw new Error(`${id}: JS result SHA-256 ${digest} != ${document.expectedSha256}`);
  }
  return true;
}

// Serializes like the fixture tool's CanonicalJson (Utf8JsonWriter with JavaScriptEncoder.Default): object members
// in insertion order, no whitespace, and strings escaped the .NET way (letters, digits and a small punctuation
// set pass through; everything else, including quotes and non-ASCII, becomes \uXXXX with uppercase hex).
export function canonicalJson(value) {
  if (value === null || value === undefined) return "null";
  if (typeof value === "string") return canonicalString(value);
  if (typeof value === "number" || typeof value === "boolean") return JSON.stringify(value);
  if (Array.isArray(value)) return `[${value.map(canonicalJson).join(",")}]`;
  // Utf8JsonWriter.WriteBase64StringValue writes base64 without escaping, so a union's RawStorage keeps its raw
  // "+" and "/" while every other string escapes "+" as \u002B.
  return `{${Object.entries(value).map(([key, item]) => `${canonicalString(key)}:${key === "RawStorage" && typeof item === "string" ? `"${item}"` : canonicalJson(item)}`).join(",")}}`;
}

const unescapedAscii = /^[A-Za-z0-9 !#$%()*,\-./:;=?@[\]^_`{|}~]$/;
function canonicalString(text) {
  let out = '"';
  for (const ch of text) {
    if (unescapedAscii.test(ch)) out += ch;
    else if (ch === "\\") out += "\\\\";
    else if (ch === "\n") out += "\\n";
    else if (ch === "\r") out += "\\r";
    else if (ch === "\t") out += "\\t";
    else if (ch === "\b") out += "\\b";
    else if (ch === "\f") out += "\\f";
    else {
      const code = ch.codePointAt(0);
      if (code > 0xffff) {
        const high = Math.floor((code - 0x10000) / 0x400) + 0xd800;
        const low = ((code - 0x10000) % 0x400) + 0xdc00;
        out += `\\u${high.toString(16).toUpperCase().padStart(4, "0")}\\u${low.toString(16).toUpperCase().padStart(4, "0")}`;
      } else {
        out += `\\u${code.toString(16).toUpperCase().padStart(4, "0")}`;
      }
    }
  }
  return `${out}"`;
}

async function sha256Hex(env, text) {
  if (env.sha256) return env.sha256(text);
  const data = new TextEncoder().encode(text);
  const digest = await crypto.subtle.digest("SHA-256", data);
  return Array.from(new Uint8Array(digest), (b) => b.toString(16).padStart(2, "0")).join("");
}

export async function boundaryCases(env) {
  const { managed, runtime } = env;
  runtime.setModuleImports("cstructsharp-source", { read: (source, offset, count) => source.read(offset, count) });
  const cases = [{ name: "boundary.noop", fn: () => managed.BenchNoop(), tags: ["boundary"] }];
  for (const size of BOUNDARY_SIZES) {
    const bytes = new Uint8Array(size);
    for (let i = 0; i < size; i++) bytes[i] = i & 0xff;
    cases.push({ name: `boundary.bytesIn.${size}`, fn: () => managed.BenchBytesIn(bytes), tags: ["boundary"], meta: { bytes: size } });
    cases.push({
      name: `boundary.bytesOut.${size}`,
      before: () => managed.BenchPrepareBytesOut(size),
      fn: () => managed.BenchBytesOut(),
      tags: ["boundary"],
      meta: { bytes: size },
    });
    const text = "x".repeat(size);
    cases.push({ name: `boundary.stringIn.${size}`, fn: () => managed.BenchStringIn(text), tags: ["boundary"], meta: { chars: size } });
    cases.push({
      name: `boundary.stringOut.${size}`,
      before: () => managed.BenchPrepareStringOut(size),
      fn: () => managed.BenchStringOut(),
      tags: ["boundary"],
      meta: { chars: size },
    });
  }
  const page = new Uint8Array(1048576);
  cases.push({
    name: "boundary.pageRead.1M-in-64K-pages",
    fn: () => managed.BenchPageReadAll(countingSource(env, page)),
    tags: ["boundary"],
    meta: { bytes: page.byteLength, pageSize: 65536 },
  });
  return cases;
}

export async function coreCases(env) {
  // Retained-layout core parse vs projection, isolating the managed parse from the JSON boundary.
  // The bridge caps MaxArrayElements at 1,000,000, so the 1 MiB payload is the uint32 array (262,144 elements).
  const ids = ["prim-le-record", "nested-x256", "array-u8-1024", "array-u32-le-262144", "real-png", "strings-1024", "cond-if128"];
  const cases = [];
  for (const id of ids) {
    const { document, bytes } = await env.loadFixture(id);
    const options = stringify(fixtureOptions(document));
    const root = document.root;
    // core.compile measures the bridge path (a cache hit after E3.2); core.compileFresh the actual compilation.
    cases.push({
      name: `core.compile.${id}`,
      fn: () => { env.managed.BenchCompile(document.definition, options); return 1; },
      tags: ["compile"],
    });
    cases.push({
      name: `core.compileFresh.${id}`,
      fn: () => { env.managed.BenchCompileFresh(document.definition, options); return 1; },
      tags: ["compile"],
    });
    cases.push({
      name: `core.parse.${id}`,
      before: () => env.managed.BenchCompile(document.definition, options),
      fn: () => env.managed.BenchParseCore(bytes, root, options),
      tags: ["core"],
      meta: { bytes: bytes.byteLength },
    });
    cases.push({
      name: `core.project.${id}`,
      before: () => { env.managed.BenchCompile(document.definition, options); env.managed.BenchParseRetain(bytes, root, options); },
      fn: () => env.managed.BenchProjectRetained(),
      tags: ["projection"],
      meta: { bytes: bytes.byteLength },
    });
    cases.push({
      name: `core.parseJson.${id}`,
      before: () => env.managed.BenchCompile(document.definition, options),
      fn: () => env.managed.BenchParseJson(bytes, root, options),
      tags: ["core", "projection"],
      meta: { bytes: bytes.byteLength },
    });
  }
  return cases;
}

export async function publicCases(env) {
  const cases = [];
  const parseIds = ["prim-le-record", "nested-x256", "real-png", "array-u32-le-262144", "strings-1024", "cond-if128"];
  for (const id of parseIds) {
    const { document, bytes } = await env.loadFixture(id);
    const options = fixtureOptions(document);
    cases.push({
      name: `public.parse.${id}`,
      fn: () => env.api.parse(document.definition, bytes, options),
      tags: ["public"],
      meta: { bytes: bytes.byteLength },
    });
    if (bytes.byteLength <= 4 * 1024 * 1024) {
      cases.push({
        name: `public.parseWithDebug.${id}`,
        fn: () => env.api.parseWithDebug(document.definition, bytes, options),
        tags: ["public", "debug"],
        meta: { bytes: bytes.byteLength },
      });
    }
    // Direct export call through the same page-based source the worker uses, on the calling thread: isolates the
    // managed parse + envelope from worker messaging.
    cases.push({
      name: `direct.parseSource.${id}`,
      fn: () => {
        const json = env.managed.ParseSource(document.definition, countingSource(env, bytes), stringify(options), false);
        env.copyCounter.jsonChars += json.length;
        return json;
      },
      tags: ["direct"],
      meta: { bytes: bytes.byteLength },
    });
  }
  // Compiled handle (warm): schema compiled once, worker retained.
  for (const id of ["prim-le-record", "nested-x256", "real-png", "cond-if128"]) {
    const { document, bytes } = await env.loadFixture(id);
    const options = fixtureOptions(document);
    let handle;
    cases.push({
      name: `compiled.parse.${id}`,
      before: async () => { handle = await env.api.compile(document.definition, options); },
      fn: () => handle.parse(bytes),
      tags: ["compiled"],
      meta: { bytes: bytes.byteLength },
    });
    cases.push({
      name: `compiled.parseWithDebug.${id}`,
      fn: () => handle.parseWithDebug(bytes),
      tags: ["compiled", "debug"],
      meta: { bytes: bytes.byteLength },
    });
  }
  // Serialize and update through the public API.
  {
    const { document, bytes } = await env.loadFixture("prim-le-record");
    const options = fixtureOptions(document);
    const value = { root: document.expected };
    cases.push({ name: "public.serialize.prim-le-record", fn: () => env.api.serialize(document.definition, value, options), tags: ["public", "write"] });
    cases.push({ name: "public.update.scalar.28B", fn: () => env.api.update(document.definition, bytes, "root.c", 305419896, options), tags: ["public", "update"] });
  }
  {
    const { document, bytes } = await env.loadFixture("array-u32-le-262144");
    const options = fixtureOptions(document);
    cases.push({ name: "public.update.scalar.1M", fn: () => env.api.update(document.definition, bytes, "root.values[262143]", 90, options), tags: ["public", "update"] });
  }
  return cases;
}

export async function streamCases(env) {
  // Source shapes accepted by the public parse: Blob (browser) / Buffer views and Node streams (node).
  const cases = [];
  for (const id of ["array-u8-65536", "array-u64-le-1000000"]) {
    const { document, bytes } = await env.loadFixture(id);
    const options = fixtureOptions(document);
    if (env.host === "browser") {
      const blob = new Blob([bytes]);
      cases.push({ name: `stream.parse.blob.${id}`, fn: () => env.api.parse(document.definition, blob, options), tags: ["stream"], meta: { bytes: bytes.byteLength } });
    } else {
      cases.push({ name: `stream.parse.bytes.${id}`, fn: () => env.api.parse(document.definition, bytes, options), tags: ["stream"], meta: { bytes: bytes.byteLength } });
      if (env.nodeStreamFactory) {
        cases.push({
          name: `stream.parse.nodeStream.${id}`,
          fn: () => env.api.parse(document.definition, env.nodeStreamFactory(id), options),
          tags: ["stream"],
          meta: { bytes: bytes.byteLength },
        });
      }
    }
  }
  return cases;
}

export async function comparatorCases(env) {
  // Hand-written DataView readers: the JS-native floor for the same fixtures.
  const cases = [];
  {
    const { bytes } = await env.loadFixture("prim-le-record");
    const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
    cases.push({
      name: "comparator.dataView.prim-le-record",
      fn: () => ({
        a: view.getUint8(0), b: view.getInt16(1, true), c: view.getUint32(3, true), d: view.getBigInt64(7, true),
        e: view.getFloat32(15, true), f: view.getFloat64(19, true), g: view.getUint8(27) !== 0,
      }),
      tags: ["comparator"],
    });
  }
  {
    const { bytes } = await env.loadFixture("nested-x256");
    const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
    const leaf = (o) => ({ kind: view.getUint8(o), value: view.getUint32(o + 1, true) });
    const mid = (o) => ({ first: leaf(o), second: leaf(o + 5), tail: view.getUint16(o + 10, true) });
    cases.push({
      name: "comparator.dataView.nested-x256",
      fn: () => {
        const items = new Array(256);
        for (let i = 0; i < 256; i++) {
          const o = i * 25;
          items[i] = { left: mid(o), right: mid(o + 12), mark: view.getUint8(o + 24) };
        }
        return { items };
      },
      tags: ["comparator"],
    });
  }
  {
    const { bytes } = await env.loadFixture("real-png");
    const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
    cases.push({
      name: "comparator.dataView.real-png",
      fn: () => ({
        signature: Array.from(bytes.subarray(0, 8)),
        ihdr: {
          length: view.getUint32(8, false), chunk_type: String.fromCharCode(...bytes.subarray(12, 16)),
          width: view.getUint32(16, false), height: view.getUint32(20, false), bit_depth: view.getUint8(24),
          color_type: view.getUint8(25), compression_method: view.getUint8(26), filter_method: view.getUint8(27),
          interlace_method: view.getUint8(28), crc: view.getUint32(29, false),
        },
      }),
      tags: ["comparator"],
    });
  }
  return cases;
}
