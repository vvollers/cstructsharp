import type {
  ParseWithDebugOptions,
  SerializeOptions,
  UpdateOptions,
} from "../../wasm/cstructsharp-wasm.js";
export type {
  LayoutOptions,
  ParseWithDebugOptions,
  SerializeOptions,
  UpdateOptions,
} from "../../wasm/cstructsharp-wasm.js";

/**
 * Single TypeScript source for the versioned wire contract shared by the Vue
 * adapter and real-browser contract tests. The C# DTOs are verified against
 * these shapes by the Playwright success and failure matrix.
 */

export const INTEROP_CONTRACT_VERSION = 7 as const;

export interface UnionValue {
  $kind: "union";
  Union: string;
  RawStorage: string | null;
  Members: Record<string, unknown>;
  SelectedMember: string | null;
}

export function isUnionValue(value: unknown): value is UnionValue {
  if (typeof value !== "object" || value === null) {
    return false;
  }

  const candidate = value as Partial<UnionValue>;
  return (
    candidate.$kind === "union" &&
    typeof candidate.Union === "string" &&
    (typeof candidate.RawStorage === "string" || candidate.RawStorage === null) &&
    typeof candidate.Members === "object" &&
    candidate.Members !== null &&
    (typeof candidate.SelectedMember === "string" || candidate.SelectedMember === null)
  );
}

export interface ParsedEnumValue {
  Enum: string;
  Name: string | null;
  Value: number | string;
}

/** A parsed value as JavaScript data (contract v7); large integers arrive as decimal strings. */
export type ParsedValue =
  null | boolean | number | string | ParsedValue[] | { [name: string]: ParsedValue };
/** A successful parse result's Data: the selected root's value under its name. */
export type ParsedData = { [rootName: string]: ParsedValue };

export interface DebugDataItem {
  CurPos: number;
  EndPos: number;
  DebugStackString: string;
  Type: string;
  Value: string | null;
}

export interface ErrorDetails {
  Code: string;
  Message: string;
  Offset: number | null;
  Path: string | null;
}

export type InteropOperation = "parse" | "serialize" | "update";

/**
 * Data is the parsed value for "parse" (an object with the root wrapper, contract v7), raw bytes for "serialize"/"update" (a native Uint8Array -
 * the managed bridge no longer transports binary payloads as Base64 text), or null on failure.
 */
export interface InteropResult {
  ContractVersion: typeof INTEROP_CONTRACT_VERSION;
  Operation: InteropOperation;
  Success: boolean;
  Data: ParsedData | Uint8Array | null;
  DebugData: DebugDataItem[];
  Error: ErrorDetails | null;
}

/** Describes the fully validated JavaScript adapter published by bootstrap.js. */
export interface RawWasmAdapter {
  ready: true;
  error: null;
  exports: unknown;
  parseSource: import("../../wasm/cstructsharp-wasm.js").RawWasmAdapter["parseSource"];
  parseWithDebug: (
    cstructDefinition: string,
    binaryData: Uint8Array,
    options?: ParseWithDebugOptions | null,
  ) => string;
  /** Returns the encoded bytes directly on success; throws (see cstruct-wasm.ts) on failure. */
  serialize: (
    cstructDefinition: string,
    dataJson: string,
    options?: SerializeOptions | null,
  ) => Uint8Array;
  /** Returns the complete updated bytes directly on success; throws (see cstruct-wasm.ts) on failure. */
  updateStream: (
    cstructDefinition: string,
    binaryData: Uint8Array,
    elementNameOrPath: string,
    valueJson: string,
    options?: UpdateOptions | null,
  ) => Uint8Array;
  getVersion: () => string;
}
