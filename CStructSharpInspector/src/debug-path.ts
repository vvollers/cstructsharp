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
