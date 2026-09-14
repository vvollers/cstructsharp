import { INTEROP_CONTRACT_VERSION, type InteropResult } from "./wasm/cstruct-contract";

export function parseFailure(
  message: string,
  code = "invalid-input",
  offset: number | null = null,
): InteropResult {
  return {
    ContractVersion: INTEROP_CONTRACT_VERSION,
    Operation: "parse",
    Success: false,
    Data: null,
    DebugData: [],
    Error: { Code: code, Message: message, Offset: offset, Path: null },
  };
}

/** Validate the scope of the unmodified ZIP example, not arbitrary user definitions. */
export function validateZipHeader(bytes: Uint8Array): InteropResult | null {
  if (bytes.length < 4) {
    return parseFailure(
      `A ZIP record signature needs 4 bytes; only ${bytes.length} bytes are loaded.`,
      "read-failed",
      0,
    );
  }
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const signature = view.getUint32(0, true);
  if (signature !== 0x04034b50) {
    const reason =
      signature === 0x06054b50
        ? "The file starts with an end-of-central-directory record, as an empty ZIP normally does. There is no local file header here."
        : signature === 0x08074b50
          ? "The file starts with a split/spanned ZIP marker rather than a local file header."
          : "The loaded data does not start with a ZIP local file header. It may have a self-extracting prefix, start at another record, or be a different format.";
    return parseFailure(
      `${reason} This example expects signature 50 4B 03 04 at byte 0; found ${Array.from(bytes.subarray(0, 4), (b) => b.toString(16).padStart(2, "0").toUpperCase()).join(" ")}. Load a local-header slice or adapt the schema.`,
      "format-mismatch",
      0,
    );
  }
  if (bytes.length < 30) {
    return parseFailure(
      `The ZIP local header requires at least 30 bytes; only ${bytes.length} bytes are loaded.`,
      "read-failed",
      bytes.length,
    );
  }
  const nameLength = view.getUint16(26, true);
  const extraLength = view.getUint16(28, true);
  const required = 30 + nameLength + extraLength;
  if (bytes.length < required) {
    return parseFailure(
      `The ZIP header declares ${nameLength} filename bytes and ${extraLength} extra-field bytes, requiring ${required} bytes in total; only ${bytes.length} bytes are loaded. The header is truncated or its length fields are incorrect.`,
      "read-failed",
      bytes.length,
    );
  }
  return null;
}
