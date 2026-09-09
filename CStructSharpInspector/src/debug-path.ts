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

/** Finds the index of the DebugData entry whose path exactly matches a JSONPath (from a JSON-tree selection). */
export function findDebugEntryIndexByPath(debugData: DebugDataItem[], path: string[]): number {
  const target = JSON.stringify(path);
  return debugData.findIndex(
    (item) => JSON.stringify(tokenizePath(item.DebugStackString)) === target,
  );
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
