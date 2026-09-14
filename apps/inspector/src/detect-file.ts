import type { FileTypeResult } from "file-type";

export function detectFile(file: Blob, signal: AbortSignal): Promise<FileTypeResult | null> {
  return new Promise((resolve, reject) => {
    signal.throwIfAborted();
    const worker = new Worker(new URL("./detection.worker.ts", import.meta.url), {
      type: "module",
    });
    const cleanup = () => {
      clearTimeout(timer);
      signal.removeEventListener("abort", abort);
      worker.terminate();
    };
    const abort = () => {
      cleanup();
      reject(signal.reason);
    };
    const timer = setTimeout(() => {
      cleanup();
      reject(new Error("Detection timed out. The file is available for manual inspection."));
    }, 15000);
    signal.addEventListener("abort", abort, { once: true });
    worker.onmessage = (event: MessageEvent<{ type?: FileTypeResult | null; error?: string }>) => {
      cleanup();
      if (event.data.error) reject(new Error(event.data.error));
      else resolve(event.data.type ?? null);
    };
    worker.onerror = () => {
      cleanup();
      reject(new Error("File detection could not finish. Select a schema manually."));
    };
    worker.postMessage(file);
  });
}
