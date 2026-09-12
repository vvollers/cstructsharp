import { supportedExtensions } from "file-type";
import type { FormatExample } from "./formats";
import { formats } from "./formats";
import { expandSchema, expansionReviews } from "./schema-expansions";

export interface SchemaProfile {
  family: string;
  scope: string;
  fields: string;
  types?: string;
  littleEndian?: boolean;
  pointerSize?: number;
  coverage: "structure" | "prefix";
}

// Explicit extension registrations keep upgrades from silently claiming new schema support.
export const schemaProfiles: Record<string, SchemaProfile> = {};
function register(
  extensions: string,
  family: string,
  fields: string,
  scope: string,
  options: Partial<SchemaProfile> = {},
) {
  for (const ext of extensions.split(" ")) {
    if (schemaProfiles[ext]) throw new Error(`Duplicate schema: ${ext}`);
    schemaProfiles[ext] = { family, fields, scope, coverage: "structure", ...options };
  }
}

register(
  "zip epub xpi docx pptx xlsx odt ods odp 3mf vsdx apk potx xltx dotx xltm ott ots otp odg otg xlsm docm dotm potm pptm jar ppsm ppsx key numbers pages",
  "ZIP",
  `uint32 signature; uint16 version_needed; uint16 flags; uint16 compression;
  uint16 modified_time; uint16 modified_date; uint32 crc32; uint32 compressed_size;
  uint32 uncompressed_size; uint16 filename_length; uint16 extra_length;
  char filename[filename_length]; uint8 extra[extra_length];`,
  "First local entry header, filename and extra fields. Container members remain compressed. Data descriptors and ZIP64 sizes require interpreting the extra records.",
);
register(
  "mp4 m4a m4v m4p m4b f4v f4p f4b f4a 3gp 3g2 mov heic avif cr3",
  "ISO base media",
  "uint32 box_size; char box_type[4];",
  "First box header. Extended-size boxes and brand records are selected from the loaded bytes; media samples remain encoded.",
  { littleEndian: false },
);
register(
  "jp2 jpm jpx mj2",
  "JPEG 2000 boxes",
  "uint32 signature_box_size; char signature_box_type[4]; uint32 signature; uint32 next_box_size; char next_box_type[4];",
  "JPEG 2000 signature and next box header. Codestream decoding is outside this layout.",
  { littleEndian: false },
);
register(
  "wav avi webp qcp",
  "RIFF",
  "char signature[4]; uint32 file_size_minus_8; char form_type[4]; char first_chunk_type[4]; uint32 first_chunk_size;",
  "RIFF form and first chunk header. WAVE fmt and WebP VP8X fields are expanded when present.",
);
register(
  "oga ogg ogv opus spx ogm ogx",
  "Ogg",
  `char signature[4]; uint8 version; uint8 header_flags; uint64 granule_position;
  uint32 stream_serial; uint32 page_sequence; uint32 checksum; uint8 segment_count;
  uint8 segment_sizes[segment_count];`,
  "First Ogg page header and lacing table. Codec packets remain encoded.",
);
register(
  "tif cr2 arw dng nef orf rw2",
  "TIFF",
  "char byte_order[2]; uint16 version; ifd *first_directory;",
  "Classic TIFF header and first image directory, including each tag's type, count and inline value or offset. BigTIFF uses its 64-bit directory layout.",
  {
    pointerSize: 4,
    types:
      "struct tag { uint16 id; uint16 data_type; uint32 count; uint32 value_or_offset; }; struct ifd { uint16 entry_count; tag entries[entry_count]; uint32 next_directory; };",
  },
);
register(
  "ttf otf",
  "SFNT",
  "uint32 scaler_type; uint16 table_count; uint16 search_range; uint16 entry_selector; uint16 range_shift; table_record tables[table_count];",
  "Font offset table and complete table directory. Glyph and shaping table payloads remain at the recorded offsets.",
  {
    littleEndian: false,
    types: "struct table_record { char tag[4]; uint32 checksum; uint32 offset; uint32 length; };",
  },
);
register(
  "woff",
  "WOFF",
  `char signature[4]; uint32 flavor; uint32 length; uint16 table_count; uint16 reserved;
  uint32 total_sfnt_size; uint16 major_version; uint16 minor_version; uint32 metadata_offset;
  uint32 metadata_length; uint32 metadata_original_length; uint32 private_offset; uint32 private_length;
  table_record tables[table_count];`,
  "WOFF header and table directory. Table decompression is not performed.",
  {
    littleEndian: false,
    types:
      "struct table_record { char tag[4]; uint32 offset; uint32 compressed_length; uint32 original_length; uint32 original_checksum; };",
  },
);
register(
  "woff2",
  "WOFF2",
  `char signature[4]; uint32 flavor; uint32 length; uint16 table_count; uint16 reserved;
  uint32 total_sfnt_size; uint32 total_compressed_size; uint16 major_version; uint16 minor_version;
  uint32 metadata_offset; uint32 metadata_length; uint32 metadata_original_length; uint32 private_offset; uint32 private_length;`,
  "WOFF2 fixed header. Variable-length table directory and Brotli data are not decoded.",
  { littleEndian: false },
);
register(
  "ttc",
  "TrueType collection",
  "char signature[4]; uint32 version; uint32 font_count; uint32 font_offsets[font_count];",
  "Collection version and font offset array.",
  { littleEndian: false },
);
register(
  "ar deb",
  "Unix archive",
  `char signature[8]; char name[16]; char modified_time[12]; char owner[6]; char group[6]; char mode[8]; char size[10]; char terminator[2];`,
  "Archive signature and first member header. Decimal and octal fields retain their on-disk text representation.",
);
register(
  "gz tar.gz",
  "gzip",
  "uint8 signature[2]; uint8 compression; uint8 flags; uint32 modified_time; uint8 extra_flags; uint8 operating_system;",
  "Fixed gzip header. Optional fields and compressed content require further decoding.",
);
register(
  "png apng",
  "PNG",
  "uint8 signature[8]; uint32 chunk_length; char chunk_type[4]; uint32 width; uint32 height; uint8 bit_depth; uint8 color_type; uint8 compression; uint8 filter; uint8 interlace; uint32 crc32;",
  "PNG signature and IHDR dimensions, pixel format and CRC. APNG animation and compressed image chunks are not decoded.",
  { littleEndian: false },
);
register(
  "ico cur",
  "Windows icon directory",
  "uint16 reserved; uint16 kind; uint16 image_count; icon_entry images[image_count];",
  "Complete image directory. CUR uses hotspot coordinates where ICO stores planes and bit depth. Image payloads remain at the recorded offsets.",
  {
    types:
      "struct icon_entry { uint8 width; uint8 height; uint8 color_count; uint8 reserved; uint16 planes_or_hotspot_x; uint16 bit_depth_or_hotspot_y; uint32 image_size; uint32 image_offset; };",
  },
);
register(
  "jpg jls",
  "JPEG",
  "uint16 start_of_image; uint16 first_marker;",
  "Start-of-image and first marker. JPEG segment layouts vary; the first length-bearing segment is exposed without assuming JFIF.",
  { littleEndian: false },
);
register(
  "gif",
  "GIF",
  "char signature[3]; char version[3]; uint16 width; uint16 height; uint8 packed_flags; uint8 background_color_index; uint8 pixel_aspect_ratio;",
  "Logical screen descriptor and, when present, the global RGB color table.",
);
register(
  "bmp",
  "BMP",
  "char signature[2]; uint32 file_size; uint16 reserved_1; uint16 reserved_2; uint32 pixel_offset; uint32 dib_header_size;",
  "Bitmap file header and version-selected DIB dimensions and pixel format.",
);
register(
  "psd",
  "Photoshop",
  "char signature[4]; uint16 version; uint8 reserved[6]; uint16 channels; uint32 height; uint32 width; uint16 depth; uint16 color_mode;",
  "Photoshop/large-document image header. Layer and image payloads are not decoded.",
  { littleEndian: false },
);
register(
  "icns",
  "Apple icon",
  "char signature[4]; uint32 file_length; char first_element_type[4]; uint32 first_element_length;",
  "Icon container and first element header.",
  { littleEndian: false },
);
register(
  "flac",
  "FLAC",
  "char signature[4]; uint8 metadata_flags; uint24> metadata_length_be; uint16 minimum_block_size; uint16 maximum_block_size; uint24> minimum_frame_size_be; uint24> maximum_frame_size_be; uint64 stream_parameters; uint8 md5[16];",
  "STREAMINFO metadata, including packed sample rate/channel/sample count bits and MD5. Audio frames remain encoded.",
  { littleEndian: false },
);
register(
  "mid",
  "MIDI",
  "char signature[4]; uint32 header_size; uint16 format; uint16 track_count; uint16 division;",
  "MIDI header, track count and timing division. Variable-length track events are not decoded.",
  { littleEndian: false },
);
register(
  "aif",
  "AIFF",
  "char signature[4]; uint32 form_size; char form_type[4]; char first_chunk_type[4]; uint32 first_chunk_size;",
  "AIFF/AIFC form and first chunk header.",
  { littleEndian: false },
);
register(
  "wasm",
  "WebAssembly",
  "uint8 signature[4]; uint32 version;",
  "Module magic and version. Section lengths and instructions use LEB128 and are not decoded by this fixed header.",
);
register(
  "sqlite",
  "SQLite",
  `char signature[16]; uint16 page_size; uint8 write_version; uint8 read_version; uint8 reserved_per_page;
  uint8 maximum_payload_fraction; uint8 minimum_payload_fraction; uint8 leaf_payload_fraction;
  uint32 change_counter; uint32 page_count; uint32 first_freelist_page; uint32 freelist_page_count;
  uint32 schema_cookie; uint32 schema_format; uint32 default_cache_size; uint32 largest_root_page;
  uint32 text_encoding; uint32 user_version; uint32 incremental_vacuum; uint32 application_id;
  uint8 reserved[20]; uint32 version_valid_for; uint32 sqlite_version;`,
  "Complete 100-byte database header. B-tree pages and SQL records are not decoded.",
  { littleEndian: false },
);
register(
  "7z",
  "7-Zip",
  "uint8 signature[6]; uint8 major_version; uint8 minor_version; uint32 start_header_crc; uint64 next_header_offset; uint64 next_header_size; uint32 next_header_crc;",
  "Signature and start header locating the next header. Compressed headers are not decoded.",
);
register(
  "xz",
  "XZ",
  "uint8 signature[6]; uint8 stream_flags[2]; uint32 header_crc32;",
  "Stream header and CRC. Block compression is not decoded.",
);
register(
  "bz2",
  "bzip2",
  "char signature[2]; char version[1]; char block_size[1]; uint8 first_block_marker[6]; uint32 block_crc;",
  "Stream header and first block marker/CRC.",
  { littleEndian: false },
);
register(
  "lz",
  "lzip",
  "char signature[4]; uint8 version; uint8 dictionary_size_code;",
  "lzip member header.",
);
register(
  "Z",
  "Unix compress",
  "uint8 signature[2]; uint8 flags;",
  "LZW signature and flags. Compressed codes remain encoded.",
);
register(
  "lz4",
  "LZ4 frame",
  "uint32 signature; uint8 flags; uint8 block_descriptor;",
  "Frame magic, flags and block-size code. Optional frame fields are not decoded.",
);
register(
  "zst",
  "Zstandard",
  "uint32 signature; uint8 frame_descriptor;",
  "Frame magic and descriptor. Frame variants and compressed blocks are not decoded.",
);
register(
  "glb",
  "glTF binary",
  "char signature[4]; uint32 version; uint32 total_length; uint32 first_chunk_length; uint32 first_chunk_type;",
  "glTF 2 container and first chunk header. JSON and buffer contents remain encoded.",
);
register(
  "class",
  "Java class",
  "uint32 signature; uint16 minor_version; uint16 major_version; uint16 constant_pool_count;",
  "Class-file version and constant pool count. Tagged constant pool entries are not decoded.",
  { littleEndian: false },
);
register(
  "nes",
  "NES ROM",
  "char signature[4]; uint8 program_banks; uint8 character_banks; uint8 flags_6; uint8 flags_7; uint8 flags_8; uint8 flags_9; uint8 flags_10; uint8 remaining_header[5];",
  "16-byte iNES/NES 2.0 header. Flags must be interpreted according to the header version.",
);
register(
  "pcap",
  "Packet capture",
  "uint32 signature; uint16 major_version; uint16 minor_version; int32 timezone; uint32 accuracy; uint32 snapshot_length; uint32 link_type;",
  "Classic PCAP global header with detected byte order. Packet records are not decoded.",
);
register(
  "lnk",
  "Shell link",
  "uint32 header_size; guid class_id; uint32 link_flags; uint32 file_attributes; uint64 creation_time; uint64 access_time; uint64 write_time; uint32 file_size; int32 icon_index; uint32 show_command; uint16 hotkey; uint16 reserved_1; uint32 reserved_2; uint32 reserved_3;",
  "Complete Shell Link header. Flag-dependent target lists, paths and extra blocks are not decoded.",
);
register(
  "cfb",
  "Compound file",
  "uint8 signature[8]; guid class_id; uint16 minor_version; uint16 major_version; uint16 byte_order; uint16 sector_shift; uint16 mini_sector_shift; uint8 reserved[6]; uint32 directory_sector_count; uint32 fat_sector_count; uint32 first_directory_sector; uint32 transaction_signature; uint32 mini_stream_cutoff; uint32 first_mini_fat_sector; uint32 mini_fat_sector_count; uint32 first_difat_sector; uint32 difat_sector_count; uint32 difat[109];",
  "Compound-file header and initial DIFAT. Sector chains and embedded Office documents are not followed.",
);
register(
  "cab",
  "Cabinet",
  "char signature[4]; uint32 reserved_1; uint32 cabinet_size; uint32 reserved_2; uint32 files_offset; uint32 reserved_3; uint8 minor_version; uint8 major_version; uint16 folder_count; uint16 file_count; uint16 flags; uint16 set_id; uint16 cabinet_index;",
  "Cabinet fixed header and folder/file counts. Optional reserved fields and compressed folders are not decoded.",
);
register(
  "rpm",
  "RPM",
  "uint32 signature; uint8 major_version; uint8 minor_version; uint16 package_type; uint16 architecture; char name[66]; uint16 operating_system; uint16 signature_type; uint8 reserved[16];",
  "96-byte package lead. Signature and payload headers are separate records.",
  { littleEndian: false },
);
register(
  "flv",
  "Flash video",
  "char signature[3]; uint8 version; uint8 flags; uint32 data_offset;",
  "FLV header. Audio/video tags are not decoded.",
  { littleEndian: false },
);
register(
  "swf",
  "Flash",
  "char signature[3]; uint8 version; uint32 uncompressed_length;",
  "SWF header. FWS, CWS and ZWS differ in body compression.",
);
register(
  "crx",
  "Chrome extension",
  "char signature[4]; uint32 version; uint32 header_length_or_public_key_length;",
  "CRX version and first length field. CRX2 and CRX3 use different signing headers.",
);
register(
  "asf",
  "ASF",
  "guid object_id; uint64 object_size; uint32 child_count; uint8 reserved[2];",
  "ASF header object and child object count.",
);
register(
  "dsf",
  "DSD stream",
  "char signature[4]; uint64 chunk_size; uint64 file_size; uint64 metadata_offset;",
  "DSF main chunk and metadata pointer.",
);
register(
  "wv",
  "WavPack",
  "char signature[4]; uint32 block_size; uint16 version; uint8 track; uint8 index; uint32 total_samples; uint32 block_index; uint32 block_samples; uint32 flags; uint32 crc;",
  "WavPack block header. Encoded samples are not decoded.",
);
register(
  "ape",
  "Monkey's Audio",
  "char signature[4]; uint16 version;",
  "Magic and version. Descriptor layout changes between codec versions.",
);
register(
  "voc",
  "Creative Voice",
  "char signature[20]; uint16 header_size; uint16 version; uint16 version_checksum;",
  "Creative Voice fixed header.",
);
register(
  "shp",
  "Shapefile",
  "uint32> file_code; uint32> unused[5]; uint32> file_length_words; uint32< version; uint32< shape_type; float64< x_min; float64< y_min; float64< x_max; float64< y_max; float64< z_min; float64< z_max; float64< m_min; float64< m_max;",
  "Complete 100-byte shapefile header and bounding ranges.",
);
register(
  "icc",
  "ICC profile",
  "uint32 profile_size; char cmm[4]; uint32 version; char profile_class[4]; char color_space[4]; char connection_space[4]; uint16 created[6]; char signature[4]; char platform[4]; uint32 flags; char manufacturer[4]; char model[4]; uint64 attributes; uint32 rendering_intent; fixed16_16 illuminant_xyz[3]; char creator[4]; uint8 profile_id[16]; uint8 reserved[28]; uint32 tag_count; tag tags[tag_count];",
  "ICC header and tag directory. XYZ values use signed 16.16 fixed-point encoding.",
  { littleEndian: false, types: "struct tag { char signature[4]; uint32 offset; uint32 size; };" },
);
register(
  "it",
  "Impulse Tracker",
  "char signature[4]; char song_name[26]; uint16 highlight; uint16 order_count; uint16 instrument_count; uint16 sample_count; uint16 pattern_count; uint16 created_version; uint16 compatible_version; uint16 flags; uint16 special; uint8 global_volume; uint8 mix_volume; uint8 speed; uint8 tempo; uint8 separation; uint8 pitch_depth; uint16 message_length; uint32 message_offset; uint32 reserved; uint8 channel_pan[64]; uint8 channel_volume[64]; uint8 orders[order_count]; uint32 instrument_offsets[instrument_count]; uint32 sample_offsets[sample_count]; uint32 pattern_offsets[pattern_count];",
  "Module header, channel defaults, orders and instrument/sample/pattern directories.",
);
register(
  "xm",
  "FastTracker",
  "char signature[17]; char module_name[20]; uint8 marker; char tracker_name[20]; uint16 version; uint32 header_size; uint16 song_length; uint16 restart_position; uint16 channel_count; uint16 pattern_count; uint16 instrument_count; uint16 flags; uint16 tempo; uint16 bpm; uint8 orders[256];",
  "XM module header and pattern order table.",
);
register(
  "s3m",
  "Scream Tracker",
  "char song_name[28]; uint8 marker; uint8 type; uint16 reserved; uint16 order_count; uint16 instrument_count; uint16 pattern_count; uint16 flags; uint16 tracker_version; uint16 sample_format; char signature[4]; uint8 global_volume; uint8 speed; uint8 tempo; uint8 master_volume; uint8 ultraclick; uint8 default_pan; uint8 reserved_2[8]; uint16 special; uint8 channels[32]; uint8 orders[order_count]; uint16 instrument_paragraphs[instrument_count]; uint16 pattern_paragraphs[pattern_count];",
  "Module header, orders and paragraph-based instrument/pattern offsets.",
);
register(
  "chm",
  "Compiled HTML",
  "char signature[4]; uint32 version; uint32 header_length; uint32 unknown; uint32 timestamp; uint32 language_id; guid directory_guid; guid stream_guid;",
  "ITSF header prefix. Compressed topic streams are not decoded.",
);
register(
  "asar",
  "Electron archive",
  "uint32 size_pickle_length; uint32 header_pickle_length; uint32 header_pickle_payload_length; uint32 json_length; utf8 json[json_length];",
  "ASAR pickle framing and byte-bounded UTF-8 JSON directory.",
);
register(
  "rm",
  "RealMedia",
  "char signature[4]; uint32 header_size; uint16 object_version; uint32 file_version; uint32 header_count;",
  "RealMedia file header.",
  { littleEndian: false },
);
register(
  "drc",
  "Draco",
  "char signature[5]; uint8 major_version; uint8 minor_version; uint8 geometry_type; uint8 encoding_method; uint16 flags;",
  "Draco header. Compressed geometry is not decoded.",
);
register(
  "fbx",
  "FBX binary",
  "uint8 signature[23]; uint32 version;",
  "Binary FBX magic and version. Node record widths depend on the version.",
);
register(
  "blend",
  "Blender",
  "char signature[7]; char pointer_size_code[1]; char byte_order_code[1]; char version[3];",
  "Blender header describing pointer width, byte order and version. DNA blocks are not decoded.",
);
register(
  "ktx",
  "KTX",
  "uint8 signature[12]; uint32 byte_order_marker; uint32 gl_type; uint32 gl_type_size; uint32 gl_format; uint32 gl_internal_format; uint32 gl_base_format; uint32 width; uint32 height; uint32 depth; uint32 array_elements; uint32 faces; uint32 mip_levels; uint32 key_value_bytes;",
  "KTX1 texture header. Image levels and key/value payloads are not decoded.",
);
register(
  "jxr",
  "JPEG XR",
  "char byte_order[2]; uint16 signature; uint32 first_directory;",
  "JPEG XR container header and directory offset.",
);
register(
  "raf",
  "Fujifilm RAW",
  "char signature[16]; char version[4]; char camera_id[8]; char camera_model[32]; char directory_version[4]; uint8 reserved[20]; uint32 jpeg_offset; uint32 jpeg_length; uint32 cfa_header_offset; uint32 cfa_header_length; uint32 cfa_data_offset; uint32 cfa_data_length;",
  "RAF header and JPEG/CFA data ranges.",
  { littleEndian: false },
);
register(
  "xcf",
  "GIMP image",
  "char signature[9]; char version[5]; uint32 width; uint32 height; uint32 base_type;",
  "XCF signature, version and canvas dimensions. Later-version precision and layer pointers are not decoded.",
  { littleEndian: false },
);
register(
  "stl",
  "Binary STL",
  "uint8 header[80]; uint32 triangle_count;",
  "Binary STL header and triangle count. Mesh records are not expanded by default.",
);
register(
  "tar",
  "TAR",
  formats
    .find((f) => f.id === "tar")!
    .definition.replace(/^struct root \{/, "")
    .replace(/\};$/, ""),
  "First 512-byte TAR entry, including POSIX ustar names and ownership fields.",
);
register(
  "elf",
  "ELF",
  "uint8 signature[4]; uint8 word_size; uint8 byte_order; uint8 version; uint8 os_abi; uint8 abi_version; uint8 padding[7];",
  "ELF identification and class-selected executable header.",
);
register(
  "exe",
  "PE executable",
  "uint16 signature; uint8 dos_fields[58]; pe_header *pe;",
  "DOS header and PE/COFF header reached through e_lfanew. DOS-only executables do not have a PE header.",
  {
    pointerSize: 4,
    types:
      "struct section { char name[8]; uint32 virtual_size; uint32 virtual_address; uint32 raw_size; uint32 raw_offset; uint32 relocations_offset; uint32 line_numbers_offset; uint16 relocation_count; uint16 line_number_count; uint32 flags; }; struct pe_header { char signature[4]; uint16 machine; uint16 section_count; uint32 timestamp; uint32 symbol_table_offset; uint32 symbol_count; uint16 optional_header_size; uint16 characteristics; uint8 optional_header[optional_header_size]; section sections[section_count]; };",
  },
);

register(
  "macho",
  "Mach-O",
  "uint32 signature; uint32 cpu_type; uint32 cpu_subtype; uint32 file_type; uint32 command_count; uint32 commands_size; uint32 flags;",
  "Mach-O executable header; 64-bit reserved word and universal-binary architecture directory are selected from the magic.",
);
register(
  "mobi",
  "Palm database / MOBI",
  "char database_name[32]; uint16 attributes; uint16 version; uint32 created; uint32 modified; uint32 backup; uint32 modification_number; uint32 app_info_offset; uint32 sort_info_offset; char database_type[4]; char creator[4]; uint32 unique_id_seed; uint32 next_record_list; uint16 record_count; record records[record_count];",
  "Palm database header and record directory. MOBI content and compression are contained in the records.",
  {
    littleEndian: false,
    types: "struct record { uint32 offset; uint8 attributes; uint24> unique_id; };",
  },
);
register(
  "eot",
  "Embedded OpenType",
  "uint32 eot_size; uint32 font_data_size; uint32 version; uint32 flags; uint8 panose[10]; uint8 charset; uint8 italic; uint32 weight; uint16 embedding_flags; uint16 magic; uint32 unicode_ranges[4]; uint32 codepage_ranges[2]; uint32 checksum_adjustment; uint32 reserved[4];",
  "EOT fixed header, embedding permissions and Unicode/codepage coverage.",
);
register(
  "dcm",
  "DICOM",
  "uint8 preamble[128]; char signature[4]; uint16 first_tag_group; uint16 first_tag_element; char value_representation[2];",
  "DICOM file preamble and first explicit-VR metadata tag. Transfer syntax determines the later dataset layout.",
);
register(
  "iso",
  "ISO 9660",
  "uint8 system_area[32768]; uint8 descriptor_type; char standard_identifier[5]; uint8 descriptor_version;",
  "System area and first volume descriptor. The primary volume descriptor fields are expanded when type 1 is present.",
);
register(
  "pst",
  "Outlook PST",
  "char signature[4]; uint32 partial_crc; uint16 client_magic; uint16 version; uint16 client_version; uint8 platform_create; uint8 platform_access; uint32 reserved_1; uint32 reserved_2;",
  "PST fixed header prefix and format version. ANSI/Unicode node and block trees require version-specific layouts.",
);
register(
  "arj",
  "ARJ",
  "uint16 signature; uint16 basic_header_size; uint8 basic_header[basic_header_size]; uint32 header_crc;",
  "First ARJ header block and CRC; compressed entries are not decoded.",
);
register(
  "cpio",
  "CPIO",
  "char signature[6];",
  "CPIO variant-selected archive entry header. Text size fields remain octal or hexadecimal strings.",
);
register(
  "lzh",
  "LHA/LZH",
  "uint8 header_size; uint8 header_checksum; char method[5]; uint32 compressed_size; uint32 original_size; uint32 timestamp; uint8 attribute; uint8 header_level;",
  "LHA fixed header prefix. Extended headers depend on header level.",
);
register(
  "ace",
  "ACE",
  "uint16 header_crc; uint16 header_size; uint8 header_type; uint16 header_flags; char signature[7]; uint8 extraction_version; uint8 creation_version; uint8 host_os; uint8 volume_number; uint32 creation_time;",
  "ACE archive header prefix.",
);
register(
  "rar",
  "RAR",
  "uint8 signature[7];",
  "RAR4 or RAR5 archive signature and first block framing. RAR5 fields use variable-length integers.",
);
register(
  "mp1 mp2 mp3",
  "MPEG audio",
  "uint32 frame_header;",
  "First MPEG audio frame's packed header, or ID3 tag header when the file starts with ID3. Audio frames are not decoded.",
  { littleEndian: false },
);
register(
  "aac",
  "AAC ADTS",
  "uint8 fixed_header[7];",
  "Seven-byte ADTS header (sync, profile, sample-rate/channel configuration and frame length are packed bits). ID3-prefixed files expose the tag header.",
);
register(
  "ac3",
  "Dolby AC-3",
  "uint16 syncword; uint16 crc1; uint8 sample_rate_and_frame_size; uint8 bitstream_id_and_mode;",
  "AC-3 synchronization header and packed stream parameters.",
  { littleEndian: false },
);
register(
  "mpg",
  "MPEG program stream",
  "uint32 start_code; uint8 pack_header_prefix[8];",
  "First MPEG start code and packed clock/mux prefix. MPEG-1 and MPEG-2 pack layouts differ.",
  { littleEndian: false },
);
register(
  "mts",
  "MPEG transport stream",
  "uint8 sync; uint16> transport_flags_and_pid; uint8 adaptation_and_continuity;",
  "First transport packet header, after a four-byte arrival timestamp for M2TS input. Payload and adaptation fields remain encoded.",
);
register(
  "mxf",
  "MXF",
  "uint8 universal_label[16]; uint8 ber_length_first_byte;",
  "First KLV universal label and initial BER length octet. Long-form BER lengths and essence are not decoded.",
);
register(
  "bpg",
  "BPG",
  "uint8 signature[4]; uint8 pixel_format_and_depth; uint8 color_space_and_flags;",
  "BPG fixed header. Image dimensions use variable-length integers and HEVC payloads remain encoded.",
);
register(
  "flif",
  "FLIF",
  "char signature[4]; uint8 channel_and_flags; uint8 bytes_per_channel;",
  "FLIF fixed signature and channel/depth codes. Variable-length dimensions and compressed pixels are not decoded.",
);
register(
  "j2c",
  "JPEG 2000 codestream",
  "uint16 start_of_codestream; uint16 size_marker; uint16 size_segment_length; uint16 capabilities; uint32 reference_width; uint32 reference_height; uint32 image_x_offset; uint32 image_y_offset; uint32 tile_width; uint32 tile_height; uint32 tile_x_offset; uint32 tile_y_offset; uint16 component_count; component components[component_count];",
  "JPEG 2000 SIZ marker with reference grid, tiling and per-component precision/subsampling.",
  {
    littleEndian: false,
    types:
      "struct component { uint8 precision_and_sign; uint8 horizontal_subsampling; uint8 vertical_subsampling; };",
  },
);
register(
  "jxl",
  "JPEG XL",
  "uint16 signature;",
  "JPEG XL codestream signature, or boxed container signature selected from the bytes. Bit-packed image metadata is not decoded.",
  { littleEndian: false },
);
register(
  "mpc",
  "Musepack",
  "char signature[3]; uint8 version_or_signature_tail;",
  "Musepack SV7/SV8 signature and version marker. Encoded audio packets are not decoded.",
);
register(
  "arrow",
  "Arrow IPC",
  "char signature[6]; uint8 alignment_padding[2];",
  "Arrow file magic and alignment padding. IPC metadata uses FlatBuffers and is not decoded.",
);
register(
  "parquet",
  "Parquet",
  "char signature[4];",
  "Parquet magic. The schema and row groups are described by a Thrift footer, not a fixed leading header.",
);
register(
  "avro",
  "Avro object container",
  "char signature[3]; uint8 version;",
  "Object-container magic and version. Metadata and records use Avro variable-length encoding.",
);
register(
  "alias",
  "Apple bookmark",
  "char signature[4]; uint32 total_size; uint32 version; uint32 data_offset;",
  "Apple bookmark fixed header. Bookmark records and tables are not followed.",
);
register(
  "dmg",
  "Apple disk image",
  "uint8 compression_method_and_window; uint8 compression_flags;",
  "Leading zlib framing recognized by file-type. The UDIF trailer and filesystem are not decoded.",
);
register(
  "mie",
  "Meta information encapsulation",
  "uint8 framing[4]; char signature[4];",
  "MIE framing and signature prefix. Nested metadata records are not decoded.",
  { coverage: "prefix" },
);
register(
  "dwg",
  "AutoCAD drawing",
  "char version[6];",
  "DWG version signature. Object and section layouts are proprietary and version-specific.",
  { coverage: "prefix" },
);
register(
  "indd",
  "InDesign",
  "uint8 signature[16];",
  "InDesign format signature. Page/object records are not decoded.",
  { coverage: "prefix" },
);
register(
  "jmp",
  "JMP data",
  "uint8 signature[16];",
  "JMP byte-order signature. Column and row data are not decoded.",
  { coverage: "prefix" },
);
register(
  "sav",
  "SPSS system file",
  "char signature[4]; char product[60]; int32 layout_code; int32 case_size; int32 compression; int32 weight_index; int32 case_count; double bias; char creation_date[9]; char creation_time[8]; char label[64]; uint8 padding[3];",
  "SPSS system-file header. Dictionary records and compressed cases are not decoded.",
);
register(
  "dat",
  "Windows registry hive",
  "char signature[4]; uint32 primary_sequence; uint32 secondary_sequence; uint64 timestamp; uint32 major_version; uint32 minor_version; uint32 file_type; uint32 file_format; uint32 root_cell_offset; uint32 hive_bins_size; uint32 clustering_factor; wchar< file_name_utf16le[32];",
  "Registry hive base-block prefix. Hive bins and key/value cells are not followed.",
);
register(
  "amr",
  "AMR audio",
  "char signature[6];",
  "AMR narrowband magic; wideband magic is selected from the input. Speech frames remain encoded.",
);
register(
  "mkv webm",
  "EBML",
  "uint32 signature;",
  "EBML header and its first-level elements. Variable-length IDs and lengths are specialized from the file's header.",
  { littleEndian: false },
);

register(
  "pdf",
  "PDF",
  "char signature[5]; char version[3];",
  "PDF version header only. Objects, cross-reference streams and encryption are not decoded.",
  { coverage: "prefix" },
);
register(
  "rtf",
  "Rich Text Format",
  "char signature[5]; char version[1];",
  "RTF header only. The document body is a text grammar, not a fixed binary structure.",
  { coverage: "prefix" },
);
register(
  "ps eps",
  "PostScript",
  "char signature[2];",
  "PostScript header line, or binary EPS preview directory when present. The PostScript program is not interpreted.",
  { coverage: "prefix" },
);
register(
  "xml",
  "XML",
  "uint8 declaration_prefix[PREFIX_BYTES];",
  "XML declaration prefix only; text encoding and XML elements are not decoded.",
  { coverage: "prefix" },
);
register(
  "ics",
  "iCalendar",
  "char calendar_header[15];",
  "BEGIN:VCALENDAR header only. Calendar properties and folded text lines are not decoded.",
  { coverage: "prefix" },
);
register(
  "vcf",
  "vCard",
  "char contact_header[11];",
  "BEGIN:VCARD header only. Contact properties and folded text lines are not decoded.",
  { coverage: "prefix" },
);
register(
  "vtt",
  "WebVTT",
  "char signature[6];",
  "WebVTT signature only. Cue timestamps and subtitle text are not decoded.",
  { coverage: "prefix" },
);
register(
  "reg",
  "Registry export",
  "uint16 byte_order_mark; wchar< version_header[35];",
  "UTF-16 registry-export header only. Registry keys and values are text records.",
  { coverage: "prefix" },
);
register(
  "skp",
  "SketchUp",
  "uint16 byte_order_mark; uint16 marker; wchar< signature[13];",
  "SketchUp signature prefix only. Proprietary model entities are not decoded.",
  { coverage: "prefix" },
);
register(
  "pgp",
  "OpenPGP",
  "uint8 packet_header;",
  "First OpenPGP packet header and encoded length. Encrypted or compressed packet bodies are not decoded.",
);

export const detectableFormatCount = supportedExtensions.size;

export function schemaForFile(ext: string, bytes: Uint8Array): FormatExample {
  const profile = schemaProfiles[ext];
  if (!profile) throw new Error(`No schema registered for detected type ${ext}`);
  let fields = profile.fields;
  let types = profile.types ?? "";
  let littleEndian = profile.littleEndian ?? true;
  let pointerSize = profile.pointerSize ?? 4;
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const ascii = (offset: number, length: number) =>
    String.fromCharCode(...bytes.subarray(offset, offset + length));
  if (ext === "pgp" && bytes.length > 1) {
    if (bytes[0]! & 0x40) {
      fields += " uint8 length_code;";
      if (bytes[1] === 255) fields += " uint32> body_length;";
      else if (bytes[1]! >= 192 && bytes[1]! < 224) fields += " uint8 length_tail;";
    } else {
      const width = [1, 2, 4, 0][bytes[0]! & 3]!;
      if (width) fields += ` uint${width * 8}${width === 1 ? "" : ">"} body_length;`;
    }
  }
  if (ext === "ps" || ext === "eps") {
    if (bytes.length >= 4 && view.getUint32(0, true) === 0xc6d3d0c5) {
      fields =
        "uint32 signature; uint32 postscript_offset; uint32 postscript_length; uint32 metafile_offset; uint32 metafile_length; uint32 tiff_offset; uint32 tiff_length; uint16 checksum;";
    } else {
      const lineEnd = bytes.subarray(0, 256).indexOf(10);
      fields = `char header_line[${lineEnd < 0 ? Math.min(bytes.length, 256) : lineEnd + 1}];`;
    }
  }
  if (ext === "pdf") {
    const start = ascii(0, 1024).indexOf("%PDF-");
    if (start > 0) fields = `uint8 leading_bytes[${start}]; ` + fields;
  }
  if (ext === "macho" && bytes.length >= 4) {
    const magic = view.getUint32(0, false);
    littleEndian =
      magic === 0xcefaedfe || magic === 0xcffaedfe || magic === 0xbebafeca || magic === 0xbfbafeca;
    if ([0xcafebabe, 0xbebafeca, 0xcafebabf, 0xbfbafeca].includes(magic)) {
      const word = magic === 0xcafebabf || magic === 0xbfbafeca ? "uint64" : "uint32";
      fields =
        "uint32 signature; uint32 architecture_count; architecture architectures[architecture_count];";
      types = `struct architecture { uint32 cpu_type; uint32 cpu_subtype; ${word} offset; ${word} size; uint32 alignment_power; ${word === "uint64" ? "uint32 reserved;" : ""} };`;
    } else if (magic === 0xfeedfacf || magic === 0xcffaedfe) fields += " uint32 reserved;";
  }
  if (ext === "mkv" || ext === "webm") {
    // EBML's variable-width integers choose the fixed widths in this file-specific schema.
    const vint = (offset: number, keepMarker: boolean) => {
      const first = bytes[offset] ?? 0;
      let width = 1;
      while (width <= 8 && !(first & (0x80 >> (width - 1)))) width++;
      if (width > 8 || offset + width > bytes.length) return null;
      let value = keepMarker ? first : first & ((0x80 >> (width - 1)) - 1);
      for (let i = 1; i < width; i++) value = value * 256 + bytes[offset + i]!;
      return { width, value };
    };
    const length = vint(4, false);
    if (length) {
      fields += ` uint8 header_size_encoded[${length.width}];`;
      let offset = 4 + length.width;
      const end = Math.min(bytes.length, offset + length.value);
      const names: Record<number, string> = {
        0x4286: "ebml_version",
        0x42f7: "read_version",
        0x42f2: "maximum_id_length",
        0x42f3: "maximum_size_length",
        0x4282: "document_type",
        0x4287: "document_type_version",
        0x4285: "document_type_read_version",
      };
      for (let i = 0; i < 32 && offset < end; i++) {
        const id = vint(offset, true);
        if (!id) break;
        const size = vint(offset + id.width, false);
        if (!size || size.value > end - offset - id.width - size.width) break;
        const name = `${names[id.value] ?? "element"}_${i}`;
        const valueField =
          id.value === 0x4282
            ? `char value[${size.value}];`
            : [1, 2, 4, 8].includes(size.value)
              ? `uint${size.value * 8}${size.value === 1 ? "" : ">"} value;`
              : `uint8 value[${size.value}];`;
        types += ` struct ${name} { uint8 id[${id.width}]; uint8 size_encoded[${size.width}]; ${valueField} };`;
        fields += ` ${name} ${name};`;
        offset += id.width + size.width + size.value;
      }
    }
  }
  if (profile.family === "gzip" && bytes.length >= 10) {
    const flags = bytes[3]!;
    if (flags & 4) fields += " uint16 extra_length; uint8 extra[extra_length];";
    if (flags & 8) fields += " char original_filename[];";
    if (flags & 16) fields += " char comment[];";
    if (flags & 2) fields += " uint16 header_crc16;";
  }
  if (ext === "cpio" && bytes.length >= 6) {
    const signature = ascii(0, 6);
    if (signature === "070701" || signature === "070702") {
      fields =
        "char signature[6]; char inode[8]; char mode[8]; char uid[8]; char gid[8]; char link_count[8]; char modified_time[8]; char file_size[8]; char device_major[8]; char device_minor[8]; char special_device_major[8]; char special_device_minor[8]; char name_size[8]; char checksum[8];";
    } else if (signature === "070707") {
      fields =
        "char signature[6]; char device[6]; char inode[6]; char mode[6]; char uid[6]; char gid[6]; char link_count[6]; char special_device[6]; char modified_time[11]; char name_size[6]; char file_size[11];";
    } else {
      littleEndian = bytes[0] === 0xc7;
      fields =
        "uint16 signature; uint16 device; uint16 inode; uint16 mode; uint16 uid; uint16 gid; uint16 link_count; uint16 special_device; uint16 modified_time_words[2]; uint16 name_size; uint16 file_size_words[2]; char name[name_size];";
    }
  }
  if (ext === "iso" && bytes.length > 32768 && bytes[32768] === 1) {
    fields +=
      " uint8 unused; char system_identifier[32]; char volume_identifier[32]; uint8 unused_2[8]; uint32< volume_blocks_le; uint32> volume_blocks_be; uint8 unused_3[32]; uint16< volume_set_size_le; uint16> volume_set_size_be; uint16< volume_sequence_le; uint16> volume_sequence_be; uint16< logical_block_size_le; uint16> logical_block_size_be; uint32< path_table_size_le; uint32> path_table_size_be; uint32< path_table_l; uint32< optional_path_table_l; uint32> path_table_m; uint32> optional_path_table_m; uint8 root_directory_record[34];";
  }
  if (["mp1", "mp2", "mp3", "aac"].includes(ext) && ascii(0, 3) === "ID3")
    fields =
      "char signature[3]; uint8 major_version; uint8 revision; uint8 flags; uint8 syncsafe_tag_size[4];";
  if (ext === "amr" && ascii(0, 9) === "#!AMR-WB\n") fields = "char signature[9];";
  if (ext === "mts" && bytes[0] !== 0x47 && bytes[4] === 0x47)
    fields = "uint32> arrival_timestamp; " + fields;
  if (ext === "rar" && bytes.length >= 8) {
    if (bytes[6] === 1) fields = "uint8 signature[8]; uint32 header_crc;";
    else fields += " uint16 header_crc; uint8 header_type; uint16 flags; uint16 header_size;";
  }
  if (ext === "jxl" && bytes[0] === 0)
    fields = "uint32 signature_box_size; char box_type[4]; uint32 signature;";
  if (ext === "sav" && bytes.length >= 68)
    littleEndian = view.getInt32(64, true) === 2 || view.getInt32(64, true) === 3;
  if (profile.family === "TIFF" && bytes.length >= 8) {
    littleEndian = ascii(0, 2) !== "MM";
    if (view.getUint16(2, littleEndian) === 43) {
      pointerSize = 8;
      fields =
        "char byte_order[2]; uint16 version; uint16 offset_size; uint16 reserved; ifd *first_directory;";
      types =
        "struct tag { uint16 id; uint16 data_type; uint64 count; uint64 value_or_offset; }; struct ifd { uint64 entry_count; tag entries[entry_count]; uint64 next_directory; };";
    }
  }
  if (ext === "pcap" && bytes.length >= 4) littleEndian = bytes[0] === 0xd4 || bytes[0] === 0x4d;
  if (ext === "ktx" && bytes.length >= 16) littleEndian = view.getUint32(12, true) === 0x04030201;
  if (ext === "gif" && bytes.length >= 13 && bytes[10]! & 0x80) {
    fields += ` rgb global_palette[${1 << ((bytes[10]! & 7) + 1)}];`;
    types = "struct rgb { uint8 red; uint8 green; uint8 blue; };";
  }
  if (ext === "bmp" && bytes.length >= 18) {
    const size = view.getUint32(14, true);
    if (size === 12) fields += " uint16 width; uint16 height; uint16 planes; uint16 bit_depth;";
    else if (size >= 40)
      fields +=
        " int32 width; int32 height; uint16 planes; uint16 bit_depth; uint32 compression; uint32 image_size; int32 horizontal_pixels_per_meter; int32 vertical_pixels_per_meter; uint32 palette_colors; uint32 important_colors;";
  }
  if (profile.family === "RIFF" && bytes.length >= 20) {
    if (ext === "wav" && ascii(12, 4) === "fmt " && view.getUint32(16, true) >= 16)
      fields +=
        " uint16 audio_format; uint16 channels; uint32 sample_rate; uint32 byte_rate; uint16 block_alignment; uint16 bits_per_sample;";
    if (ext === "webp" && ascii(12, 4) === "VP8X")
      fields +=
        " uint8 feature_flags; uint8 reserved[3]; uint24< canvas_width_minus_one_le; uint24< canvas_height_minus_one_le;";
  }
  if (profile.family === "ISO base media" && bytes.length >= 8) {
    const extended = view.getUint32(0, false) === 1;
    if (extended) fields += " uint64 extended_box_size;";
    if (ascii(4, 4) === "ftyp") {
      const headerSize = extended ? 16 : 8;
      fields += " char major_brand[4]; uint32 minor_version;";
      const size =
        extended && bytes.length >= 16
          ? Number(view.getBigUint64(8, false))
          : view.getUint32(0, false);
      const brands = Math.max(0, Math.min(64, Math.floor((size - headerSize - 8) / 4)));
      fields += ` brand compatible_brands[${brands}];`;
      types = "struct brand { char code[4]; };";
    }
  }
  if (ext === "elf" && bytes.length >= 16) {
    littleEndian = bytes[5] !== 2;
    const word = bytes[4] === 2 ? "uint64" : "uint32";
    fields += ` uint16 object_type; uint16 machine; uint32 elf_version; ${word} entry_point; ${word} program_headers_offset; ${word} section_headers_offset; uint32 flags; uint16 header_size; uint16 program_header_size; uint16 program_header_count; uint16 section_header_size; uint16 section_header_count; uint16 section_names_index;`;
  }
  if (profile.family === "ZIP" && bytes.length >= 4 && view.getUint32(0, true) === 0x06054b50)
    fields =
      "uint32 signature; uint16 disk; uint16 directory_disk; uint16 disk_entries; uint16 total_entries; uint32 directory_size; uint32 directory_offset; uint16 comment_length; char comment[comment_length];";
  if (
    profile.family === "JPEG" &&
    bytes.length >= 4 &&
    bytes[2] === 0xff &&
    ![0xd8, 0xd9, 0x01].includes(bytes[3]!)
  )
    fields += " uint16 segment_length; uint8 segment_data[segment_length - 2];";
  ({ fields, types, littleEndian, pointerSize } = expandSchema(ext, bytes, {
    fields,
    types,
    littleEndian,
    pointerSize,
  }));
  const scope = expansionReviews[ext]?.scope ?? profile.scope;
  const name = `file_${ext.replace(/[^a-zA-Z0-9_]/g, "_")}`;
  const declarations = `${types}\nstruct ${name} { ${fields} };\nstruct root { ${name} header; };`;
  let depth = 0;
  const formatted = declarations
    .replace(/\{\s*/g, "{\n")
    .replace(/;\s*/g, ";\n")
    .split("\n")
    .map((line) => {
      const trimmed = line.trim();
      if (trimmed.startsWith("}")) depth--;
      const result = "    ".repeat(Math.max(0, depth)) + trimmed;
      if (trimmed.endsWith("{")) depth++;
      return result;
    })
    .join("\n");
  const definition = `// ${ext.toUpperCase()} — ${profile.family}\n// ${scope}\n// Preview determines bounded coverage; native conditions select supported record variants. Reload detection after structural changes.\n// char preserves byte code units; explicit utf8/latin1/cp437/utf16 types decode declared encodings.\n// Detection is a signature hint, not file validation.\n#define PREFIX_BYTES ${Math.min(bytes.length, 256)}\n${formatted}`;
  return {
    id: `detected-${ext}`,
    extension: ext,
    title: `${ext.toUpperCase()} · ${profile.family}`,
    definition,
    binaryHex: "",
    rootType: "root",
    parserOptions: { aligned: false, littleEndian, pointerSize },
    documentation: { summary: scope },
    sourceFixture: "",
  };
}

// Reuse real samples where available; other entries are selectable schema templates.
export const schemaCatalog: FormatExample[] = [
  ...Object.keys(schemaProfiles).map((ext) => {
    const sample = formats.find((format) => format.id === (ext === "exe" ? "pe-exe" : ext));
    return sample
      ? { ...sample, extension: ext }
      : { ...schemaForFile(ext, new Uint8Array()), id: `schema-${ext}`, schemaOnly: true };
  }),
  ...formats
    .filter((format) => format.id === "pe-dll")
    .map((format) => ({ ...format, extension: "dll" })),
].sort((a, b) => a.extension!.localeCompare(b.extension!, undefined, { sensitivity: "base" }));

export function rawFileSchema(bytes: Uint8Array): FormatExample {
  return {
    id: "detected-unknown",
    title: "Unknown format",
    definition: `// Unrecognized file. Select an example or edit this schema.\nstruct root { uint8 prefix[${Math.min(bytes.length, 256)}]; };`,
    binaryHex: "",
    rootType: "root",
    parserOptions: { aligned: false, littleEndian: true, pointerSize: 4 },
    documentation: { summary: "Raw prefix; no file type recognized." },
    sourceFixture: "",
  };
}
