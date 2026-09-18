import type {
  CompileOptions,
  DebugItem,
  ErrorDetails,
  ParseOptions,
  ParsedValue,
  SerializeOptions,
  UpdateOptions,
} from "../../wasm/cstructsharp-wasm.js";
export type {
  CompileOptions,
  DebugItem,
  EnumValue,
  ErrorDetails,
  ParseOptions,
  ParsedStruct,
  ParsedValue,
  PointerValue,
  SerializeOptions,
  UnionValue,
  UpdateOptions,
} from "../../wasm/cstructsharp-wasm.js";

/**
 * Single TypeScript source for the versioned wire contract (v8) shared by the Vue adapter and the real-browser
 * contract tests. The C# DTOs are verified against these shapes by the Playwright success and failure matrix.
 */

export const INTEROP_CONTRACT_VERSION = 8 as const;

/** Every option an operation accepts: the compile-time choices plus the operation's own. */
export type ParseWithDebugOptions = CompileOptions & ParseOptions;
export type SerializeCallOptions = CompileOptions & SerializeOptions;
export type UpdateCallOptions = CompileOptions & UpdateOptions;

export function isUnionValue(
  value: unknown,
): value is import("../../wasm/cstructsharp-wasm.js").UnionValue {
  if (typeof value !== "object" || value === null) {
    return false;
  }

  const candidate = value as Record<string, unknown>;
  return (
    candidate.kind === "union" &&
    typeof candidate.union === "string" &&
    (typeof candidate.rawStorage === "string" || candidate.rawStorage === null) &&
    typeof candidate.members === "object" &&
    candidate.members !== null &&
    (typeof candidate.selectedMember === "string" || candidate.selectedMember === null)
  );
}

export function isEnumValue(
  value: unknown,
): value is import("../../wasm/cstructsharp-wasm.js").EnumValue {
  if (typeof value !== "object" || value === null) {
    return false;
  }

  const candidate = value as Record<string, unknown>;
  return (
    candidate.kind === "enum" &&
    typeof candidate.enum === "string" &&
    (typeof candidate.name === "string" || candidate.name === null)
  );
}

export function isPointerValue(
  value: unknown,
): value is import("../../wasm/cstructsharp-wasm.js").PointerValue {
  if (typeof value !== "object" || value === null) {
    return false;
  }

  const candidate = value as Record<string, unknown>;
  return (
    candidate.kind === "pointer" &&
    (typeof candidate.address === "number" || typeof candidate.address === "string") &&
    typeof candidate.dereferenced === "boolean"
  );
}

export type InteropOperation = "parse" | "serialize" | "update" | "resolveAddress" | "compile";

/**
 * `data` is the selected value for "parse" (the root struct's members, or the union/array/scalar a path selects),
 * raw bytes for "serialize"/"update" (a native Uint8Array), the position for "resolveAddress", or null on failure.
 */
export interface InteropResult {
  contractVersion: typeof INTEROP_CONTRACT_VERSION;
  operation: InteropOperation;
  success: boolean;
  root: string | null;
  data: ParsedValue | Uint8Array | number | string | null;
  debug: DebugItem[];
  error: ErrorDetails | null;
}

/** Describes the fully validated JavaScript adapter published by bootstrap.js. */
export interface RawWasmAdapter {
  ready: true;
  error: null;
  exports: unknown;
  parseWithDebug: (
    cstructDefinition: string,
    binaryData: Uint8Array,
    options?: ParseWithDebugOptions | null,
  ) => string;
  parseBytes: (
    cstructDefinition: string,
    binaryData: Uint8Array,
    options: ParseWithDebugOptions | null,
    debug: boolean,
  ) => string;
  parseSource: import("../../wasm/cstructsharp-wasm.js").RawWasmAdapter["parseSource"];
  /** Returns the encoded bytes directly on success; throws (see cstruct-wasm.ts) on failure. */
  serialize: (
    cstructDefinition: string,
    dataJson: string,
    options?: SerializeCallOptions | null,
  ) => Uint8Array;
  /** Returns the complete updated bytes directly on success; throws (see cstruct-wasm.ts) on failure. */
  updateStream: (
    cstructDefinition: string,
    binaryData: Uint8Array,
    elementNameOrPath: string,
    valueJson: string,
    options?: UpdateCallOptions | null,
  ) => Uint8Array;
  getVersion: () => string;
}
