/**
 * TAR headers, with the TAR teaching sample.
 */

import { sampleParserOptions, type FormatDefinition } from "./types";

// TAR's sample and detection schema read the same fields, with different root wrappers.
const tarFields = `
    char name[100];
    char mode[8];
    char uid[8];
    char gid[8];
    char size[12];
    char mtime[12];
    char chksum[8];
    char typeflag[1];
    char linkname[100];
    char magic[6];
    char version[2];
    char uname[32];
    char gname[32];
    char devmajor[8];
    char devminor[8];
    char prefix[155];
    char padding[12];
`;

export const formats: FormatDefinition[] = [
  {
    extensions: ["tar"],
    family: "TAR",
    scope: "First 512-byte TAR entry, including POSIX ustar names and ownership fields.",
    fields: tarFields,
    samples: [
      {
        id: "tar",
        extension: "tar",
        title: "TAR - POSIX ustar header",
        description: "TAR archive",
        definition: `struct root {${tarFields}};`,
        binaryHex:
          "68 65 6c 6c 6f 2e 74 78 74 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 30 30 30 30 36 34 34 00 30 30 30 30 30 30 30 00 30 30 30 30 30 30 30 00 30 30 30 30 30 30 30 30 30 31 32 00 30 30 30 30 30 30 30 30 30 30 30 00 30 31 31 35 35 36 00 20 30 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 75 73 74 61 72 00 30 30 75 73 65 72 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 67 72 6f 75 70 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00",
        rootType: "root",
        parserOptions: sampleParserOptions,
        documentation: {
          summary:
            'A real 512-byte POSIX ustar header for a file named "hello.txt", with a correctly computed checksum. Fixed-width ASCII/octal text fields throughout - a different complexity flavor from the other binary-integer formats.',
        },
      },
    ],
  },
];
