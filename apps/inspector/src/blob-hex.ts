import type { VueHexEditIntent, VueHexSearchRequest, VueHexSearchResponse } from "vuehex";

/**
 * Build an edited file from three pieces: the unchanged prefix, the replacement bytes,
 * and the unchanged suffix. Blob slices let us do this without reading the whole file into memory.
 */
export function editBlob(source: Blob, intent: VueHexEditIntent): Blob {
  if (intent.kind === "undo" || intent.kind === "redo") return source;

  // Edits can replace one byte, replace a range, or delete bytes (an empty replacement).
  const start = "index" in intent ? intent.index : intent.start;
  const replacement =
    "values" in intent
      ? new Uint8Array(intent.values)
      : "value" in intent
        ? new Uint8Array([intent.value])
        : new Uint8Array();

  // Insertion removes nothing. Other edits remove the specified range before adding replacement
  // bytes. VueHex uses an inclusive end index; Blob.slice expects the first index to leave out.
  const end = intent.kind.startsWith("insert")
    ? start
    : "end" in intent
      ? intent.end + 1
      : intent.kind === "delete-byte"
        ? start + 1
        : start + replacement.length;

  return new Blob([source.slice(0, start), replacement, source.slice(end)]);
}

/**
 * Search the whole file one 64 KiB chunk at a time. Remember a partial match between chunks,
 * so a byte sequence that starts at the end of one chunk can finish at the start of the next.
 */
export async function searchBlob(
  source: Blob,
  request: VueHexSearchRequest,
): Promise<VueHexSearchResponse> {
  const { query, signal } = request;
  if (!query.length) return { total: 0, hit: null, activeOrdinal: 0 };

  // Build the fallback table used by Knuth–Morris–Pratt string matching. If a match fails partway
  // through, this table tells us how much of the matched prefix we can reuse instead of starting over.
  const failure = new Int32Array(query.length);
  for (let i = 1, j = 0; i < query.length; i++) {
    while (j && query[i] !== query[j]) j = failure[j - 1]!;
    if (query[i] === query[j]) j++;
    failure[i] = j;
  }

  let matched = 0;
  let total = 0;
  let selected: { start: number; ordinal: number } | null = null;
  let first: { start: number; ordinal: number } | null = null;
  let last: { start: number; ordinal: number } | null = null;

  for (let offset = 0; offset < source.size; offset += 65536) {
    signal?.throwIfAborted();
    const bytes = new Uint8Array(await source.slice(offset, offset + 65536).arrayBuffer());

    for (let i = 0; i < bytes.length; i++) {
      while (matched && bytes[i] !== query[matched]) matched = failure[matched - 1]!;
      if (bytes[i] === query[matched]) matched++;

      if (matched === query.length) {
        // Count every hit, but select the next/previous one relative to the requested start position.
        const hit = { start: offset + i - query.length + 1, ordinal: ++total };
        first ??= hit;
        last = hit;
        if (request.direction === "next" && !selected && hit.start >= request.from) selected = hit;
        if (request.direction === "previous" && hit.start <= request.from) selected = hit;

        // Start a fresh match after this hit, so reported hits do not overlap.
        matched = 0;
      }
    }
  }

  signal?.throwIfAborted();

  // If the search reached an end without a suitable hit, wrapping picks one from the other end.
  if (!selected && request.wrap) selected = request.direction === "next" ? first : last;

  return {
    total,
    hit: selected ? { start: selected.start, end: selected.start + query.length - 1 } : null,
    activeOrdinal: selected?.ordinal ?? 0,
  };
}
