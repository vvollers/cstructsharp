/** Shared operation conversions for the ZIP, Node, and browser adapters (contract v8). */
const INTEROP_CONTRACT_VERSION = 8;
/**
 * Byte inputs up to this size, without a cancellation signal, are parsed on the calling thread (E3.6). Kept in
 * step with SYNCHRONOUS_PARSE_LIMIT in large-source.js; this module is staged at the npm package root while the
 * source adapter lives beside the runtime, so it cannot import it.
 */
const SYNCHRONOUS_PARSE_LIMIT = 64 * 1024;
/** Byte inputs beyond this size are staged and read by the worker rather than copied into WASM memory. */
const MANAGED_COPY_LIMIT = 4 * 1024 * 1024;

export function createPublicApi(loadCStructSharpWasm) {
  /** Parse bytes and record every value's byte range. `data` is the selected value; `debug` lists the ranges.
   * @param {string} definition Portable layout source.
   * @param {import("./cstructsharp-wasm.js").BinarySource} bytes Binary source.
   * @param {import("./cstructsharp-wasm.js").ParseOptions | null} [options]
   * @returns {Promise<import("./cstructsharp-wasm.js").ParseResult>}
   */
  async function parseWithDebug(definition, bytes, options = null) {
    const api = await loadCStructSharpWasm();
    if (!(bytes instanceof Uint8Array) || bytes.byteLength > MANAGED_COPY_LIMIT || options?.signal) {
      return api.parseSource(definition, bytes, options, true);
    }
    return parseEnvelope(api.parseWithDebug(definition, bytes, options), "parse");
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
      return parseEnvelope(api.parseBytes(definition, bytes, options, false), "parse");
    }
    return api.parseSource(definition, source, options, false);
  }

  /** Serialize a value: the selected root's fields, or a parse result's `data`. BigInt values keep exact digits.
   * @param {string} definition Portable layout source.
   * @param {unknown} value A JSON-serializable value, optionally containing BigInt values.
   * @param {import("./cstructsharp-wasm.js").SerializeOptions | null} [options]
   * @returns {Promise<import("./cstructsharp-wasm.js").Result<Uint8Array, "serialize">>}
   */
  async function serialize(definition, value, options = null) {
    const api = await loadCStructSharpWasm();
    return runBinaryOperation("serialize", options, () =>
      api.serialize(definition, stringifyInteropValue(value), options),
    );
  }

  /** Replace the value at one path and return the complete updated bytes; the input is never mutated.
   * @param {string} definition Portable layout source.
   * @param {import("./cstructsharp-wasm.js").BinarySource} source Original input (any binary source; it is read completely).
   * @param {string} path Case-sensitive field path.
   * @param {unknown} value Replacement value; BigInt is sent as decimal text.
   * @param {import("./cstructsharp-wasm.js").UpdateOptions | null} [options]
   * @returns {Promise<import("./cstructsharp-wasm.js").Result<Uint8Array, "update">>}
   */
  async function update(definition, source, path, value, options = null) {
    const api = await loadCStructSharpWasm();
    const bytes = await api.collectBytes(source, options);
    return runBinaryOperation("update", options, () =>
      api.updateStream(definition, bytes, path, stringifyInteropValue(value), options),
    );
  }

  /** Resolve the absolute byte position of a path in a source; `data` is the position (a decimal string beyond 2^53).
   * @param {string} definition Portable layout source.
   * @param {import("./cstructsharp-wasm.js").BinarySource} source Binary source.
   * @param {string} path Case-sensitive field path.
   * @param {import("./cstructsharp-wasm.js").ParseOptions | null} [options]
   * @returns {Promise<import("./cstructsharp-wasm.js").Result<number | string, "resolveAddress">>}
   */
  async function resolveAddress(definition, source, path, options = null) {
    const api = await loadCStructSharpWasm();
    return api.resolveAddressSource(definition, source, path, options);
  }

  /** Return the managed library version from the loaded bundle. */
  async function getVersion() {
    const api = await loadCStructSharpWasm();
    return api.getVersion();
  }

  async function compile(definition, options = null) {
    const api = await loadCStructSharpWasm();
    return api.compile(definition, options, { serialize, update });
  }

  return {
    compile,
    loadCStructSharpWasm,
    parse,
    parseWithDebug,
    serialize,
    update,
    resolveAddress,
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
    throw new TypeError(`CStructSharp returned an invalid ${operation} response envelope.`, { cause });
  }
}

/** The v8 envelope every operation returns. */
export function envelope(operation, root, data, error = null, debug = []) {
  return {
    contractVersion: INTEROP_CONTRACT_VERSION,
    operation,
    success: error === null,
    root,
    data: error === null ? data : null,
    debug,
    error,
  };
}

/**
 * Runs a byte-returning managed export: success returns the bytes directly; failure is reported by the managed
 * export throwing (its message is the same JSON-serialized error-details shape the "parse" envelope's error field
 * uses), since there is no envelope object to carry an error field alongside a native byte-array success payload.
 * Reconstructs the same envelope shape parseEnvelope produces either way.
 */
function runBinaryOperation(operation, options, invoke) {
  const root = typeof options?.root === "string" ? options.root : null;
  try {
    return envelope(operation, root, invoke());
  } catch (cause) {
    return envelope(operation, root, null, parseBridgeError(cause, operation));
  }
}

/** Reconstructs the structured error a byte-returning export threw. */
export function parseBridgeError(cause, operation) {
  const message = cause instanceof Error ? cause.message : String(cause);
  let parsed;
  try {
    parsed = JSON.parse(message);
  } catch (parseCause) {
    throw new TypeError(`CStructSharp returned an invalid ${operation} error.`, { cause: parseCause });
  }
  if (typeof parsed !== "object" || parsed === null || typeof parsed.code !== "string" || typeof parsed.message !== "string") {
    throw new TypeError(`CStructSharp returned an invalid ${operation} error.`);
  }
  return parsed;
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
// strings, float32 as the shortest round-trip decimal, Latin-1 character buffers, `{kind, enum, name, value}` enums).
// Any option that changes read semantics (limits, pointers), a buffer shorter than the plan, a non-finite float, or
// a plan the bundle cannot describe sends the parse to WASM, which is the reference implementation.
// ---------------------------------------------------------------------------------------------------------------

const NATIVE_PLAN_OPTION_KEYS = new Set([
  "aligned",
  "littleEndian",
  "pointerSize",
  "root",
  "bitfieldPacking",
  "bitfieldAllocation",
  "cLongWidth",
  "maxDefinitionLength",
  "maxLayoutNestingDepth",
  "maxExpressionNestingDepth",
  "maxExpressionTokens",
]);
const NATIVE_PLAN_CACHE_LIMIT = 64;
const nativePlanCache = new Map();

function tryParseNative(api, definition, bytes, options) {
  if (typeof api.getStaticPlan !== "function" || !nativePlanOptionsEligible(options)) {
    return null;
  }
  const plan = getNativePlan(api, definition, options);
  if (plan === null || bytes.byteLength < plan.plan.size) {
    return null;
  }
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  return envelope("parse", plan.root, executeNativePlan(view, 0, plan.plan));
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
        result[op.name] = { kind: "enum", enum: op.enum, name: op.lookup.get(String(value)) ?? null, value };
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
      return Number.isFinite(value) ? value : nonFiniteText(value);
    }
    default:
      throw new TypeError(`Unknown static plan codec '${op.t}'.`);
  }
}

function safeInteger(value) {
  return value >= -SAFE_INTEGER_BIG && value <= SAFE_INTEGER_BIG ? Number(value) : value.toString(10);
}

/** JSON has no NaN or infinities; the projection and the write path use these strings for them. */
function nonFiniteText(value) {
  return Number.isNaN(value) ? "NaN" : value > 0 ? "Infinity" : "-Infinity";
}

/**
 * The JSON projection writes a float32 as the shortest decimal that round-trips to the same float32, and JSON.parse
 * turns that decimal into the nearest double - which is not always the float32 widened. Find that decimal here,
 * with the same tie rule as the managed formatter: when two shortest decimals are equally close to the exact
 * binary value, the one whose last digit is even wins. `toPrecision` alone rounds such ties away from zero, which
 * made ~0.3 % of float32 values differ between this path and the WASM path.
 *
 * "Some decimal of p digits round-trips" is monotonic in p (rounding the exact value to more digits moves it closer),
 * and nine digits always do, so the shortest precision is found by bisection - three or four probes instead of up to
 * nine - and the exact BigInt comparison runs only when both candidates at that precision round-trip (a tie is
 * possible), which is rare.
 */
function float32AsProjected(value) {
  if (!Number.isFinite(value)) return nonFiniteText(value);
  if (value === 0) return value;
  const magnitude = Math.abs(value);
  let low = 1;
  let high = 9;
  while (low < high) {
    const middle = (low + high) >> 1;
    if (float32CandidatesAt(magnitude, middle, false) === null) {
      low = middle + 1;
    } else {
      high = middle;
    }
  }
  const found = float32CandidatesAt(magnitude, low, true);
  if (found === null) return value;
  let chosen;
  if (found.upperFits && found.lowerFits) {
    const comparison = compareDecimalDistances(magnitude, BigInt(found.digits), BigInt(found.digits - 1), found.scale);
    chosen = comparison < 0 ? found.upper : comparison > 0 ? found.lower : found.digits % 2 === 0 ? found.upper : found.lower;
  } else {
    chosen = found.upperFits ? found.upper : found.lower;
  }
  return value < 0 ? -chosen : chosen;
}

/**
 * The two decimals of `precision` significant digits nearest to `magnitude` - `toExponential`'s rounding (ties away
 * from zero) and the one a unit below it - and whether each round-trips to the same float32; null when neither does.
 * A probe that only asks whether some decimal fits (`complete` false) stops at the upper candidate when it fits.
 * Digit strings of at most nine digits are exact as Numbers, so the BigInt arithmetic stays out of this probe.
 */
function float32CandidatesAt(magnitude, precision, complete) {
  const upperText = magnitude.toExponential(precision - 1);
  const upper = Number(upperText);
  const upperFits = Math.fround(upper) === magnitude;
  if (upperFits && !complete) return { precision, upper, lower: NaN, upperFits, lowerFits: false, digits: 0, scale: 0 };
  const exponentAt = upperText.indexOf("e");
  const digits = Number(upperText.slice(0, exponentAt).replace(".", ""));
  const scale = Number(upperText.slice(exponentAt + 1)) - (precision - 1);
  const lower = digits > 1 ? Number(`${digits - 1}e${scale}`) : NaN;
  const lowerFits = Number.isFinite(lower) && Math.fround(lower) === magnitude;
  if (!upperFits && !lowerFits) return null;
  return { precision, upper, lower, upperFits, lowerFits, digits, scale };
}

/**
 * Compares |value - upper·10^scale| with |value - lower·10^scale| exactly (BigInt arithmetic on the float32's
 * binary mantissa and exponent): negative when the upper decimal is closer, positive when the lower one is, zero on
 * an exact tie.
 */
function compareDecimalDistances(value, upperDigits, lowerDigits, scale) {
  const view = new DataView(new ArrayBuffer(4));
  view.setFloat32(0, value);
  const bits = view.getUint32(0);
  const exponentBits = (bits >>> 23) & 0xff;
  let mantissa = BigInt(bits & 0x7fffff);
  let exponent;
  if (exponentBits === 0) {
    exponent = -149;
  } else {
    mantissa |= 0x800000n;
    exponent = exponentBits - 150;
  }
  // Bring value = mantissa·2^exponent and candidate = digits·10^scale to a common integer scale.
  const twoShift = exponent < 0 ? -exponent : 0;
  const tenShift = scale < 0 ? -scale : 0;
  const exact = mantissa * 2n ** BigInt(exponent + twoShift) * 10n ** BigInt(tenShift);
  const toInteger = (digits) => digits * 10n ** BigInt(scale + tenShift) * 2n ** BigInt(twoShift);
  const abs = (n) => (n < 0n ? -n : n);
  const upperDistance = abs(exact - toInteger(upperDigits));
  const lowerDistance = abs(exact - toInteger(lowerDigits));
  return upperDistance < lowerDistance ? -1 : upperDistance > lowerDistance ? 1 : 0;
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

