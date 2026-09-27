/**
 * Converts a hexadecimal string to bytes, only after validating the entire input. Whitespace is ignored. Silent
 * truncation of an odd final nibble or parseInt's partial parsing would produce plausible-looking but incorrect
 * binary data, so both are rejected.
 * @param hex Hex digits, two per byte, optionally separated by whitespace.
 * @returns The bytes.
 * @throws TypeError when the digits do not form whole bytes or contain a non-hex character.
 */
export function hexToBytes(hex: string): Uint8Array {
  const cleanHex = hex.replace(/\s/g, "");
  if (cleanHex.length % 2 !== 0) {
    throw new TypeError("Hex input must contain a whole number of bytes.");
  }

  if (!/^[0-9a-f]*$/i.test(cleanHex)) {
    throw new TypeError("Hex input contains a non-hexadecimal character.");
  }

  const bytes = new Uint8Array(cleanHex.length / 2);
  for (let index = 0; index < bytes.length; index++) {
    bytes[index] = Number.parseInt(cleanHex.slice(index * 2, index * 2 + 2), 16);
  }

  return bytes;
}
