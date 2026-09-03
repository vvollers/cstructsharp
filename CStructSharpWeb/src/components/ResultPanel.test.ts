import { mount } from "@vue/test-utils";
import { describe, expect, it } from "vitest";

import { VueHex } from "vuehex";
import ResultPanel from "./ResultPanel.vue";

describe("ResultPanel", () => {
  it("uses VueHex as an editable binary viewer for successful output", async () => {
    const bytes = new Uint8Array([0x2a, 0x00, 0xff]);
    const wrapper = mount(ResultPanel, {
      props: {
        bytes,
        result: {
          ContractVersion: 4,
          Operation: "parse",
          Success: true,
          Data: '{"value":42}',
          DebugData: [],
          Error: null,
        },
      },
    });

    const editor = wrapper.getComponent(VueHex);
    expect(editor.props("modelValue")).toEqual(bytes);
    expect(editor.props("editable")).toBe(true);
    expect(editor.props("dataMode")).toBe("buffer");
    expect(wrapper.get('[data-testid="binary-editor"]')).toBeTruthy();

    const edited = new Uint8Array([0x2b, 0x00, 0xff]);
    await editor.vm.$emit("update:modelValue", edited);
    expect(wrapper.emitted("bytes-edited")?.[0]?.[0]).toEqual(edited);
  });
});
