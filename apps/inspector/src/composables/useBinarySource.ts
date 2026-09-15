import { onScopeDispose, ref, shallowRef, watch } from "vue";
import type { VueHexEditIntent, VueHexSearchRequest, VueHexWindowRequest } from "vuehex";
import { editBlob, searchBlob } from "../blob-hex";

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
