/**
 * The default of every operation option, as the managed library and the public browser package define them. The
 * settings dialogs start from these values, and the explorer's code generator leaves an option out of generated
 * code when it has its default.
 */
export const OPTION_DEFAULTS = {
  root: null,
  pointerSize: 8,
  aligned: false,
  littleEndian: true,
  addressingMode: "Absolute",
  origin: 0,
  dereferencePointers: true,
  maxPointerDepth: 64,
  maxPointerTargetBytes: null,
  maxArrayElements: 1_000_000,
  maxStringBytes: 16 * 1024 * 1024,
  maxNestingDepth: 256,
  maxTotalBytesRead: 64 * 1024 * 1024,
  maxTotalBytesWritten: 64 * 1024 * 1024,
  requireExistingPointerTarget: true,
  clearUnionStorage: true,
  maxTraversalPointerDepth: 64,
  maxTraversalPointerTargetBytes: null,
  maxTraversalStringBytes: 16 * 1024 * 1024,
  maxTraversalBytesRead: 64 * 1024 * 1024,
  maxTraversalNestingDepth: 256,
} as const;

/**
 * Formats a byte count for a settings summary: whole mebibytes as "N MiB", anything else as a grouped byte count.
 * @param value The number of bytes.
 * @returns For example "16 MiB" or "1,500 B".
 */
export function formatBytes(value: number): string {
  return value >= 1_048_576 && value % 1_048_576 === 0
    ? `${value / 1_048_576} MiB`
    : `${value.toLocaleString()} B`;
}
