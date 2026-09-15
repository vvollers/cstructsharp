import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { defineComponent, h, isReactive } from "vue";
import { mount } from "@vue/test-utils";
import { detectFile } from "../detect-file";
import { formats } from "../formats";
import { parseFailure } from "../parse-diagnostics";
import { parseSourceWithDebug } from "../wasm/cstruct-wasm";
import { useInspector, type Inspector } from "./useInspector";

vi.mock("../detect-file", () => ({ detectFile: vi.fn() }));
vi.mock("../wasm/cstruct-wasm", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../wasm/cstruct-wasm")>()),
  initWasm: vi.fn().mockResolvedValue(undefined),
  isLoaded: () => true,
  getVersion: () => "test",
  parseSourceWithDebug: vi.fn(),
}));

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((done) => {
    resolve = done;
  });
  return { promise, resolve };
}

const cleanups: (() => void)[] = [];
async function session() {
  let inspector!: Inspector;
  const wrapper = mount(
    defineComponent({
      setup() {
        inspector = useInspector();
        return () => h("div");
      },
    }),
  );
  cleanups.push(() => wrapper.unmount());
  await Promise.resolve();
  return { inspector, wrapper };
}
beforeEach(() => vi.clearAllMocks());
afterEach(() => cleanups.splice(0).forEach((dispose) => dispose()));

describe("inspection session", () => {
  it("discards an old parse after editing, even when it finishes after the replacement parse", async () => {
    const { inspector } = await session();
    const first = deferred<ReturnType<typeof parseFailure>>();
    const second = deferred<ReturnType<typeof parseFailure>>();
    vi.mocked(parseSourceWithDebug)
      .mockReturnValueOnce(first.promise)
      .mockReturnValueOnce(second.promise);
    const oldRun = inspector.runParse({});
    const signal = vi.mocked(parseSourceWithDebug).mock.calls[0]![2]!.signal;
    inspector.setDefinition("struct root { uint8 changed; };");
    expect(signal?.aborted).toBe(true);
    expect(inspector.isRunning.value).toBe(false);
    expect(inspector.result.value).toBeNull();
    const newRun = inspector.runParse({});
    const current = parseFailure("Current result");
    second.resolve(current);
    await newRun;
    first.resolve(parseFailure("Stale result"));
    await oldRun;
    expect(inspector.result.value).toBe(current);
    expect(isReactive(inspector.result.value)).toBe(false);
  });

  it("manual loads preserve editor settings revision and parsing receives the complete Blob", async () => {
    const { inspector } = await session();
    const definition = inspector.definition.value;
    const revision = inspector.schemaRevision.value;
    const file = new File([new Uint8Array(70000)], "large.bin");
    await inspector.loadFile(file);
    expect(inspector.definition.value).toBe(definition);
    expect(inspector.schemaRevision.value).toBe(revision);
    expect(inspector.bytes.value.length).toBe(65536);
    vi.mocked(parseSourceWithDebug).mockResolvedValue(parseFailure("Test"));
    await inspector.runParse({});
    expect(vi.mocked(parseSourceWithDebug).mock.calls[0]![1]).toBe(file);
  });

  it("a late detection cannot replace a schema edited while detection was pending", async () => {
    const { inspector } = await session();
    const detection = deferred<Awaited<ReturnType<typeof detectFile>>>();
    vi.mocked(detectFile).mockReturnValue(detection.promise);
    const load = inspector.loadFile(new File(["GIF89a"], "image.gif"), true);
    await vi.waitFor(() => expect(detectFile).toHaveBeenCalledOnce());
    expect(inspector.schemaDisabled.value).toBe(true);
    inspector.setDefinition("struct root { uint8 mine; };");
    detection.resolve({ ext: "gif", mime: "image/gif" });
    await load;
    expect(inspector.definition.value).toContain("mine");
    expect(inspector.loadedFileName.value).toBeNull();
    expect(inspector.isDetecting.value).toBe(false);
  });

  it("a new document or unmount invalidates an outstanding file preview", async () => {
    const { inspector, wrapper } = await session();
    const preview = deferred<ArrayBuffer>();
    const file = new File(["delayed"], "old.bin");
    vi.spyOn(file, "slice").mockReturnValue({ arrayBuffer: () => preview.promise } as Blob);
    const load = inspector.loadFile(file);
    inspector.startNew();
    preview.resolve(new ArrayBuffer(7));
    await load;
    expect(inspector.loadedFileName.value).toBeNull();
    expect(inspector.bytes.value).toHaveLength(0);

    const pending = deferred<ArrayBuffer>();
    vi.spyOn(file, "slice").mockReturnValue({ arrayBuffer: () => pending.promise } as Blob);
    const disposedLoad = inspector.loadFile(file);
    wrapper.unmount();
    pending.resolve(new ArrayBuffer(7));
    await disposedLoad;
    expect(inspector.fileSource.value).toBeNull();
  });

  it("cancellation stays visible when an aborted parse settles", async () => {
    const { inspector } = await session();
    const pending = deferred<ReturnType<typeof parseFailure>>();
    vi.mocked(parseSourceWithDebug).mockReturnValue(pending.promise);
    const run = inspector.runParse({});
    inspector.cancelParse();
    expect(inspector.isRunning.value).toBe(false);
    pending.resolve(parseFailure("Too late"));
    await run;
    expect(inspector.result.value?.Error?.Code).toBe("cancelled");
    inspector.selectExample(formats[1]!);
    expect(inspector.result.value).toBeNull();
  });
});
