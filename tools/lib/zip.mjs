/**
 * A minimal ZIP reader (central directory + stored/deflated entries) for inspecting NuGet packages and release
 * archives without a dependency. Reads whole files into memory; the archives the tools inspect are a few MB.
 */
import fs from "node:fs";
import { inflateRawSync } from "node:zlib";

const END_OF_CENTRAL_DIRECTORY = 0x06054b50;
const CENTRAL_FILE_HEADER = 0x02014b50;
const LOCAL_FILE_HEADER = 0x04034b50;

/** Opens an archive: `entries` lists {name, compressedSize, size, method} and `read(name)` returns a Buffer. */
export function openZip(file) {
  const data = fs.readFileSync(file);
  let end = -1;
  for (let index = data.length - 22; index >= Math.max(0, data.length - 22 - 65535); index--) {
    if (data.readUInt32LE(index) === END_OF_CENTRAL_DIRECTORY) {
      end = index;
      break;
    }
  }
  if (end < 0) throw new Error(`${file} is not a ZIP archive (no end-of-central-directory record).`);
  const entryCount = data.readUInt16LE(end + 10);
  let offset = data.readUInt32LE(end + 16);
  const entries = [];
  for (let index = 0; index < entryCount; index++) {
    if (data.readUInt32LE(offset) !== CENTRAL_FILE_HEADER) throw new Error(`${file} has a corrupt central directory.`);
    const method = data.readUInt16LE(offset + 10);
    const compressedSize = data.readUInt32LE(offset + 20);
    const size = data.readUInt32LE(offset + 24);
    const nameLength = data.readUInt16LE(offset + 28);
    const extraLength = data.readUInt16LE(offset + 30);
    const commentLength = data.readUInt16LE(offset + 32);
    const localOffset = data.readUInt32LE(offset + 42);
    const name = data.toString("utf8", offset + 46, offset + 46 + nameLength);
    entries.push({ name, method, compressedSize, size, localOffset });
    offset += 46 + nameLength + extraLength + commentLength;
  }
  const read = (name) => {
    const entry = entries.find((candidate) => candidate.name === name);
    if (!entry) throw new Error(`${file} has no entry '${name}'.`);
    const local = entry.localOffset;
    if (data.readUInt32LE(local) !== LOCAL_FILE_HEADER) throw new Error(`${file} has a corrupt local header for '${name}'.`);
    const start = local + 30 + data.readUInt16LE(local + 26) + data.readUInt16LE(local + 28);
    const compressed = data.subarray(start, start + entry.compressedSize);
    if (entry.method === 0) return Buffer.from(compressed);
    if (entry.method === 8) return inflateRawSync(compressed);
    throw new Error(`${file} entry '${name}' uses unsupported compression method ${entry.method}.`);
  };
  return { entries, read };
}
