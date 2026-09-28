/**
 * Locate and validate the managed exports, then expose the stable browser-facing adapter.
 * This module has no dependency on the .NET runtime and is therefore directly unit-testable.
 */
import { parseEnvelope, stringifyInteropJson } from "./cstructsharp-shared.js";
import { collectBytes, compileLargeSource, parseLargeSource, resolveAddressLargeSource } from "./large-source.js";

/**
 * Binds the managed exports to the synchronous adapter the public API and the apps call.
 * @param {object} assemblyExports The runtime's assembly exports, nested (`CStructSharpWeb.Wasm.CStructExports`) or flat.
 * @returns {object} The adapter; it throws at creation when a required managed export is missing.
 */
export function createCStructSharpWasm(assemblyExports) {
  const managed =
    assemblyExports?.CStructSharpWeb?.Wasm?.CStructExports ??
    assemblyExports?.CStructExports;
  if (!managed) {
    throw new Error("Managed CStructExports object was not found.");
  }

  const required = [
    "ParseBytes",
    "Serialize",
    "UpdateStream",
    "TakeOutput",
    "ResolveAddress",
    "GetVersion",
    "GetStaticPlan",
  ];
  const missing = required.filter(
    (name) => typeof managed[name] !== "function",
  );
  if (missing.length > 0) {
    throw new Error(
      `Managed CStruct exports are missing: ${missing.join(", ")}`,
    );
  }

  /**
   * Runs a byte-producing managed write. The export returns the usual envelope; on success its `data` is
   * `{ byteLength }` and the bytes themselves wait in managed memory until TakeOutput hands them over as a native
   * Uint8Array, which replaces `data` in the returned envelope.
   * @param {"serialize" | "update"} operation The operation the envelope reports.
   * @param {() => string} invoke Calls the managed export.
   * @returns {object} The envelope: `data` is the bytes on success and null on failure.
   * @throws {TypeError} When the envelope is not JSON or the handed-over bytes do not match its byte length.
   */
  function runOutputOperation(operation, invoke) {
    const result = parseEnvelope(invoke(), operation);
    if (!result.success) {
      return result;
    }
    // TakeOutput must follow the envelope directly: the next managed call discards the pending bytes.
    const bytes = managed.TakeOutput();
    if (!(bytes instanceof Uint8Array) || bytes.byteLength !== result.data?.byteLength) {
      throw new TypeError(`CStructSharp returned an invalid ${operation} output.`);
    }
    return { ...result, data: bytes };
  }

  return {
    exports: assemblyExports,
    parseSource: parseLargeSource,
    /**
     * A dedicated worker runtime that retains one compiled layout. The public API supplies its serialize/update
     * so the compiled layout's writes run on the calling thread through the same envelope builders.
     */
    compile: (definition, options = null, writers = {}) =>
      compileLargeSource(definition, options ?? {}, {
        parseBytes: (layout, bytes, parserOptions, debug) =>
          managed.ParseBytes(layout, bytes, stringifyOptions(parserOptions), debug),
        ...writers,
      }),
    resolveAddressSource: resolveAddressLargeSource,
    collectBytes,
    /** Parses a byte array on the calling thread and records every value's byte range; the JSON envelope text. */
    parseWithDebug(definition, bytes, options = null) {
      return managed.ParseBytes(definition, bytes, stringifyOptions(options), true);
    },
    /** Parses a byte array on the calling thread; the JSON envelope text. */
    parseBytes(definition, bytes, options = null, debug = false) {
      return managed.ParseBytes(
        definition,
        bytes,
        stringifyOptions(options),
        debug,
      );
    },
    /** Encodes a value given as JSON text; the serialize envelope, whose `data` is the bytes on success. */
    serialize(definition, dataJson, options = null) {
      return runOutputOperation("serialize", () =>
        managed.Serialize(definition, dataJson, stringifyOptions(options)),
      );
    },
    /** Replaces one path's value in a copy of the bytes; the update envelope, whose `data` is the complete bytes. */
    updateStream(definition, bytes, path, valueJson, options = null) {
      return runOutputOperation("update", () =>
        managed.UpdateStream(
          definition,
          bytes,
          path,
          valueJson,
          stringifyOptions(options),
        ),
      );
    },
    /** The managed library version of the loaded bundle, from the version envelope. */
    getVersion() {
      return parseEnvelope(managed.GetVersion(), "version").data.version;
    },
    /**
     * The staticPlan envelope of a root: `data` is `{ root, plan }` when the layout is fully fixed and null when it
     * has no static plan; a failure envelope reports invalid options or input.
     */
    getStaticPlan(definition, options = null) {
      return parseEnvelope(managed.GetStaticPlan(definition, stringifyOptions(options)), "staticPlan");
    },
    ready: true,
    error: null,
  };
}

/**
 * Encodes options as the JSON text the managed exports read; BigInt values become exact decimal strings.
 * @param {object | null | undefined} options The caller's options.
 * @returns {string} The JSON text, `{}` when there are none.
 */
function stringifyOptions(options) {
  return stringifyInteropJson(options ?? {});
}
