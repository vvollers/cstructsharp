/** Display-only JSON comments; strings and the underlying parse result remain unchanged. */
export function formatParsedJson(value: unknown): string {
  const json = JSON.stringify(value, null, 2) ?? "null";
  return json.replace(
    /^(\s*(?:"(?:\\.|[^"\\])*": )?)(-?\d+)(,?)$/gm,
    (_line, prefix: string, decimal: string, comma: string) => {
      const integer = BigInt(decimal);
      const magnitude = integer < 0n ? -integer : integer;
      const hex = `${integer < 0n ? "-" : ""}0x${magnitude.toString(16).toUpperCase()}`;
      return `${prefix}${decimal}${comma} // ${hex}`;
    },
  );
}
