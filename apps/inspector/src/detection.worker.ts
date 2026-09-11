import { fileTypeFromBlob } from "file-type";

// Blob-backed detection can seek without copying a large file into memory.
self.onmessage = async (event: MessageEvent<Blob>) => {
  try {
    const type = await fileTypeFromBlob(event.data);
    self.postMessage({ type: type ?? null });
  } catch (error) {
    self.postMessage({ error: error instanceof Error ? error.message : String(error) });
  }
};
