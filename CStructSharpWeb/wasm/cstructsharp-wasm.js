/**
 * Browser library entry point for the CStructSharp WebAssembly bundle.
 *
 * Keep this file beside main.js and _framework/ when distributing the bundle.
 */

let loading;

/** Load the managed CStructSharp exports and return the browser API. */
export async function loadCStructSharpWasm() {
  if (globalThis.CStructSharpWasm?.ready) {
    return globalThis.CStructSharpWasm;
  }

  if (!loading) {
    loading = import("./main.js").then(() => {
      if (globalThis.CStructSharpWasm?.ready) {
        return globalThis.CStructSharpWasm;
      }
      throw new Error("CStructSharp WASM finished loading without usable exports.");
    });
  }

  return loading;
}

/** Parse bytes and return the versioned CStructSharp result envelope. */
export async function parseWithDebug(definition, bytes, options = null) {
  const api = await loadCStructSharpWasm();
  return parseEnvelope(api.parseWithDebug(definition, toBase64(bytes), options), "parse");
}

/** Serialize a JavaScript value. Successful Data is a Uint8Array ready to use. */
export async function serialize(definition, value, options = null) {
  const api = await loadCStructSharpWasm();
  return parseByteEnvelope(
    api.serializeToBase64(definition, stringifyInteropValue(value), options),
    "serialize",
  );
}

/** Update one path in bytes. Successful Data is the complete updated Uint8Array. */
export async function update(definition, bytes, path, value, options = null) {
  const api = await loadCStructSharpWasm();
  return parseByteEnvelope(
    api.updateStreamToBase64(
      definition,
      toBase64(bytes),
      path,
      stringifyInteropValue(value),
      options,
    ),
    "update",
  );
}

/** Return the managed library version from the loaded bundle. */
export async function getVersion() {
  const api = await loadCStructSharpWasm();
  return api.getVersion();
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

// Base64 is only a transport detail of the raw managed bridge.
function parseByteEnvelope(value, operation) {
  const result = parseEnvelope(value, operation);
  if (result.Success) {
    result.Data = Uint8Array.from(atob(result.Data), (character) => character.charCodeAt(0));
  }
  return result;
}

function toBase64(bytes) {
  if (!(bytes instanceof Uint8Array)) {
    throw new TypeError("Binary data must be a Uint8Array.");
  }

  const chunks = [];
  for (let offset = 0; offset < bytes.length; offset += 24_576) {
    chunks.push(String.fromCharCode(...bytes.subarray(offset, offset + 24_576)));
  }
  return btoa(chunks.join(""));
}

function stringifyInteropValue(value) {
  return JSON.stringify(value, (_key, current) =>
    typeof current === "bigint" ? current.toString(10) : current,
  );
}
