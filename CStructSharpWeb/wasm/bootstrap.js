/**
 * Locate and validate the managed exports, then expose the stable browser-facing adapter.
 * This module has no dependency on the .NET runtime and is therefore directly unit-testable.
 */
export function createCStructSharpWasm(assemblyExports) {
  const managed =
    assemblyExports?.CStructSharpWeb?.Wasm?.CStructExports ?? assemblyExports?.CStructExports;
  if (!managed) {
    throw new Error("Managed CStructExports object was not found.");
  }

  const required = ["ParseWithDebug", "Serialize", "UpdateStream", "GetVersion"];
  const missing = required.filter((name) => typeof managed[name] !== "function");
  if (missing.length > 0) {
    throw new Error(`Managed CStruct exports are missing: ${missing.join(", ")}`);
  }

  return {
    exports: assemblyExports,
    parseWithDebug(definition, bytes, options = null) {
      return managed.ParseWithDebug(definition, bytes, stringifyOptions(options));
    },
    serialize(definition, dataJson, options = null) {
      return managed.Serialize(definition, dataJson, stringifyOptions(options));
    },
    updateStream(definition, bytes, path, valueJson, options = null) {
      return managed.UpdateStream(definition, bytes, path, valueJson, stringifyOptions(options));
    },
    getVersion() {
      return managed.GetVersion();
    },
    ready: true,
    error: null,
  };
}

function stringifyOptions(options) {
  return JSON.stringify(options ?? {}, (_key, value) =>
    typeof value === "bigint" ? value.toString(10) : value,
  );
}
