import { describe, expect, it } from "vitest";

import { bytesToHex, hexToBytes } from "./hex";

describe("hex text", () => {
  it("reads pairs of digits, ignoring whitespace and letter case", () => {
    expect(Array.from(hexToBytes("0a FF\n00"))).toEqual([0x0a, 0xff, 0x00]);
    expect(hexToBytes("")).toEqual(new Uint8Array());
  });

  it("rejects half a byte and non-hex characters instead of truncating", () => {
    expect(() => hexToBytes("abc")).toThrow("whole number of bytes");
    expect(() => hexToBytes("0g")).toThrow("non-hexadecimal");
  });

  it("writes lowercase pairs that read back to the same bytes", () => {
    const bytes = new Uint8Array([0, 15, 16, 255]);
    expect(bytesToHex(bytes)).toBe("00 0f 10 ff");
    expect(hexToBytes(bytesToHex(bytes))).toEqual(bytes);
  });
});
