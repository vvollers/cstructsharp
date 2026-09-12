import type { DebugDataItem } from "./wasm/cstruct-contract";

/**
 * Converts a DebugData entry's dot/bracket path notation (e.g. "root.values[2].tail") into the
 * string-array JSONPath shape vanilla-jsoneditor's select()/scrollTo() expect (e.g.
 * ["root", "values", "2", "tail"]). The parsed JSON tree's own root already carries the same
 * top-level root-type key DebugStackString paths start with, so no stripping is needed.
 */
export function tokenizePath(stackString: string): string[] {
  return stackString.match(/[^.[\]]+/g) ?? [];
}

/** Map declaration paths into the serialized result, which wraps pointer targets in Value. */
export function debugEntryJsonPath(item: DebugDataItem, result?: unknown): string[] {
  const path: string[] = [];
  let value = result;
  for (const segment of tokenizePath(item.DebugStackString)) {
    while (isPointer(value)) {
      path.push("Value");
      value = value.Value;
    }
    path.push(segment);
    value =
      value !== null && typeof value === "object"
        ? (value as Record<string, unknown>)[segment]
        : undefined;
  }
  // The pointer's own debug record covers its stored address, not its target's fields.
  if (isPointer(value)) path.push("Address");
  return path;
}

function isPointer(
  value: unknown,
): value is { Address: number; Depth: number; IsDereferenced: boolean; Value: unknown } {
  return (
    value !== null &&
    typeof value === "object" &&
    "Address" in value &&
    typeof value.Address === "number" &&
    "Depth" in value &&
    typeof value.Depth === "number" &&
    "IsDereferenced" in value &&
    typeof value.IsDereferenced === "boolean" &&
    "Value" in value
  );
}

/**
 * Finds every DebugData entry related to a JSONPath (from a JSON-tree selection) - not just the first
 * one, since a single click can genuinely need more than one entry:
 *
 * - A struct array's element leaves each get their own indexed path (e.g. "entries[0].width",
 *   "entries[1].width", ...), so clicking the array's own container key needs every descendant leaf
 *   whose path the clicked path is a prefix of.
 * - A *scalar* array (e.g. `uint16 e_res[4]`) is recorded differently by the managed debug output: every
 *   element shares the exact same un-indexed path ("root.dos.e_res" four times, once per element, each
 *   with its own CurPos/EndPos) rather than being indexed per element. A plain first-match lookup here
 *   would only ever select the first element's 2 bytes - "only the first element is highlighted" - so an
 *   exact path match must return every entry sharing that path, and a *specific* index the caller cannot
 *   otherwise distinguish (no per-element path exists for it) still resolves to the same full set via the
 *   entry's shorter path being a prefix of the clicked (deeper, indexed) one.
 *
 * When a parsed result is supplied, declaration paths are mapped to the JSON pointer wrappers first.
 * Two resulting paths are "related" when one is a prefix of the other.
 */
export function findDebugEntryIndicesByPath(
  debugData: DebugDataItem[],
  path: string[],
  result?: unknown,
): number[] {
  const indices: number[] = [];
  debugData.forEach((item, index) => {
    if (isPathRelated(debugEntryJsonPath(item, result), path)) {
      indices.push(index);
    }
  });
  return indices;
}

function isPathRelated(a: string[], b: string[]): boolean {
  const length = Math.min(a.length, b.length);
  for (let i = 0; i < length; i++) {
    if (a[i] !== b[i]) {
      return false;
    }
  }
  return true;
}

/** Finds the index of the DebugData entry covering an absolute byte offset (from a hex-view click). */
export function findDebugEntryIndexByOffset(debugData: DebugDataItem[], offset: number): number {
  return debugData.findIndex((item) => offset >= item.CurPos && offset < item.EndPos);
}

/**
 * Maps each DebugData entry (by its index in the array) to a "field group" index, for coloring the hex
 * view. Every element of one array - and everything nested inside each element - shares a single group,
 * so the whole array highlights as one block instead of every element (or every leaf field inside every
 * element) getting its own separate color. Every other (non-array) leaf field keeps its own individual
 * group, unchanged. An array element is detected by truncating a tokenized path right before its first
 * purely-numeric segment - a Portable identifier can never be all digits, so that segment can only be an
 * array index.
 */
export function computeFieldGroups(debugData: DebugDataItem[]): number[] {
  const groupIndexByKey = new Map<string, number>();
  return debugData.map((item) => {
    const key = arrayGroupKey(tokenizePath(item.DebugStackString));
    let index = groupIndexByKey.get(key);
    if (index === undefined) {
      index = groupIndexByKey.size;
      groupIndexByKey.set(key, index);
    }
    return index;
  });
}

function arrayGroupKey(path: string[]): string {
  const arrayIndexPosition = path.findIndex((segment) => /^\d+$/.test(segment));
  const truncated = arrayIndexPosition === -1 ? path : path.slice(0, arrayIndexPosition);
  return JSON.stringify(truncated);
}
