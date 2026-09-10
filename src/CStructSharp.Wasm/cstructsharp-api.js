/** Shared operation conversions for the ZIP, Node, and browser adapters. */
const INTEROP_CONTRACT_VERSION = 5;

export function createPublicApi(loadCStructSharpWasm) {
  /** Parse bytes; successful Data is JSON text with the selected root wrapper.
   * @param {string} definition Portable layout source.
   * @param {Uint8Array} bytes Input bytes.
   * @param {import("./cstructsharp-wasm.js").ParseWithDebugOptions | null} [options]
   * @returns {Promise<import("./cstructsharp-wasm.js").Result<string, "parse">>}
   */
  async function parseWithDebug(definition, bytes, options = null) {
    requireBytes(bytes);
    const api = await loadCStructSharpWasm();
    return parseEnvelope(api.parseWithDebug(definition, bytes, options), "parse");
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
      api.updateStream(definition, bytes, path, stringifyInteropValue(value), options),
    );
  }

  /** Return the managed library version from the loaded bundle. */
  async function getVersion() {
    const api = await loadCStructSharpWasm();
    return api.getVersion();
  }

  return { loadCStructSharpWasm, parseWithDebug, serialize, update, getVersion };
}

function parseEnvelope(value, operation) {
  try {
    return JSON.parse(value);
  } catch (cause) {
    throw new TypeError(`CStructSharp returned an invalid ${operation} response envelope.`, {
      cause,
    });
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
    throw new TypeError(`CStructSharp returned an invalid ${operation} error.`, {
      cause: parseCause,
    });
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
