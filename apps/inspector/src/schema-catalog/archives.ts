/**
 * Other archives, compressed streams, packages and disk images: gzip, 7-Zip, XZ, RAR, CAB, RPM, ISO 9660 and others.
 */

import type { FormatDefinition } from "./types";

export const formats: FormatDefinition[] = [
  {
    extensions: ["ar", "deb"],
    family: "Unix archive",
    scope:
      "Archive signature and first member header. Decimal and octal fields retain their on-disk text representation.",
    fields: `char signature[8];
char name[16];
char modified_time[12];
char owner[6];
char group[6];
char mode[8];
char size[10];
char terminator[2];`,
  },
  {
    extensions: ["gz", "tar.gz"],
    family: "gzip",
    scope: "Fixed gzip header. Optional fields and compressed content require further decoding.",
    fields: `uint8 signature[2];
uint8 compression;
uint8 text:1;
uint8 header_crc_present:1;
uint8 extra_present:1;
uint8 name_present:1;
uint8 comment_present:1;
uint8 reserved:3;
uint32 modified_time;
uint8 extra_flags;
uint8 operating_system;`,
  },
  {
    extensions: ["7z"],
    family: "7-Zip",
    scope:
      "Signature and start header locating the next header. Compressed headers are not decoded.",
    fields: `uint8 signature[6];
uint8 major_version;
uint8 minor_version;
uint32 start_header_crc;
uint64 next_header_offset;
uint64 next_header_size;
uint32 next_header_crc;`,
  },
  {
    extensions: ["xz"],
    family: "XZ",
    scope: "Stream header and CRC. Block compression is not decoded.",
    fields: `uint8 signature[6];
uint8 reserved;
uint8 check_type:4;
uint8 reserved_flags:4;
uint32 header_crc32;`,
  },
  {
    extensions: ["bz2"],
    family: "bzip2",
    scope: "Stream header and first block marker/CRC.",
    fields: `char signature[2];
char version[1];
char block_size[1];
uint8 first_block_marker[6];
uint32 block_crc;`,
    littleEndian: false,
  },
  {
    extensions: ["lz"],
    family: "lzip",
    scope: "lzip member header.",
    fields: `char signature[4];
uint8 version;
uint8 dictionary_size_code;`,
  },
  {
    extensions: ["Z"],
    family: "Unix compress",
    scope: "LZW signature and flags. Compressed codes remain encoded.",
    fields: `uint8 signature[2];
uint8 maximum_code_bits:5;
uint8 reserved:2;
uint8 block_mode:1;`,
  },
  {
    extensions: ["lz4"],
    family: "LZ4 frame",
    scope:
      "LZ4 standard frame descriptor, optional content size/dictionary ID and header checksum. Skippable frames expose their payload; compressed blocks remain encoded.",
    fields: `uint32 signature;
if (signature == 0x184d2204) {
    lz4_descriptor descriptor;
    if (content_size_present) {
        uint64 content_size;
    }
    if (dictionary_id_present) {
        uint32 dictionary_id;
    }
    uint8 header_checksum;
} else {
    if (signature >= 0x184d2a50 && signature <= 0x184d2a5f) {
        uint32 skippable_size;
        uint8 skippable_data[skippable_size];
    }
}`,
    types: `struct lz4_descriptor {
    uint8 dictionary_id_present:1;
    uint8 reserved_flag:1;
    uint8 content_checksum_present:1;
    uint8 content_size_present:1;
    uint8 block_checksum_present:1;
    uint8 independent_blocks:1;
    uint8 version:2;
    uint8 reserved_low:4;
    uint8 maximum_block_size_code:3;
    uint8 reserved_high:1;
};`,
  },
  {
    extensions: ["zst"],
    family: "Zstandard",
    scope:
      "Zstandard frame flags, optional window/dictionary ID and content-size variants. The 16-bit size stores actual size minus 256. Skippable payloads are exposed; compressed blocks remain encoded.",
    fields: `// The magic is the unsigned value 0xFD2FB528; a literal is its exact value, so the field is unsigned too.
uint32 signature;
if (signature == 0xfd2fb528) {
    zstd_descriptor descriptor;
    if (single_segment == 0) {
        zstd_window window;
    }
    switch (dictionary_id_size_code) {
        case 1: { uint8 dictionary_id8; }
        case 2: { uint16 dictionary_id16; }
        case 3: { uint32 dictionary_id32; }
    }
    switch (content_size_code) {
        case 0: {
            if (single_segment) { uint8 content_size8; }
        }
        case 1: { uint16 content_size_minus_256; }
        case 2: { uint32 content_size32; }
        case 3: { uint64 content_size64; }
    }
} else {
    if (signature >= 0x184d2a50 && signature <= 0x184d2a5f) {
        uint32 skippable_size;
        uint8 skippable_data[skippable_size];
    }
}`,
    types: `struct zstd_descriptor {
    uint8 dictionary_id_size_code:2;
    uint8 content_checksum_present:1;
    uint8 reserved:1;
    uint8 unused:1;
    uint8 single_segment:1;
    uint8 content_size_code:2;
};
struct zstd_window {
    uint8 window_mantissa:3;
    uint8 window_exponent:5;
};`,
  },
  {
    extensions: ["cab"],
    family: "Cabinet",
    scope:
      "Cabinet header and complete file table: paths, sizes, folder offsets, timestamps and attributes. Names support ASCII or flagged UTF-8; other legacy code pages are not supported. Folder data remains compressed.",
    fields: `char signature[4];
uint32 reserved_1;
uint32 cabinet_size;
uint32 reserved_2;
uint32 files_offset;
uint32 reserved_3;
uint8 minor_version;
uint8 major_version;
uint16 folder_count;
uint16 file_count;
uint16 flags;
uint16 set_id;
uint16 cabinet_index;
// The offset includes optional headers and folder records. Their sizes need not be guessed.
if (file_count > 0 && files_offset >= 36) {
    uint8 folder_and_optional_headers[files_offset - 36];
    cab_file files[file_count];
}`,
    types: `struct cab_file {
    uint32 uncompressed_size;
    uint32 offset_in_folder;
    uint16 folder_index;
    uint16 modified_date;
    uint16 modified_time;
    uint16 attributes;
    if (attributes & 0x80) {
        utf8_string_zero path_utf8;
    } else {
        cstring path_ascii;
    }
};`,
  },
  {
    extensions: ["rpm"],
    family: "RPM",
    scope: "96-byte package lead. Signature and payload headers are separate records.",
    fields: `uint32 signature;
uint8 major_version;
uint8 minor_version;
uint16 package_type;
uint16 architecture;
char name[66];
uint16 operating_system;
uint16 signature_type;
uint8 reserved[16];`,
    littleEndian: false,
  },
  {
    extensions: ["crx"],
    family: "Chrome extension",
    scope:
      "CRX native version switch decodes CRX2 key/signature lengths and bytes or the CRX3 signed-header bytes.",
    fields: `char signature[4];
uint32 version;
switch (version) {
    case 2: {
        uint32 public_key_length;
        uint32 signature_length;
        uint8 public_key[public_key_length];
        uint8 signature_bytes[signature_length];
    } case 3: {
        uint32 header_length;
        uint8 signed_header[header_length];
    } default: {
        uint32 unknown_header_length;
    }
}`,
  },
  {
    extensions: ["asar"],
    family: "Electron archive",
    scope: "ASAR pickle framing and byte-bounded UTF-8 JSON directory.",
    fields: `uint32 size_pickle_length;
uint32 header_pickle_length;
uint32 header_pickle_payload_length;
uint32 json_length;
utf8 json[json_length];`,
  },
  {
    extensions: ["iso"],
    family: "ISO 9660",
    scope:
      "First ISO 9660 volume descriptor. Primary descriptors expose volume/publisher IDs, block counts, path-table locations and the root directory record; boot descriptors expose boot identifiers. Directory contents are not traversed.",
    fields: `uint8 system_area[32768];
uint8 descriptor_type;
char standard_identifier[5];
uint8 descriptor_version;
if (descriptor_type == 1) {
    uint8 unused_1;
    char system_identifier[32];
    char volume_identifier[32];
    uint8 unused_2[8];
    uint32< volume_blocks_le;
    uint32> volume_blocks_be;
    uint8 unused_3[32];
    uint16< volume_set_size_le;
    uint16> volume_set_size_be;
    uint16< volume_sequence_le;
    uint16> volume_sequence_be;
    uint16< logical_block_size_le;
    uint16> logical_block_size_be;
    uint32< path_table_size_le;
    uint32> path_table_size_be;
    uint32< path_table_block_le;
    uint32< optional_path_table_block_le;
    uint32> path_table_block_be;
    uint32> optional_path_table_block_be;
    iso_root_directory root_directory;
    char volume_set_identifier[128];
    char publisher_identifier[128];
    char preparer_identifier[128];
    char application_identifier[128];
    char copyright_file[37];
    char abstract_file[37];
    char bibliographic_file[37];
    iso_timestamp created;
    iso_timestamp modified;
    iso_timestamp expires;
    iso_timestamp effective;
    uint8 file_structure_version;
} else {
    if (descriptor_type == 0) {
        char boot_system_identifier[32];
        char boot_identifier[32];
    }
}`,
    types: `struct iso_root_directory {
    uint8 record_length;
    uint8 extended_attribute_blocks;
    uint32< extent_block_le;
    uint32> extent_block_be;
    uint32< data_length_le;
    uint32> data_length_be;
    uint8 recording_date[7];
    uint8 flags;
    uint8 file_unit_size;
    uint8 interleave_gap;
    uint16< volume_sequence_le;
    uint16> volume_sequence_be;
    uint8 identifier_length;
    uint8 root_identifier;
};
struct iso_timestamp {
    char decimal_date_and_time[16];
    int8 timezone_quarters;
};`,
  },
  {
    extensions: ["arj"],
    family: "ARJ",
    scope: "First ARJ header block and CRC; compressed entries are not decoded.",
    fields: `uint16 signature;
uint16 basic_header_size;
uint8 basic_header[basic_header_size];
uint32 header_crc;`,
  },
  {
    extensions: ["cpio"],
    family: "CPIO",
    scope:
      "Binary CPIO entry header and counted name using the selected byte order. ASCII variants expose their six-byte signature only; ASCII sizes require text-to-integer conversion.",
    fields: `uint16 magic;
if (magic == 0x71c7) {
    uint16 device;
    uint16 inode;
    uint16 mode;
    uint16 uid;
    uint16 gid;
    uint16 link_count;
    uint16 rdevice;
    uint16 modified_time_words[2];
    uint16 name_size;
    uint16 file_size_words[2];
    char name[name_size];
} else {
    char remaining_signature[4];
}`,
  },
  {
    extensions: ["lzh"],
    family: "LHA/LZH",
    scope:
      "LHA first-entry header, level 0/1 counted path and CRC, or level 2/3 CRC and OS. Counted paths preserve raw character bytes; extended-header paths and compressed data remain encoded.",
    fields: `uint8 header_size;
uint8 header_checksum;
char method[5];
uint32 compressed_size;
uint32 original_size;
uint32 timestamp;
uint8 attribute;
uint8 header_level;
if (header_level == 0 || header_level == 1) {
    uint8 path_length;
    char path[path_length];
    uint16 data_crc;
    if (header_level == 1) {
        uint8 operating_system;
    }
} else {
    if (header_level == 2 || header_level == 3) {
        uint16 data_crc_level23;
        uint8 operating_system_level23;
    }
}`,
  },
  {
    extensions: ["ace"],
    family: "ACE",
    scope: "ACE archive header prefix.",
    fields: `uint16 header_crc;
uint16 header_size;
uint8 header_type;
uint16 header_flags;
char signature[7];
uint8 extraction_version;
uint8 creation_version;
uint8 host_os;
uint8 volume_number;
uint32 creation_time;`,
  },
  {
    extensions: ["rar"],
    family: "RAR",
    scope:
      "RAR4/RAR5 signature and first block. Main-header flags and volume metadata or RAR5 encryption parameters are exposed. File records and encrypted headers are not traversed.",
    fields: `uint8 signature[6];
uint8 format_version;
if (format_version == 0) {
    rar4_header main;
} else {
    if (format_version == 1) {
        uint8 signature_end;
        rar5_header first_block;
    }
}`,
    types: `struct rar4_header {
    uint16 header_crc;
    uint8 header_type;
    uint16 header_flags;
    uint16 header_size;
    if (header_type == 0x73) {
        uint16 reserved_1;
        uint32 reserved_2;
    }
};
struct rar5_header {
    uint32 header_crc;
    uleb128_32 header_size;
    uleb128_64 header_type;
    uleb128_64 header_flags;
    if (header_flags & 1) { uleb128_32 extra_size; }
    if (header_flags & 2) { uleb128_64 data_size; }
    switch (header_type) {
        case 1: {
            uleb128_64 archive_flags;
            if (archive_flags & 2) { uleb128_64 volume_number; }
            if (header_flags & 1) { uint8 extra[extra_size]; }
        }
        case 4: {
            uleb128_64 encryption_version;
            uleb128_64 encryption_flags;
            uint8 kdf_count;
            uint8 salt[16];
            if (encryption_flags & 1) { uint8 password_check[12]; }
        }
    }
};`,
  },
  {
    extensions: ["dmg"],
    family: "Apple disk image",
    scope:
      "Leading zlib framing recognized by file-type. The UDIF trailer and filesystem are not decoded.",
    fields: `uint8 compression_method_and_window;
uint8 compression_flags;`,
  },
];
