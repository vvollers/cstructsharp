/**
 * Declarations of cstructsharp-shared.js for the repository's TypeScript consumers (the apps' WASM adapter). The
 * module is internal to the package; its public contract is index.d.ts.
 */

/** Byte inputs up to this size, without a cancellation signal, are parsed on the calling thread. */
export const SYNCHRONOUS_PARSE_LIMIT: number;
/** The largest byte input the managed bridge copies into WebAssembly memory (4 MiB). */
export const MANAGED_COPY_LIMIT: number;
/** Number.MAX_SAFE_INTEGER as a BigInt. */
export const MAX_SAFE_INTEGER_BIG: bigint;
/** Options fixed when a layout is compiled. */
export const COMPILE_OPTION_KEYS: ReadonlySet<string>;
/** Whether a source is a small byte buffer or view without a cancellation signal. */
export function isSmallByteInput(source: unknown, options: { signal?: AbortSignal } | null | undefined): boolean;
/** Serializes a value to JSON text, writing each BigInt as its exact decimal string. */
export function stringifyInteropJson(value: unknown): string;
/** Decodes the UTF-8 envelope a parse export returned into JSON text; throws a TypeError for anything else. */
export function decodeEnvelopeText(bytes: unknown, operation: string): string;
/** Parses the JSON envelope a managed export returned; throws a TypeError for invalid JSON. */
export function parseEnvelope(text: string, operation: string): unknown;
