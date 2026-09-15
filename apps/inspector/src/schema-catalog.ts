import { formatLayout } from "./format-layout";

export interface FormatParserOptions {
  aligned: boolean;
  littleEndian: boolean;
  pointerSize: number;
  addressingMode?: "Absolute" | "Relative";
}

/** One selectable editor example. Sample examples also carry bytes and a managed test reference. */
export interface InspectorExample {
  id: string;
  title: string;
  description: string;
  definition: string;
  binaryHex: string;
  rootType: string;
  parserOptions: FormatParserOptions;
  documentation: { summary: string };
  sourceFixture: string;
  coverage: "structure" | "prefix";
  extension?: string;
  schemaOnly?: boolean;
}

type SampleExample = Omit<InspectorExample, "coverage" | "schemaOnly"> & { extension: string };
interface FormatDefinition {
  extensions: string[];
  aliases?: Record<string, string>;
  family: string;
  scope: string;
  fields: string;
  types?: string;
  littleEndian?: boolean;
  pointerSize?: number;
  coverage?: "structure" | "prefix";
  samples?: SampleExample[];
}

const sampleParserOptions: FormatParserOptions = {
  aligned: false,
  littleEndian: true,
  pointerSize: 8,
};

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

// TAR's sample and detection schema read the same fields, with different root wrappers.
const tarFields = `
    char name[100];
    char mode[8];
    char uid[8];
    char gid[8];
    char size[12];
    char mtime[12];
    char chksum[8];
    char typeflag[1];
    char linkname[100];
    char magic[6];
    char version[2];
    char uname[32];
    char gname[32];
    char devmajor[8];
    char devminor[8];
    char prefix[155];
    char padding[12];
`;

// DICOM stores its length in two possible widths. Both branches use this same value decoder.
const dicomValueFields = `if (value_representation == 17729 || value_representation == 21313 || value_representation == 21315 || value_representation == 16708 || value_representation == 21316 || value_representation == 21572 || value_representation == 21321 || value_representation == 20300 || value_representation == 21580 || value_representation == 20048 || value_representation == 18515 || value_representation == 21587 || value_representation == 19796 || value_representation == 17237 || value_representation == 18773 || value_representation == 21077 || value_representation == 21589) {
    char text[value_length];
} else {
    switch (value_representation) {
        case 21333: {
            if (value_length / 2 * 2 == value_length) {
                uint16 values_US[value_length / 2];
            } else {
                uint8 malformed_US[value_length];
            }
        } case 21331: {
            if (value_length / 2 * 2 == value_length) {
                int16 values_SS[value_length / 2];
            } else {
                uint8 malformed_SS[value_length];
            }
        } case 19541: {
            if (value_length / 4 * 4 == value_length) {
                uint32 values_UL[value_length / 4];
            } else {
                uint8 malformed_UL[value_length];
            }
        } case 19539: {
            if (value_length / 4 * 4 == value_length) {
                int32 values_SL[value_length / 4];
            } else {
                uint8 malformed_SL[value_length];
            }
        } case 19526: {
            if (value_length / 4 * 4 == value_length) {
                float32 values_FL[value_length / 4];
            } else {
                uint8 malformed_FL[value_length];
            }
        } case 17478: {
            if (value_length / 8 * 8 == value_length) {
                float64 values_FD[value_length / 8];
            } else {
                uint8 malformed_FD[value_length];
            }
        } case 22101: {
            if (value_length / 8 * 8 == value_length) {
                uint64 values_UV[value_length / 8];
            } else {
                uint8 malformed_UV[value_length];
            }
        } case 22099: {
            if (value_length / 8 * 8 == value_length) {
                int64 values_SV[value_length / 8];
            } else {
                uint8 malformed_SV[value_length];
            }
        } default: {
            uint8 bytes[value_length];
        }
    }
}`;

/**
 * The single registration list for the inspector. Each entry owns its file extensions, detection
 * layout and optional teaching samples. A sample can intentionally show a smaller structure than
 * the detection layout, but it is registered beside that layout rather than matched by an ID later.
 * The sample definitions and bytes are checked against tests/CStructSharpTests/WellKnownFormatFixtures.cs.
 */
const formatDefinitions: FormatDefinition[] = [
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
        sourceFixture: "WellKnownFormatFixtures.Bmp_FileAndInfoHeaders_DecodeExpectedFields",
      },
    ],
  },
  {
    extensions: ["wav", "avi", "webp", "qcp"],
    family: "RIFF",
    scope: "RIFF form and first chunk header only. Chunk payloads are not decoded.",
    fields: `char signature[4];
uint32 file_size_minus_8;
char form_type[4];
char first_chunk_type[4];
uint32 first_chunk_size;`,
    samples: [
      {
        id: "wav",
        extension: "wav",
        title: "WAV - RIFF/WAVE header",
        description: "Wave audio",
        definition: `struct riff_header {
    char chunk_id[4];
    uint32 chunk_size;
    char format[4];
};

struct fmt_chunk {
    char chunk_id[4];
    uint32 chunk_size;
    uint16 audio_format;
    uint16 num_channels;
    uint32 sample_rate;
    uint32 byte_rate;
    uint16 block_align;
    uint16 bits_per_sample;
};

struct data_chunk_header {
    char chunk_id[4];
    uint32 chunk_size;
};

struct root {
    riff_header riff;
    fmt_chunk fmt;
    data_chunk_header data;
};`,
        binaryHex:
          "52 49 46 46 24 00 00 00 57 41 56 45 66 6d 74 20 10 00 00 00 01 00 02 00 44 ac 00 00 10 b1 02 00 04 00 10 00 64 61 74 61 00 00 00 00",
        rootType: "root",
        parserOptions: sampleParserOptions,
        documentation: {
          summary:
            "The classic 44-byte canonical WAV header: RIFF/WAVE container, a PCM fmt subchunk (44.1kHz stereo 16-bit), and an empty data subchunk. FourCC tags decode as fixed char[4] buffers.",
        },
        sourceFixture: "WellKnownFormatFixtures.Wav_RiffFmtAndDataChunks_DecodeExpectedFields",
      },
    ],
  },
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
        sourceFixture:
          "WellKnownFormatFixtures.Zip_LocalFileHeader_DecodesBitflagsAndRuntimeLengthName",
      },
    ],
  },
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
        sourceFixture: "WellKnownFormatFixtures.Png_SignatureAndIhdrChunk_DecodeBigEndianFields",
      },
    ],
  },
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
        sourceFixture: "WellKnownFormatFixtures.Jpg_SoiAndJfifApp0Segment_DecodeExpectedFields",
      },
    ],
  },
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
        sourceFixture:
          "WellKnownFormatFixtures.Pe_DosHeaderPointerToCoffHeader_DecodesAcrossExeAndDll (EXE)",
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
        sourceFixture:
          "WellKnownFormatFixtures.Pe_DosHeaderPointerToCoffHeader_DecodesAcrossExeAndDll (DLL)",
      },
    ],
  },
  {
    extensions: ["ico", "cur"],
    family: "Windows icon directory",
    scope:
      "Complete image directory. CUR uses hotspot coordinates where ICO stores planes and bit depth. Image payloads remain at the recorded offsets.",
    fields: `uint16 reserved;
uint16 kind;
uint16 image_count;
icon_entry images[image_count];`,
    types: `struct icon_entry {
    uint8 width;
    uint8 height;
    uint8 color_count;
    uint8 reserved;
    uint16 planes_or_hotspot_x;
    uint16 bit_depth_or_hotspot_y;
    uint32 image_size;
    uint32 image_offset;
};`,
    samples: [
      {
        id: "ico",
        extension: "ico",
        title: "ICO - icon directory",
        description: "Windows icon",
        definition: `struct icon_dir_entry {
    uint8 width;
    uint8 height;
    uint8 color_count;
    uint8 reserved;
    uint16 color_planes;
    uint16 bits_per_pixel;
    uint32 size_in_bytes;
    uint32 data_offset;
};

struct root {
    uint16 reserved;
    uint16 image_type;
    uint16 image_count;
    icon_dir_entry entries[image_count];
};`,
        binaryHex:
          "00 00 01 00 02 00 10 10 00 00 01 00 20 00 68 04 00 00 26 00 00 00 20 20 00 00 01 00 20 00 a8 10 00 00 8e 04 00 00",
        rootType: "root",
        parserOptions: sampleParserOptions,
        documentation: {
          summary:
            "A 2-entry ICO directory (16x16 and 32x32 images). image_count drives the length of the trailing entries array - a runtime-sized array of a composite (struct) element type, not just a byte array.",
        },
        sourceFixture: "WellKnownFormatFixtures.Ico_IconDirectory_DecodesCountDrivenEntryArray",
      },
    ],
  },
  {
    extensions: ["tar"],
    family: "TAR",
    scope: "First 512-byte TAR entry, including POSIX ustar names and ownership fields.",
    fields: tarFields,
    samples: [
      {
        id: "tar",
        extension: "tar",
        title: "TAR - POSIX ustar header",
        description: "TAR archive",
        definition: `struct root {${tarFields}};`,
        binaryHex:
          "68 65 6c 6c 6f 2e 74 78 74 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 30 30 30 30 36 34 34 00 30 30 30 30 30 30 30 00 30 30 30 30 30 30 30 00 30 30 30 30 30 30 30 30 30 31 32 00 30 30 30 30 30 30 30 30 30 30 30 00 30 31 31 35 35 36 00 20 30 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 75 73 74 61 72 00 30 30 75 73 65 72 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 67 72 6f 75 70 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00",
        rootType: "root",
        parserOptions: sampleParserOptions,
        documentation: {
          summary:
            'A real 512-byte POSIX ustar header for a file named "hello.txt", with a correctly computed checksum. Fixed-width ASCII/octal text fields throughout - a different complexity flavor from the other binary-integer formats.',
        },
        sourceFixture: "WellKnownFormatFixtures.Tar_UstarHeader_DecodesFixedWidthTextFields",
      },
    ],
  },
  {
    extensions: [
      "mp4",
      "m4a",
      "m4v",
      "m4p",
      "m4b",
      "f4v",
      "f4p",
      "f4b",
      "f4a",
      "3gp",
      "3g2",
      "mov",
      "heic",
      "avif",
      "cr3",
    ],
    family: "ISO base media",
    scope:
      "First ISO box size and type only. Extended sizes, brands and media payloads are not decoded.",
    fields: `uint32 box_size;
char box_type[4];`,
    littleEndian: false,
  },
  {
    extensions: ["jp2", "jpm", "jpx", "mj2"],
    family: "JPEG 2000 boxes",
    scope: "JPEG 2000 signature and next box header. Codestream decoding is outside this layout.",
    fields: `uint32 signature_box_size;
char signature_box_type[4];
uint32 signature;
uint32 next_box_size;
char next_box_type[4];`,
    littleEndian: false,
  },
  {
    extensions: ["oga", "ogg", "ogv", "opus", "spx", "ogm", "ogx"],
    family: "Ogg",
    scope: "First Ogg page header and lacing table. Codec packets remain encoded.",
    fields: `char signature[4];
uint8 version;
uint8 continued_packet:1;
uint8 beginning_of_stream:1;
uint8 end_of_stream:1;
uint8 reserved:5;
uint64 granule_position;
uint32 stream_serial;
uint32 page_sequence;
uint32 checksum;
uint8 segment_count;
uint8 segment_sizes[segment_count];`,
  },
  {
    extensions: ["tif", "cr2", "arw", "dng", "nef", "orf", "rw2"],
    family: "TIFF",
    scope:
      "Classic TIFF follows typed IFD links using the selected byte order and 4-byte pointer setting. BigTIFF header offsets remain numeric. Choose the byte order in settings; no file bytes are consulted to change parser settings.",
    fields: `char byte_order[2];
uint16 version;
if (version == 42) {
    ifd *first_directory;
} else {
    if (version == 43) {
        uint16 offset_size;
        uint16 reserved;
        uint64 first_directory_offset;
    }
}`,
    types: `enum tiff_tag : uint16 {
    ImageWidth=256,
    ImageLength=257,
    BitsPerSample=258,
    Compression=259,
    PhotometricInterpretation=262,
    StripOffsets=273,
    SamplesPerPixel=277,
    RowsPerStrip=278,
    StripByteCounts=279,
    XResolution=282,
    YResolution=283,
    ExifIFD=34665,
    GPSIFD=34853
};
enum tiff_type : uint16 {
    Byte=1,
    Ascii=2,
    Short=3,
    Long=4,
    Rational=5,
    SByte=6,
    Undefined=7,
    SShort=8,
    SLong=9,
    SRational=10,
    Float=11,
    Double=12
};
struct tag {
    tiff_tag id;
    tiff_type data_type;
    uint32 count;
    uint32 value_or_offset;
};
struct ifd {
    uint16 entry_count;
    tag entries[entry_count];
    ifd *next_directory;
};`,
  },
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
    extensions: ["jls"],
    family: "JPEG",
    scope:
      "Start-of-image and first marker. JPEG segment layouts vary; the first length-bearing segment is exposed without assuming JFIF.",
    fields: `uint16 start_of_image;
uint16 first_marker;`,
    littleEndian: false,
  },
  {
    extensions: ["gif"],
    family: "GIF",
    scope:
      "Logical screen fields, packed flags and the runtime-sized global color palette. Image/extension streams are not scanned outside the schema.",
    fields: `char signature[3];
char version[3];
uint16 width;
uint16 height;
uint8 palette_size_code:3;
uint8 sorted:1;
uint8 color_resolution_minus_one:3;
uint8 global_palette_present:1;
uint8 background_color_index;
uint8 pixel_aspect_ratio;
if (global_palette_present) {
    gif_rgb global_palette[1 << (palette_size_code + 1)];
}`,
    types: `struct gif_rgb {
    uint8 red;
    uint8 green;
    uint8 blue;
};`,
  },
  {
    extensions: ["psd"],
    family: "Photoshop",
    scope: "Photoshop/large-document image header. Layer and image payloads are not decoded.",
    fields: `char signature[4];
uint16 version;
uint8 reserved[6];
uint16 channels;
uint32 height;
uint32 width;
uint16 depth;
uint16 color_mode;`,
    littleEndian: false,
  },
  {
    extensions: ["icns"],
    family: "Apple icon",
    scope: "Icon container and first element header.",
    fields: `char signature[4];
uint32 file_length;
char first_element_type[4];
uint32 first_element_length;`,
    littleEndian: false,
  },
  {
    extensions: ["flac"],
    family: "FLAC",
    scope:
      "STREAMINFO metadata, including packed sample rate/channel/sample count bits and MD5. Audio frames remain encoded.",
    fields: `char signature[4];
uint8 metadata_type:7;
uint8 last_metadata:1;
uint24> metadata_length_be;
uint16 minimum_block_size;
uint16 maximum_block_size;
uint24> minimum_frame_size_be;
uint24> maximum_frame_size_be;
uint64 total_samples:36;
uint64 bits_per_sample_minus_one:5;
uint64 channels_minus_one:3;
uint64 sample_rate:20;
uint8 md5[16];`,
    littleEndian: false,
  },
  {
    extensions: ["mid"],
    family: "MIDI",
    scope:
      "MIDI header and declared track chunks, including their byte lengths and raw event bytes. Assumes consecutive MTrk chunks; variable-length events remain encoded.",
    fields: `char signature[4];
uint32 header_size;
uint16 format;
uint16 track_count;
uint16 division;
if (header_size >= 6) {
    uint8 header_extension[header_size - 6];
    midi_track tracks[track_count];
}`,
    types: `struct midi_track {
    char chunk_type[4];
    uint32 byte_length;
    uint8 event_bytes[byte_length];
};`,
    littleEndian: false,
  },
  {
    extensions: ["aif"],
    family: "AIFF",
    scope: "AIFF/AIFC form and first chunk header.",
    fields: `char signature[4];
uint32 form_size;
char form_type[4];
char first_chunk_type[4];
uint32 first_chunk_size;`,
    littleEndian: false,
  },
  {
    extensions: ["wasm"],
    family: "WebAssembly",
    scope:
      "Module magic and version. Section lengths and instructions use LEB128 and are not decoded by this fixed header.",
    fields: `uint8 signature[4];
uint32 version;`,
  },
  {
    extensions: ["sqlite"],
    family: "SQLite",
    scope: "Complete 100-byte database header. B-tree pages and SQL records are not decoded.",
    fields: `char signature[16];
uint16 page_size;
uint8 write_version;
uint8 read_version;
uint8 reserved_per_page;
uint8 maximum_payload_fraction;
uint8 minimum_payload_fraction;
uint8 leaf_payload_fraction;
uint32 change_counter;
uint32 page_count;
uint32 first_freelist_page;
uint32 freelist_page_count;
uint32 schema_cookie;
uint32 schema_format;
uint32 default_cache_size;
uint32 largest_root_page;
uint32 text_encoding;
uint32 user_version;
uint32 incremental_vacuum;
uint32 application_id;
uint8 reserved[20];
uint32 version_valid_for;
uint32 sqlite_version;`,
    littleEndian: false,
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
    fields: `// Conditions use signed 32-bit values, including hexadecimal constants with bit 31 set.
int32 signature;
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
    extensions: ["glb"],
    family: "glTF binary",
    scope:
      "GLB 2 header and its first chunk, with native JSON/binary alternatives. JSON is decoded as UTF-8 text; buffer bytes stay opaque.",
    fields: `char signature[4];
uint32 version;
uint32 total_length;
if (version == 2 && total_length >= 20) {
    glb_chunk chunk_0;
}`,
    types: `enum glb_chunk_kind : uint32 {
    Json=1313821514,
    Binary=5130562
};
struct glb_chunk {
    uint32 length;
    glb_chunk_kind type;
    if (type == 1313821514) {
        utf8 json_data[length];
    } else {
        uint8 binary_data[length];
    }
};`,
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
    extensions: ["pcap"],
    family: "Packet capture",
    scope:
      "Classic PCAP global header using the selected byte order. Packet records are not decoded.",
    fields: `uint32 signature;
uint16 major_version;
uint16 minor_version;
int32 timezone;
uint32 accuracy;
uint32 snapshot_length;
uint32 link_type;`,
  },
  {
    extensions: ["lnk"],
    family: "Shell link",
    scope:
      "Complete Shell Link header. Flag-dependent target lists, paths and extra blocks are not decoded.",
    fields: `uint32 header_size;
guid class_id;
uint32 link_flags;
uint32 file_attributes;
uint64 creation_time;
uint64 access_time;
uint64 write_time;
uint32 file_size;
int32 icon_index;
uint32 show_command;
uint16 hotkey;
uint16 reserved_1;
uint32 reserved_2;
uint32 reserved_3;`,
  },
  {
    extensions: ["cfb"],
    family: "Compound file",
    scope:
      "Compound-file header and initial DIFAT. Sector chains and embedded Office documents are not followed.",
    fields: `uint8 signature[8];
guid class_id;
uint16 minor_version;
uint16 major_version;
uint16 byte_order;
uint16 sector_shift;
uint16 mini_sector_shift;
uint8 reserved[6];
uint32 directory_sector_count;
uint32 fat_sector_count;
uint32 first_directory_sector;
uint32 transaction_signature;
uint32 mini_stream_cutoff;
uint32 first_mini_fat_sector;
uint32 mini_fat_sector_count;
uint32 first_difat_sector;
uint32 difat_sector_count;
uint32 difat[109];`,
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
    extensions: ["flv"],
    family: "Flash video",
    scope: "FLV header. Audio/video tags are not decoded.",
    fields: `char signature[3];
uint8 version;
uint8 video_present:1;
uint8 reserved_low:1;
uint8 audio_present:1;
uint8 reserved_high:5;
uint32 data_offset;`,
    littleEndian: false,
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
    extensions: ["asf"],
    family: "ASF",
    scope:
      "ASF header and all declared child objects, with GUIDs, sizes and raw bodies. Media packets and object-specific metadata remain encoded.",
    fields: `guid object_id;
uint64 object_size;
uint32 child_count;
uint8 reserved[2];
asf_object children[child_count];`,
    types: `struct asf_object {
    guid id;
    uint64 size;
    uint8 body[size - 24];
};`,
  },
  {
    extensions: ["dsf"],
    family: "DSD stream",
    scope: "DSF main chunk and metadata pointer.",
    fields: `char signature[4];
uint64 chunk_size;
uint64 file_size;
uint64 metadata_offset;`,
  },
  {
    extensions: ["wv"],
    family: "WavPack",
    scope: "WavPack block header. Encoded samples are not decoded.",
    fields: `char signature[4];
uint32 block_size;
uint16 version;
uint8 track;
uint8 index;
uint32 total_samples;
uint32 block_index;
uint32 block_samples;
uint32 flags;
uint32 crc;`,
  },
  {
    extensions: ["ape"],
    family: "Monkey's Audio",
    scope: "Magic and version. Descriptor layout changes between codec versions.",
    fields: `char signature[4];
uint16 version;`,
  },
  {
    extensions: ["voc"],
    family: "Creative Voice",
    scope: "Creative Voice fixed header.",
    fields: `char signature[20];
uint16 header_size;
uint16 version;
uint16 version_checksum;`,
  },
  {
    extensions: ["shp"],
    family: "Shapefile",
    scope: "Complete 100-byte shapefile header and bounding ranges.",
    fields: `uint32> file_code;
uint32> unused[5];
uint32> file_length_words;
uint32< version;
uint32< shape_type;
float64< x_min;
float64< y_min;
float64< x_max;
float64< y_max;
float64< z_min;
float64< z_max;
float64< m_min;
float64< m_max;`,
  },
  {
    extensions: ["icc"],
    family: "ICC profile",
    scope: "ICC header and tag directory. XYZ values use signed 16.16 fixed-point encoding.",
    fields: `uint32 profile_size;
char cmm[4];
uint32 version;
char profile_class[4];
char color_space[4];
char connection_space[4];
uint16 created[6];
char signature[4];
char platform[4];
uint32 flags;
char manufacturer[4];
char model[4];
uint64 attributes;
uint32 rendering_intent;
fixed16_16 illuminant_xyz[3];
char creator[4];
uint8 profile_id[16];
uint8 reserved[28];
uint32 tag_count;
tag tags[tag_count];`,
    types: `struct tag {
    char signature[4];
    uint32 offset;
    uint32 size;
};`,
    littleEndian: false,
  },
  {
    extensions: ["it"],
    family: "Impulse Tracker",
    scope: "Module header, channel defaults, orders and instrument/sample/pattern directories.",
    fields: `char signature[4];
char song_name[26];
uint16 highlight;
uint16 order_count;
uint16 instrument_count;
uint16 sample_count;
uint16 pattern_count;
uint16 created_version;
uint16 compatible_version;
uint16 flags;
uint16 special;
uint8 global_volume;
uint8 mix_volume;
uint8 speed;
uint8 tempo;
uint8 separation;
uint8 pitch_depth;
uint16 message_length;
uint32 message_offset;
uint32 reserved;
uint8 channel_pan[64];
uint8 channel_volume[64];
uint8 orders[order_count];
uint32 instrument_offsets[instrument_count];
uint32 sample_offsets[sample_count];
uint32 pattern_offsets[pattern_count];`,
  },
  {
    extensions: ["xm"],
    family: "FastTracker",
    scope: "XM module header and pattern order table.",
    fields: `char signature[17];
char module_name[20];
uint8 marker;
char tracker_name[20];
uint16 version;
uint32 header_size;
uint16 song_length;
uint16 restart_position;
uint16 channel_count;
uint16 pattern_count;
uint16 instrument_count;
uint16 flags;
uint16 tempo;
uint16 bpm;
uint8 orders[256];`,
  },
  {
    extensions: ["s3m"],
    family: "Scream Tracker",
    scope: "Module header, orders and paragraph-based instrument/pattern offsets.",
    fields: `char song_name[28];
uint8 marker;
uint8 type;
uint16 reserved;
uint16 order_count;
uint16 instrument_count;
uint16 pattern_count;
uint16 flags;
uint16 tracker_version;
uint16 sample_format;
char signature[4];
uint8 global_volume;
uint8 speed;
uint8 tempo;
uint8 master_volume;
uint8 ultraclick;
uint8 default_pan;
uint8 reserved_2[8];
uint16 special;
uint8 channels[32];
uint8 orders[order_count];
uint16 instrument_paragraphs[instrument_count];
uint16 pattern_paragraphs[pattern_count];`,
  },
  {
    extensions: ["chm"],
    family: "Compiled HTML",
    scope: "ITSF header prefix. Compressed topic streams are not decoded.",
    fields: `char signature[4];
uint32 version;
uint32 header_length;
uint32 unknown;
uint32 timestamp;
uint32 language_id;
guid directory_guid;
guid stream_guid;`,
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
    extensions: ["rm"],
    family: "RealMedia",
    scope: "RealMedia file header.",
    fields: `char signature[4];
uint32 header_size;
uint16 object_version;
uint32 file_version;
uint32 header_count;`,
    littleEndian: false,
  },
  {
    extensions: ["drc"],
    family: "Draco",
    scope: "Draco header. Compressed geometry is not decoded.",
    fields: `char signature[5];
uint8 major_version;
uint8 minor_version;
uint8 geometry_type;
uint8 encoding_method;
uint16 flags;`,
  },
  {
    extensions: ["fbx"],
    family: "FBX binary",
    scope:
      "Binary FBX header and first node identifier. Native version branches select 32-bit or 64-bit node fields. Properties and child nodes remain undecoded.",
    fields: `uint8 signature[23];
uint32 version;
fbx_node first_node;`,
    types: `struct fbx_node {
    if (version >= 7500) {
        uint64 end_offset64;
        uint64 property_count64;
        uint64 property_bytes64;
    } else {
        uint32 end_offset32;
        uint32 property_count32;
        uint32 property_bytes32;
    } uint8 name_length;
    char name[name_length];
};`,
  },
  {
    extensions: ["blend"],
    family: "Blender",
    scope:
      "Blender header describing pointer width, byte order and version. DNA blocks are not decoded.",
    fields: `char signature[7];
char pointer_size_code[1];
char byte_order_code[1];
char version[3];`,
  },
  {
    extensions: ["ktx"],
    family: "KTX",
    scope: "KTX1 texture header. Image levels and key/value payloads are not decoded.",
    fields: `uint8 signature[12];
uint32 byte_order_marker;
uint32 gl_type;
uint32 gl_type_size;
uint32 gl_format;
uint32 gl_internal_format;
uint32 gl_base_format;
uint32 width;
uint32 height;
uint32 depth;
uint32 array_elements;
uint32 faces;
uint32 mip_levels;
uint32 key_value_bytes;`,
  },
  {
    extensions: ["jxr"],
    family: "JPEG XR",
    scope: "JPEG XR container header and directory offset.",
    fields: `char byte_order[2];
uint16 signature;
ifd *first_directory;`,
    types: `enum tiff_tag_id : uint16 {
    ImageWidth=256,
    ImageLength=257,
    BitsPerSample=258,
    Compression=259,
    Photometric=262,
    StripOffsets=273,
    SamplesPerPixel=277,
    RowsPerStrip=278,
    StripByteCounts=279,
    XResolution=282,
    YResolution=283,
    Software=305,
    DateTime=306,
    ExifIfd=34665,
    GpsIfd=34853
};
enum tiff_value_type : uint16 {
    Byte=1,
    Ascii=2,
    Short=3,
    Long=4,
    Rational=5,
    SByte=6,
    Undefined=7,
    SShort=8,
    SLong=9,
    SRational=10,
    Float=11,
    Double=12,
    Ifd=13,
    Long8=16,
    SLong8=17,
    Ifd8=18
};
struct tag {
    tiff_tag_id id;
    tiff_value_type data_type;
    uint32 count;
    uint32 value_or_offset;
};
struct ifd {
    uint16 entry_count;
    tag entries[entry_count];
    uint32 next_directory;
};`,
  },
  {
    extensions: ["raf"],
    family: "Fujifilm RAW",
    scope: "RAF header and JPEG/CFA data ranges.",
    fields: `char signature[16];
char version[4];
char camera_id[8];
char camera_model[32];
char directory_version[4];
uint8 reserved[20];
uint32 jpeg_offset;
uint32 jpeg_length;
uint32 cfa_header_offset;
uint32 cfa_header_length;
uint32 cfa_data_offset;
uint32 cfa_data_length;`,
    littleEndian: false,
  },
  {
    extensions: ["xcf"],
    family: "GIMP image",
    scope:
      "XCF signature, version and canvas dimensions. Later-version precision and layer pointers are not decoded.",
    fields: `char signature[9];
char version[5];
uint32 width;
uint32 height;
uint32 base_type;`,
    littleEndian: false,
  },
  {
    extensions: ["stl"],
    family: "Binary STL",
    scope:
      "Binary STL header and all declared triangles, with float normals and vertex arrays. The ordinary array/read budgets apply to decoded data, not file length.",
    fields: `char header[80];
uint32 triangle_count;
stl_triangle triangles[triangle_count];`,
    types: `struct stl_triangle {
    float normal[3];
    float vertices[3][3];
    uint16 attribute_byte_count;
};`,
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
  {
    extensions: ["mobi"],
    family: "Palm database / MOBI",
    scope:
      "Palm database header and record directory. MOBI content and compression are contained in the records.",
    fields: `char database_name[32];
uint16 attributes;
uint16 version;
uint32 created;
uint32 modified;
uint32 backup;
uint32 modification_number;
uint32 app_info_offset;
uint32 sort_info_offset;
char database_type[4];
char creator[4];
uint32 unique_id_seed;
uint32 next_record_list;
uint16 record_count;
record records[record_count];`,
    types: `struct record {
    uint32 offset;
    uint8 attributes;
    uint24> unique_id;
};`,
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
  {
    extensions: ["dcm"],
    family: "DICOM",
    scope:
      "DICOM file preamble and first explicit-VR metadata tag. Transfer syntax determines the later dataset layout.",
    fields: `uint8 preamble[128]; char signature[4]; uint16 first_tag_group; uint16 first_tag_element; dicom_vr value_representation; if (!(value_representation == 17729 || value_representation == 21313 || value_representation == 21569 || value_representation == 21315 || value_representation == 16708 || value_representation == 21316 || value_representation == 21572 || value_representation == 19526 || value_representation == 17478 || value_representation == 21321 || value_representation == 20300 || value_representation == 21580 || value_representation == 20048 || value_representation == 18515 || value_representation == 19539 || value_representation == 21331 || value_representation == 21587 || value_representation == 19796 || value_representation == 18773 || value_representation == 19541 || value_representation == 21333)) { struct { uint16 reserved; uint32 value_length; ${dicomValueFields} } long_value; } else { struct { uint16 value_length; ${dicomValueFields} } short_value; }`,
    types: `enum dicom_vr : uint16 {
    AE=17729,
    AS=21313,
    CS=21315,
    DA=16708,
    DS=21316,
    DT=21572,
    IS=21321,
    LO=20300,
    LT=21580,
    PN=20048,
    SH=18515,
    ST=21587,
    TM=19796,
    UC=17237,
    UI=18773,
    UR=21077,
    UT=21589,
    OB=16975,
    OD=17487,
    OF=17999,
    OL=19535,
    OV=22095,
    OW=22351,
    SQ=20819,
    SV=22099,
    UV=22101,
    UN=20053,
    US=21333,
    SS=21331,
    UL=19541,
    SL=19539,
    FL=19526,
    FD=17478,
    AT=21569
};`,
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
    extensions: ["pst"],
    family: "Outlook PST",
    scope:
      "PST fixed header prefix and format version. ANSI/Unicode node and block trees require version-specific layouts.",
    fields: `char signature[4];
uint32 partial_crc;
uint16 client_magic;
uint16 version;
uint16 client_version;
uint8 platform_create;
uint8 platform_access;
uint32 reserved_1;
uint32 reserved_2;`,
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
    extensions: ["mp1", "mp2", "mp3"],
    family: "MPEG audio",
    scope:
      "First MPEG audio frame packed header. ID3-prefixed input requires a separate tag layout; audio samples are not decoded.",
    fields: `uint32 emphasis:2;
uint32 original:1;
uint32 copyright:1;
uint32 mode_extension:2;
uint32 channel_mode:2;
uint32 private_bit:1;
uint32 padding:1;
uint32 sample_rate_index:2;
uint32 bitrate_index:4;
uint32 no_crc:1;
uint32 layer:2;
uint32 mpeg_version:2;
uint32 sync:11;`,
    littleEndian: false,
  },
  {
    extensions: ["aac"],
    family: "AAC ADTS",
    scope: "Seven raw ADTS header bytes. Packed parameters and ID3 tags are not decoded.",
    fields: "uint8 fixed_header[7];",
  },
  {
    extensions: ["ac3"],
    family: "Dolby AC-3",
    scope: "AC-3 synchronization header and packed stream parameters.",
    fields: `uint16 syncword;
uint16 crc1;
uint8 frame_size_code:6;
uint8 sample_rate_code:2;
uint8 bitstream_mode:3;
uint8 bitstream_id:5;`,
    littleEndian: false,
  },
  {
    extensions: ["mpg"],
    family: "MPEG program stream",
    scope:
      "First MPEG start code and packed clock/mux prefix. MPEG-1 and MPEG-2 pack layouts differ.",
    fields: `uint32 start_code;
uint8 pack_header_prefix[8];`,
    littleEndian: false,
  },
  {
    extensions: ["mts"],
    family: "MPEG transport stream",
    scope:
      "First MPEG transport packet header at offset zero with packed flags. M2TS timestamp prefixes require an adapted layout.",
    fields: `uint8 sync;
uint16> pid:13;
uint16> transport_priority:1;
uint16> payload_unit_start:1;
uint16> transport_error:1;
uint8 continuity_counter:4;
uint8 adaptation_control:2;
uint8 scrambling_control:2;`,
  },
  {
    extensions: ["mxf"],
    family: "MXF",
    scope:
      "First KLV universal label and initial BER length octet. Long-form BER lengths and essence are not decoded.",
    fields: `uint8 universal_label[16];
uint8 ber_length_first_byte;`,
  },
  {
    extensions: ["bpg"],
    family: "BPG",
    scope:
      "BPG fixed header. Image dimensions use variable-length integers and HEVC payloads remain encoded.",
    fields: `uint8 signature[4];
uint8 pixel_format_and_depth;
uint8 color_space_and_flags;`,
  },
  {
    extensions: ["flif"],
    family: "FLIF",
    scope:
      "FLIF fixed signature and channel/depth codes. Variable-length dimensions and compressed pixels are not decoded.",
    fields: `char signature[4];
uint8 channel_and_flags;
uint8 bytes_per_channel;`,
  },
  {
    extensions: ["j2c"],
    family: "JPEG 2000 codestream",
    scope:
      "JPEG 2000 SIZ marker with reference grid, tiling and per-component precision/subsampling.",
    fields: `uint16 start_of_codestream;
uint16 size_marker;
uint16 size_segment_length;
uint16 capabilities;
uint32 reference_width;
uint32 reference_height;
uint32 image_x_offset;
uint32 image_y_offset;
uint32 tile_width;
uint32 tile_height;
uint32 tile_x_offset;
uint32 tile_y_offset;
uint16 component_count;
component components[component_count];`,
    types: `struct component {
    uint8 precision_and_sign;
    uint8 horizontal_subsampling;
    uint8 vertical_subsampling;
};`,
    littleEndian: false,
  },
  {
    extensions: ["jxl"],
    family: "JPEG XL",
    scope:
      "JPEG XL two-byte signature prefix only. Boxed containers and bit-packed image metadata are not decoded.",
    fields: "uint16 signature;",
    littleEndian: false,
  },
  {
    extensions: ["mpc"],
    family: "Musepack",
    scope: "Musepack SV7/SV8 signature and version marker. Encoded audio packets are not decoded.",
    fields: `char signature[3];
uint8 version_or_signature_tail;`,
  },
  {
    extensions: ["arrow"],
    family: "Arrow IPC",
    scope:
      "Arrow file magic and alignment padding. IPC metadata uses FlatBuffers and is not decoded.",
    fields: `char signature[6];
uint8 alignment_padding[2];`,
  },
  {
    extensions: ["parquet"],
    family: "Parquet",
    scope:
      "Parquet magic. The schema and row groups are described by a Thrift footer, not a fixed leading header.",
    fields: "char signature[4];",
  },
  {
    extensions: ["avro"],
    family: "Avro object container",
    scope:
      "Object-container magic and version. Metadata and records use Avro variable-length encoding.",
    fields: `char signature[3];
uint8 version;`,
  },
  {
    extensions: ["alias"],
    family: "Apple bookmark",
    scope: "Apple bookmark fixed header. Bookmark records and tables are not followed.",
    fields: `char signature[4];
uint32 total_size;
uint32 version;
uint32 data_offset;`,
  },
  {
    extensions: ["dmg"],
    family: "Apple disk image",
    scope:
      "Leading zlib framing recognized by file-type. The UDIF trailer and filesystem are not decoded.",
    fields: `uint8 compression_method_and_window;
uint8 compression_flags;`,
  },
  {
    extensions: ["mie"],
    family: "Meta information encapsulation",
    scope: "MIE framing and signature prefix. Nested metadata records are not decoded.",
    fields: `uint8 framing[4];
char signature[4];`,
    coverage: "prefix",
  },
  {
    extensions: ["dwg"],
    family: "AutoCAD drawing",
    scope:
      "DWG version signature. Object and section layouts are proprietary and version-specific.",
    fields: "char version[6];",
    coverage: "prefix",
  },
  {
    extensions: ["indd"],
    family: "InDesign",
    scope: "InDesign format signature. Page/object records are not decoded.",
    fields: "uint8 signature[16];",
    coverage: "prefix",
  },
  {
    extensions: ["jmp"],
    family: "JMP data",
    scope: "JMP byte-order signature. Column and row data are not decoded.",
    fields: "uint8 signature[16];",
    coverage: "prefix",
  },
  {
    extensions: ["sav"],
    family: "SPSS system file",
    scope: "SPSS system-file header. Dictionary records and compressed cases are not decoded.",
    fields: `char signature[4];
char product[60];
int32 layout_code;
int32 case_size;
int32 compression;
int32 weight_index;
int32 case_count;
double bias;
char creation_date[9];
char creation_time[8];
char label[64];
uint8 padding[3];`,
  },
  {
    extensions: ["dat"],
    family: "Windows registry hive",
    scope: "Registry hive base-block prefix. Hive bins and key/value cells are not followed.",
    fields: `char signature[4];
uint32 primary_sequence;
uint32 secondary_sequence;
uint64 timestamp;
uint32 major_version;
uint32 minor_version;
uint32 file_type;
uint32 file_format;
uint32 root_cell_offset;
uint32 hive_bins_size;
uint32 clustering_factor;
wchar< file_name_utf16le[32];`,
  },
  {
    extensions: ["amr"],
    family: "AMR audio",
    scope:
      "Six-byte AMR narrowband signature prefix. Wideband extensions and speech frames are not decoded.",
    fields: "char signature[6];",
  },
  {
    extensions: ["mkv", "webm"],
    family: "EBML",
    scope:
      "EBML signature and first encoded size octet only. Variable-length elements are not decoded.",
    fields: "uint32 signature;",
    littleEndian: false,
  },
  {
    extensions: ["pdf"],
    family: "PDF",
    scope:
      "PDF signature and version. PDF dictionaries, decimal offsets and compressed cross-reference streams require a text/container parser; they are not binary C fields.",
    fields: `char signature[5];
char version[3];`,
    coverage: "prefix",
  },
  {
    extensions: ["rtf"],
    family: "Rich Text Format",
    scope: "RTF header only. The document body is a text grammar, not a fixed binary structure.",
    fields: `char signature[5];
char version[1];`,
    coverage: "prefix",
  },
  {
    extensions: ["ps", "eps"],
    family: "PostScript",
    scope:
      "Two-byte PostScript/EPS prefix only. Binary preview directories and PostScript programs are not decoded.",
    fields: "char signature[2];",
    coverage: "prefix",
  },
  {
    extensions: ["xml"],
    family: "XML",
    scope: "XML declaration prefix only; text encoding and XML elements are not decoded.",
    fields: "uint8 declaration_prefix[16];",
    coverage: "prefix",
  },
  {
    extensions: ["ics"],
    family: "iCalendar",
    scope:
      "BEGIN:VCALENDAR header only. Calendar properties and folded text lines are not decoded.",
    fields: "char calendar_header[15];",
    coverage: "prefix",
  },
  {
    extensions: ["vcf"],
    family: "vCard",
    scope: "BEGIN:VCARD header only. Contact properties and folded text lines are not decoded.",
    fields: "char contact_header[11];",
    coverage: "prefix",
  },
  {
    extensions: ["vtt"],
    family: "WebVTT",
    scope: "WebVTT signature only. Cue timestamps and subtitle text are not decoded.",
    fields: "char signature[6];",
    coverage: "prefix",
  },
  {
    extensions: ["reg"],
    family: "Registry export",
    scope: "UTF-16 registry-export header only. Registry keys and values are text records.",
    fields: `uint16 byte_order_mark;
wchar< version_header[35];`,
    coverage: "prefix",
  },
  {
    extensions: ["skp"],
    family: "SketchUp",
    scope: "SketchUp signature prefix only. Proprietary model entities are not decoded.",
    fields: `uint16 byte_order_mark;
uint16 marker;
wchar< signature[13];`,
    coverage: "prefix",
  },
  {
    extensions: ["pgp"],
    family: "OpenPGP",
    scope: "First OpenPGP packet-header octet only. Lengths and packet bodies are not decoded.",
    fields: "uint8 packet_header;",
  },
];

// Build the lookups once. Reject duplicate registrations so later entries cannot silently replace one.
const formatsByExtension = new Map<
  string,
  { format: FormatDefinition; canonicalExtension: string }
>();
const samplesByExtension = new Map<string, InspectorExample>();
for (const format of formatDefinitions) {
  for (const [extension, canonicalExtension] of [
    ...format.extensions.map((extension) => [extension, extension] as const),
    ...Object.entries(format.aliases ?? {}),
  ]) {
    if (formatsByExtension.has(extension))
      throw new Error("Duplicate format extension: " + extension);
    if (!format.extensions.includes(canonicalExtension))
      throw new Error("Unknown alias target: " + canonicalExtension);
    formatsByExtension.set(extension, { format, canonicalExtension });
  }

  for (const sample of format.samples ?? []) {
    if (formatsByExtension.get(sample.extension)?.format !== format)
      throw new Error("Sample extension is not registered with its format: " + sample.extension);
    if (samplesByExtension.has(sample.extension))
      throw new Error("Duplicate sample extension: " + sample.extension);
    samplesByExtension.set(sample.extension, {
      ...sample,
      coverage: format.coverage ?? "structure",
    });
  }
}

export const detectorExtensions = formatDefinitions.flatMap((format) => format.extensions);
export const detectableFormatCount = detectorExtensions.length;
export const sampleExamples = [...samplesByExtension.values()];

/** Select a fixed layout from the extension; the file's bytes never change its declarations. */
export function schemaForFile(extension: string): InspectorExample {
  const registration = formatsByExtension.get(extension);
  if (!registration) throw new Error("No schema registered for detected type " + extension);
  const { format, canonicalExtension: ext } = registration;
  const name = "file_" + ext.replace(/[^a-zA-Z0-9_]/g, "_");
  const definition = formatLayout(
    "// " +
      ext.toUpperCase() +
      " - " +
      format.family +
      "\n// " +
      format.scope +
      "\n// Standalone CStruct. All conditions, lengths and pointer reads execute in the library.\n" +
      "// No file scans, generated offsets, record discovery or merged parse results.\n" +
      (format.types ?? "") +
      "\nstruct " +
      name +
      " { " +
      format.fields +
      " };\nstruct root { " +
      name +
      " header; };",
  );
  return {
    id: "detected-" + ext,
    extension: ext,
    title: ext.toUpperCase() + " · " + format.family,
    description: format.family,
    coverage: format.coverage ?? "structure",
    definition,
    binaryHex: "",
    rootType: "root",
    parserOptions: {
      aligned: false,
      littleEndian: format.littleEndian ?? true,
      pointerSize: format.pointerSize ?? 4,
    },
    documentation: { summary: format.scope },
    sourceFixture: "",
  };
}

// The sidebar prefers a sample for that extension; other entries ask the user to supply a file.
export const schemaCatalog: InspectorExample[] = [...formatsByExtension.keys()]
  .map(
    (extension) =>
      samplesByExtension.get(extension) ?? {
        ...schemaForFile(extension),
        id: "schema-" + extension,
        extension,
        schemaOnly: true,
      },
  )
  .sort((a, b) => a.extension!.localeCompare(b.extension!, undefined, { sensitivity: "base" }));

export function rawFileSchema(): InspectorExample {
  return {
    id: "detected-unknown",
    title: "Unknown format",
    description: "Raw prefix",
    coverage: "prefix",
    definition: formatLayout(
      "// Unrecognized file. Select an example or edit this schema.\nstruct root { uint8 prefix[1]; };",
    ),
    binaryHex: "",
    rootType: "root",
    parserOptions: { aligned: false, littleEndian: true, pointerSize: 4 },
    documentation: { summary: "Raw prefix; no file type recognized." },
    sourceFixture: "",
  };
}
