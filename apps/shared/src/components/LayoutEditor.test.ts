import { flushPromises, mount } from "@vue/test-utils";
import { describe, expect, it, vi } from "vitest";
import type * as Monaco from "monaco-editor/editor";

import LayoutEditor from "./LayoutEditor.vue";

/**
 * Builds the smallest Monaco stand-in the editor uses: a model, an editor instance that records its actions, and
 * the key codes for the format shortcut.
 * @returns The fake namespace, typed as Monaco for the `loadMonaco` prop, and the spies the tests inspect.
 */
function fakeMonaco() {
  const addAction = vi.fn();
  const createModel = vi.fn(() => ({
    updateOptions: vi.fn(),
    getFullModelRange: vi.fn(),
    dispose: vi.fn(),
  }));
  const instance = {
    addAction,
    onDidContentSizeChange: vi.fn(),
    onDidChangeModelContent: vi.fn(),
    /** Reports a fixed content height in pixels. */
    getContentHeight: () => 200,
    /** Reports empty editor text. */
    getValue: () => "",
    setValue: vi.fn(),
    dispose: vi.fn(),
  };
  const monaco = {
    editor: { createModel, create: vi.fn(() => instance) },
    KeyMod: { Shift: 1024, Alt: 512 },
    KeyCode: { KeyF: 36 },
  } as unknown as typeof Monaco;
  return { monaco, addAction, createModel };
}

/**
 * Mounts the editor with a fake Monaco and waits until the lazy load has created the editor.
 * @param language The `language` prop, or undefined to use the default.
 * @returns The spies from the fake Monaco.
 */
async function mountEditor(language: string | undefined) {
  const fake = fakeMonaco();
  mount(LayoutEditor, {
    props: {
      modelValue: "struct root { byte value; };",
      language,
      /** Resolves to a setup module holding the fake Monaco namespace. */
      loadMonaco: async () => ({ monaco: fake.monaco }),
    },
  });
  await flushPromises();
  return fake;
}

describe("LayoutEditor", () => {
  it.each([undefined, "cstruct"])(
    "offers the layout format action for CStruct text (language %s)",
    async (language) => {
      const { addAction, createModel } = await mountEditor(language);

      expect(createModel).toHaveBeenCalledWith(expect.any(String), "cstruct");
      expect(addAction).toHaveBeenCalledWith(
        expect.objectContaining({ id: "format-binary-layout" }),
      );
    },
  );

  it.each(["json", "csharp", "c"])(
    "does not offer the layout format action for %s text",
    async (language) => {
      const { addAction } = await mountEditor(language);

      expect(addAction).not.toHaveBeenCalled();
    },
  );
});
