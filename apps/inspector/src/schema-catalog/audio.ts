/**
 * Other audio: Ogg, lossless and lossy codecs, MIDI, AIFF and tracker modules.
 */

import type { FormatDefinition } from "./types";

export const formats: FormatDefinition[] = [
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
    extensions: ["mpc"],
    family: "Musepack",
    scope: "Musepack SV7/SV8 signature and version marker. Encoded audio packets are not decoded.",
    fields: `char signature[3];
uint8 version_or_signature_tail;`,
  },
  {
    extensions: ["amr"],
    family: "AMR audio",
    scope:
      "Six-byte AMR narrowband signature prefix. Wideband extensions and speech frames are not decoded.",
    fields: "char signature[6];",
  },
];
