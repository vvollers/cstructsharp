import { mount } from "@vue/test-utils";
import { describe, expect, it, vi } from "vitest";

import { VueHex } from "vuehex";
import ResultPanel from "./ResultPanel.vue";
import LayoutEditor from "./LayoutEditor.vue";
vi.mock("./LayoutEditor.vue", () => ({
  default: { props: ["modelValue", "language", "readOnly"], template: "<div />" },
}));

describe("ResultPanel", () => {
  it("adds exact hexadecimal integers in the field map and displayed JSON without changing text", () => {
    const values = [
      ["uint16", "42", "42 (0x2A)"],
      ["uint8", "0", "0 (0x0)"],
      ["int8", "-1", "-1 (-0x1)"],
      ["uint64", "18446744073709551615", "18446744073709551615 (0xFFFFFFFFFFFFFFFF)"],
      ["state", "255", "255 (0xFF)"],
      ["char", "123", "123"],
      ["utf8_string_zero", "123", "123"],
      ["float", "1.5", "1.5"],
    ];
    const wrapper = mount(ResultPanel, {
      props: {
        bytes: new Uint8Array(),
        result: {
          ContractVersion: 4,
          Operation: "parse",
          Success: true,
          Data: '{"value":42}',
          Error: null,
          DebugData: values.map(([Type, Value], index) => ({
            Type: Type!,
            Value: Value!,
            CurPos: index,
            EndPos: index + 1,
            DebugStackString: `root.field${index}`,
            Buffer: null,
          })),
        },
      },
    });
    wrapper.findAll(".debug-list button").forEach((button, index) => {
      expect(button.text()).toContain(`· value ${values[index]![2]} · offset`);
    });
    const jsonEditor = wrapper.getComponent(LayoutEditor);
    expect(jsonEditor.props("modelValue")).toBe('{\n  "value": 42 // 0x2A\n}');
    expect(jsonEditor.props("language")).toBe("json");
    expect(jsonEditor.props("readOnly")).toBe("");
    expect(wrapper.props("result")!.Data).toBe('{"value":42}');
  });

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
