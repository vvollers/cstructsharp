/**
 * Locate and validate the managed exports, then expose the stable browser-facing adapter (contract v8).
 * This module has no dependency on the .NET runtime and is therefore directly unit-testable.
 */
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
    parseBytes(definition, bytes, options = null, debug = false) {
      return managed.ParseBytes(
        definition,
        bytes,
        stringifyOptions(options),
        debug,
      );
    },
    serialize(definition, dataJson, options = null) {
      return managed.Serialize(definition, dataJson, stringifyOptions(options));
    },
    updateStream(definition, bytes, path, valueJson, options = null) {
      return managed.UpdateStream(
        definition,
        bytes,
        path,
        valueJson,
        stringifyOptions(options),
      );
    },
    /** The managed library version of the loaded bundle. */
    getVersion() {
      return managed.GetVersion();
    },
    /** The static read plan of a root as JSON text, or "" when the layout is not fully fixed. */
    getStaticPlan(definition, options = null) {
      return managed.GetStaticPlan(definition, stringifyOptions(options));
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
  return JSON.stringify(options ?? {}, (_key, value) =>
    typeof value === "bigint" ? value.toString(10) : value,
  );
}
