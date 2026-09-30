/**
 * PNG and APNG chunks, with the PNG teaching sample.
 */

import { sampleParserOptions, type FormatDefinition } from "./types";

export const formats: FormatDefinition[] = [
  {
    extensions: ["png", "apng"],
    family: "PNG",
    scope:
      "Up to eight sequential chunks, selected by native if/switch statements. Includes image, palette, color, text and animation metadata. Encoded bodies are ordinary arrays; increase the read/array budgets when intentionally decoding large bodies.",
    fields: `uint8 signature[8];
png_chunk chunk_0;
if (kind != 0x49454e44) {
    png_chunk chunk_1;
} if (kind != 0x49454e44) {
    png_chunk chunk_2;
} if (kind != 0x49454e44) {
    png_chunk chunk_3;
} if (kind != 0x49454e44) {
    png_chunk chunk_4;
} if (kind != 0x49454e44) {
    png_chunk chunk_5;
} if (kind != 0x49454e44) {
    png_chunk chunk_6;
} if (kind != 0x49454e44) {
    png_chunk chunk_7;
}`,
    types: `enum png_kind : uint32 {
    IHDR=0x49484452,
    PLTE=0x504c5445,
    IDAT=0x49444154,
    IEND=0x49454e44,
    gAMA=0x67414d41,
    cHRM=0x6348524d,
    pHYs=0x70485973,
    sRGB=0x73524742,
    tIME=0x74494d45,
    tEXt=0x74455874,
    acTL=0x6163544c,
    fcTL=0x6663544c,
    fdAT=0x66644154
};
enum png_color : uint8 {
    Grayscale=0,
    Truecolor=2,
    Indexed=3,
    GrayscaleAlpha=4,
    TruecolorAlpha=6
};
struct png_rgb {
    uint8 red;
    uint8 green;
    uint8 blue;
};
struct png_chunk {
    uint32 length;
    png_kind kind;
    switch (kind) {
        case 0x49484452: {
            if (length == 13) {
                uint32 width;
                uint32 height;
                uint8 bit_depth;
                png_color color_type;
                uint8 compression;
                uint8 filter;
                uint8 interlace;
            } else {
                uint8 invalid_header[length];
            }
        } case 0x504c5445: {
            if (length / 3 * 3 == length) {
                png_rgb colors[length / 3];
            } else {
                uint8 invalid_palette[length];
            }
        } case 0x67414d41: {
            if (length == 4) {
                uint32 gamma_times_100000;
            } else {
                uint8 invalid_gamma[length];
            }
        } case 0x6348524d: {
            if (length == 32) {
                uint32 white_x;
                uint32 white_y;
                uint32 red_x;
                uint32 red_y;
                uint32 green_x;
                uint32 green_y;
                uint32 blue_x;
                uint32 blue_y;
            } else {
                uint8 invalid_chromaticity[length];
            }
        } case 0x70485973: {
            if (length == 9) {
                uint32 pixels_per_unit_x;
                uint32 pixels_per_unit_y;
                uint8 unit;
            } else {
                uint8 invalid_resolution[length];
            }
        } case 0x73524742: {
            if (length == 1) {
                uint8 rendering_intent;
            } else {
                uint8 invalid_srgb[length];
            }
        } case 0x74494d45: {
            if (length == 7) {
                uint16 year;
                uint8 month;
                uint8 day;
                uint8 hour;
                uint8 minute;
                uint8 second;
            } else {
                uint8 invalid_time[length];
            }
        } case 0x74455874: {
            latin1 keyword_and_text[length];
        } case 0x6163544c: {
            if (length == 8) {
                uint32 frame_count;
                uint32 play_count;
            } else {
                uint8 invalid_animation[length];
            }
        } case 0x6663544c: {
            if (length == 26) {
                uint32 sequence;
                uint32 frame_width;
                uint32 frame_height;
                uint32 x_offset;
                uint32 y_offset;
                uint16 delay_numerator;
                uint16 delay_denominator;
                uint8 dispose;
                uint8 blend;
            } else {
                uint8 invalid_frame[length];
            }
        } case 0x66644154: {
            if (length >= 4) {
                uint32 sequence_number;
                uint8 encoded_frame[length - 4];
            } else {
                uint8 invalid_frame_data[length];
            }
        } default: {
            uint8 payload[length];
        }
    } uint32 crc32;
};`,
    littleEndian: false,
    samples: [
      {
        id: "png",
        extension: "png",
        title: "PNG - signature + IHDR chunk",
        description: "PNG image",
        definition: `enum png_color_type : uint8 {
    Grayscale = 0,
    Rgb = 2,
    Palette = 3,
    GrayscaleAlpha = 4,
    Rgba = 6
};

struct ihdr_chunk {
    uint32 length;
    char chunk_type[4];
    uint32 width;
    uint32 height;
    uint8 bit_depth;
    png_color_type color_type;
    uint8 compression_method;
    uint8 filter_method;
    uint8 interlace_method;
    uint32 crc;
};

struct root {
    uint8 signature[8];
    ihdr_chunk ihdr;
};`,
        binaryHex:
          "89 50 4e 47 0d 0a 1a 0a 00 00 00 0d 49 48 44 52 00 00 00 01 00 00 00 01 08 02 00 00 00 00 00 00 00",
        rootType: "root",
        parserOptions: { ...sampleParserOptions, littleEndian: false },
        documentation: {
          summary:
            "The 8-byte PNG signature plus a 1x1 RGB IHDR chunk. PNG is the one big-endian format in this catalog, modeled with the global littleEndian: false option since every multi-byte field is big-endian.",
        },
      },
    ],
  },
];
