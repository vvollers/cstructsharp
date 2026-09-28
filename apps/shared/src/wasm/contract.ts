/**
 * The browser wire contract as the apps see it. Every shape comes from the public package declarations
 * (`packages/cstructsharp/index.d.ts`), so the apps and the published package cannot describe the envelope
 * differently; `tools/quality/browser-contract.mjs` checks the version below against the managed bridge.
 */
import type {
  CompileOptions,
  Operation,
  ParseOptions,
  ParsedValue,
  Result,
  SerializeOptions,
  UpdateOptions,
} from "../../../../packages/cstructsharp/index.js";

export type {
  DebugItem,
  ErrorDetails,
  ParsedValue,
  RawWasmAdapter,
  SerializeOptions,
  UpdateOptions,
} from "../../../../packages/cstructsharp/index.js";

/** The contract version every envelope carries; the declarations fix it as a literal type. */
export const INTEROP_CONTRACT_VERSION = 9 as const satisfies Result<unknown>["contractVersion"];

/** The operations an envelope can report. */
export type InteropOperation = Operation;

/** Every option a parse accepts: the compile-time choices plus the parse's own. */
export type ParseWithDebugOptions = CompileOptions & ParseOptions;

/** Every option a serialize accepts. */
export type SerializeCallOptions = CompileOptions & SerializeOptions;

/** Every option an update accepts. */
export type UpdateCallOptions = CompileOptions & UpdateOptions;

/**
 * One operation's envelope. `data` is the selected value for "parse" (the root struct's members, or the
 * union/array/scalar a path selects), the bytes for "serialize"/"update", the position for "resolveAddress";
 * it is null on failure, when `error` says why.
 */
export type InteropResult = Result<ParsedValue | Uint8Array | number | string | null>;
