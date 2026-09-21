/** Shared benchmark correctness-gate selection; importing this module never loads fixture bytes or starts timing. */

/** Returns whether a fixture has a managed expected value and fits the existing synchronous browser limits. */
export function canVerifyPublicFixture(document) {
  if ((document.expected === null || document.expected === undefined) && !document.expectedSha256) return false;
  if (document.byteLength > 4 * 1024 * 1024 || (document.readOptions?.maxArrayElements ?? 0) > 1_000_000) return false;
  return true;
}

/** The representative correctness fixtures the browser harness verifies before its warm timing cases. */
export const browserVerificationFixtures = Object.freeze([
  "prim-le-record", "nested-x256", "array-u8-1024", "union-x1k", "strings-1024",
  "pointer-depth-8", "cond-if128", "real-png", "real-pe-exe",
]);
