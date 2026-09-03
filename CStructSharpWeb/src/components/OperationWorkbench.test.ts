import { mount } from "@vue/test-utils";
import { describe, expect, it } from "vitest";
import { VueHex } from "vuehex";

import OperationWorkbench from "./OperationWorkbench.vue";

describe("OperationWorkbench", () => {
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

  it("collects editable parse, serialize, and update inputs without hiding binary options", async () => {
    const wrapper = mount(OperationWorkbench, {
      props: {
        definition: "struct root { byte value; };",
        binaryHex: "2a",
        disabled: false,
      },
    });

    expect(wrapper.get('[data-testid="definition-input"]').element).toHaveProperty(
      "value",
      "struct root { byte value; };",
    );
    expect(
      wrapper.get('[data-testid="binary-input"]').getComponent(VueHex).props("modelValue"),
    ).toEqual(new Uint8Array([0x2a]));

    await wrapper.get('[data-testid="operation-select"]').setValue("serialize");
    expect(wrapper.get('[data-testid="json-input"]').isVisible()).toBe(true);
    await wrapper.get('[data-testid="operation-select"]').setValue("update");
    expect(wrapper.get('[data-testid="path-input"]').isVisible()).toBe(true);

    await wrapper.get('[data-testid="endian-select"]').setValue("big");
    await wrapper.get("form").trigger("submit");
    expect(wrapper.emitted("run")?.[0]?.[0]).toMatchObject({
      operation: "update",
      definition: "struct root { byte value; };",
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
