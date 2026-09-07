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

export const INTEROP_CONTRACT_VERSION = 4 as const;

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

export interface DebugDataItem {
  CurPos: number;
  EndPos: number;
  DebugStackString: string;
  Type: string;
  Value: string | null;
  Buffer: string | null;
}

export interface ErrorDetails {
  Code: string;
  Message: string;
  Offset: number | null;
  Path: string | null;
}

export type InteropOperation = "parse" | "serialize" | "update";

export interface InteropResult {
  ContractVersion: typeof INTEROP_CONTRACT_VERSION;
  Operation: InteropOperation;
  Success: boolean;
  Data: string | null;
  DebugData: DebugDataItem[];
  Error: ErrorDetails | null;
}

/** Describes the fully validated JavaScript adapter published by bootstrap.js. */
export interface RawWasmAdapter {
  ready: true;
  error: null;
  exports: unknown;
  parseWithDebug: (
    cstructDefinition: string,
    binaryDataBase64: string,
    options?: ParseWithDebugOptions | null,
  ) => string;
  serializeToBase64: (
    cstructDefinition: string,
    dataJson: string,
    options?: SerializeOptions | null,
  ) => string;
  updateStreamToBase64: (
    cstructDefinition: string,
    binaryDataBase64: string,
    elementNameOrPath: string,
    valueJson: string,
    options?: UpdateOptions | null,
  ) => string;
  getVersion: () => string;
}
