/**
 * Constants and helpers shared by the public API module (cstructsharp-api.js) and the runtime adapter modules
 * (bootstrap.js, large-source.js, source-worker.js). They do not depend on the wire contract version, which comes
 * from the envelopes the managed exports return.
 *
 * The public API module and the runtime modules are staged in different directories (the npm package keeps the API at
 * its root and the runtime under runtime/), so the packaging tools stage a copy of this self-contained module next to
 * each: in the WASM publication, and at the npm package root. It must therefore import nothing and keep no state.
 */

/** Byte inputs up to this size, without a cancellation signal, are parsed on the calling thread. */
export const SYNCHRONOUS_PARSE_LIMIT = 64 * 1024;

/**
 * The largest byte input the managed bridge copies into WebAssembly memory (4 MiB, its MaximumBinaryInputLength);
 * larger inputs are staged and read by a worker.
 */
export const MANAGED_COPY_LIMIT = 4 * 1024 * 1024;

/** Number.MAX_SAFE_INTEGER (2^53 - 1) as a BigInt: larger integers travel as decimal strings. */
export const MAX_SAFE_INTEGER_BIG = BigInt(Number.MAX_SAFE_INTEGER);

/** Options fixed when a layout is compiled; every operation also accepts them alongside its own options. */
export const COMPILE_OPTION_KEYS = new Set([
  "aligned",
  "pointerSize",
  "littleEndian",
  "bitfieldPacking",
  "bitfieldAllocation",
  "cLongWidth",
  "maxDefinitionLength",
  "maxLayoutNestingDepth",
  "maxExpressionNestingDepth",
  "maxExpressionTokens",
]);

/**
 * Whether a source is a byte buffer or view of at most `limit` bytes, with no cancellation signal.
 * @param {unknown} source Binary source.
 * @param {object | null | undefined} options Operation options; a `signal` excludes the synchronous path.
 * @param {number} [limit] The largest input, in bytes, the calling thread takes: SYNCHRONOUS_PARSE_LIMIT unless an
 *   operation is cheaper there up to a larger size.
 * @returns {boolean} True when the source may be parsed on the calling thread.
 */
export function isSmallByteInput(source, options, limit = SYNCHRONOUS_PARSE_LIMIT) {
  if (options?.signal) return false;
  if (
    source instanceof ArrayBuffer ||
    ArrayBuffer.isView(source) ||
    (typeof SharedArrayBuffer !== "undefined" && source instanceof SharedArrayBuffer)
  ) {
    return source.byteLength <= limit;
  }
  return false;
}

/**
 * Serializes a value to the JSON text the managed exports read, writing each BigInt as its exact decimal string.
 * @param {unknown} value A JSON-serializable value, optionally containing BigInt values.
 * @returns {string | undefined} The JSON text; undefined for a value JSON cannot represent, as `JSON.stringify` does.
 * @throws {TypeError} When the value contains a cycle.
 */
export function stringifyInteropJson(value) {
  return JSON.stringify(value, (_key, current) => (typeof current === "bigint" ? current.toString(10) : current));
}

/**
 * Parses the JSON envelope a managed export returned.
 * @param {string} text The envelope JSON.
 * @param {string} operation The operation name, used in the error message.
 * @returns {object} The envelope.
 * @throws {TypeError} When the text is not valid JSON.
 */
export function parseEnvelope(text, operation) {
  try {
    return JSON.parse(text);
  } catch (cause) {
    throw new TypeError(`CStructSharp returned an invalid ${operation} response envelope.`, { cause });
  }
}
