/**
 * Other images: JPEG 2000, TIFF and camera raw files, GIF, Photoshop, textures, colour profiles, medical images
 * (DICOM) and newer codecs.
 */

import type { FormatDefinition } from "./types";

// DICOM stores its length in two possible widths. Both branches use this same value decoder.
const dicomValueFields = `if (
    value_representation == dicom_vr.AE ||
    value_representation == dicom_vr.AS ||
    value_representation == dicom_vr.CS ||
    value_representation == dicom_vr.DA ||
    value_representation == dicom_vr.DS ||
    value_representation == dicom_vr.DT ||
    value_representation == dicom_vr.IS ||
    value_representation == dicom_vr.LO ||
    value_representation == dicom_vr.LT ||
    value_representation == dicom_vr.PN ||
    value_representation == dicom_vr.SH ||
    value_representation == dicom_vr.ST ||
    value_representation == dicom_vr.TM ||
    value_representation == dicom_vr.UC ||
    value_representation == dicom_vr.UI ||
    value_representation == dicom_vr.UR ||
    value_representation == dicom_vr.UT
) {
    char text[value_length];
} else {
    switch (value_representation) {
        case dicom_vr.US: {
            if (value_length / 2 * 2 == value_length) {
                uint16 values_US[value_length / 2];
            } else {
                uint8 malformed_US[value_length];
            }
        } case dicom_vr.SS: {
            if (value_length / 2 * 2 == value_length) {
                int16 values_SS[value_length / 2];
            } else {
                uint8 malformed_SS[value_length];
            }
        } case dicom_vr.UL: {
            if (value_length / 4 * 4 == value_length) {
                uint32 values_UL[value_length / 4];
            } else {
                uint8 malformed_UL[value_length];
            }
        } case dicom_vr.SL: {
            if (value_length / 4 * 4 == value_length) {
                int32 values_SL[value_length / 4];
            } else {
                uint8 malformed_SL[value_length];
            }
        } case dicom_vr.FL: {
            if (value_length / 4 * 4 == value_length) {
                float32 values_FL[value_length / 4];
            } else {
                uint8 malformed_FL[value_length];
            }
        } case dicom_vr.FD: {
            if (value_length / 8 * 8 == value_length) {
                float64 values_FD[value_length / 8];
            } else {
                uint8 malformed_FD[value_length];
            }
        } case dicom_vr.UV: {
            if (value_length / 8 * 8 == value_length) {
                uint64 values_UV[value_length / 8];
            } else {
                uint8 malformed_UV[value_length];
            }
        } case dicom_vr.SV: {
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

export const formats: FormatDefinition[] = [
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
    extensions: ["dcm"],
    family: "DICOM",
    scope:
      "DICOM file preamble and first explicit-VR metadata tag. Transfer syntax determines the later dataset layout.",
    fields: `uint8 preamble[128];
char signature[4];
uint16 first_tag_group;
uint16 first_tag_element;
dicom_vr value_representation;
// Most value representations store a 2-byte length; the others reserve 2 bytes and store a 4-byte length.
if (!(
    value_representation == dicom_vr.AE ||
    value_representation == dicom_vr.AS ||
    value_representation == dicom_vr.AT ||
    value_representation == dicom_vr.CS ||
    value_representation == dicom_vr.DA ||
    value_representation == dicom_vr.DS ||
    value_representation == dicom_vr.DT ||
    value_representation == dicom_vr.FL ||
    value_representation == dicom_vr.FD ||
    value_representation == dicom_vr.IS ||
    value_representation == dicom_vr.LO ||
    value_representation == dicom_vr.LT ||
    value_representation == dicom_vr.PN ||
    value_representation == dicom_vr.SH ||
    value_representation == dicom_vr.SL ||
    value_representation == dicom_vr.SS ||
    value_representation == dicom_vr.ST ||
    value_representation == dicom_vr.TM ||
    value_representation == dicom_vr.UI ||
    value_representation == dicom_vr.UL ||
    value_representation == dicom_vr.US
)) {
    struct { uint16 reserved; uint32 value_length; ${dicomValueFields} } long_value;
} else {
    struct { uint16 value_length; ${dicomValueFields} } short_value;
}`,
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
];
