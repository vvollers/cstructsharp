/**
 * PE executables (EXE, and DLL through an alias), with the EXE and DLL teaching samples.
 */

import { sampleParserOptions, type FormatDefinition } from "./types";

// EXE and DLL samples differ in their bytes, but describe the same PE header structure.
const peSampleDefinition = `struct pe_header {
    char signature[4];
    uint16 machine;
    uint16 number_of_sections;
    uint32 time_date_stamp;
    uint32 pointer_to_symbol_table;
    uint32 number_of_symbols;
    uint16 size_of_optional_header;
    uint16 characteristics;
    uint16 optional_header_magic;
};

struct dos_header {
    uint16 e_magic;
    uint16 e_cblp;
    uint16 e_cp;
    uint16 e_crlc;
    uint16 e_cparhdr;
    uint16 e_minalloc;
    uint16 e_maxalloc;
    uint16 e_ss;
    uint16 e_sp;
    uint16 e_csum;
    uint16 e_ip;
    uint16 e_cs;
    uint16 e_lfarlc;
    uint16 e_ovno;
    uint16 e_res[4];
    uint16 e_oemid;
    uint16 e_oeminfo;
    uint16 e_res2[10];
    pe_header *e_lfanew;
};

struct root {
    dos_header dos;
};`;

export const formats: FormatDefinition[] = [
  {
    extensions: ["exe"],
    aliases: { dll: "exe" },
    family: "PE executable",
    scope:
      "DOS pointer to PE/COFF, native PE32/PE32+ optional-header branches, directory and section arrays. Section file offsets follow typed pointers to small payload previews, regardless of distance. RVAs remain numeric because they are not file offsets.",
    fields: `uint16 signature;
uint8 dos_fields[58];
pe_header *pe;`,
    types: `struct pe_directory {
    uint32 rva_or_file_offset;
    uint32 size;
};
struct pe_section_preview {
    if (raw_size >= 16) {
        uint8 first_bytes[16];
    } else {
        uint8 short_data[raw_size];
    }
};
struct pe_section {
    char name[8];
    uint32 virtual_size;
    uint32 virtual_address;
    uint32 raw_size;
    pe_section_preview *raw_data;
    uint32 relocations_offset;
    uint32 line_numbers_offset;
    uint16 relocation_count;
    uint16 line_number_count;
    uint32 characteristics;
};
struct pe_optional32 {
    uint8 linker_major;
    uint8 linker_minor;
    uint32 code_size;
    uint32 initialized_data_size;
    uint32 uninitialized_data_size;
    uint32 entry_point_rva;
    uint32 code_base_rva;
    uint32 data_base_rva;
    uint32 image_base;
    uint32 section_alignment;
    uint32 file_alignment;
    uint16 os_major;
    uint16 os_minor;
    uint16 image_major;
    uint16 image_minor;
    uint16 subsystem_major;
    uint16 subsystem_minor;
    uint32 win32_version;
    uint32 image_size;
    uint32 headers_size;
    uint32 checksum;
    uint16 subsystem;
    uint16 dll_characteristics;
    uint32 stack_reserve;
    uint32 stack_commit;
    uint32 heap_reserve;
    uint32 heap_commit;
    uint32 loader_flags;
    uint32 directory_count;
    if (directory_count <= (optional_header_size - 96) / 8) {
        pe_directory directories[directory_count];
        uint8 remaining[optional_header_size - 96 - directory_count * 8];
    } else {
        uint8 invalid_directories[optional_header_size - 96];
    }
};
struct pe_optional64 {
    uint8 linker_major;
    uint8 linker_minor;
    uint32 code_size;
    uint32 initialized_data_size;
    uint32 uninitialized_data_size;
    uint32 entry_point_rva;
    uint32 code_base_rva;
    uint64 image_base;
    uint32 section_alignment;
    uint32 file_alignment;
    uint16 os_major;
    uint16 os_minor;
    uint16 image_major;
    uint16 image_minor;
    uint16 subsystem_major;
    uint16 subsystem_minor;
    uint32 win32_version;
    uint32 image_size;
    uint32 headers_size;
    uint32 checksum;
    uint16 subsystem;
    uint16 dll_characteristics;
    uint64 stack_reserve;
    uint64 stack_commit;
    uint64 heap_reserve;
    uint64 heap_commit;
    uint32 loader_flags;
    uint32 directory_count;
    if (directory_count <= (optional_header_size - 112) / 8) {
        pe_directory directories[directory_count];
        uint8 remaining[optional_header_size - 112 - directory_count * 8];
    } else {
        uint8 invalid_directories[optional_header_size - 112];
    }
};
struct pe_header {
    uint32 pe_signature;
    if (pe_signature == 0x00004550) {
        uint16 machine;
        uint16 section_count;
        uint32 timestamp;
        uint32 symbol_table_offset;
        uint32 symbol_count;
        uint16 optional_header_size;
        uint16 characteristics;
        if (optional_header_size >= 2) {
            uint16 optional_magic;
            if (optional_magic == 0x10b && optional_header_size >= 96) {
                pe_optional32 pe32;
            } else {
                if (optional_magic == 0x20b && optional_header_size >= 112) {
                    pe_optional64 pe64;
                } else {
                    uint8 unknown_optional[optional_header_size - 2];
                }
            }
        } else {
            uint8 short_optional[optional_header_size];
        } pe_section sections[section_count];
    }
};`,
    samples: [
      {
        id: "pe-exe",
        extension: "exe",
        title: "EXE - PE image (DOS header + pointer)",
        description: "Executable",
        definition: peSampleDefinition,
        binaryHex:
          "4d 5a 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 40 00 00 00 50 45 00 00 64 86 01 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 22 00 0b 02",
        rootType: "root",
        parserOptions: { ...sampleParserOptions, pointerSize: 4, addressingMode: "Absolute" },
        documentation: {
          summary:
            "A minimal 90-byte PE image: a real 64-byte DOS header whose e_lfanew is a real CStruct pointer field (absolute addressing) dereferencing to a COFF file header + optional header magic - the flagship pointer-following showcase, using a real well-known offset (0x3C) from a real well-known format.",
        },
      },
      {
        id: "pe-dll",
        extension: "dll",
        title: "DLL - PE image (DOS header + pointer)",
        description: "Shared library",
        definition: peSampleDefinition,
        binaryHex:
          "4d 5a 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 40 00 00 00 50 45 00 00 64 86 01 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 22 20 0b 02",
        rootType: "root",
        parserOptions: { ...sampleParserOptions, pointerSize: 4, addressingMode: "Absolute" },
        documentation: {
          summary:
            "The same PE definition as the EXE example - a DLL is a PE file with the IMAGE_FILE_DLL characteristic bit (0x2000) set in its COFF header, the only byte that differs from the EXE sample.",
        },
      },
    ],
  },
];
