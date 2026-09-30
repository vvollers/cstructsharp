/**
 * Video and multimedia containers: ISO base media (MP4, MOV, HEIC), Matroska/WebM, MPEG streams and others.
 */

import type { FormatDefinition } from "./types";

export const formats: FormatDefinition[] = [
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
    extensions: ["mkv", "webm"],
    family: "EBML",
    scope:
      "EBML signature and first encoded size octet only. Variable-length elements are not decoded.",
    fields: "uint32 signature;",
    littleEndian: false,
  },
];
