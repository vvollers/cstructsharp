/** Public browser bundle. Operations load WASM lazily and can throw for loading/argument failures. */
export interface LayoutOptions {
  /** Insert Portable alignment padding. Default: false. */
  aligned?: boolean;
  /** Stored pointer width in bytes (1, 2, 4, or 8). Default: 8. */
  pointerSize?: number;
  /** Case-sensitive root/path. Omitted: first struct selected by the bridge. */
  rootTypeName?: string | null;
  /** Default byte order. Default: true. */
  littleEndian?: boolean;
  maxDefinitionLength?: number;
  maxLayoutNestingDepth?: number;
  maxExpressionNestingDepth?: number;
  maxExpressionTokens?: number;
}

export interface ParseWithDebugOptions extends LayoutOptions {
  addressingMode?: "Absolute" | "Relative";
  /** Address origin. Use a decimal string or bigint for an exact large integer. */
  origin?: number | string | bigint;
  dereferencePointers?: boolean;
  maxPointerDepth?: number;
  maxPointerTargetBytes?: number | null;
  maxArrayElements?: number;
  maxStringBytes?: number;
  maxTotalBytesRead?: number;
  maxNestingDepth?: number;
}

export interface SerializeOptions extends LayoutOptions {
  addressingMode?: "Absolute" | "Relative";
  /** Address origin. Use a decimal string or bigint for an exact large integer. */
  origin?: number | string | bigint;
  bindingMode?: "PublicReadable" | "PublicReadWrite";
  maxArrayElements?: number;
  maxStringBytes?: number;
  maxTotalBytesWritten?: number;
  maxNestingDepth?: number;
}

export interface UpdateOptions extends SerializeOptions {
  dereferencePointers?: boolean;
  requireExistingPointerTarget?: boolean;
  clearUnionStorage?: boolean;
  maxTraversalPointerDepth?: number;
  maxTraversalPointerTargetBytes?: number | null;
  maxTraversalStringBytes?: number;
  maxTraversalBytesRead?: number;
  maxTraversalNestingDepth?: number;
}

export interface ErrorDetails {
  Code: string;
  Message: string;
  /** Binary byte offset, when known; not a layout source position. */
  Offset: number | null;
  Path: string | null;
}
export interface DebugDataItem {
  CurPos: number;
  EndPos: number;
  DebugStackString: string;
  Type: string;
  /** Decimal text preserves exact 64-bit integer values. */
  Value: string | null;
  /** Base64 field storage, when available. */
  Buffer: string | null;
}
export type Operation = "parse" | "serialize" | "update";
export type Result<T, O extends Operation> = {
  ContractVersion: 5;
  Operation: O;
  DebugData: DebugDataItem[];
} & (
  | { Success: true; Data: T; Error: null }
  | {
      Success: false;
      Data: null;
      Error: ErrorDetails;
    }
);
/** JSON-compatible application values; bigint is sent as exact decimal text. */
export type InputValue =
  null | boolean | number | string | bigint | InputValue[] | { [name: string]: InputValue };
/**
 * Advanced raw transport API. Binary data crosses the boundary as native Uint8Array/MemoryView, never Base64
 * text. serialize/updateStream report failure by throwing (their JS Error's message is the same JSON-serialized
 * ErrorDetails shape parseWithDebug's envelope carries in its Error field) rather than through a JSON envelope,
 * since there is no envelope object left to carry an Error field alongside a native byte-array success payload.
 */
export interface RawWasmAdapter {
  ready: true;
  error: null;
  exports: unknown;
  parseWithDebug(
    definition: string,
    bytes: Uint8Array,
    options?: ParseWithDebugOptions | null,
  ): string;
  serialize(definition: string, json: string, options?: SerializeOptions | null): Uint8Array;
  updateStream(
    definition: string,
    bytes: Uint8Array,
    path: string,
    json: string,
    options?: UpdateOptions | null,
  ): Uint8Array;
  getVersion(): string;
}
/** Load the bundle, or reject if the runtime cannot load. Prefer the public operations below. */
export function loadCStructSharpWasm(): Promise<RawWasmAdapter>;
/** Read bytes. Successful Data is JSON text with a root wrapper, e.g. values.header.kind.
 * Large integers in that JSON may be decimal strings; do not coerce them to Number.
 */
export function parseWithDebug(
  definition: string,
  bytes: Uint8Array,
  options?: ParseWithDebugOptions | null,
): Promise<Result<string, "parse">>;
/** Create bytes. Pass the selected root's fields without the parse result's root wrapper. */
export function serialize(
  definition: string,
  value: unknown,
  options?: SerializeOptions | null,
): Promise<Result<Uint8Array, "serialize">>;
/** Replace a fixed field. Successful Data is the complete updated bytes; input is not mutated. */
export function update(
  definition: string,
  bytes: Uint8Array,
  path: string,
  value: unknown,
  options?: UpdateOptions | null,
): Promise<Result<Uint8Array, "update">>;
export function getVersion(): Promise<string>;
