import type { SchemaProfile } from "./detected-schemas";

// These declarations are identical for every file. Only CStructSharp selects branches and lengths.
export const standaloneLayouts: Record<string, Partial<SchemaProfile>> = {
  exe: {
    scope:
      "DOS pointer to PE/COFF, native PE32/PE32+ optional-header branches, directory and section arrays. Section file offsets follow typed pointers to small payload previews, regardless of distance. RVAs remain numeric because they are not file offsets.",
    pointerSize: 4,
    fields: "uint16 signature; uint8 dos_fields[58]; pe_header *pe;",
    types: `
struct pe_directory { uint32 rva_or_file_offset; uint32 size; };
struct pe_section_preview { if (raw_size >= 16) { uint8 first_bytes[16]; } else { uint8 short_data[raw_size]; } };
struct pe_section { char name[8]; uint32 virtual_size; uint32 virtual_address; uint32 raw_size; pe_section_preview *raw_data; uint32 relocations_offset; uint32 line_numbers_offset; uint16 relocation_count; uint16 line_number_count; uint32 characteristics; };
struct pe_optional32 {
    uint8 linker_major; uint8 linker_minor; uint32 code_size; uint32 initialized_data_size; uint32 uninitialized_data_size; uint32 entry_point_rva; uint32 code_base_rva; uint32 data_base_rva; uint32 image_base; uint32 section_alignment; uint32 file_alignment; uint16 os_major; uint16 os_minor; uint16 image_major; uint16 image_minor; uint16 subsystem_major; uint16 subsystem_minor; uint32 win32_version; uint32 image_size; uint32 headers_size; uint32 checksum; uint16 subsystem; uint16 dll_characteristics; uint32 stack_reserve; uint32 stack_commit; uint32 heap_reserve; uint32 heap_commit; uint32 loader_flags; uint32 directory_count;
    if (directory_count <= (optional_header_size - 96) / 8) { pe_directory directories[directory_count]; uint8 remaining[optional_header_size - 96 - directory_count * 8]; } else { uint8 invalid_directories[optional_header_size - 96]; }
};
struct pe_optional64 {
    uint8 linker_major; uint8 linker_minor; uint32 code_size; uint32 initialized_data_size; uint32 uninitialized_data_size; uint32 entry_point_rva; uint32 code_base_rva; uint64 image_base; uint32 section_alignment; uint32 file_alignment; uint16 os_major; uint16 os_minor; uint16 image_major; uint16 image_minor; uint16 subsystem_major; uint16 subsystem_minor; uint32 win32_version; uint32 image_size; uint32 headers_size; uint32 checksum; uint16 subsystem; uint16 dll_characteristics; uint64 stack_reserve; uint64 stack_commit; uint64 heap_reserve; uint64 heap_commit; uint32 loader_flags; uint32 directory_count;
    if (directory_count <= (optional_header_size - 112) / 8) { pe_directory directories[directory_count]; uint8 remaining[optional_header_size - 112 - directory_count * 8]; } else { uint8 invalid_directories[optional_header_size - 112]; }
};
struct pe_header {
    uint32 pe_signature;
    if (pe_signature == 0x00004550) {
        uint16 machine; uint16 section_count; uint32 timestamp; uint32 symbol_table_offset; uint32 symbol_count; uint16 optional_header_size; uint16 characteristics;
        if (optional_header_size >= 2) {
            uint16 optional_magic;
            if (optional_magic == 0x10b && optional_header_size >= 96) { pe_optional32 pe32; }
            else { if (optional_magic == 0x20b && optional_header_size >= 112) { pe_optional64 pe64; } else { uint8 unknown_optional[optional_header_size - 2]; } }
        } else { uint8 short_optional[optional_header_size]; }
        pe_section sections[section_count];
    }
};`,
  },
  png: {
    scope:
      "Up to eight sequential chunks, selected by native if/switch statements. Includes image, palette, color, text and animation metadata. Encoded bodies are ordinary arrays; increase the read/array budgets when intentionally decoding large bodies.",
    littleEndian: false,
    fields: `uint8 signature[8]; png_chunk chunk_0;
      if (kind != 0x49454e44) { png_chunk chunk_1; }
      if (kind != 0x49454e44) { png_chunk chunk_2; }
      if (kind != 0x49454e44) { png_chunk chunk_3; }
      if (kind != 0x49454e44) { png_chunk chunk_4; }
      if (kind != 0x49454e44) { png_chunk chunk_5; }
      if (kind != 0x49454e44) { png_chunk chunk_6; }
      if (kind != 0x49454e44) { png_chunk chunk_7; }`,
    types: `
enum png_kind : uint32 { IHDR=0x49484452, PLTE=0x504c5445, IDAT=0x49444154, IEND=0x49454e44, gAMA=0x67414d41, cHRM=0x6348524d, pHYs=0x70485973, sRGB=0x73524742, tIME=0x74494d45, tEXt=0x74455874, acTL=0x6163544c, fcTL=0x6663544c, fdAT=0x66644154 };
enum png_color : uint8 { Grayscale=0, Truecolor=2, Indexed=3, GrayscaleAlpha=4, TruecolorAlpha=6 };
struct png_rgb { uint8 red; uint8 green; uint8 blue; };
struct png_chunk {
    uint32 length; png_kind kind;
    switch (kind) {
        case 0x49484452: { if (length == 13) { uint32 width; uint32 height; uint8 bit_depth; png_color color_type; uint8 compression; uint8 filter; uint8 interlace; } else { uint8 invalid_header[length]; } }
        case 0x504c5445: { if (length / 3 * 3 == length) { png_rgb colors[length / 3]; } else { uint8 invalid_palette[length]; } }
        case 0x67414d41: { if (length == 4) { uint32 gamma_times_100000; } else { uint8 invalid_gamma[length]; } }
        case 0x6348524d: { if (length == 32) { uint32 white_x; uint32 white_y; uint32 red_x; uint32 red_y; uint32 green_x; uint32 green_y; uint32 blue_x; uint32 blue_y; } else { uint8 invalid_chromaticity[length]; } }
        case 0x70485973: { if (length == 9) { uint32 pixels_per_unit_x; uint32 pixels_per_unit_y; uint8 unit; } else { uint8 invalid_resolution[length]; } }
        case 0x73524742: { if (length == 1) { uint8 rendering_intent; } else { uint8 invalid_srgb[length]; } }
        case 0x74494d45: { if (length == 7) { uint16 year; uint8 month; uint8 day; uint8 hour; uint8 minute; uint8 second; } else { uint8 invalid_time[length]; } }
        case 0x74455874: { latin1 keyword_and_text[length]; }
        case 0x6163544c: { if (length == 8) { uint32 frame_count; uint32 play_count; } else { uint8 invalid_animation[length]; } }
        case 0x6663544c: { if (length == 26) { uint32 sequence; uint32 frame_width; uint32 frame_height; uint32 x_offset; uint32 y_offset; uint16 delay_numerator; uint16 delay_denominator; uint8 dispose; uint8 blend; } else { uint8 invalid_frame[length]; } }
        case 0x66644154: { if (length >= 4) { uint32 sequence_number; uint8 encoded_frame[length - 4]; } else { uint8 invalid_frame_data[length]; } }
        default: { uint8 payload[length]; }
    }
    uint32 crc32;
};`,
  },
  jpg: {
    scope:
      "Up to eight JPEG segments before the first SOS/EOI. Native marker/length branches decode JFIF, frame/scan components, first quantization/Huffman tables and comments. Entropy-coded scans and vendor APP data are not searched or decoded.",
    littleEndian: false,
    fields: `uint16 start_of_image; jpeg_segment segment_0;
      if (marker != 0xffda && marker != 0xffd9) { jpeg_segment segment_1; }
      if (marker != 0xffda && marker != 0xffd9) { jpeg_segment segment_2; }
      if (marker != 0xffda && marker != 0xffd9) { jpeg_segment segment_3; }
      if (marker != 0xffda && marker != 0xffd9) { jpeg_segment segment_4; }
      if (marker != 0xffda && marker != 0xffd9) { jpeg_segment segment_5; }
      if (marker != 0xffda && marker != 0xffd9) { jpeg_segment segment_6; }
      if (marker != 0xffda && marker != 0xffd9) { jpeg_segment segment_7; }`,
    types: `
struct jpeg_component { uint8 id; uint8 vertical_sampling:4; uint8 horizontal_sampling:4; uint8 quantization_table; };
struct jpeg_scan_component { uint8 id; uint8 ac_table:4; uint8 dc_table:4; };
struct jpeg_approximation { uint8 low:4; uint8 high:4; };
struct jpeg_quantization_selector { uint8 table_id:4; uint8 precision:4; };
struct jpeg_frame { uint8 precision; uint16 height; uint16 width; uint8 component_count; jpeg_component components[component_count]; };
struct jpeg_segment {
    uint16 marker;
    if (marker != 0xffd8 && marker != 0xffd9 && marker != 0xff01 && (marker < 0xffd0 || marker > 0xffd7)) {
        uint16 segment_length;
        if (segment_length >= 2) {
            switch (marker) {
                case 0xffc0: { jpeg_frame baseline; }
                case 0xffc1: { jpeg_frame extended; }
                case 0xffc2: { jpeg_frame progressive; }
                case 0xffc3: { jpeg_frame lossless; }
                case 0xffda: { uint8 scan_component_count; jpeg_scan_component components[scan_component_count]; uint8 spectral_start; uint8 spectral_end; jpeg_approximation approximation; }
                case 0xffdd: { if (segment_length == 4) { uint16 restart_interval; } else { uint8 invalid_restart[segment_length - 2]; } }
                case 0xfffe: { char comment[segment_length - 2]; }
                case 0xffdb: { if (segment_length >= 3) { jpeg_quantization_selector selector; if (precision == 0 && segment_length >= 67) { uint8 coefficients[64]; uint8 additional_tables[segment_length - 67]; } else { if (precision == 1 && segment_length >= 131) { uint16 coefficients16[64]; uint8 additional_tables16[segment_length - 131]; } else { uint8 invalid_table[segment_length - 3]; } } } }
                case 0xffc4: { if (segment_length >= 19) { uint8 table_selector; uint8 code_counts[16]; uint8 symbols_and_tables[segment_length - 19]; } else { uint8 invalid_huffman[segment_length - 2]; } }
                case 0xffe0: { if (segment_length >= 7) { uint32 identifier; uint8 terminator; if (identifier == 0x4a464946 && terminator == 0 && segment_length >= 16) { uint8 major; uint8 minor; uint8 density_unit; uint16 x_density; uint16 y_density; uint8 thumbnail_width; uint8 thumbnail_height; uint8 thumbnail[segment_length - 16]; } else { uint8 application_data[segment_length - 7]; } } else { uint8 short_application[segment_length - 2]; } }
                default: { uint8 segment_data[segment_length - 2]; }
            }
        }
    }
};`,
  },
  zip: {
    scope:
      "Native signature branches decode a local header, central-directory record, ZIP64 end record or empty archive footer at the current root. Streaming entries stop at their header; no footer search or offset repair occurs outside the library.",
    pointerSize: 4,
    fields: `uint32 signature;
      switch (signature) {
        case 0x04034b50: { zip_local local; if (data_descriptor == 0 && compressed_size != 0xffffffff) { uint8 compressed_payload[compressed_size]; } }
        case 0x02014b50: { zip_central directory_entry; }
        case 0x06054b50: { uint16 disk; uint16 directory_disk; uint16 disk_entries; uint16 total_entries; uint32 directory_size; zip_directory *directory; uint16 comment_length; char comment[comment_length]; }
        case 0x06064b50: { uint64 record_size; uint16 made_by; uint16 needed; uint32 disk64; uint32 directory_disk64; uint64 disk_entries64; uint64 total_entries64; uint64 directory_size64; uint64 directory_offset64; }
      }`,
    types: `
struct zip_flags { uint16 encrypted:1; uint16 compression_options:2; uint16 data_descriptor:1; uint16 reserved_a:2; uint16 strong_encryption:1; uint16 reserved_b:4; uint16 utf8_names:1; uint16 reserved_c:1; uint16 masked_header:1; uint16 reserved_d:2; };
enum zip_method : uint16 { Stored=0, Deflate=8, Deflate64=9, Bzip2=12, Lzma=14, Zstandard=93, Xz=95, Ppmd=98, Aes=99 };
struct dos_time { uint16 seconds_divided_by_two:5; uint16 minutes:6; uint16 hours:5; };
struct dos_date { uint16 day:5; uint16 month:4; uint16 years_since_1980:7; };
struct zip_local {
    uint16 version_needed; zip_flags flags; zip_method compression; dos_time modified_time; dos_date modified_date;
    uint32 crc32; uint32 compressed_size; uint32 uncompressed_size; uint16 filename_length; uint16 extra_length;
    if (utf8_names) { utf8 filename_utf8[filename_length]; } else { cp437 filename_cp437[filename_length]; }
    uint8 extra[extra_length];
};


struct zip_central {
    uint16 version_made_by; uint16 version_needed; zip_flags flags; zip_method compression; dos_time modified_time; dos_date modified_date;
    uint32 crc32; uint32 compressed_size; uint32 uncompressed_size; uint16 filename_length; uint16 extra_length; uint16 comment_length; uint16 disk; uint16 internal_attributes; uint32 external_attributes; uint32 local_header_offset;
    if (utf8_names) { utf8 filename_utf8[filename_length]; } else { cp437 filename_cp437[filename_length]; }
    uint8 extra[extra_length]; char comment[comment_length];
};
struct zip_directory_entry { uint32 entry_signature; if (entry_signature == 0x02014b50) { zip_central header; } };
struct zip_directory { zip_directory_entry entries[total_entries]; };`,
  },
  pdf: {
    scope:
      "PDF signature and version. PDF dictionaries, decimal offsets and compressed cross-reference streams require a text/container parser; they are not binary C fields.",
    fields: "char signature[5]; char version[3];",
    types: "",
    coverage: "prefix",
  },
};

// Build a fixed declaration for each storage combination, never a file-dependent schema.
const elfHeaders = (word: string, suffix: string) =>
  `uint16${suffix} object_type; uint16${suffix} machine; uint32${suffix} version; ${word}${suffix} entry_point; ${word}${suffix} program_headers_offset; ${word}${suffix} section_headers_offset; uint32${suffix} flags; uint16${suffix} header_size; uint16${suffix} program_header_size; uint16${suffix} program_header_count; uint16${suffix} section_header_size; uint16${suffix} section_header_count; uint16${suffix} section_names_index;`;
standaloneLayouts.elf = {
  scope:
    "ELF32/ELF64 headers use native class and byte-order conditions. Table offsets remain numeric: their widths and later counts cannot be inferred by preprocessing. For a known ABI, define typed table pointers with its fixed pointer width.",
  fields: `char magic[4]; uint8 file_class; uint8 byte_order; uint8 identification_version; uint8 os_abi; uint8 abi_version; uint8 padding[7];
    if (byte_order == 1) { if (file_class == 1) { elf32_le header32_le; } else { if (file_class == 2) { elf64_le header64_le; } } }
    else { if (byte_order == 2) { if (file_class == 1) { elf32_be header32_be; } else { if (file_class == 2) { elf64_be header64_be; } } } }`,
  types: `struct elf32_le { ${elfHeaders("uint32", "<")} }; struct elf64_le { ${elfHeaders("uint64", "<")} }; struct elf32_be { ${elfHeaders("uint32", ">")} }; struct elf64_be { ${elfHeaders("uint64", ">")} };`,
};

standaloneLayouts.tif = {
  scope:
    "Classic TIFF follows typed IFD links using the selected byte order and 4-byte pointer setting. BigTIFF header offsets remain numeric. Choose the byte order in settings; no file bytes are consulted to change parser settings.",
  pointerSize: 4,
  fields:
    "char byte_order[2]; uint16 version; if (version == 42) { ifd *first_directory; } else { if (version == 43) { uint16 offset_size; uint16 reserved; uint64 first_directory_offset; } }",
  types:
    "enum tiff_tag : uint16 { ImageWidth=256, ImageLength=257, BitsPerSample=258, Compression=259, PhotometricInterpretation=262, StripOffsets=273, SamplesPerPixel=277, RowsPerStrip=278, StripByteCounts=279, XResolution=282, YResolution=283, ExifIFD=34665, GPSIFD=34853 }; enum tiff_type : uint16 { Byte=1, Ascii=2, Short=3, Long=4, Rational=5, SByte=6, Undefined=7, SShort=8, SLong=9, SRational=10, Float=11, Double=12 }; struct tag { tiff_tag id; tiff_type data_type; uint32 count; uint32 value_or_offset; }; struct ifd { uint16 entry_count; tag entries[entry_count]; ifd *next_directory; };",
};

standaloneLayouts.stl = {
  scope:
    "Binary STL header and all declared triangles, with float normals and vertex arrays. The ordinary array/read budgets apply to decoded data, not file length.",
  fields: "char header[80]; uint32 triangle_count; stl_triangle triangles[triangle_count];",
  types:
    "struct stl_triangle { float normal[3]; float vertices[3][3]; uint16 attribute_byte_count; };",
};

standaloneLayouts.gif = {
  scope:
    "Logical screen fields, packed flags and the runtime-sized global color palette. Image/extension streams are not scanned outside the schema.",
  fields:
    "char signature[3]; char version[3]; uint16 width; uint16 height; uint8 palette_size_code:3; uint8 sorted:1; uint8 color_resolution_minus_one:3; uint8 global_palette_present:1; uint8 background_color_index; uint8 pixel_aspect_ratio; if (global_palette_present) { gif_rgb global_palette[1 << (palette_size_code + 1)]; }",
  types: "struct gif_rgb { uint8 red; uint8 green; uint8 blue; };",
};

standaloneLayouts.glb = {
  scope:
    "GLB 2 header and its first chunk, with native JSON/binary alternatives. JSON is decoded as UTF-8 text; buffer bytes stay opaque.",
  fields:
    "char signature[4]; uint32 version; uint32 total_length; if (version == 2 && total_length >= 20) { glb_chunk chunk_0; }",
  types:
    "enum glb_chunk_kind : uint32 { Json=1313821514, Binary=5130562 }; struct glb_chunk { uint32 length; glb_chunk_kind type; if (type == 1313821514) { utf8 json_data[length]; } else { uint8 binary_data[length]; } };",
};

standaloneLayouts.fbx = {
  scope:
    "Binary FBX header and first node identifier. Native version branches select 32-bit or 64-bit node fields. Properties and child nodes remain undecoded.",
  fields: "uint8 signature[23]; uint32 version; fbx_node first_node;",
  types:
    "struct fbx_node { if (version >= 7500) { uint64 end_offset64; uint64 property_count64; uint64 property_bytes64; } else { uint32 end_offset32; uint32 property_count32; uint32 property_bytes32; } uint8 name_length; char name[name_length]; };",
};

standaloneLayouts.bmp = {
  scope:
    "BMP file header and native DIB size branches: CORE, INFO, V2/V3, V4/V5 masks and calibrated RGB fixed-point fields. Pixels remain undecoded.",
  fields: `char signature[2]; uint32 file_size; uint16 reserved_1; uint16 reserved_2; uint32 pixel_offset; uint32 dib_header_size;
    if (dib_header_size == 12) { uint16 core_width; uint16 core_height; uint16 core_planes; uint16 core_bits_per_pixel; }
    else { if (dib_header_size >= 40) {
      int32 width; int32 height; uint16 planes; uint16 bits_per_pixel; uint32 compression; uint32 image_size; int32 pixels_per_meter_x; int32 pixels_per_meter_y; uint32 colors_used; uint32 important_colors;
      if (dib_header_size >= 52) { uint32 red_mask; uint32 green_mask; uint32 blue_mask; }
      if (dib_header_size >= 56) { uint32 alpha_mask; }
      if (dib_header_size >= 108) { uint32 color_space; if (color_space == 0) { fixed2_30 endpoints_xyz[3][3]; ufixed16_16 gamma_red; ufixed16_16 gamma_green; ufixed16_16 gamma_blue; } else { uint8 unused_color_calibration[48]; } }
      if (dib_header_size >= 124) { uint32 rendering_intent; uint32 profile_offset; uint32 profile_size; uint32 reserved; }
    } }`,
  types: "",
};

standaloneLayouts.cpio = {
  scope:
    "Binary CPIO entry header and counted name using the selected byte order. ASCII variants expose their six-byte signature only; ASCII sizes require text-to-integer conversion.",
  fields:
    "uint16 magic; if (magic == 0x71c7) { uint16 device; uint16 inode; uint16 mode; uint16 uid; uint16 gid; uint16 link_count; uint16 rdevice; uint16 modified_time_words[2]; uint16 name_size; uint16 file_size_words[2]; char name[name_size]; } else { char remaining_signature[4]; }",
  types: "",
};
