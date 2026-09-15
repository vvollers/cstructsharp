import { onScopeDispose, ref, shallowRef, watch } from "vue";
import type {
  VueHexEditIntent,
  VueHexSearchRequest,
  VueHexSearchResponse,
  VueHexWindowRequest,
} from "vuehex";

const WINDOW_BYTES = 65536;
const MAX_WINDOW_BYTES = 1024 * 1024;
const HISTORY_LIMIT = 100;

/**
 * Gives the hex editor a small window into a potentially large file.
 * A Blob cannot be changed in place, so each edit produces a new Blob. Keeping the previous
 * Blobs lets us undo and redo edits without loading the entire file into a byte array.
 */
export function useBinarySource(source: () => Blob | null, onEdit: (value: Blob) => void) {
  const windowBytes = shallowRef(new Uint8Array());
  const windowOffset = ref(0);
  const windowError = ref("");

  const undo: Blob[] = [];
  const redo: Blob[] = [];
  let editedSource: Blob | null = null;
  let windowVersion = 0;

  async function loadWindow({ offset, length }: VueHexWindowRequest): Promise<void> {
    const current = source();
    const version = ++windowVersion;
    if (!current) return;

    try {
      // Keep the start inside the file and limit how much data one window request can read.
      const start = Math.max(0, Math.min(offset, current.size));
      const count = Math.min(Math.max(length, WINDOW_BYTES), MAX_WINDOW_BYTES);
      const bytes = new Uint8Array(await current.slice(start, start + count).arrayBuffer());

      // Scrolling or opening another file may have requested a different window during the read.
      if (version !== windowVersion || current !== source()) return;

      // Replace the bytes and their starting address together. Otherwise the editor could label
      // bytes from the old window with addresses belonging to the new window.
      windowOffset.value = start;
      windowBytes.value = bytes;
      windowError.value = "";
    } catch {
      if (version === windowVersion && current === source())
        windowError.value = "Could not read this file range. Reload the file and try again.";
    }
  }

  function clearHistory(): void {
    undo.length = 0;
    redo.length = 0;
    editedSource = null;
  }

  watch(
    source,
    (current) => {
      windowVersion++;
      windowError.value = "";

      // Our own edit is sent to the parent, then comes back as the new source prop. Recognize that
      // Blob so we keep its undo history. Loading another file should instead start a fresh history.
      if (current !== editedSource || !current) {
        clearHistory();
        windowOffset.value = 0;
        windowBytes.value = new Uint8Array();
      }

      if (current) void loadWindow({ offset: windowOffset.value, length: WINDOW_BYTES });
    },
    { immediate: true, flush: "sync" },
  );

  function handleEdit(intent: VueHexEditIntent): void {
    const current = source();
    if (!current) return;

    let next: Blob | undefined;
    if (intent.kind === "undo") {
      // Restore a previous version and save the current one so the user can redo it.
      next = undo.pop();
      if (next) redo.push(current);
    } else if (intent.kind === "redo") {
      next = redo.pop();
      if (next) undo.push(current);
    } else {
      // A new edit branches away from the redo history. Keep only the most recent undo versions.
      next = editBlob(current, intent);
      undo.push(current);
      if (undo.length > HISTORY_LIMIT) undo.shift();
      redo.length = 0;
    }

    if (next) {
      // Remember this exact Blob before notifying the parent; the source watcher will see it next.
      editedSource = next;
      onEdit(next);
    }
  }

  function searchSource(request: VueHexSearchRequest) {
    const current = source();
    return current
      ? searchBlob(current, request)
      : Promise.resolve({ total: 0, hit: null, activeOrdinal: 0 });
  }

  onScopeDispose(() => {
    // Make any unfinished window read obsolete and release references to old file versions.
    windowVersion++;
    clearHistory();
  });

  return { windowBytes, windowOffset, windowError, loadWindow, handleEdit, searchSource };
}

// Byte operations used by the window/history logic above. They create new Blobs or read ranges;
// they do not depend on Vue state, so the tests can also call them directly.

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
