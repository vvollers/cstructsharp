/**
 * Locate and validate the managed exports, then expose the stable browser-facing adapter (contract v8).
 * This module has no dependency on the .NET runtime and is therefore directly unit-testable.
 */
import { collectBytes, compileLargeSource, parseLargeSource, resolveAddressLargeSource } from "./large-source.js";

export function createCStructSharpWasm(assemblyExports) {
  const managed =
    assemblyExports?.CStructSharpWeb?.Wasm?.CStructExports ??
    assemblyExports?.CStructExports;
  if (!managed) {
    throw new Error("Managed CStructExports object was not found.");
  }

  const required = [
    "ParseWithDebug",
    "ParseBytes",
    "Serialize",
    "UpdateStream",
    "ResolveAddress",
    "GetVersion",
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
    parseWithDebug(definition, bytes, options = null) {
      return managed.ParseWithDebug(
        definition,
        bytes,
        stringifyOptions(options),
      );
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
    resolveAddress(definition, bytes, path, options = null) {
      return managed.ResolveAddress(definition, bytes, path, stringifyOptions(options));
    },
    getVersion() {
      return managed.GetVersion();
    },
    /** E3.9: the static read plan of a root as JSON text, or "" when the layout is not fully fixed (older bundles: undefined). */
    getStaticPlan: bindOptional(managed, "GetStaticPlan", (definition, options = null) =>
      managed.GetStaticPlan(definition, stringifyOptions(options)),
    ),
    ready: true,
    error: null,
  };
}

/** Exports added after the reviewed baseline are optional: an older bundle simply lacks the feature. */
function bindOptional(managed, name, binding) {
  return typeof managed[name] === "function" ? binding : undefined;
}

function stringifyOptions(options) {
  return JSON.stringify(options ?? {}, (_key, value) =>
    typeof value === "bigint" ? value.toString(10) : value,
  );
}
