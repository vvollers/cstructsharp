/**
 * CStructSharp for JavaScript (contract v9). Every operation loads the WebAssembly runtime on first use and
 * returns a `Result` envelope; loading and argument failures reject the returned promise instead.
 */

/** Choices fixed when a layout is compiled; every operation also accepts them alongside its own options. */
export interface CompileOptions {
  /** Insert Portable alignment padding. Default: false. */
  aligned?: boolean;
  /** Stored pointer width in bytes (1, 2, 4, or 8). Default: 8. */
  pointerSize?: number;
  /** Default byte order. Default: true. */
  littleEndian?: boolean;
  /** How adjacent bitfields of different declared sizes share storage. Default: "SysV" (GCC/Clang); "Msvc" keeps one unit per size. */
  bitfieldPacking?: "SysV" | "Msvc";
  /** Which end of a storage unit the first bitfield takes. Default: "LowBitFirst". */
  bitfieldAllocation?: "LowBitFirst" | "HighBitFirst";
  /** The width of C `long`: 64 (default) or 32. */
  cLongWidth?: 32 | 64;
  maxDefinitionLength?: number;
  maxLayoutNestingDepth?: number;
  maxExpressionNestingDepth?: number;
  maxExpressionTokens?: number;
}

/** Choices every operation accepts. */
export interface OperationOptions {
  /** Case-sensitive root name or nested path. Omitted: the first declared struct. */
  root?: string | null;
  /** Keep only the category text in `error.message` and drop `path`, `member`, and `memberType`. Default: false. */
  redactDiagnostics?: boolean;
}

export interface ParseOptions extends OperationOptions {
  /** Cancel source staging or worker parsing. Cancellation rejects with AbortError. */
  signal?: AbortSignal;
  /** Maximum temporary storage for a one-pass source. Default: 1 GiB. Not a File/Blob size limit in browsers. */
  maxSpoolBytes?: number;
  addressingMode?: "Absolute" | "Relative";
  /** Address origin. Use a decimal string or bigint for an exact large integer. */
  origin?: number | string | bigint;
  dereferencePointers?: boolean;
  maxPointerDepth?: number;
  maxPointerTargetBytes?: number | null;
  /** Default: 1,000,000. Configurable through 2,147,483,647; decoded results must fit memory. */
  maxArrayElements?: number;
  /** Default: 16 MiB. Configurable through 2,147,483,647 encoded bytes. */
  maxStringBytes?: number;
  /** Default: 64 MiB. Counts actual reads, not pointer distance. Maximum: Number.MAX_SAFE_INTEGER. */
  maxTotalBytesRead?: number;
  maxNestingDepth?: number;
  /** Drop the trailing NUL padding of fixed text (char[N], bounded utf8[N]). Default: false. */
  trimFixedText?: boolean;
}

export interface SerializeOptions extends OperationOptions {
  addressingMode?: "Absolute" | "Relative";
  /** Address origin. Use a decimal string or bigint for an exact large integer. */
  origin?: number | string | bigint;
  /** What a member the layout does not declare does: "Ignore" (default) or "Reject". */
  unknownMembers?: "Ignore" | "Reject";
  /** Default: 1,000,000. Configurable through 2,147,483,647; decoded results must fit memory. */
  maxArrayElements?: number;
  /** Default: 16 MiB. Configurable through 2,147,483,647 encoded bytes. */
  maxStringBytes?: number;
  maxTotalBytesWritten?: number;
  maxNestingDepth?: number;
}

export interface UpdateOptions extends SerializeOptions {
  /** Cancel reading a streamed source. */
  signal?: AbortSignal;
  maxSpoolBytes?: number;
  dereferencePointers?: boolean;
  requireExistingPointerTarget?: boolean;
  clearUnionStorage?: boolean;
  maxTraversalPointerDepth?: number;
  maxTraversalPointerTargetBytes?: number | null;
  maxTraversalStringBytes?: number;
  maxTraversalBytesRead?: number;
  maxTraversalNestingDepth?: number;
}

/** The stable failure categories. */
export type ErrorCode =
  | "invalid-layout"
  | "invalid-path"
  | "read-failed"
  | "read-budget"
  | "write-failed"
  | "write-budget"
  | "invalid-input"
  | "invalid-json"
  | "operation-failed";

export interface ErrorDetails {
  code: ErrorCode | string;
  /** The library's own diagnostic (the category text when `redactDiagnostics` is set). */
  message: string;
  /** The selected or failing path, when known. */
  path: string | null;
  /** Binary byte offset where the operation stopped, when known; not a layout source position. */
  offset: number | null;
  /** The innermost field the failure concerns, when known. */
  member: string | null;
  memberType: string | null;
  /** One-based source line and column of a layout error, when known. */
  line: number | null;
  column: number | null;
}

export interface DebugItem {
  /** Half-open byte range [start, end) relative to the operation origin. */
  start: number;
  end: number;
  /** The value's path, e.g. "header.length". */
  path: string;
  type: string;
  /** Decimal text preserves exact 64-bit integer values. */
  value: string | null;
}

export type Operation = "parse" | "serialize" | "update" | "resolveAddress" | "compile";

/** A parsed enum value; a flag enum also lists the set members and the bits no member covers. */
export interface EnumValue {
  kind: "enum";
  enum: string;
  name: string | null;
  value: number | string;
  names?: string[];
  remainder?: number | string;
}
/** A parsed union: every decoded member view plus the raw storage (Base64); `selectedMember` names the member a write uses. */
export interface UnionValue {
  kind: "union";
  union: string;
  rawStorage: string | null;
  members: { [name: string]: ParsedValue };
  selectedMember: string | null;
}
/** A parsed pointer: the stored address, the pointer depth, and the target when it was followed. */
export interface PointerValue {
  kind: "pointer";
  address: number | string;
  depth: number;
  dereferenced: boolean;
  value: ParsedValue;
}
/**
 * A parsed value as JavaScript data: numbers, booleans, strings (fixed text keeps its padding unless
 * `trimFixedText` is set; integers beyond Number's exact range and 64-bit enum values arrive as decimal strings; a
 * float that is NaN or infinite arrives as "NaN", "Infinity", or "-Infinity"), nested objects for structs, arrays,
 * and the tagged shapes for enums, unions, and pointers. A promoted (anonymous) struct or union member's fields
 * appear directly on the parent object, and a `_` padding field never appears.
 */
export type ParsedValue =
  | null
  | boolean
  | number
  | string
  | ParsedValue[]
  | EnumValue
  | UnionValue
  | PointerValue
  | { [name: string]: ParsedValue };
/** A parsed struct: its members by name, e.g. `data.kind`. */
export type ParsedStruct = { [name: string]: ParsedValue };

export type Result<T, O extends Operation = Operation> = {
  contractVersion: 9;
  operation: O;
  /**
   * The root or path the operation selected. A parse, resolveAddress, or compile reports the resolved name even
   * when the option was omitted; serialize and update echo the `root` option (null when the default root was used).
   */
  root: string | null;
  /** Byte ranges recorded by parseWithDebug; empty otherwise. */
  debug: DebugItem[];
} & ({ success: true; data: T; error: null } | { success: false; data: null; error: ErrorDetails });

export type ParseResult = Result<ParsedValue, "parse">;

/** JSON-compatible application values; bigint is sent as exact decimal text. */
export type InputValue =
  | null
  | boolean
  | number
  | string
  | bigint
  | InputValue[]
  | { [name: string]: InputValue };

/** Raw bytes of a view are used, respecting byteOffset/byteLength; no numeric element conversion. */
export type BinaryChunk = ArrayBufferLike | ArrayBufferView;
/** Streams/iterables yield binary chunks only and are staged to temporary storage before parsing. */
export type BinarySource =
  | BinaryChunk
  | Blob
  | Response
  | ReadableStream<BinaryChunk>
  | Iterable<BinaryChunk>
  | AsyncIterable<BinaryChunk>
  | { getFile(): Promise<File> };

/**
 * The envelope of the static read plan a fully fixed root has. `data` is null when the root has no static plan
 * (a variable-length or union root); a failure reports invalid options or input. The plan's shape is an internal
 * detail of the public parse's fast path, not a stable format.
 */
export type StaticPlanResult = {
  contractVersion: 9;
  operation: "staticPlan";
  root: string | null;
  debug: DebugItem[];
} & (
  | { success: true; data: { root: string; plan: unknown } | null; error: null }
  | { success: false; data: null; error: ErrorDetails }
);

/**
 * Advanced raw transport API: the object the runtime publishes once it is ready. Binary data crosses the boundary
 * as a native Uint8Array, never Base64 text. The byte parses return the envelope's JSON text; serialize/updateStream
 * return the envelope itself, with the produced bytes as `data` on success and the structured error on failure,
 * exactly like the public operations. Prefer the public operations below.
 */
export interface RawWasmAdapter {
  /**
   * Compile a layout into a dedicated worker runtime. `writers` supplies the functions the compiled layout's
   * serialize/update call on the calling thread (the public API passes its own `serialize` and `update`); a
   * layout compiled without them rejects its writes.
   */
  compile(
    definition: string,
    options?: (CompileOptions & OperationOptions) | null,
    writers?: { serialize?: typeof serialize; update?: typeof update },
  ): Promise<CompiledLayout>;
  ready: true;
  error: null;
  /** Parse any binary source in the shared worker; `debug` records every value's byte range. */
  parseSource(
    definition: string,
    source: BinarySource,
    options: (CompileOptions & ParseOptions) | null,
    debug: boolean,
  ): Promise<ParseResult>;
  /** Resolve the absolute byte position of a path in the shared worker; the source is staged like a parse. */
  resolveAddressSource(
    definition: string,
    source: BinarySource,
    path: string,
    options?: (CompileOptions & ParseOptions) | null,
  ): Promise<Result<number | string, "resolveAddress">>;
  /**
   * Read a whole binary source into one Uint8Array. A buffer or view is returned as a view without copying; any
   * other source is read completely, bounded by `maxSpoolBytes`.
   */
  collectBytes(
    source: BinarySource,
    options?: Pick<ParseOptions, "signal" | "maxSpoolBytes"> | null,
  ): Promise<Uint8Array>;
  /** Synchronous byte-array parse that records every value's byte range; the JSON text of a ParseResult envelope. */
  parseWithDebug(definition: string, bytes: Uint8Array, options?: (CompileOptions & ParseOptions) | null): string;
  /** Synchronous byte-array parse; the JSON text of a ParseResult envelope. */
  parseBytes(
    definition: string,
    bytes: Uint8Array,
    options?: (CompileOptions & ParseOptions) | null,
    debug?: boolean,
  ): string;
  serialize(
    definition: string,
    json: string,
    options?: (CompileOptions & SerializeOptions) | null,
  ): Result<Uint8Array, "serialize">;
  updateStream(
    definition: string,
    bytes: Uint8Array,
    path: string,
    json: string,
    options?: (CompileOptions & UpdateOptions) | null,
  ): Result<Uint8Array, "update">;
  /** The static read plan of a root, which the public parse uses to read small fixed inputs directly. */
  getStaticPlan(definition: string, options?: (CompileOptions & OperationOptions) | null): StaticPlanResult;
  getVersion(): string;
}
/** Load the runtime, or reject if it cannot load. Prefer the public operations below. */
export function loadCStructSharpWasm(options?: { runtimeUrl?: string }): Promise<RawWasmAdapter>;

/**
 * Parse a binary source. `data` is the selected value (the root struct's members by name, e.g. `data.kind`, or the
 * union, array, or scalar a path selects). Large source length is independent of the read limits.
 */
export function parse(
  definition: string,
  source: BinarySource,
  options?: (CompileOptions & ParseOptions) | null,
): Promise<ParseResult>;
/** Parse and record each value's byte range in `debug`. */
export function parseWithDebug(
  definition: string,
  source: BinarySource,
  options?: (CompileOptions & ParseOptions) | null,
): Promise<ParseResult>;
/** Create bytes from the selected root's fields (a parse result's `data` works as is). */
export function serialize(
  definition: string,
  value: unknown,
  options?: (CompileOptions & SerializeOptions) | null,
): Promise<Result<Uint8Array, "serialize">>;
/** Replace the value at one path. `data` is the complete updated bytes; the input is never mutated. */
export function update(
  definition: string,
  source: BinarySource,
  path: string,
  value: unknown,
  options?: (CompileOptions & UpdateOptions) | null,
): Promise<Result<Uint8Array, "update">>;
/** Resolve the absolute byte position of a path. `data` is the position (a decimal string beyond 2^53). */
export function resolveAddress(
  definition: string,
  source: BinarySource,
  path: string,
  options?: (CompileOptions & ParseOptions) | null,
): Promise<Result<number | string, "resolveAddress">>;
export function getVersion(): Promise<string>;

/** A compiled layout; compile-time options are fixed, every operation takes its own options. */
export interface CompiledLayout {
  /** The root the layout selects by default. */
  readonly root: string;
  parse(source: BinarySource, options?: ParseOptions | null): Promise<ParseResult>;
  parseWithDebug(source: BinarySource, options?: ParseOptions | null): Promise<ParseResult>;
  serialize(value: unknown, options?: SerializeOptions | null): Promise<Result<Uint8Array, "serialize">>;
  update(source: BinarySource, path: string, value: unknown, options?: UpdateOptions | null): Promise<Result<Uint8Array, "update">>;
  resolveAddress(source: BinarySource, path: string, options?: ParseOptions | null): Promise<Result<number | string, "resolveAddress">>;
  /** Idempotent; cancels outstanding calls, closes sources and releases the runtime. */
  dispose(): Promise<void>;
}
/**
 * Compile once and reuse. An invalid definition rejects with an Error whose `details` is the ErrorDetails (with
 * the source line and column).
 */
export function compile(
  definition: string,
  options?: (CompileOptions & OperationOptions) | null,
): Promise<CompiledLayout>;
