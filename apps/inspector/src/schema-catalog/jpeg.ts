/**
 * JPEG segments, with the JPEG teaching sample.
 */

import { sampleParserOptions, type FormatDefinition } from "./types";

export const formats: FormatDefinition[] = [
  {
    extensions: ["jpg"],
    family: "JPEG",
    scope:
      "Up to eight JPEG segments before the first SOS/EOI. Native marker/length branches decode JFIF, frame/scan components, first quantization/Huffman tables and comments. Entropy-coded scans and vendor APP data are not searched or decoded.",
    fields: `uint16 start_of_image;
jpeg_segment segment_0;
if (marker != 0xffda && marker != 0xffd9) {
    jpeg_segment segment_1;
} if (marker != 0xffda && marker != 0xffd9) {
    jpeg_segment segment_2;
} if (marker != 0xffda && marker != 0xffd9) {
    jpeg_segment segment_3;
} if (marker != 0xffda && marker != 0xffd9) {
    jpeg_segment segment_4;
} if (marker != 0xffda && marker != 0xffd9) {
    jpeg_segment segment_5;
} if (marker != 0xffda && marker != 0xffd9) {
    jpeg_segment segment_6;
} if (marker != 0xffda && marker != 0xffd9) {
    jpeg_segment segment_7;
}`,
    types: `struct jpeg_component {
    uint8 id;
    uint8 vertical_sampling:4;
    uint8 horizontal_sampling:4;
    uint8 quantization_table;
};
struct jpeg_scan_component {
    uint8 id;
    uint8 ac_table:4;
    uint8 dc_table:4;
};
struct jpeg_approximation {
    uint8 low:4;
    uint8 high:4;
};
struct jpeg_quantization_selector {
    uint8 table_id:4;
    uint8 precision:4;
};
struct jpeg_frame {
    uint8 precision;
    uint16 height;
    uint16 width;
    uint8 component_count;
    jpeg_component components[component_count];
};
struct jpeg_segment {
    uint16 marker;
    if (marker != 0xffd8 && marker != 0xffd9 && marker != 0xff01 && (marker < 0xffd0 || marker > 0xffd7)) {
        uint16 segment_length;
        if (segment_length >= 2) {
            switch (marker) {
                case 0xffc0: {
                    jpeg_frame baseline;
                } case 0xffc1: {
                    jpeg_frame extended;
                } case 0xffc2: {
                    jpeg_frame progressive;
                } case 0xffc3: {
                    jpeg_frame lossless;
                } case 0xffda: {
                    uint8 scan_component_count;
                    jpeg_scan_component components[scan_component_count];
                    uint8 spectral_start;
                    uint8 spectral_end;
                    jpeg_approximation approximation;
                } case 0xffdd: {
                    if (segment_length == 4) {
                        uint16 restart_interval;
                    } else {
                        uint8 invalid_restart[segment_length - 2];
                    }
                } case 0xfffe: {
                    char comment[segment_length - 2];
                } case 0xffdb: {
                    if (segment_length >= 3) {
                        jpeg_quantization_selector selector;
                        if (precision == 0 && segment_length >= 67) {
                            uint8 coefficients[64];
                            uint8 additional_tables[segment_length - 67];
                        } else {
                            if (precision == 1 && segment_length >= 131) {
                                uint16 coefficients16[64];
                                uint8 additional_tables16[segment_length - 131];
                            } else {
                                uint8 invalid_table[segment_length - 3];
                            }
                        }
                    }
                } case 0xffc4: {
                    if (segment_length >= 19) {
                        uint8 table_selector;
                        uint8 code_counts[16];
                        uint8 symbols_and_tables[segment_length - 19];
                    } else {
                        uint8 invalid_huffman[segment_length - 2];
                    }
                } case 0xffe0: {
                    if (segment_length >= 7) {
                        uint32 identifier;
                        uint8 terminator;
                        if (identifier == 0x4a464946 && terminator == 0 && segment_length >= 16) {
                            uint8 major;
                            uint8 minor;
                            uint8 density_unit;
                            uint16 x_density;
                            uint16 y_density;
                            uint8 thumbnail_width;
                            uint8 thumbnail_height;
                            uint8 thumbnail[segment_length - 16];
                        } else {
                            uint8 application_data[segment_length - 7];
                        }
                    } else {
                        uint8 short_application[segment_length - 2];
                    }
                } default: {
                    uint8 segment_data[segment_length - 2];
                }
            }
        }
    }
};`,
    littleEndian: false,
    samples: [
      {
        id: "jpg",
        extension: "jpg",
        title: "JPG - SOI + JFIF APP0 segment",
        description: "JPEG image",
        definition: `struct app0_segment {
    uint16> marker;
    uint16> length;
    char identifier[5];
    uint8 version_major;
    uint8 version_minor;
    uint8 density_units;
    uint16> x_density;
    uint16> y_density;
    uint8 thumbnail_width;
    uint8 thumbnail_height;
};

struct root {
    uint16> soi_marker;
    app0_segment app0;
};`,
        binaryHex: "ff d8 ff e0 00 10 4a 46 49 46 00 01 01 01 00 48 00 48 00 00",
        rootType: "root",
        parserOptions: sampleParserOptions,
        documentation: {
          summary:
            "A JPEG SOI marker plus a standard JFIF APP0 segment (72 DPI, no thumbnail). Scoped to the JFIF header specifically - a full JPEG is a variable chain of marker segments, not representable as one fixed struct. Uses explicit per-field '>' suffixes rather than a global option, since only these fields are big-endian.",
        },
      },
    ],
  },
];
