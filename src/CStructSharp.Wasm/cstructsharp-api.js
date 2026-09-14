/** Shared operation conversions for the ZIP, Node, and browser adapters. */
const INTEROP_CONTRACT_VERSION = 7;
/**
 * Byte inputs up to this size, without a cancellation signal, are parsed on the calling thread (E3.6). Kept in
 * step with SYNCHRONOUS_PARSE_LIMIT in large-source.js; this module is staged at the npm package root while the
 * source adapter lives beside the runtime, so it cannot import it.
 */
const SYNCHRONOUS_PARSE_LIMIT = 64 * 1024;

export function createPublicApi(loadCStructSharpWasm) {
  /** Parse bytes; successful Data is the parsed value with the selected root wrapper (contract v7).
   * @param {string} definition Portable layout source.
   * @param {import("./cstructsharp-wasm.js").BinarySource} bytes Binary source.
   * @param {import("./cstructsharp-wasm.js").ParseWithDebugOptions | null} [options]
   * @returns {Promise<import("./cstructsharp-wasm.js").Result<import("./cstructsharp-wasm.js").ParsedData, "parse">>}
   */
  async function parseWithDebug(definition, bytes, options = null) {
    const api = await loadCStructSharpWasm();
    if (
      !(bytes instanceof Uint8Array) ||
      bytes.byteLength > 4 * 1024 * 1024 ||
      options?.signal
    ) {
      return api.parseSource(definition, bytes, options, true);
    }
    return parseEnvelope(
      api.parseWithDebug(definition, bytes, options),
      "parse",
    );
  }

  /**
   * Parse any supported binary source without allocating debug byte copies. Small byte inputs without a
   * cancellation signal are parsed on the calling thread (one managed copy, no worker round trip); everything
   * else goes through the staged source and the worker.
   */
  async function parse(definition, source, options = null) {
    const api = await loadCStructSharpWasm();
    if (isSmallByteInput(source, options) && typeof api.parseBytes === "function") {
      const bytes = toUint8Array(source);
      // E3.9: a fully fixed layout is read by the static plan in JavaScript; everything else crosses into WASM.
      const native = tryParseNative(api, definition, bytes, options);
      if (native !== null) return native;
      return parseEnvelope(
        api.parseBytes(definition, bytes, options, false),
        "parse",
      );
    }
    return api.parseSource(definition, source, options, false);
  }

  /** Serialize root fields (without a parse root wrapper). BigInt values retain exact decimal digits.
   * @param {string} definition Portable layout source.
   * @param {unknown} value A JSON-serializable value, optionally containing BigInt values.
   * @param {import("./cstructsharp-wasm.js").SerializeOptions | null} [options]
   * @returns {Promise<import("./cstructsharp-wasm.js").Result<Uint8Array, "serialize">>}
   */
  async function serialize(definition, value, options = null) {
    const api = await loadCStructSharpWasm();
    return runBinaryOperation("serialize", () =>
      api.serialize(definition, stringifyInteropValue(value), options),
    );
  }

  /** Update one path without mutating input. Successful Data is the complete updated byte array.
   * @param {string} definition Portable layout source.
   * @param {Uint8Array} bytes Original input.
   * @param {string} path Case-sensitive field path.
   * @param {unknown} value Replacement value; BigInt is sent as decimal text.
   * @param {import("./cstructsharp-wasm.js").UpdateOptions | null} [options]
   * @returns {Promise<import("./cstructsharp-wasm.js").Result<Uint8Array, "update">>}
   */
  async function update(definition, bytes, path, value, options = null) {
    requireBytes(bytes);
    const api = await loadCStructSharpWasm();
    return runBinaryOperation("update", () =>
      api.updateStream(
        definition,
        bytes,
        path,
        stringifyInteropValue(value),
        options,
      ),
    );
  }

  /** Return the managed library version from the loaded bundle. */
  async function getVersion() {
    const api = await loadCStructSharpWasm();
    return api.getVersion();
  }

  async function compile(definition, options = null) {
    const api = await loadCStructSharpWasm();
    return api.compile(definition, options);
  }

  return {
    compile,
    loadCStructSharpWasm,
    parse,
    parseWithDebug,
    serialize,
    update,
    getVersion,
  };
}

function isSmallByteInput(source, options) {
  if (options?.signal) return false;
  if (
    source instanceof ArrayBuffer ||
    ArrayBuffer.isView(source) ||
    (typeof SharedArrayBuffer !== "undefined" && source instanceof SharedArrayBuffer)
  ) {
    return source.byteLength <= SYNCHRONOUS_PARSE_LIMIT;
  }
  return false;
}

function toUint8Array(source) {
  if (source instanceof Uint8Array) return source;
  if (source instanceof ArrayBuffer || (typeof SharedArrayBuffer !== "undefined" && source instanceof SharedArrayBuffer)) {
    return new Uint8Array(source);
  }
  return new Uint8Array(source.buffer, source.byteOffset, source.byteLength);
}

function parseEnvelope(value, operation) {
  try {
    return JSON.parse(value);
  } catch (cause) {
    throw new TypeError(
      `CStructSharp returned an invalid ${operation} response envelope.`,
      {
        cause,
      },
    );
  }
}

/**
 * Runs a byte-returning managed export: success returns the bytes directly; failure is reported by the managed
 * export throwing (its message is the same JSON-serialized ErrorDetails shape the "parse" envelope's Error field
 * uses), since there is no envelope object to carry an Error field alongside a native byte-array success payload.
 * Reconstructs the same envelope shape parseEnvelope produces either way.
 */
function runBinaryOperation(operation, invoke) {
  try {
    return {
      ContractVersion: INTEROP_CONTRACT_VERSION,
      Operation: operation,
      Success: true,
      Data: invoke(),
      DebugData: [],
      Error: null,
    };
  } catch (cause) {
    return {
      ContractVersion: INTEROP_CONTRACT_VERSION,
      Operation: operation,
      Success: false,
      Data: null,
      DebugData: [],
      Error: parseBridgeError(cause, operation),
    };
  }
}

function parseBridgeError(cause, operation) {
  const message = cause instanceof Error ? cause.message : String(cause);
  let parsed;
  try {
    parsed = JSON.parse(message);
  } catch (parseCause) {
    throw new TypeError(
      `CStructSharp returned an invalid ${operation} error.`,
      {
        cause: parseCause,
      },
    );
  }

  if (
    typeof parsed !== "object" ||
    parsed === null ||
    typeof parsed.Code !== "string" ||
    typeof parsed.Message !== "string"
  ) {
    throw new TypeError(`CStructSharp returned an invalid ${operation} error.`);
  }

  return parsed;
}

function requireBytes(bytes) {
  if (!(bytes instanceof Uint8Array)) {
    throw new TypeError("Binary data must be a Uint8Array.");
  }
}

function stringifyInteropValue(value) {
  return JSON.stringify(value, (_key, current) =>
    typeof current === "bigint" ? current.toString(10) : current,
  );
}

// ---------------------------------------------------------------------------------------------------------------
// E3.9 — JavaScript execution of the static read plan.
//
// The managed side describes a fully fixed root (the compiler's static read plan: member offsets, codecs, counts,
// nested plans, enum tables) once per definition and options; parsing such a layout is then a DataView walk that
// produces exactly the value shapes the JSON projection produces (safe integers as numbers, larger ones as decimal
// strings, float32 as the shortest round-trip decimal, Latin-1 character buffers, `{Enum, Name, Value}` enums).
// Any option that changes read semantics (limits, pointers), a buffer shorter than the plan, a non-finite float, or
// a plan the bundle cannot describe sends the parse to WASM, which is the reference implementation.
// ---------------------------------------------------------------------------------------------------------------

const NATIVE_PLAN_OPTION_KEYS = new Set([
  "aligned",
  "littleEndian",
  "pointerSize",
  "rootTypeName",
  "maxDefinitionLength",
  "maxLayoutNestingDepth",
  "maxExpressionNestingDepth",
  "maxExpressionTokens",
]);
const NATIVE_PLAN_CACHE_LIMIT = 64;
const nativePlanCache = new Map();
const NON_FINITE = Symbol("non-finite");

function tryParseNative(api, definition, bytes, options) {
  if (typeof api.getStaticPlan !== "function" || !nativePlanOptionsEligible(options)) {
    return null;
  }
  const plan = getNativePlan(api, definition, options);
  if (plan === null || bytes.byteLength < plan.plan.size) {
    return null;
  }
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  let value;
  try {
    value = executeNativePlan(view, 0, plan.plan);
  } catch (error) {
    if (error === NON_FINITE) return null;
    throw error;
  }
  return {
    ContractVersion: INTEROP_CONTRACT_VERSION,
    Operation: "parse",
    Success: true,
    Data: { [plan.root]: value },
    DebugData: [],
    Error: null,
  };
}

function nativePlanOptionsEligible(options) {
  if (options === null || options === undefined) return true;
  if (typeof options !== "object") return false;
  for (const [key, value] of Object.entries(options)) {
    if (value !== undefined && value !== null && !NATIVE_PLAN_OPTION_KEYS.has(key)) return false;
  }
  return true;
}

function getNativePlan(api, definition, options) {
  const key = definition + "\u0000" + JSON.stringify(options ?? {}, Array.from(NATIVE_PLAN_OPTION_KEYS).sort());
  if (nativePlanCache.has(key)) {
    const cached = nativePlanCache.get(key);
    // Refresh recency so the small cache keeps the layouts in active use.
    nativePlanCache.delete(key);
    nativePlanCache.set(key, cached);
    return cached;
  }
  let plan = null;
  const text = api.getStaticPlan(definition, options);
  if (typeof text === "string" && text.length > 0) {
    plan = JSON.parse(text);
    prepareNativePlan(plan.plan);
  }
  if (nativePlanCache.size >= NATIVE_PLAN_CACHE_LIMIT) {
    nativePlanCache.delete(nativePlanCache.keys().next().value);
  }
  nativePlanCache.set(key, plan);
  return plan;
}

const NATIVE_ELEMENT_SIZE = { u8: 1, i8: 1, bool: 1, i16: 2, u16: 2, i24: 3, u24: 3, i32: 4, u32: 4, i64: 8, u64: 8, f32: 4, f64: 8 };

function prepareNativePlan(plan) {
  for (const op of plan.ops) {
    if (op.k === "e") {
      op.lookup = new Map(op.members.map((member) => [String(member.v), member.n]));
    } else if (op.k === "a") {
      op.size = NATIVE_ELEMENT_SIZE[op.t];
    } else if (op.k === "s" || op.k === "sa") {
      prepareNativePlan(op.p);
    }
  }
}

function executeNativePlan(view, base, plan) {
  const result = {};
  const ops = plan.ops;
  for (let index = 0; index < ops.length; index++) {
    const op = ops[index];
    const at = base + op.o;
    switch (op.k) {
      case "n":
        result[op.name] = readNativeNumber(view, at, op);
        break;
      case "e": {
        const value = readNativeNumber(view, at, op);
        result[op.name] = { Enum: op.enum, Name: op.lookup.get(String(value)) ?? null, Value: value };
        break;
      }
      case "c":
        result[op.name] = readLatin1(view, at, op.n);
        break;
      case "a": {
        const items = new Array(op.n);
        for (let element = 0; element < op.n; element++) {
          items[element] = readNativeNumber(view, at + element * op.size, op);
        }
        result[op.name] = items;
        break;
      }
      case "s":
        result[op.name] = executeNativePlan(view, at, op.p);
        break;
      case "sa": {
        const items = new Array(op.n);
        const size = op.p.size;
        for (let element = 0; element < op.n; element++) {
          items[element] = executeNativePlan(view, at + element * size, op.p);
        }
        result[op.name] = items;
        break;
      }
      default:
        throw new TypeError(`Unknown static plan operation '${op.k}'.`);
    }
  }
  return result;
}

const SAFE_INTEGER_BIG = 9007199254740991n;

function readNativeNumber(view, at, op) {
  switch (op.t) {
    case "u8":
      return view.getUint8(at);
    case "i8":
      return view.getInt8(at);
    case "bool":
      return view.getUint8(at) !== 0;
    case "i16":
      return view.getInt16(at, op.le);
    case "u16":
      return view.getUint16(at, op.le);
    case "i24": {
      const raw = op.le
        ? view.getUint8(at) | (view.getUint8(at + 1) << 8) | (view.getUint8(at + 2) << 16)
        : view.getUint8(at + 2) | (view.getUint8(at + 1) << 8) | (view.getUint8(at) << 16);
      return (raw << 8) >> 8;
    }
    case "u24":
      return op.le
        ? view.getUint8(at) | (view.getUint8(at + 1) << 8) | (view.getUint8(at + 2) << 16)
        : view.getUint8(at + 2) | (view.getUint8(at + 1) << 8) | (view.getUint8(at) << 16);
    case "i32":
      return view.getInt32(at, op.le);
    case "u32":
      return view.getUint32(at, op.le);
    case "i64":
      return safeInteger(view.getBigInt64(at, op.le));
    case "u64":
      return safeInteger(view.getBigUint64(at, op.le));
    case "f32":
      return float32AsProjected(view.getFloat32(at, op.le));
    case "f64": {
      const value = view.getFloat64(at, op.le);
      if (!Number.isFinite(value)) throw NON_FINITE;
      return value;
    }
    default:
      throw new TypeError(`Unknown static plan codec '${op.t}'.`);
  }
}

function safeInteger(value) {
  return value >= -SAFE_INTEGER_BIG && value <= SAFE_INTEGER_BIG ? Number(value) : value.toString(10);
}

/**
 * The JSON projection writes a float32 as the shortest decimal that round-trips to the same float32, and JSON.parse
 * turns that decimal into the nearest double - which is not always the float32 widened. Find that decimal here.
 */
function float32AsProjected(value) {
  if (!Number.isFinite(value)) throw NON_FINITE;
  if (value === 0) return value;
  for (let precision = 1; precision <= 9; precision++) {
    const candidate = Number(value.toPrecision(precision));
    if (Math.fround(candidate) === value) return candidate;
  }
  return value;
}

const LATIN1_CHUNK = 4096;

/** Latin-1 (one code unit per byte, no replacement, no trimming) - what the managed projection produces for char[N]. */
function readLatin1(view, at, count) {
  const bytes = new Uint8Array(view.buffer, view.byteOffset + at, count);
  if (count <= LATIN1_CHUNK) {
    return String.fromCharCode.apply(null, bytes);
  }
  let text = "";
  for (let offset = 0; offset < count; offset += LATIN1_CHUNK) {
    text += String.fromCharCode.apply(null, bytes.subarray(offset, Math.min(offset + LATIN1_CHUNK, count)));
  }
  return text;
}

