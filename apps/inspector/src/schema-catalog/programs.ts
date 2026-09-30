/**
 * Other executables and program images: ELF, Mach-O, WebAssembly, Java classes, Flash and NES ROMs.
 */

import type { FormatDefinition } from "./types";

export const formats: FormatDefinition[] = [
  {
    extensions: ["wasm"],
    family: "WebAssembly",
    scope:
      "Module magic and version. Section lengths and instructions use LEB128 and are not decoded by this fixed header.",
    fields: `uint8 signature[4];
uint32 version;`,
  },
  {
    extensions: ["class"],
    family: "Java class",
    scope:
      "Class-file version and constant pool count. Tagged constant pool entries are not decoded.",
    fields: `uint32 signature;
uint16 minor_version;
uint16 major_version;
uint16 constant_pool_count;`,
    littleEndian: false,
  },
  {
    extensions: ["nes"],
    family: "NES ROM",
    scope:
      "16-byte iNES/NES 2.0 header. Flags must be interpreted according to the header version.",
    fields: `char signature[4];
uint8 program_banks;
uint8 character_banks;
uint8 flags_6;
uint8 flags_7;
uint8 flags_8;
uint8 flags_9;
uint8 flags_10;
uint8 remaining_header[5];`,
  },
  {
    extensions: ["swf"],
    family: "Flash",
    scope: "SWF header. FWS, CWS and ZWS differ in body compression.",
    fields: `char signature[3];
uint8 version;
uint32 uncompressed_length;`,
  },
  {
    extensions: ["elf"],
    family: "ELF",
    scope:
      "ELF32/ELF64 headers use native class and byte-order conditions. Table offsets remain numeric: their widths and later counts cannot be inferred by preprocessing. For a known ABI, define typed table pointers with its fixed pointer width.",
    fields: `char magic[4];
uint8 file_class;
uint8 byte_order;
uint8 identification_version;
uint8 os_abi;
uint8 abi_version;
uint8 padding[7];
if (byte_order == 1) {
    if (file_class == 1) {
        elf32_le header32_le;
    } else {
        if (file_class == 2) {
            elf64_le header64_le;
        }
    }
} else {
    if (byte_order == 2) {
        if (file_class == 1) {
            elf32_be header32_be;
        } else {
            if (file_class == 2) {
                elf64_be header64_be;
            }
        }
    }
}`,
    types: `struct elf32_le {
    uint16< object_type;
    uint16< machine;
    uint32< version;
    uint32< entry_point;
    uint32< program_headers_offset;
    uint32< section_headers_offset;
    uint32< flags;
    uint16< header_size;
    uint16< program_header_size;
    uint16< program_header_count;
    uint16< section_header_size;
    uint16< section_header_count;
    uint16< section_names_index;
};
struct elf64_le {
    uint16< object_type;
    uint16< machine;
    uint32< version;
    uint64< entry_point;
    uint64< program_headers_offset;
    uint64< section_headers_offset;
    uint32< flags;
    uint16< header_size;
    uint16< program_header_size;
    uint16< program_header_count;
    uint16< section_header_size;
    uint16< section_header_count;
    uint16< section_names_index;
};
struct elf32_be {
    uint16> object_type;
    uint16> machine;
    uint32> version;
    uint32> entry_point;
    uint32> program_headers_offset;
    uint32> section_headers_offset;
    uint32> flags;
    uint16> header_size;
    uint16> program_header_size;
    uint16> program_header_count;
    uint16> section_header_size;
    uint16> section_header_count;
    uint16> section_names_index;
};
struct elf64_be {
    uint16> object_type;
    uint16> machine;
    uint32> version;
    uint64> entry_point;
    uint64> program_headers_offset;
    uint64> section_headers_offset;
    uint32> flags;
    uint16> header_size;
    uint16> program_header_size;
    uint16> program_header_count;
    uint16> section_header_size;
    uint16> section_header_count;
    uint16> section_names_index;
};`,
  },
  {
    extensions: ["macho"],
    family: "Mach-O",
    scope:
      "Mach-O magic and fixed 32-bit header fields using the selected byte order. Universal-binary directories and 64-bit extensions are not decoded.",
    fields: `uint32 signature;
uint32 cpu_type;
uint32 cpu_subtype;
uint32 file_type;
uint32 command_count;
uint32 commands_size;
uint32 flags;`,
  },
];
