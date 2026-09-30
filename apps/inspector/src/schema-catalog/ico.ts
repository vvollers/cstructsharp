/**
 * Windows icon and cursor directories, with the ICO teaching sample.
 */

import { sampleParserOptions, type FormatDefinition } from "./types";

export const formats: FormatDefinition[] = [
  {
    extensions: ["ico", "cur"],
    family: "Windows icon directory",
    scope:
      "Complete image directory. CUR uses hotspot coordinates where ICO stores planes and bit depth. Image payloads remain at the recorded offsets.",
    fields: `uint16 reserved;
uint16 kind;
uint16 image_count;
icon_entry images[image_count];`,
    types: `struct icon_entry {
    uint8 width;
    uint8 height;
    uint8 color_count;
    uint8 reserved;
    uint16 planes_or_hotspot_x;
    uint16 bit_depth_or_hotspot_y;
    uint32 image_size;
    uint32 image_offset;
};`,
    samples: [
      {
        id: "ico",
        extension: "ico",
        title: "ICO - icon directory",
        description: "Windows icon",
        definition: `struct icon_dir_entry {
    uint8 width;
    uint8 height;
    uint8 color_count;
    uint8 reserved;
    uint16 color_planes;
    uint16 bits_per_pixel;
    uint32 size_in_bytes;
    uint32 data_offset;
};

struct root {
    uint16 reserved;
    uint16 image_type;
    uint16 image_count;
    icon_dir_entry entries[image_count];
};`,
        binaryHex:
          "00 00 01 00 02 00 10 10 00 00 01 00 20 00 68 04 00 00 26 00 00 00 20 20 00 00 01 00 20 00 a8 10 00 00 8e 04 00 00",
        rootType: "root",
        parserOptions: sampleParserOptions,
        documentation: {
          summary:
            "A 2-entry ICO directory (16x16 and 32x32 images). image_count drives the length of the trailing entries array - a runtime-sized array of a composite (struct) element type, not just a byte array.",
        },
      },
    ],
  },
];
