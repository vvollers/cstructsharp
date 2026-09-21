import { mount } from "@vue/test-utils";
import { describe, expect, it } from "vitest";
import InspectorHeader from "./InspectorHeader.vue";

// The status should not crowd out the file name; retain complete runtime identity for inspection.
describe("InspectorHeader status", () => {
  // Keep the long build identity available without rendering it as permanent header text.
  it("shows a short ready status with the full version in its tooltip", () => {
    const version = "0.8.1+0123456789abcdef0123456789abcdef01234567";
    const wrapper = mount(InspectorHeader, {
      props: {
        sourceLabel: "sample.bin",
        isRunning: false,
        wasmStatus: "ready",
        wasmVersion: version,
        wasmError: "",
      },
    });
    const badge = wrapper.get(".status-badge");
    expect(badge.text()).toBe("Ready");
    expect(badge.attributes("title")).toBe(version);
    expect(wrapper.text()).not.toContain(version);
  });
});
