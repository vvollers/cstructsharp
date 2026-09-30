/**
 * ZIP and the formats built on it (EPUB, Office Open XML, OpenDocument, APK, ...), with the ZIP teaching sample.
 */

import { sampleParserOptions, type FormatDefinition } from "./types";

export const formats: FormatDefinition[] = [
  {
    extensions: [
      "zip",
      "epub",
      "xpi",
      "docx",
      "pptx",
      "xlsx",
      "odt",
      "ods",
      "odp",
      "3mf",
      "vsdx",
      "apk",
      "potx",
      "xltx",
      "dotx",
      "xltm",
      "ott",
      "ots",
      "otp",
      "odg",
      "otg",
      "xlsm",
      "docm",
      "dotm",
      "potm",
      "pptm",
      "jar",
      "ppsm",
      "ppsx",
      "key",
      "numbers",
      "pages",
    ],
    family: "ZIP",
    scope:
      "Native signature branches decode a local header, central-directory record, ZIP64 end record or empty archive footer at the current root. Streaming entries stop at their header; no footer search or offset repair occurs outside the library.",
    fields: `uint32 signature;
switch (signature) {
    case 0x04034b50: {
        zip_local local;
        if (data_descriptor == 0 && compressed_size != 0xffffffff) {
            uint8 compressed_payload[compressed_size];
        }
    } case 0x02014b50: {
        zip_central directory_entry;
    } case 0x06054b50: {
        uint16 disk;
        uint16 directory_disk;
        uint16 disk_entries;
        uint16 total_entries;
        uint32 directory_size;
        zip_directory *directory;
        uint16 comment_length;
        char comment[comment_length];
    } case 0x06064b50: {
        uint64 record_size;
        uint16 made_by;
        uint16 needed;
        uint32 disk64;
        uint32 directory_disk64;
        uint64 disk_entries64;
        uint64 total_entries64;
        uint64 directory_size64;
        uint64 directory_offset64;
    }
}`,
    types: `struct zip_flags {
    uint16 encrypted:1;
    uint16 compression_options:2;
    uint16 data_descriptor:1;
    uint16 reserved_a:2;
    uint16 strong_encryption:1;
    uint16 reserved_b:4;
    uint16 utf8_names:1;
    uint16 reserved_c:1;
    uint16 masked_header:1;
    uint16 reserved_d:2;
};
enum zip_method : uint16 {
    Stored=0,
    Deflate=8,
    Deflate64=9,
    Bzip2=12,
    Lzma=14,
    Zstandard=93,
    Xz=95,
    Ppmd=98,
    Aes=99
};
struct dos_time {
    uint16 seconds_divided_by_two:5;
    uint16 minutes:6;
    uint16 hours:5;
};
struct dos_date {
    uint16 day:5;
    uint16 month:4;
    uint16 years_since_1980:7;
};
struct zip_local {
    uint16 version_needed;
    zip_flags flags;
    zip_method compression;
    dos_time modified_time;
    dos_date modified_date;
    uint32 crc32;
    uint32 compressed_size;
    uint32 uncompressed_size;
    uint16 filename_length;
    uint16 extra_length;
    if (utf8_names) {
        utf8 filename_utf8[filename_length];
    } else {
        cp437 filename_cp437[filename_length];
    } uint8 extra[extra_length];
};
struct zip_central {
    uint16 version_made_by;
    uint16 version_needed;
    zip_flags flags;
    zip_method compression;
    dos_time modified_time;
    dos_date modified_date;
    uint32 crc32;
    uint32 compressed_size;
    uint32 uncompressed_size;
    uint16 filename_length;
    uint16 extra_length;
    uint16 comment_length;
    uint16 disk;
    uint16 internal_attributes;
    uint32 external_attributes;
    uint32 local_header_offset;
    if (utf8_names) {
        utf8 filename_utf8[filename_length];
    } else {
        cp437 filename_cp437[filename_length];
    } uint8 extra[extra_length];
    char comment[comment_length];
};
struct zip_directory_entry {
    uint32 entry_signature;
    if (entry_signature == 0x02014b50) {
        zip_central header;
    }
};
struct zip_directory {
    zip_directory_entry entries[total_entries];
};`,
    samples: [
      {
        id: "zip",
        extension: "zip",
        title: "ZIP - local file header",
        description: "ZIP archive",
        definition: `struct root {
    uint32 signature;
    uint16 version_needed;
    struct {
        uint16 encrypted : 1;
        uint16 compression_option : 2;
        uint16 has_data_descriptor : 1;
        uint16 reserved : 12;
    } general_purpose_flag;
    uint16 compression_method;
    uint16 last_mod_time;
    uint16 last_mod_date;
    uint32 crc_32;
    uint32 compressed_size;
    uint32 uncompressed_size;
    uint16 file_name_length;
    uint16 extra_field_length;
    char file_name[file_name_length];
    uint8 extra_field[extra_field_length];
};`,
        binaryHex:
          "50 4b 03 04 14 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 08 00 00 00 74 65 73 74 2e 74 78 74",
        rootType: "root",
        parserOptions: sampleParserOptions,
        documentation: {
          summary:
            "Reads the first ZIP local file header at byte 0, including its filename and raw extra fields. Files of any supported size are read on demand. This is not an archive extractor: empty archives, self-extracting prefixes, and split archives need a different starting layout. Data-descriptor entries can leave CRC/sizes unset here; ZIP64 sizes live in extra fields. Filename bytes are shown as raw characters, without UTF-8/CP437 decoding.",
        },
      },
    ],
  },
];
