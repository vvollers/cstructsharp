/**
 * The built-in "well-known binary format" example catalog. Every definition and binaryHex value here is
 * verified byte-for-byte against the real managed library by a matching test in
 * tests/CStructSharpTests/WellKnownFormatFixtures.cs (see sourceFixture) - this file must stay in sync with that
 * test file by hand; neither is generated from the other.
 */

export interface FormatParserOptions {
  aligned: boolean;
  littleEndian: boolean;
  pointerSize: number;
  addressingMode?: "Absolute" | "Relative";
}

export interface FormatExample {
  id: string;
  title: string;
  definition: string;
  binaryHex: string;
  rootType: string;
  parserOptions: FormatParserOptions;
  documentation: { summary: string };
  sourceFixture: string;
}

const defaultParserOptions: FormatParserOptions = {
  aligned: false,
  littleEndian: true,
  pointerSize: 8,
};

export const formats: FormatExample[] = [
  {
    id: "bmp",
    title: "BMP - bitmap header",
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
    parserOptions: defaultParserOptions,
    documentation: {
      summary:
        "A minimal 54-byte BMP: the 14-byte BITMAPFILEHEADER plus the 40-byte BITMAPINFOHEADER, with no pixel data. Nested composites and an enum for the compression method.",
    },
    sourceFixture: "WellKnownFormatFixtures.Bmp_FileAndInfoHeaders_DecodeExpectedFields",
  },
  {
    id: "wav",
    title: "WAV - RIFF/WAVE header",
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
    parserOptions: defaultParserOptions,
    documentation: {
      summary:
        "The classic 44-byte canonical WAV header: RIFF/WAVE container, a PCM fmt subchunk (44.1kHz stereo 16-bit), and an empty data subchunk. FourCC tags decode as fixed char[4] buffers.",
    },
    sourceFixture: "WellKnownFormatFixtures.Wav_RiffFmtAndDataChunks_DecodeExpectedFields",
  },
  {
    id: "zip",
    title: "ZIP - local file header",
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
    parserOptions: defaultParserOptions,
    documentation: {
      summary:
        'Reads the first ZIP local file header at byte 0, including its filename and raw extra fields. Files of any supported size are read on demand. This is not an archive extractor: empty archives, self-extracting prefixes, and split archives need a different starting layout. Data-descriptor entries can leave CRC/sizes unset here; ZIP64 sizes live in extra fields. Filename bytes are shown as raw characters, without UTF-8/CP437 decoding.',
    },
    sourceFixture:
      "WellKnownFormatFixtures.Zip_LocalFileHeader_DecodesBitflagsAndRuntimeLengthName",
  },
  {
    id: "png",
    title: "PNG - signature + IHDR chunk",
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
    parserOptions: { ...defaultParserOptions, littleEndian: false },
    documentation: {
      summary:
        "The 8-byte PNG signature plus a 1x1 RGB IHDR chunk. PNG is the one big-endian format in this catalog, modeled with the global littleEndian: false option since every multi-byte field is big-endian.",
    },
    sourceFixture: "WellKnownFormatFixtures.Png_SignatureAndIhdrChunk_DecodeBigEndianFields",
  },
  {
    id: "jpg",
    title: "JPG - SOI + JFIF APP0 segment",
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
    parserOptions: defaultParserOptions,
    documentation: {
      summary:
        "A JPEG SOI marker plus a standard JFIF APP0 segment (72 DPI, no thumbnail). Scoped to the JFIF header specifically - a full JPEG is a variable chain of marker segments, not representable as one fixed struct. Uses explicit per-field '>' suffixes rather than a global option, since only these fields are big-endian.",
    },
    sourceFixture: "WellKnownFormatFixtures.Jpg_SoiAndJfifApp0Segment_DecodeExpectedFields",
  },
  {
    id: "pe-exe",
    title: "EXE - PE image (DOS header + pointer)",
    definition: `struct pe_header {
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
};`,
    binaryHex:
      "4d 5a 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 40 00 00 00 50 45 00 00 64 86 01 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 22 00 0b 02",
    rootType: "root",
    parserOptions: { ...defaultParserOptions, pointerSize: 4, addressingMode: "Absolute" },
    documentation: {
      summary:
        "A minimal 90-byte PE image: a real 64-byte DOS header whose e_lfanew is a real CStruct pointer field (absolute addressing) dereferencing to a COFF file header + optional header magic - the flagship pointer-following showcase, using a real well-known offset (0x3C) from a real well-known format.",
    },
    sourceFixture:
      "WellKnownFormatFixtures.Pe_DosHeaderPointerToCoffHeader_DecodesAcrossExeAndDll (EXE)",
  },
  {
    id: "pe-dll",
    title: "DLL - PE image (DOS header + pointer)",
    definition: `struct pe_header {
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
};`,
    binaryHex:
      "4d 5a 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 40 00 00 00 50 45 00 00 64 86 01 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 22 20 0b 02",
    rootType: "root",
    parserOptions: { ...defaultParserOptions, pointerSize: 4, addressingMode: "Absolute" },
    documentation: {
      summary:
        "The same PE definition as the EXE example - a DLL is a PE file with the IMAGE_FILE_DLL characteristic bit (0x2000) set in its COFF header, the only byte that differs from the EXE sample.",
    },
    sourceFixture:
      "WellKnownFormatFixtures.Pe_DosHeaderPointerToCoffHeader_DecodesAcrossExeAndDll (DLL)",
  },
  {
    id: "ico",
    title: "ICO - icon directory",
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
    parserOptions: defaultParserOptions,
    documentation: {
      summary:
        "A 2-entry ICO directory (16x16 and 32x32 images). image_count drives the length of the trailing entries array - a runtime-sized array of a composite (struct) element type, not just a byte array.",
    },
    sourceFixture: "WellKnownFormatFixtures.Ico_IconDirectory_DecodesCountDrivenEntryArray",
  },
  {
    id: "tar",
    title: "TAR - POSIX ustar header",
    definition: `struct root {
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
};`,
    binaryHex:
      "68 65 6c 6c 6f 2e 74 78 74 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 30 30 30 30 36 34 34 00 30 30 30 30 30 30 30 00 30 30 30 30 30 30 30 00 30 30 30 30 30 30 30 30 30 31 32 00 30 30 30 30 30 30 30 30 30 30 30 00 30 31 31 35 35 36 00 20 30 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 75 73 74 61 72 00 30 30 75 73 65 72 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 67 72 6f 75 70 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00",
    rootType: "root",
    parserOptions: defaultParserOptions,
    documentation: {
      summary:
        'A real 512-byte POSIX ustar header for a file named "hello.txt", with a correctly computed checksum. Fixed-width ASCII/octal text fields throughout - a different complexity flavor from the other binary-integer formats.',
    },
    sourceFixture: "WellKnownFormatFixtures.Tar_UstarHeader_DecodesFixedWidthTextFields",
  },
];
