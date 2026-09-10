import { mount } from "@vue/test-utils";
import { describe, expect, it, vi } from "vitest";
import { VueHex } from "vuehex";

import OperationWorkbench from "./OperationWorkbench.vue";
import LayoutEditor from "./LayoutEditor.vue";
vi.mock("./LayoutEditor.vue", () => ({ default: { props: ["modelValue"], template: "<div />" } }));

describe("OperationWorkbench", () => {
  it.each(["parse", "serialize", "update"] as const)(
    "fixes the workbench to %s with its valid preset",
    async (operation) => {
      const wrapper = mount(OperationWorkbench, {
        props: {
          definition: "struct header { uint16 kind; uint32 length; };",
          binaryHex: "02 00 06 00 00 00",
          disabled: false,
          operation,
          presets: {
            [operation]: {
              json: operation === "update" ? "4" : '{"kind":3,"length":6}',
              path: "header.kind",
              expected: {},
            },
          },
        },
      });
      expect(wrapper.find('[data-testid="operation-select"]').exists()).toBe(false);
      expect(wrapper.find('[data-testid="json-input"]').exists()).toBe(operation !== "parse");
      expect(wrapper.find('[data-testid="path-input"]').exists()).toBe(operation === "update");
      await wrapper.get("form").trigger("submit");
      expect(wrapper.emitted("run")?.[0]?.[0]).toMatchObject({ operation });
      if (operation === "update")
        expect(wrapper.emitted("run")?.[0]?.[0]).toMatchObject({
          jsonValue: "4",
          path: "header.kind",
        });
    },
  );

  it("initializes the byte order supplied by a generated demo", async () => {
    const wrapper = mount(OperationWorkbench, {
      props: {
        definition: "struct root { uint16 value; };",
        binaryHex: "12 34",
        disabled: false,
        initialLittleEndian: false,
      },
    });

    expect(wrapper.get('[data-testid="endian-select"]').element).toHaveProperty("value", "big");
    await wrapper.get("form").trigger("submit");
    expect(wrapper.emitted("run")?.[0]?.[0]).toMatchObject({
      options: { littleEndian: false },
    });
  });

  it("collects editable update inputs without hiding binary options", async () => {
    const wrapper = mount(OperationWorkbench, {
      props: {
        definition: "struct root { byte value; };",
        binaryHex: "2a",
        disabled: false,
        operation: "update",
      },
    });

    expect(wrapper.getComponent(LayoutEditor).props("modelValue")).toBe(
      "struct root {\n    byte value;\n};",
    );
    expect(
      wrapper.get('[data-testid="binary-input"]').getComponent(VueHex).props("modelValue"),
    ).toEqual(new Uint8Array([0x2a]));

    expect(wrapper.get('[data-testid="json-input"]').isVisible()).toBe(true);
    expect(wrapper.get('[data-testid="path-input"]').isVisible()).toBe(true);

    await wrapper.get('[data-testid="endian-select"]').setValue("big");
    await wrapper.get("form").trigger("submit");
    expect(wrapper.emitted("run")?.[0]?.[0]).toMatchObject({
      operation: "update",
      definition: "struct root {\n    byte value;\n};",
      binaryHex: "2a",
      options: { littleEndian: false },
    });
  });

  it("accepts binary data updates from the result editor", async () => {
    const wrapper = mount(OperationWorkbench, {
      props: {
        definition: "struct root { byte value; };",
        binaryHex: "2a",
        disabled: false,
      },
    });

    await wrapper.setProps({ binaryHex: "2b 00" });
    expect(
      wrapper.get('[data-testid="binary-input"]').getComponent(VueHex).props("modelValue"),
    ).toEqual(new Uint8Array([0x2b, 0x00]));
  });

  it("converts bytes edited in VueHex back to the workbench hex request", async () => {
    const wrapper = mount(OperationWorkbench, {
      props: {
        definition: "struct root { byte value; };",
        binaryHex: "2a",
        disabled: false,
      },
    });

    await wrapper.getComponent(VueHex).vm.$emit("update:modelValue", new Uint8Array([0xab, 0x01]));
    await wrapper.get("form").trigger("submit");

    expect(wrapper.emitted("run")?.[0]?.[0]).toMatchObject({ binaryHex: "ab 01" });
  });
});
