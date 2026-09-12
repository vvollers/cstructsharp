import {
  parse,
  parseWithDebug,
  serialize,
  update,
  getVersion,
  loadCStructSharpWasm,
} from "./cstructsharp-wasm.js";

const definition = "struct root { uint64 value; };";
const created = await serialize(
  definition,
  { value: 18446744073709551615n },
  { rootTypeName: "root" },
);
if (created.Success) {
  const bytes: Uint8Array = created.Data;
  const parsed = await parseWithDebug(definition, bytes, { origin: "9007199254740993" });
  if (parsed.Success) {
    const text: string = parsed.Data;
    JSON.parse(text);
    // @ts-expect-error Parse Data remains JSON text, not bytes.
    const wrong: Uint8Array = parsed.Data;
    void wrong;
  } else {
    const code: string = parsed.Error.Code;
    const noData: null = parsed.Data;
    console.log(code, noData);
  }
  const changed = await update(definition, bytes, "root.value", "9007199254740993");
  if (changed.Success) changed.Data.subarray(0, 2);
}
// @ts-expect-error Unknown option should be caught by editor/type checker.
await parseWithDebug(definition, new Uint8Array(), { littleEdnian: true });
// @ts-expect-error Chunk collections contain buffers/views, not numbers.
await parseWithDebug(definition, [1, 2]);
// @ts-expect-error Successful serialize Data must be narrowed before use.
created.Data.subarray(0);
const version: string = await getVersion();
const raw = await loadCStructSharpWasm();
const rawBytes: Uint8Array = raw.serialize(definition, '{"value":"42"}');
console.log(version, rawBytes);

await parse(definition, new Blob([new Uint8Array(8)]));
await parse(definition, new DataView(new ArrayBuffer(8)), { signal: AbortSignal.abort() });
await parseWithDebug(definition, new Response(new Uint8Array(8)), { maxSpoolBytes: 1024 });
await parse(definition, [new Uint8Array(4), new Uint8Array(4)]);
async function* chunks() {
  yield new Uint8Array(8);
}
await parse(definition, chunks());

async function compiledConsumer() {
  const { compile } = await import("./cstructsharp-wasm.js");
  const layout = await compile("struct root { uint8 value; };", { littleEndian: true });
  try {
    await layout.parse(new Uint8Array([1]), { maxTotalBytesRead: 1 });
    await layout.parseWithDebug(new Blob(), { signal: new AbortController().signal });
    // @ts-expect-error layout settings cannot be changed for a retained layout
    await layout.parse(new Uint8Array(), { littleEndian: false });
  } finally { await layout.dispose(); }
}
void compiledConsumer;
