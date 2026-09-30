/**
 * BMP: the file header and the DIB header variants, with the teaching sample the inspector opens with.
 */

import { sampleParserOptions, type FormatDefinition } from "./types";

export const formats: FormatDefinition[] = [
  {
    extensions: ["bmp"],
    family: "BMP",
    scope:
      "BMP file header and native DIB size branches: CORE, INFO, V2/V3, V4/V5 masks and calibrated RGB fixed-point fields. Pixels remain undecoded.",
    fields: `char signature[2];
uint32 file_size;
uint16 reserved_1;
uint16 reserved_2;
uint32 pixel_offset;
uint32 dib_header_size;
if (dib_header_size == 12) {
    uint16 core_width;
    uint16 core_height;
    uint16 core_planes;
    uint16 core_bits_per_pixel;
} else {
    if (dib_header_size >= 40) {
        int32 width;
        int32 height;
        uint16 planes;
        uint16 bits_per_pixel;
        uint32 compression;
        uint32 image_size;
        int32 pixels_per_meter_x;
        int32 pixels_per_meter_y;
        uint32 colors_used;
        uint32 important_colors;
        if (dib_header_size >= 52) {
            uint32 red_mask;
            uint32 green_mask;
            uint32 blue_mask;
        } if (dib_header_size >= 56) {
            uint32 alpha_mask;
        } if (dib_header_size >= 108) {
            uint32 color_space;
            if (color_space == 0) {
                fixed2_30 endpoints_xyz[3][3];
                ufixed16_16 gamma_red;
                ufixed16_16 gamma_green;
                ufixed16_16 gamma_blue;
            } else {
                uint8 unused_color_calibration[48];
            }
        } if (dib_header_size >= 124) {
            uint32 rendering_intent;
            uint32 profile_offset;
            uint32 profile_size;
            uint32 reserved;
        }
    }
}`,
    samples: [
      {
        id: "bmp",
        extension: "bmp",
        title: "BMP - bitmap header",
        description: "Bitmap image",
        definition: `enum bmp_compression : uint32 {
    Rgb = 0,
    Rle8 = 1,
    Rle4 = 2,
    Bitfields = 3
};

struct bitmap_file_header {
    char signature[2];
    uint32 file_size;
    uint16 reserved1;
    uint16 reserved2;
    uint32 pixel_data_offset;
};

struct bitmap_info_header {
    uint32 header_size;
    int32 width;
    int32 height;
    uint16 planes;
    uint16 bits_per_pixel;
    bmp_compression compression;
    uint32 image_size;
    int32 x_pixels_per_meter;
    int32 y_pixels_per_meter;
    uint32 colors_used;
    uint32 colors_important;
};

struct root {
    bitmap_file_header file_header;
    bitmap_info_header info_header;
};`,
        binaryHex:
          "42 4d 36 00 00 00 00 00 00 00 36 00 00 00 28 00 00 00 02 00 00 00 01 00 00 00 01 00 18 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00",
        rootType: "root",
        parserOptions: sampleParserOptions,
        documentation: {
          summary:
            "A minimal 54-byte BMP: the 14-byte BITMAPFILEHEADER plus the 40-byte BITMAPINFOHEADER, with no pixel data. Nested composites and an enum for the compression method.",
        },
      },
    ],
  },
];
