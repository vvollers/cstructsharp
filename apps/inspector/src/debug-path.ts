import type { DebugItem } from "./wasm/cstruct-contract";

/**
 * Split a parser path into the steps needed to walk the JSON result.
 * For example, "root.values[2].tail" becomes ["root", "values", "2", "tail"].
 * The JSON result includes the root name, so we keep that first step.
 */
export function tokenizePath(stackString: string): string[] {
  return stackString.match(/[^.[\]]+/g) ?? [];
}

/**
 * Convert a schema field's path into the path used by the JSON result.
 * Pointer results have extra properties: address holds the stored address and value holds the
 * data found there. Add those steps where necessary so clicks select the right JSON property.
 */
export function debugEntryJsonPath(item: DebugItem, result?: unknown): string[] {
  const path: string[] = [];
  let value = result;

  for (const segment of tokenizePath(item.path)) {
    // Follow pointer wrappers before looking up the next ordinary field.
    while (isPointer(value)) {
      path.push("value");
      value = value.value;
    }

    path.push(segment);
    value =
      value !== null && typeof value === "object"
        ? (value as Record<string, unknown>)[segment]
        : undefined;
  }

  // If the path ends at the pointer itself, highlight address: these bytes store the address.
  if (isPointer(value)) path.push("address");

  return path;
}

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

function arrayGroupKey(path: string[]): string {
  // The first all-digit step is an array index. For example, both entries[0].width and
  // entries[1].height reduce to the same key, "entries", by stopping before that index.
  const arrayIndexPosition = path.findIndex((segment) => /^\d+$/.test(segment));
  const truncated = arrayIndexPosition === -1 ? path : path.slice(0, arrayIndexPosition);
  return JSON.stringify(truncated);
}
