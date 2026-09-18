import {
  compile,
  parse,
  parseWithDebug,
  resolveAddress,
  serialize,
  update,
  getVersion,
  loadCStructSharpWasm,
  type ErrorDetails,
  type ParsedStruct,
  type ParsedValue,
  type UnionValue,
} from "./cstructsharp-wasm.js";

const definition = "struct root { uint64 value; };";
const created = await serialize(definition, { value: 18446744073709551615n }, { root: "root" });
if (created.success) {
  const bytes: Uint8Array = created.data;
  const parsed = await parseWithDebug(definition, bytes, {
    origin: "9007199254740993",
    trimFixedText: true,
  });
  if (parsed.success) {
    const data: ParsedValue = parsed.data;
    const members = data as ParsedStruct;
    const value: ParsedValue = members.value;
    const root: string | null = parsed.root;
    const firstRange: number = parsed.debug[0]!.start;
    void value;
    void root;
    void firstRange;
    // @ts-expect-error Parse data is the parsed value, not JSON text.
    const wrongText: string = parsed.data;
    void wrongText;
    // @ts-expect-error Parse data is the parsed value, not bytes.
    const wrong: Uint8Array = parsed.data;
    void wrong;
  } else {
    const error: ErrorDetails = parsed.error;
    const code: string = error.code;
    const line: number | null = error.line;
    const noData: null = parsed.data;
    console.log(code, line, noData);
  }
  const changed = await update(definition, bytes, "root.value", "9007199254740993", {
    unknownMembers: "Reject",
  });
  if (changed.success) changed.data.subarray(0, 2);
  const changedFromBlob = await update(definition, new Blob([new Uint8Array(4)]), "root.value", 1);
  void changedFromBlob;
  const address = await resolveAddress(definition, bytes, "root.value");
  if (address.success) {
    const position: number | string = address.data;
    void position;
  }
}
// @ts-expect-error Unknown option should be caught by editor/type checker.
await parseWithDebug(definition, new Uint8Array(), { littleEdnian: true });
// @ts-expect-error The old option name is gone; use root.
await parseWithDebug(definition, new Uint8Array(), { rootTypeName: "root" });
// @ts-expect-error Chunk collections contain buffers/views, not numbers.
await parseWithDebug(definition, [1, 2]);
// @ts-expect-error Successful serialize data must be narrowed before use.
created.data.subarray(0);
const version: string = await getVersion();
const raw = await loadCStructSharpWasm();
const rawBytes: Uint8Array = raw.serialize(definition, '{"value":"42"}');
console.log(version, rawBytes);

await parse(definition, new Blob([new Uint8Array(8)]), { bitfieldPacking: "Msvc", cLongWidth: 32 });
await parse(definition, new DataView(new ArrayBuffer(8)), { signal: AbortSignal.abort() });
await parseWithDebug(definition, new Response(new Uint8Array(8)), { maxSpoolBytes: 1024 });
await parse(definition, [new Uint8Array(4), new Uint8Array(4)]);
async function* chunks() {
  yield new Uint8Array(8);
}
await parse(definition, chunks());

const unionParse = await parse("union choice { uint8 small; uint16 large; };", new Uint8Array(2), {
  root: "choice",
});
if (unionParse.success) {
  const union = unionParse.data as UnionValue;
  const selected: string | null = union.selectedMember;
  void selected;
}

async function compiledConsumer() {
  const layout = await compile("struct root { uint8 value; };", {
    littleEndian: true,
    root: "root",
  });
  try {
    const name: string = layout.root;
    void name;
    await layout.parse(new Uint8Array([1]), { maxTotalBytesRead: 1 });
    await layout.parseWithDebug(new Blob(), { signal: new AbortController().signal });
    const written = await layout.serialize({ value: 1 }, { unknownMembers: "Reject" });
    if (written.success) await layout.update(written.data, "root.value", 2);
    await layout.resolveAddress(new Uint8Array([1]), "root.value");
    // @ts-expect-error compile options cannot be changed for a retained layout
    await layout.parse(new Uint8Array(), { littleEndian: false });
  } finally {
    await layout.dispose();
  }
}
void compiledConsumer;
