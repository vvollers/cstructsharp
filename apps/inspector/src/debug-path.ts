import type { DebugItem } from "@cstructsharp/app-shared/wasm/contract";

/**
 * Split a parser path into the steps needed to walk the JSON result.
 * For example, "root.values[2].tail" becomes ["root", "values", "2", "tail"].
 * The JSON result includes the root name, so we keep that first step.
 */
export function tokenizePath(stackString: string): string[] {
  return stackString.match(/[^.[\]]+/g) ?? [];
}

/**
 * Convert a debug entry's path into the path of the same value in the JSON result.
 * The parser names a pointer's target with a value step (root.ptr.value.a), exactly as the JSON
 * pointer object holds its target in value, so the tokens already walk the JSON result. The one
 * addition: an entry that ends at the pointer itself covers the stored address, so it maps to the
 * pointer's address property.
 * @param item The debug entry whose path is mapped.
 * @param result The parsed JSON result, used to see whether the path ends at a pointer.
 * @returns The JSON path steps, the root name first.
 */
export function debugEntryJsonPath(item: DebugItem, result?: unknown): string[] {
  const path = tokenizePath(item.path);
  let value = result;

  for (const segment of path) {
    value =
      value !== null && typeof value === "object"
        ? (value as Record<string, unknown>)[segment]
        : undefined;
  }

  // If the path ends at the pointer itself, highlight address: these bytes store the address.
  return isPointer(value) ? [...path, "address"] : path;
}

/**
 * Reports whether a parsed value is a pointer object (`kind: "pointer"` with its address,
 * dereference flag, and target value).
 */
function isPointer(value: unknown): value is {
  kind: "pointer";
  address: number | string;
  depth: number;
  dereferenced: boolean;
  value: unknown;
} {
  return (
    value !== null &&
    typeof value === "object" &&
    "kind" in value &&
    value.kind === "pointer" &&
    "address" in value &&
    "dereferenced" in value &&
    typeof value.dereferenced === "boolean" &&
    "value" in value
  );
}

/**
 * Find all byte ranges belonging to a selected JSON field. A debug entry describes one range,
 * but a single selection may need several entries:
 *
 * - Selecting a struct or array also selects its children, such as entries[0].width.
 * - In a scalar array such as uint16 values[4], all four entries share the path "root.values".
 *   Match all of them so the whole array is highlighted, rather than just its first two bytes.
 *
 * Treat two paths as related when either is the start of the other. This also lets a selected
 * array element match the shorter, shared path used by the parser's scalar-array entries.
 */
export function findDebugEntryIndicesByPath(
  debugData: DebugItem[],
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

/**
 * Reports whether one path is a prefix of the other, so a selected struct also selects its members.
 * @param a Path segments.
 * @param b Path segments.
 * @returns True when the shorter path matches the start of the longer one.
 */
function isPathRelated(a: string[], b: string[]): boolean {
  // Compare only the shared part: ["root", "header"] also includes ["root", "header", "size"].
  const length = Math.min(a.length, b.length);
  for (let i = 0; i < length; i++) {
    if (a[i] !== b[i]) {
      return false;
    }
  }
  return true;
}

/** Find the field containing the clicked byte. start is inclusive; end is exclusive. */
export function findDebugEntryIndexByOffset(debugData: DebugItem[], offset: number): number {
  return debugData.findIndex((item) => offset >= item.start && offset < item.end);
}

/**
 * Assign a color-group number to each debug entry. All fields inside the same array share a
 * group, so an array looks like one block in the hex view. Other fields get their own groups.
 * These numbers are later used to choose colors from a repeating palette.
 */
export function computeFieldGroups(debugData: DebugItem[]): number[] {
  const groupIndexByKey = new Map<string, number>();

  return debugData.map((item) => {
    const key = arrayGroupKey(tokenizePath(item.path));
    let index = groupIndexByKey.get(key);

    // Reuse a known group, or assign the next number when this field first appears.
    if (index === undefined) {
      index = groupIndexByKey.size;
      groupIndexByKey.set(key, index);
    }

    return index;
  });
}

/**
 * Returns the color-group key of a path: the path up to its first array index, as JSON text.
 * @param path Path segments.
 */
function arrayGroupKey(path: string[]): string {
  // The first all-digit step is an array index. For example, both entries[0].width and
  // entries[1].height reduce to the same key, "entries", by stopping before that index.
  const arrayIndexPosition = path.findIndex((segment) => /^\d+$/.test(segment));
  const truncated = arrayIndexPosition === -1 ? path : path.slice(0, arrayIndexPosition);
  return JSON.stringify(truncated);
}
