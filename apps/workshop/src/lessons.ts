import type { TestEntry } from "./demo-types";
import type {
  InteropOperation,
  InteropResult,
  ParseWithDebugOptions,
} from "./wasm/cstruct-contract";

export interface LessonOperation {
  json?: string;
  path?: string;
  expected: { data?: unknown; hex?: string; error?: string };
}

export interface Lesson extends TestEntry {
  title: string;
  level: "Beginner" | "Intermediate" | "Advanced";
  tags: string[];
  prerequisite: string;
  exercise: string;
  answer: string;
  guide: string;
  sourceScenario: string;
  operations: Partial<Record<InteropOperation, LessonOperation>>;
  options?: ParseWithDebugOptions;
}

function lesson(
  id: string,
  title: string,
  definition: string,
  binaryHex: string,
  rootType: string,
  extra: Omit<Lesson, keyof TestEntry | "title"> & { summary: string },
): Lesson {
  return {
    id,
    title,
    definition,
    binaryHex,
    rootType,
    className: "Lessons",
    methodName: title,
    filePath: `docs/examples/recipes/${extra.sourceScenario}.cs`,
    sourceUrl: `https://vvollers.github.io/cstructsharp/docs/examples/recipes/${extra.sourceScenario}.cs`,
    line: 1,
    runnable: true,
    parserOptions: {
      aligned: extra.options?.aligned ?? false,
      littleEndian: extra.options?.littleEndian ?? true,
      pointerSize: extra.options?.pointerSize ?? 8,
    },
    documentation: {
      summary: extra.summary,
      usage:
        "Change an input, predict the result, then run it. Reset example restores all starting values.",
    },
    ...extra,
  };
}

const lessonTopics: Lesson[] = [
  lesson(
    "header",
    "Read, write, and change a file header",
    "struct header { uint16 kind; uint32 length; };",
    "02 00 06 00 00 00",
    "header",
    {
      level: "Beginner",
      tags: ["file", "read", "write", "update", "bytes"],
      sourceScenario: "decode-header",
      summary:
        "Two bytes store the message kind; four bytes store its length. Start with Read bytes to see kind 2 and length 6.",
      prerequisite: "You can read a simple C struct. No binary-format experience is needed.",
      exercise: "Change the first byte from 02 to 03. Which value changes?",
      answer: "kind becomes 3; length stays 6. Reset before comparing with the starting result.",
      guide: "guides/install-and-first-parse.html",
      operations: {
        parse: { expected: { data: { header: { kind: 2, length: 6 } } } },
        serialize: { json: '{"kind":3,"length":6}', expected: { hex: "03 00 06 00 00 00" } },
        update: { json: "4", path: "header.kind", expected: { hex: "04 00 06 00 00 00" } },
      },
    },
  ),
  lesson(
    "byte-order",
    "Understand byte order",
    "struct header { uint16 kind; uint32 length; };",
    "02 00 06 00 00 00",
    "header",
    {
      level: "Beginner",
      tags: ["byte order", "endian", "wrong value"],
      sourceScenario: "decode-header",
      summary:
        "Read the same six bytes with a different byte order to see why a valid read can still give the wrong value.",
      prerequisite: "Complete the file header lesson.",
      exercise: "Select Big endian and read again.",
      answer: "kind becomes 512 and length becomes 100663296. The field widths did not change.",
      guide: "guides/binary-layout-basics.html",
      operations: { parse: { expected: { data: { header: { kind: 2, length: 6 } } } } },
    },
  ),
  lesson(
    "truncated",
    "Fix a header with missing bytes",
    "struct header { uint16 kind; uint32 length; };",
    "02 00 06",
    "header",
    {
      level: "Beginner",
      tags: ["error", "missing", "truncated", "read"],
      sourceScenario: "decode-header",
      summary:
        "This lesson intentionally fails: three bytes cannot fill a six-byte header. The error is the expected starting result.",
      prerequisite: "Complete the file header lesson.",
      exercise: "Paste 02 00 06 00 00 00 into the hex editor, then read again.",
      answer:
        "02 00 06 00 00 00 reads kind 2 and length 6. A success now means you fixed the exercise.",
      guide: "guides/errors-and-recovery.html",
      operations: { parse: { expected: { error: "read-failed" } } },
    },
  ),
  lesson(
    "invalid-path",
    "Fix a misspelled root name",
    "struct header { uint16 kind; uint32 length; };",
    "02 00 06 00 00 00",
    "Header",
    {
      level: "Beginner",
      tags: ["error", "path", "root", "case"],
      sourceScenario: "decode-header",
      summary: "Names are case-sensitive. Header does not select the declaration named header.",
      prerequisite: "Complete the file header lesson.",
      exercise: "Open Workbench settings and change Root type/path to header.",
      answer:
        "The read succeeds. The declaration name and the selected root must use the same spelling.",
      guide: "guides/reading-values.html",
      operations: { parse: { expected: { error: "invalid-path" } } },
    },
  ),
  lesson(
    "text",
    "Read and write fixed-capacity text",
    "struct label { char text[4]; };",
    "41 42 43 00",
    "label",
    {
      level: "Intermediate",
      tags: ["string", "text", "write", "capacity"],
      sourceScenario: "fixed-text",
      summary:
        "Four bytes hold ABC and a zero character. A fixed text field keeps that final character when read.",
      prerequisite: "Read and write the header first.",
      exercise: "Change the first byte from 41 to 58 and read again.",
      answer:
        "The text becomes XBC followed by a zero character. The fixed field still occupies four bytes.",
      guide: "guides/strings-and-encodings.html",
      operations: {
        parse: { expected: { data: { label: { text: "ABC\0" } } } },
        serialize: { json: '{"text":"XY"}', expected: { hex: "58 59 00 00" } },
        update: { json: '"XY"', path: "label.text", expected: { hex: "58 59 00 00" } },
      },
    },
  ),
  lesson(
    "nested",
    "Change a nested field",
    "struct item { uint16 id; uint8 flags; }; struct root { item value; };",
    "34 12 01",
    "root",
    {
      level: "Intermediate",
      tags: ["nested", "path", "update", "flags"],
      sourceScenario: "patch-field",
      summary:
        "A dot separates each part of a field path. Change flags while preserving the two id bytes.",
      prerequisite: "Try updating the header.",
      exercise: "Change the last byte from 01 to A5 and read again.",
      answer: "flags becomes 165; id stays 4660. The nested fields occupy separate bytes.",
      guide: "guides/updating-existing-data.html",
      operations: {
        parse: { expected: { data: { root: { value: { id: 4660, flags: 1 } } } } },
        serialize: { json: '{"value":{"id":4660,"flags":165}}', expected: { hex: "34 12 a5" } },
        update: { json: "165", path: "root.value.flags", expected: { hex: "34 12 a5" } },
      },
    },
  ),
  lesson(
    "arrays",
    "Read records in a fixed array",
    "struct item { uint16 id; }; struct packet { item items[2]; };",
    "01 00 02 00",
    "packet",
    {
      level: "Intermediate",
      tags: ["array", "nested", "records", "write"],
      sourceScenario: "nested-array",
      summary:
        "Two records each hold a two-byte id. Array indexes start at zero, so items[1] is the second record.",
      prerequisite: "Read the header and nested field lessons.",
      exercise: "Change the third byte from 02 to 03, then read again.",
      answer: "The second id becomes 3. The first record stays 01 00.",
      guide: "guides/reading-values.html",
      operations: {
        parse: { expected: { data: { packet: { items: [{ id: 1 }, { id: 2 }] } } } },
        serialize: { json: '{"items":[{"id":1},{"id":2}]}', expected: { hex: "01 00 02 00" } },
        update: { json: "3", path: "packet.items[1].id", expected: { hex: "01 00 03 00" } },
      },
    },
  ),
  lesson(
    "alignment",
    "Find padding between fields",
    "struct header { uint16 kind; uint32 length; };",
    "02 00 00 00 06 00 00 00",
    "header",
    {
      level: "Intermediate",
      tags: ["alignment", "padding", "packed", "offset"],
      sourceScenario: "aligned-header",
      summary:
        "This aligned header has two padding bytes. The four-byte length starts at offset 4.",
      prerequisite: "Complete the header and byte-order lessons.",
      exercise: "Turn off Align fields without changing the bytes.",
      answer:
        "The packed read starts length at offset 2, so it reads 393216. Remove the two padding bytes to use the packed format.",
      guide: "guides/binary-layout-basics.html",
      options: { aligned: true },
      operations: {
        parse: { expected: { data: { header: { kind: 2, length: 6 } } } },
        serialize: { json: '{"kind":2,"length":6}', expected: { hex: "02 00 00 00 06 00 00 00" } },
      },
    },
  ),
  lesson(
    "bitfields",
    "Read several flags from one byte",
    "struct flags { uint8 enabled : 1; uint8 mode : 3; uint8 reserved : 4; };",
    "0b",
    "flags",
    {
      level: "Intermediate",
      tags: ["bitfield", "flags", "bits", "write"],
      sourceScenario: "bit-flags",
      summary:
        "The lowest bit stores enabled. The next three bits store mode. One byte can hold both values.",
      prerequisite: "Understand bytes and binary digits.",
      exercise: "Change 0B to 0A and read again.",
      answer:
        "enabled changes from 1 to 0; mode stays 5. Portable bitfields start at the low bits.",
      guide: "language/bitfields.html",
      operations: {
        parse: { expected: { data: { flags: { enabled: 1, mode: 5, reserved: 0 } } } },
        serialize: { json: '{"enabled":1,"mode":5,"reserved":0}', expected: { hex: "0b" } },
      },
    },
  ),
  lesson(
    "terminated-text",
    "Find the end of a text field",
    "struct label { char text[]; };",
    "41 42 00",
    "label",
    {
      level: "Intermediate",
      tags: ["string", "text", "terminator", "zero"],
      sourceScenario: "terminated-text",
      summary:
        "An empty char array declaration reads until a zero byte. The returned text does not include the terminator.",
      prerequisite: "Complete fixed-capacity text first.",
      exercise: "Remove the final 00 and read again.",
      answer: "The read fails because the terminator is missing. End of input does not replace it.",
      guide: "guides/strings-and-encodings.html",
      operations: {
        parse: { expected: { data: { label: { text: "AB" } } } },
        serialize: { json: '{"text":"AB"}', expected: { hex: "41 42 00" } },
      },
    },
  ),
  lesson(
    "enum",
    "Keep an unknown enum value",
    "enum state : uint32 { Known = 1 }; struct root { state value; };",
    "ff ff ff ff",
    "root",
    {
      level: "Advanced",
      tags: ["enum", "unknown", "preserve", "integer"],
      sourceScenario: "preserve-enum",
      summary:
        "An enum can contain a number without a known member name. Keep the number instead of discarding the data.",
      prerequisite: "Understand integer widths and JSON objects.",
      exercise: "Change the bytes to 01 00 00 00.",
      answer:
        "Name becomes Known and Value becomes 1. The original value 4294967295 has Name null.",
      guide: "guides/enums.html",
      operations: {
        parse: {
          expected: { data: { root: { value: { Enum: "state", Name: null, Value: 4294967295 } } } },
        },
        serialize: { json: '{"value":4294967295}', expected: { hex: "ff ff ff ff" } },
      },
    },
  ),
  lesson(
    "union",
    "Inspect and select overlapping storage",
    "union choice { uint8 small; uint16 large; }; struct root { choice value; };",
    "34 12",
    "root",
    {
      level: "Advanced",
      tags: ["union", "raw", "storage", "overlap", "write"],
      sourceScenario: "preserve-union",
      summary:
        "Both union members interpret the same bytes. Reading keeps their views and the original raw storage.",
      prerequisite: "Read the union guide and understand Base64 in browser results.",
      exercise: "Compare the small and large member values after reading 34 12.",
      answer:
        "small reads the first byte as 52; large reads both bytes as 4660. RawStorage NBI= preserves the same two bytes.",
      guide: "guides/unions.html",
      operations: {
        parse: {
          expected: {
            data: {
              root: {
                value: {
                  $kind: "union",
                  Union: "choice",
                  RawStorage: "NBI=",
                  Members: { small: 52, large: 4660 },
                  SelectedMember: null,
                },
              },
            },
          },
        },
        serialize: {
          json: '{"value":{"$kind":"union","Union":"choice","RawStorage":null,"Members":{"small":165},"SelectedMember":"small"}}',
          expected: { hex: "a5 00" },
        },
      },
    },
  ),
  lesson("pointer", "Follow a stored pointer", "struct root { uint8 *target; };", "01 2a", "root", {
    level: "Advanced",
    tags: ["pointer", "address", "target", "offset"],
    sourceScenario: "follow-pointer",
    summary:
      "The one-byte pointer stores address 1. That position contains value 42. This is a position in the input, not process memory.",
    prerequisite: "Understand offsets. Open Workbench settings to see the one-byte pointer width.",
    exercise: "Turn off Follow pointers.",
    answer:
      "Address stays 1, IsDereferenced becomes false, and Value becomes null. The pointed-to byte was not read.",
    guide: "guides/pointers.html",
    options: { pointerSize: 1 },
    operations: {
      parse: {
        expected: {
          data: { root: { target: { Address: 1, Depth: 1, IsDereferenced: true, Value: 42 } } },
        },
      },
    },
  }),
  lesson(
    "limits",
    "Stop a read at a small byte limit",
    "struct header { uint16 kind; uint32 length; };",
    "02 00 06 00 00 00",
    "header",
    {
      level: "Advanced",
      tags: ["limits", "error", "bounded", "bytes"],
      sourceScenario: "decode-header",
      summary:
        "This lesson intentionally allows only three bytes of reading, although the input contains six.",
      prerequisite: "Understand field widths and the missing-bytes lesson.",
      exercise: "Open Workbench settings and increase Total bytes to 12 under Safety limits.",
      answer:
        "The workbench reads six bytes to parse the header and rereads those six bytes for its debug view. A budget of 12 lets it finish with kind 2 and length 6.",
      guide: "guides/variables-options-and-limits.html",
      options: { maxTotalBytesRead: 3 },
      operations: { parse: { expected: { error: "read-budget" } } },
    },
  ),
  lesson(
    "large-integer",
    "Preserve a large integer in JavaScript",
    "struct root { uint64 value; };",
    "ff ff ff ff ff ff ff ff",
    "root",
    {
      level: "Advanced",
      tags: ["uint64", "integer", "JSON", "precision"],
      sourceScenario: "preserve-enum",
      summary:
        "A uint64 can exceed JavaScript's exact number range. The bridge returns large integers as decimal strings.",
      prerequisite: "Understand JSON strings and numbers; see the browser value guide.",
      exercise: "Read the eight FF bytes and check whether the JSON result is a string or number.",
      answer:
        "18446744073709551615 needs a string or BigInt in JavaScript. Converting it to Number loses precision.",
      guide: "guides/browser/api.html",
      operations: {
        parse: { expected: { data: { root: { value: "18446744073709551615" } } } },
        serialize: {
          json: '{"value":"18446744073709551615"}',
          expected: { hex: "ff ff ff ff ff ff ff ff" },
        },
      },
    },
  ),
  lesson(
    "integers-24",
    "Read three-byte integers",
    "struct root { uint24< size; int24< delta; uint8 tail; };",
    "ff ff ff fe ff ff 63",
    "root",
    {
      level: "Intermediate",
      tags: ["integers-24", "binary", "metadata"],
      sourceScenario: "integers-24",
      summary:
        "Three-byte integers keep a three-byte stride. The unsigned size is 16777215 and the signed delta is -2.",
      prerequisite:
        "Understand byte offsets and the difference between reading, writing, and updating.",
      exercise: "Change size to 16777216 when writing.",
      answer: "The value exceeds 24 unsigned bits and is rejected.",
      guide: "guides/binary-metadata-types.html",
      operations: {
        parse: { expected: { data: { root: { size: 16777215, delta: -2, tail: 99 } } } },
        serialize: {
          json: '{"size":16777215,"delta":-2,"tail":99}',
          expected: { hex: "ff ff ff fe ff ff 63" },
        },
        update: { path: "root.size", json: "42", expected: { hex: "2a 00 00 fe ff ff 63" } },
      },
    },
  ),
  lesson(
    "bounded-encodings",
    "Read byte-bounded text",
    "struct root { utf8 currency[3]; latin1 western[1]; cp437 dos[1]; utf16le little[4]; utf16be big[4]; uint8 tail; };",
    "e2 82 ac e9 82 3d d8 00 de d8 3d de 00 63",
    "root",
    {
      level: "Intermediate",
      tags: ["bounded-encodings", "binary", "metadata"],
      sourceScenario: "bounded-encodings",
      summary:
        "Array counts measure encoded bytes. Euro needs three UTF-8 bytes, and the supplementary character needs four UTF-16 bytes. Latin-1 and CP437 encode the same accented letter differently.",
      prerequisite:
        "Understand byte offsets and the difference between reading, writing, and updating.",
      exercise: "Replace the Euro sign with a pound sign.",
      answer: "The pound sign uses two UTF-8 bytes; the third byte is zero padding.",
      guide: "guides/binary-metadata-types.html",
      operations: {
        parse: {
          expected: {
            data: {
              root: { currency: "€", western: "é", dos: "é", little: "😀", big: "😀", tail: 99 },
            },
          },
        },
        serialize: {
          json: '{"currency":"€","western":"é","dos":"é","little":"😀","big":"😀","tail":99}',
          expected: { hex: "e2 82 ac e9 82 3d d8 00 de d8 3d de 00 63" },
        },
        update: {
          path: "root.currency",
          json: '"£"',
          expected: { hex: "c2 a3 00 e9 82 3d d8 00 de d8 3d de 00 63" },
        },
      },
    },
  ),
  lesson(
    "variable-integers",
    "Read variable-width integers",
    "struct root { uleb128_32 count; uleb128_64 values[count]; sleb128_32 delta; uint8 tail; };",
    "02 7f 80 01 bf 7f 63",
    "root",
    {
      level: "Advanced",
      tags: ["variable-integers", "binary", "metadata"],
      sourceScenario: "variable-integers",
      summary:
        "LEB128 stores 127 in one byte and 128 in two. Decoded count 2 selects two elements; delta is signed -65.",
      prerequisite:
        "Understand byte offsets and the difference between reading, writing, and updating.",
      exercise: "Try updating the second value to 1.",
      answer: "That changes its encoded width and is rejected. Updating it to 129 keeps two bytes.",
      guide: "guides/binary-metadata-types.html",
      operations: {
        parse: {
          expected: { data: { root: { count: 2, values: [127, 128], delta: -65, tail: 99 } } },
        },
        serialize: {
          json: '{"count":2,"values":[127,128],"delta":-65,"tail":99}',
          expected: { hex: "02 7f 80 01 bf 7f 63" },
        },
        update: { path: "root.values[1]", json: "129", expected: { hex: "02 7f 81 01 bf 7f 63" } },
      },
    },
  ),
  lesson(
    "fixed-point",
    "Read fixed-point metadata",
    "struct root { fixed16_16> revision; ufixed8_8< volume; };",
    "ff fe 80 00 80 00",
    "root",
    {
      level: "Intermediate",
      tags: ["fixed-point", "binary", "metadata"],
      sourceScenario: "fixed-point",
      summary:
        "Fixed-point metadata is a binary-scaled integer. The values are revision -1.5 and volume 0.5; their byte orders are explicit.",
      prerequisite:
        "Understand byte offsets and the difference between reading, writing, and updating.",
      exercise: "Try writing volume 0.1.",
      answer:
        "It does not lie on the unsigned 8.8 grid, so the writer rejects it instead of rounding.",
      guide: "guides/binary-metadata-types.html",
      operations: {
        parse: { expected: { data: { root: { revision: -1.5, volume: 0.5 } } } },
        serialize: {
          json: '{"revision":-1.5,"volume":0.5}',
          expected: { hex: "ff fe 80 00 80 00" },
        },
        update: { path: "root.volume", json: "0.25", expected: { hex: "ff fe 80 00 40 00" } },
      },
    },
  ),
  lesson(
    "identifier-order",
    "Read UUID and GUID storage",
    "struct root { uuid network; guid windows; };",
    "00 11 22 33 44 55 66 77 88 99 aa bb cc dd ee ff 33 22 11 00 55 44 77 66 88 99 aa bb cc dd ee ff",
    "root",
    {
      level: "Intermediate",
      tags: ["identifier-order", "binary", "metadata"],
      sourceScenario: "identifier-order",
      summary:
        "Both fields describe the same identifier. UUID stores network order; Windows GUID reverses the first three integer fields. Each occupies 16 bytes.",
      prerequisite:
        "Understand byte offsets and the difference between reading, writing, and updating.",
      exercise: "Swap the uuid and guid type names.",
      answer:
        "The byte ranges stay the same, but the first groups of the decoded identifiers change.",
      guide: "guides/binary-metadata-types.html",
      operations: {
        parse: {
          expected: {
            data: {
              root: {
                network: "00112233-4455-6677-8899-aabbccddeeff",
                windows: "00112233-4455-6677-8899-aabbccddeeff",
              },
            },
          },
        },
        serialize: {
          json: '{"network":"00112233-4455-6677-8899-aabbccddeeff","windows":"00112233-4455-6677-8899-aabbccddeeff"}',
          expected: {
            hex: "00 11 22 33 44 55 66 77 88 99 aa bb cc dd ee ff 33 22 11 00 55 44 77 66 88 99 aa bb cc dd ee ff",
          },
        },
        update: {
          path: "root.windows",
          json: '"00000000-0000-0000-0000-000000000000"',
          expected: {
            hex: "00 11 22 33 44 55 66 77 88 99 aa bb cc dd ee ff 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00",
          },
        },
      },
    },
  ),
  lesson(
    "conditional-records",
    "Read tagged record variants",
    "struct entry { uint8 kind; switch(kind) { case 1: { utf8 label[3]; } default: { uint24< number; } } if(kind == 1) { uint8 flags; } }; struct root { uleb128_32 count; entry items[count]; };",
    "02 01 e2 82 ac 07 02 2a 00 00",
    "root",
    {
      level: "Advanced",
      tags: ["conditional-records", "binary", "metadata"],
      sourceScenario: "conditional-records",
      summary:
        "Each record selects its own fields: kind 1 has a UTF-8 label and flags, while the second record has a three-byte number. Inactive members are absent.",
      prerequisite:
        "Understand byte offsets and the difference between reading, writing, and updating.",
      exercise: "Try changing the first record kind through an update.",
      answer:
        "A branch-changing update is rejected. Serialize a new buffer when the active storage layout must change.",
      guide: "guides/conditional-fields.html",
      operations: {
        parse: {
          expected: {
            data: {
              root: {
                count: 2,
                items: [
                  { kind: 1, label: "€", flags: 7 },
                  { kind: 2, number: 42 },
                ],
              },
            },
          },
        },
        serialize: {
          json: '{"count":2,"items":[{"kind":1,"label":"€","flags":7},{"kind":2,"number":42}]}',
          expected: { hex: "02 01 e2 82 ac 07 02 2a 00 00" },
        },
        update: {
          path: "root.items[1].number",
          json: "43",
          expected: { hex: "02 01 e2 82 ac 07 02 2b 00 00" },
        },
      },
    },
  ),
  lesson(
    "conditional-decisions",
    "Choose branches separately for each array item",
    "struct entry { uint8 tag; int8 some_parameter; if (some_parameter * 20 > 10) { uint8 high; } else { uint8 low; } switch (tag) { case 1: { uint8 first; } case 2: { uint8 second; } default: { uint8 other; } } }; struct root { entry items[3]; };",
    "01 00 0a 0b 02 01 14 15 03 ff 1e 1f",
    "root",
    {
      level: "Intermediate",
      tags: ["conditionals", "if", "switch", "arrays", "expressions"],
      sourceScenario: "conditional-records",
      summary:
        "Each array item uses its own tag and calculated condition. A compiled layout does not reuse the first item's decision.",
      prerequisite: "Complete the fixed-array lesson and understand integer multiplication.",
      exercise:
        "Change only the first byte from 01 to 02. Reset, then change the second byte from 00 to 01.",
      answer:
        "The first edit changes the first item's first member to second, still 11. After reset, the parameter edit selects high instead of low, still 10. Other items are unchanged. Both alternatives here have equal widths.",
      guide: "guides/conditional-fields.html",
      operations: {
        parse: {
          expected: {
            data: {
              root: {
                items: [
                  { tag: 1, some_parameter: 0, low: 10, first: 11 },
                  { tag: 2, some_parameter: 1, high: 20, second: 21 },
                  { tag: 3, some_parameter: -1, low: 30, other: 31 },
                ],
              },
            },
          },
        },
      },
    },
  ),
  lesson(
    "conditional-scope",
    "Understand an unavailable local value",
    "#define count 99\nstruct entry { uint8 tag; if (tag) { uint8 count; } if (count > 0) { uint8 payload[count]; } }; struct root { entry items[2]; };",
    "01 01 2a 00",
    "root",
    {
      level: "Intermediate",
      tags: ["conditionals", "scope", "variables", "errors"],
      sourceScenario: "conditional-records",
      summary:
        "This intentional error shows why an inactive local cannot borrow a value from the previous array item or the caller.",
      prerequisite: "Complete the per-item branch lesson.",
      exercise: "Change if (count > 0) to if (tag != 0 && count > 0), then run again.",
      answer:
        "The second item has tag 0. Short-circuit evaluation skips count, so parsing succeeds. The first item contains count 1 and payload [42]; the second contains only tag 0.",
      guide: "guides/conditional-fields.html",
      operations: { parse: { expected: { error: "invalid-layout" } } },
    },
  ),
  lesson(
    "conditional-nesting",
    "Skip a condition inside an inactive branch",
    "struct root { uint8 tag; if (tag) { if (missing > 0) { uint8 value; } } uint8 tail; };",
    "00 09",
    "root",
    {
      level: "Intermediate",
      tags: ["conditionals", "nesting", "expressions", "errors"],
      sourceScenario: "conditional-records",
      summary:
        "An inner condition runs only if its outer branch was selected. The starting input never looks up missing.",
      prerequisite: "Understand if/else and unavailable local values.",
      exercise: "Change the first byte from 00 to 01, then run again.",
      answer:
        "The outer branch becomes active. The inner condition now evaluates missing and reports a layout error. Reset restores the successful parse with tail 9.",
      guide: "guides/conditional-fields.html",
      operations: { parse: { expected: { data: { root: { tag: 0, tail: 9 } } } } },
    },
  ),
];

const operationTitles: Record<string, [string, string?, string?]> = {
  "integers-24": [
    "Read three-byte integers",
    "Write three-byte integers",
    "Update three-byte integers",
  ],
  "bounded-encodings": [
    "Read byte-bounded text",
    "Write byte-bounded text",
    "Update byte-bounded text",
  ],
  "variable-integers": [
    "Read variable-width integers",
    "Write variable-width integers",
    "Update variable-width integers",
  ],
  "fixed-point": [
    "Read fixed-point metadata",
    "Write fixed-point metadata",
    "Update fixed-point metadata",
  ],
  "identifier-order": [
    "Read UUID and GUID storage",
    "Write UUID and GUID storage",
    "Update UUID and GUID storage",
  ],
  "conditional-records": [
    "Read tagged record variants",
    "Write tagged record variants",
    "Update tagged record variants",
  ],
  header: ["Read a file header", "Create a file header", "Change a file header field"],
  text: ["Read fixed-capacity text", "Create fixed-capacity text", "Replace fixed-capacity text"],
  nested: ["Read a nested record", "Create a nested record", "Change a nested field"],
  arrays: [
    "Read records in a fixed array",
    "Create records in a fixed array",
    "Change an array element",
  ],
  alignment: ["Find padding between fields", "Create an aligned header"],
  bitfields: ["Read several flags from one byte", "Create a byte from flags"],
  "terminated-text": ["Find the end of a text field", "Create zero-terminated text"],
  enum: ["Read an unknown enum value", "Write an unknown enum value"],
  union: ["Inspect overlapping storage", "Create bytes from a union member"],
  "large-integer": ["Read a large integer in JavaScript", "Write a large integer in JavaScript"],
};

const readExplanations: Record<string, string> = {
  "conditional-decisions":
    "A decision is evaluated once per group, per struct instance, per operation. Each item reads its own tag and some_parameter first. Parameters 0, 1, and -1 select low, high, and low; tags 1, 2, and 3 select first, second, and default's other. A selected group keeps its decision for all its fields. Later and nested groups decide when reached. Reusing a compiled layout does not share decisions between items or parses.",
  "conditional-scope":
    "The first item reads count 1 and payload 42. The second has tag 0, so its local count is unavailable and the next condition fails. Local declarations hide caller variables from entry: even the #define count 99 cannot rescue this expression. Each item starts fresh. Guard count with tag != 0 && count > 0 to avoid evaluating an unavailable value.",
  "conditional-nesting":
    "Tag 0 skips the entire outer branch, including the inner condition, so tail reads the next byte as 9. No value for missing is needed. Tag 1 reaches the inner condition and produces a missing-variable error. Calculations use checked 32-bit integers; active expressions still enforce overflow, division, depth, and work limits.",
  "integers-24":
    "Three-byte integers keep a three-byte stride. The unsigned size is 16777215 and the signed delta is -2.",
  "bounded-encodings":
    "Array counts measure encoded bytes. Euro needs three UTF-8 bytes, and the supplementary character needs four UTF-16 bytes. Latin-1 and CP437 encode the same accented letter differently.",
  "variable-integers":
    "LEB128 stores 127 in one byte and 128 in two. Decoded count 2 selects two elements; delta is signed -65.",
  "fixed-point":
    "Fixed-point metadata is a binary-scaled integer. The values are revision -1.5 and volume 0.5; their byte orders are explicit.",
  "identifier-order":
    "Both fields describe the same identifier. UUID stores network order; Windows GUID reverses the first three integer fields. Each occupies 16 bytes.",
  "conditional-records":
    "Each record selects its own fields: kind 1 has a UTF-8 label and flags, while the second record has a three-byte number. Inactive members are absent.",
  header:
    "This six-byte header stores kind in two bytes and length in four bytes. Reading 02 00 06 00 00 00 in little-endian order gives kind 2 and length 6. Changing the first byte to 03 changes kind to 3 without changing length.",
  "byte-order":
    "The same bytes produce different values depending on byte order. This header reads kind 2 and length 6 with little-endian order. Selecting Big endian in Workbench settings instead produces kind 512 and length 100663296; the field widths stay the same.",
  truncated:
    "This read fails because the input has only three bytes, while the header needs six. Paste 02 00 06 00 00 00 into the hex editor and run again to read kind 2 and length 6.",
  "invalid-path":
    "This read fails because the selected root is Header, but the layout declares header. Names are case-sensitive. Open Workbench settings, change Root type/path to header, and run again to read kind 2 and length 6.",
  text: "The four-byte text field reads 41 42 43 00 as ABC followed by a zero character. Fixed-capacity text keeps that character in the result. Changing 41 to 58 produces XBC followed by zero, using the same four bytes.",
  nested:
    "The root contains a nested record with a two-byte id and a one-byte flags field. Bytes 34 12 01 read as id 4660 and flags 1. Changing the last byte to A5 changes flags to 165 while preserving the id.",
  arrays:
    "The packet contains two records, each holding a two-byte id. Bytes 01 00 02 00 read as ids 1 and 2. Array indexes start at zero, so packet.items[1].id selects the second record.",
  alignment:
    "This aligned header has two padding bytes between kind and length, so length starts at offset 4. The values are kind 2 and length 6. Turning off alignment makes length start at offset 2 and read as 393216; a packed version needs the two padding bytes removed.",
  bitfields:
    "One byte stores several flags: the lowest bit is enabled, the next three bits are mode, and the remaining four are reserved. Byte 0B reads as enabled 1, mode 5, and reserved 0. Byte 0A clears enabled while keeping mode 5.",
  "terminated-text":
    "The text field reads until a zero byte. Bytes 41 42 00 produce AB; the terminator is not included in the returned text. Removing the final 00 causes the read to fail. Restore that byte to make it succeed.",
  enum: "The four FF bytes contain the number 4294967295, which has no named member in the state enum. The result preserves the number and reports Name as null. Bytes 01 00 00 00 instead produce the named member Known with value 1.",
  union:
    "Both union members interpret the same bytes. For 34 12, small reads the first byte as 52 and large reads both bytes as 4660. The result keeps both interpretations and the original raw storage so the bytes can be preserved when writing again.",
  pointer:
    "The first byte stores pointer address 1, and the byte at that position contains 42. The result includes the address and the value read there. Turning off Follow pointers in Workbench settings keeps the address but leaves the target unread; this is a position in the input, not a process memory address.",
  limits:
    "This read fails because its total-byte limit is 3. The header contains six bytes, but the workbench reads them twice: once to parse the fields and once to collect their bytes for the debug view. Total bytes counts every read, including rereads, so 6 is still too small. Open Workbench settings and increase Total bytes to 12 under Safety limits, then run again to get kind 2 and length 6. A plain parse without debug data needs only 6 for this header.",
  "large-integer":
    "Eight FF bytes represent the largest uint64 value, 18446744073709551615. The browser returns it as a decimal string because JavaScript Number cannot represent it exactly. Keep it as a string or convert it to BigInt to preserve all digits.",
};

const writeExplanations: Record<string, string> = {
  "integers-24":
    "Serialize the supplied metadata values with the declared encodings and byte order. Three-byte integers keep a three-byte stride. The unsigned size is 16777215 and the signed delta is -2.",
  "bounded-encodings":
    "Serialize the supplied metadata values with the declared encodings and byte order. Array counts measure encoded bytes. Euro needs three UTF-8 bytes, and the supplementary character needs four UTF-16 bytes. Latin-1 and CP437 encode the same accented letter differently.",
  "variable-integers":
    "Serialize the supplied metadata values with the declared encodings and byte order. LEB128 stores 127 in one byte and 128 in two. Decoded count 2 selects two elements; delta is signed -65.",
  "fixed-point":
    "Serialize the supplied metadata values with the declared encodings and byte order. Fixed-point metadata is a binary-scaled integer. The values are revision -1.5 and volume 0.5; their byte orders are explicit.",
  "identifier-order":
    "Serialize the supplied metadata values with the declared encodings and byte order. Both fields describe the same identifier. UUID stores network order; Windows GUID reverses the first three integer fields. Each occupies 16 bytes.",
  "conditional-records":
    "Serialize the supplied metadata values with the declared encodings and byte order. Each record selects its own fields: kind 1 has a UTF-8 label and flags, while the second record has a three-byte number. Inactive members are absent.",
  header:
    "Create a six-byte header with kind 3 and length 6. The two-byte kind comes first, followed by the four-byte length, both in little-endian order.",
  text: "Store XY in a four-byte text field. The two unused bytes are filled with zeros; text longer than the field's capacity cannot be written.",
  nested:
    "Create a nested record with id 4660 and flags 165. The id occupies two bytes and the flags field occupies one byte.",
  arrays:
    "Create an array of two records with ids 1 and 2. Each id occupies two bytes, and the records are written consecutively.",
  alignment:
    "Create an aligned header with kind 2 and length 6. Two padding bytes place the four-byte length at offset 4, making the header eight bytes long.",
  bitfields:
    "Pack enabled 1, mode 5, and reserved 0 into one byte. The lowest bit holds enabled and the next three bits hold mode.",
  "terminated-text":
    "Write AB followed by a zero terminator. The terminator is added to the bytes so a later read can find the end of the text.",
  enum: "Write the numeric enum value 4294967295 even though it has no named member. Keeping the number preserves information that a newer version of the format may understand.",
  union:
    "Write the selected small member with value 165. The union occupies two bytes, so the remaining byte is cleared to zero. Selecting a member determines which interpretation is written.",
  "large-integer":
    "Write the largest uint64 value, 18446744073709551615. The JSON uses a quoted decimal string to preserve the digits that JavaScript Number cannot represent exactly.",
};

// Preserve existing read URLs; each additional operation gets its own stable lesson URL.
export const lessons: (Lesson & { operation: InteropOperation; explanation: string })[] =
  lessonTopics.flatMap((topic) =>
    Object.entries(topic.operations).map(([key, preset]) => {
      const operation = key as InteropOperation;
      const title =
        operationTitles[topic.id]?.[{ parse: 0, serialize: 1, update: 2 }[operation]] ??
        topic.title;
      return {
        ...topic,
        id: operation === "parse" ? topic.id : `${topic.id}-${operation}`,
        title,
        methodName: title,
        operation,
        explanation:
          operation === "parse"
            ? readExplanations[topic.id]!
            : operation === "serialize"
              ? `${writeExplanations[topic.id]} The resulting bytes are ${preset!.expected.hex}.`
              : `Replace ${preset!.path} with ${preset!.json} in the existing bytes. The resulting bytes are ${preset!.expected.hex}; other fields keep their values and positions. The replacement must fit the selected field.`,
        guide:
          topic.id === "header" && operation !== "parse"
            ? "guides/header-next-steps.html"
            : topic.guide,
        operations: { [operation]: preset },
        tags: [
          ...topic.tags,
          operation,
          operation === "serialize" ? "create write" : operation === "update" ? "change" : "read",
        ],
        documentation: {
          ...topic.documentation!,
          summary:
            operation === "parse"
              ? topic.documentation!.summary
              : operation === "serialize"
                ? "Create new binary data from the supplied JSON value. Compare the output bytes with the expected result."
                : `Change ${preset!.path} in the starting bytes. Other fields keep their values.`,
        },
        prerequisite:
          operation === "parse"
            ? topic.prerequisite
            : `Complete “${operationTitles[topic.id]?.[0] ?? topic.title}” first.`,
        exercise:
          operation === "parse"
            ? topic.exercise
            : operation === "serialize"
              ? "Run the starting value, then change a JSON value and predict which output bytes will change."
              : `Run the replacement ${preset!.json} at ${preset!.path}, then compare the changed bytes.`,
        answer:
          operation === "parse"
            ? topic.answer
            : `The starting output is ${preset!.expected.hex}. Reset restores the original inputs.`,
      };
    }),
  );

export function compareLessonResult(
  expected: LessonOperation["expected"],
  result: InteropResult,
  bytes: Uint8Array,
): boolean {
  if (expected.error) return !result.Success && result.Error?.Code === expected.error;
  if (!result.Success) return false;
  if (expected.hex !== undefined) {
    return (
      Array.from(bytes, (byte) => byte.toString(16).padStart(2, "0")).join(" ") === expected.hex
    );
  }
  const parsedJson = typeof result.Data === "string" ? result.Data : "null";
  return JSON.stringify(JSON.parse(parsedJson)) === JSON.stringify(expected.data);
}
