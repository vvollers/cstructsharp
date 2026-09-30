/**
 * Font files: SFNT (TrueType/OpenType), WOFF, WOFF2, TrueType collections and Embedded OpenType.
 */

import type { FormatDefinition } from "./types";

export const formats: FormatDefinition[] = [
  {
    extensions: ["ttf", "otf"],
    family: "SFNT",
    scope:
      "Font offset table and complete table directory. Glyph and shaping table payloads remain at the recorded offsets.",
    fields: `uint32 scaler_type;
uint16 table_count;
uint16 search_range;
uint16 entry_selector;
uint16 range_shift;
table_record tables[table_count];`,
    types: `struct table_record {
    char tag[4];
    uint32 checksum;
    uint32 offset;
    uint32 length;
};`,
    littleEndian: false,
  },
  {
    extensions: ["woff"],
    family: "WOFF",
    scope: "WOFF header and table directory. Table decompression is not performed.",
    fields: `char signature[4];
uint32 flavor;
uint32 length;
uint16 table_count;
uint16 reserved;
uint32 total_sfnt_size;
uint16 major_version;
uint16 minor_version;
uint32 metadata_offset;
uint32 metadata_length;
uint32 metadata_original_length;
uint32 private_offset;
uint32 private_length;
table_record tables[table_count];`,
    types: `struct table_record {
    char tag[4];
    uint32 offset;
    uint32 compressed_length;
    uint32 original_length;
    uint32 original_checksum;
};`,
    littleEndian: false,
  },
  {
    extensions: ["woff2"],
    family: "WOFF2",
    scope: "WOFF2 fixed header. Variable-length table directory and Brotli data are not decoded.",
    fields: `char signature[4];
uint32 flavor;
uint32 length;
uint16 table_count;
uint16 reserved;
uint32 total_sfnt_size;
uint32 total_compressed_size;
uint16 major_version;
uint16 minor_version;
uint32 metadata_offset;
uint32 metadata_length;
uint32 metadata_original_length;
uint32 private_offset;
uint32 private_length;`,
    littleEndian: false,
  },
  {
    extensions: ["ttc"],
    family: "TrueType collection",
    scope: "Collection version and font offset array.",
    fields: `char signature[4];
uint32 version;
uint32 font_count;
uint32 font_offsets[font_count];`,
    littleEndian: false,
  },
  {
    extensions: ["eot"],
    family: "Embedded OpenType",
    scope: "EOT fixed header, embedding permissions and Unicode/codepage coverage.",
    fields: `uint32 eot_size;
uint32 font_data_size;
uint32 version;
uint32 flags;
uint8 panose[10];
uint8 charset;
uint8 italic;
uint32 weight;
uint16 embedding_flags;
uint16 magic;
uint32 unicode_ranges[4];
uint32 codepage_ranges[2];
uint32 checksum_adjustment;
uint32 reserved[4];`,
  },
];
