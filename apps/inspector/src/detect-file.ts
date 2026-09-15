import type { FileTypeResult } from "file-type";

export function detectFile(file: Blob, signal: AbortSignal): Promise<FileTypeResult | null> {
  return new Promise((resolve, reject) => {
    signal.throwIfAborted();

    // Detection can scan a large file. Run it in a worker so the user can keep using the page.
    const worker = new Worker(new URL("./detection.worker.ts", import.meta.url), {
      type: "module",
    });

    // Success, failure, timeout and cancellation all need to stop the worker and its timer.
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

    // The worker sends back the detected type or an error message. Neither result changes the UI
    // directly; useInspector decides whether this detection still belongs to the current file load.
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
