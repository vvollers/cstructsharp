/**
 * Suggests what to check next for a failed operation, by its error code. Both apps show the hint above the error
 * details; each names its own limits dialog and its own fallback advice.
 * @param code The envelope's error code, when there is one.
 * @param context Where this app keeps its read limits, and the advice for a code without a specific hint.
 * @param context.limits The name of the settings section that holds the read and write limits.
 * @param context.fallback The advice shown for any other code.
 * @returns One or two sentences of advice.
 */
export function errorRecoveryHint(
  code: string | undefined,
  context: { limits: string; fallback: string },
): string {
  const hints: Record<string, string> = {
    "invalid-layout":
      "Check the declaration spelling and supported layout syntax. C headers may need translation.",
    "invalid-path":
      "Check the root and field names, including their letter case. Use dots between nested fields.",
    "read-failed":
      "Check that all required bytes are present and that the selected root, byte order, and pointer settings match the format.",
    "read-budget": `Compare the expected field sizes with ${context.limits}. Increase a limit only when the format requires that amount of data.`,
    "write-failed":
      "Check the JSON field names, numeric ranges, and text capacity. An update cannot move later fields.",
    "write-budget": `Check the output size against ${context.limits} before increasing the allowed work.`,
    "invalid-input":
      "Check the input and the settings. The size of a file is independent of the read limits.",
    "file-read-failed": "Reload the file after checking its location and access permissions.",
    "browser-error":
      "Check that bytes are pairs of hexadecimal digits and the value is valid JSON.",
  };
  return hints[code ?? ""] ?? context.fallback;
}
