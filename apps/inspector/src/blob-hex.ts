import type { VueHexEditIntent, VueHexSearchRequest, VueHexSearchResponse } from "vuehex";

/** Apply an edit by composing immutable ranges; never materialize the full file. */
export function editBlob(source: Blob, intent: VueHexEditIntent): Blob {
  if (intent.kind === "undo" || intent.kind === "redo") return source;
  const start = "index" in intent ? intent.index : intent.start;
  const replacement =
    "values" in intent
      ? new Uint8Array(intent.values)
      : "value" in intent
        ? new Uint8Array([intent.value])
        : new Uint8Array();
  const end = intent.kind.startsWith("insert")
    ? start
    : "end" in intent
      ? intent.end + 1
      : intent.kind === "delete-byte"
        ? start + 1
        : start + replacement.length;
  return new Blob([source.slice(0, start), replacement, source.slice(end)]);
}

/** Full-source search with bounded buffers and matches spanning page boundaries. */
export async function searchBlob(
  source: Blob,
  request: VueHexSearchRequest,
): Promise<VueHexSearchResponse> {
  const { query, signal } = request;
  if (!query.length) return { total: 0, hit: null, activeOrdinal: 0 };
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
        const hit = { start: offset + i - query.length + 1, ordinal: ++total };
        first ??= hit;
        last = hit;
        if (request.direction === "next" && !selected && hit.start >= request.from) selected = hit;
        if (request.direction === "previous" && hit.start <= request.from) selected = hit;
        matched = 0;
      }
    }
  }
  signal?.throwIfAborted();
  if (!selected && request.wrap) selected = request.direction === "next" ? first : last;
  return {
    total,
    hit: selected ? { start: selected.start, end: selected.start + query.length - 1 } : null,
    activeOrdinal: selected?.ordinal ?? 0,
  };
}
