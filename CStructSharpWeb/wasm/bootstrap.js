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

  const required = ["ParseWithDebug", "SerializeToBase64", "UpdateStreamToBase64", "GetVersion"];
  const missing = required.filter((name) => typeof managed[name] !== "function");
  if (missing.length > 0) {
    throw new Error(`Managed CStruct exports are missing: ${missing.join(", ")}`);
  }

  return {
    exports: assemblyExports,
    parseWithDebug(definition, binaryBase64, options = null) {
      return managed.ParseWithDebug(definition, binaryBase64, stringifyOptions(options));
    },
    serializeToBase64(definition, dataJson, options = null) {
      return managed.SerializeToBase64(definition, dataJson, stringifyOptions(options));
    },
    updateStreamToBase64(definition, binaryBase64, path, valueJson, options = null) {
      return managed.UpdateStreamToBase64(
        definition,
        binaryBase64,
        path,
        valueJson,
        stringifyOptions(options),
      );
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
