import { describe, expect, it } from "vitest";
import { validateZipHeader } from "./parse-diagnostics";

function header(nameLength = 0, extraLength = 0): Uint8Array {
  const bytes = new Uint8Array(30 + nameLength + extraLength);
  const view = new DataView(bytes.buffer);
  view.setUint32(0, 0x04034b50, true);
  view.setUint16(26, nameLength, true);
  view.setUint16(28, extraLength, true);
  return bytes;
}

describe("ZIP header diagnostics", () => {
  it("accepts descriptor and ZIP64 headers without requiring local sizes", () => {
    const bytes = header(8, 20);
    const view = new DataView(bytes.buffer);
    view.setUint16(6, 8, true);
    view.setUint32(18, 0xffffffff, true);
    expect(validateZipHeader(bytes)).toBeNull();
  });

  it("explains empty archives and prefixed files instead of treating them as corrupt local headers", () => {
    expect(validateZipHeader(new Uint8Array([0x50, 0x4b, 5, 6]))?.Error?.Message).toContain(
      "empty ZIP",
    );
    expect(validateZipHeader(new Uint8Array([0x4d, 0x5a, 0, 0]))?.Error?.Message).toContain(
      "self-extracting prefix",
    );
  });

  it("reports required and available bytes for truncated variable fields", () => {
    const error = validateZipHeader(header(8, 20).subarray(0, 40))?.Error;
    expect(error?.Message).toContain("requiring 58 bytes");
    expect(error?.Message).toContain("only 40 bytes");
    expect(error?.Offset).toBe(40);
  });

  it("handles missing signatures, short fixed headers, and sliced buffers", () => {
    expect(validateZipHeader(new Uint8Array())?.Error?.Message).toContain("only 0 bytes");
    expect(validateZipHeader(header().subarray(0, 15))?.Error?.Message).toContain(
      "at least 30 bytes",
    );
    const padded = new Uint8Array(40);
    padded.set(header(), 10);
    expect(validateZipHeader(padded.subarray(10))).toBeNull();
  });
});
