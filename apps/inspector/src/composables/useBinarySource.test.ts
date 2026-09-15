import { afterEach, expect, it, vi } from "vitest";
import { effectScope, shallowRef } from "vue";
import { useBinarySource } from "./useBinarySource";

const cleanups: (() => void)[] = [];
afterEach(() => cleanups.splice(0).forEach((dispose) => dispose()));
function sourceSession(initial: Blob | null) {
  const source = shallowRef(initial);
  const scope = effectScope();
  const editor = scope.run(() =>
    useBinarySource(
      () => source.value,
      (next) => {
        source.value = next;
      },
    ),
  )!;
  cleanups.push(() => scope.stop());
  return { source, editor, scope };
}

it("keeps undo/redo across its own edits and clears history when another file is loaded", async () => {
  const original = new Blob([new Uint8Array([1, 2, 3])]);
  const { source, editor } = sourceSession(original);
  editor.handleEdit({ kind: "overwrite-byte", index: 1, value: 42, column: "hex" });
  const edited = source.value;
  expect([...new Uint8Array(await edited!.arrayBuffer())]).toEqual([1, 42, 3]);
  editor.handleEdit({ kind: "undo" });
  expect(source.value).toBe(original);
  editor.handleEdit({ kind: "redo" });
  expect(source.value).toBe(edited);
  const replacement = new Blob(["new file"]);
  source.value = replacement;
  editor.handleEdit({ kind: "undo" });
  expect(source.value).toBe(replacement);
});

it("discards old window reads and errors when the source changes or the panel is disposed", async () => {
  const original = new Blob(["old"]);
  const { source, editor, scope } = sourceSession(null);
  let finish!: (value: ArrayBuffer) => void;
  vi.spyOn(original, "slice").mockReturnValue({
    arrayBuffer: () =>
      new Promise<ArrayBuffer>((resolve) => {
        finish = resolve;
      }),
  } as Blob);
  source.value = original;
  source.value = new Blob(["new"]);
  await vi.waitFor(() => expect(editor.windowBytes.value).toHaveLength(3));
  finish(new Uint8Array([1, 2, 3]).buffer);
  await Promise.resolve();
  expect(new TextDecoder().decode(editor.windowBytes.value)).toBe("new");

  let reject!: (error: Error) => void;
  vi.spyOn(original, "slice").mockReturnValue({
    arrayBuffer: () =>
      new Promise<ArrayBuffer>((_, fail) => {
        reject = fail;
      }),
  } as Blob);
  source.value = original;
  scope.stop();
  reject(new Error("Disposed read"));
  await Promise.resolve();
  expect(editor.windowError.value).toBe("");
});
