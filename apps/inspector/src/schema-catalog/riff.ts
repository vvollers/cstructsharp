/**
 * RIFF containers (WAV, AVI, WebP, QCP), with the WAV teaching sample.
 */

import { sampleParserOptions, type FormatDefinition } from "./types";

export const formats: FormatDefinition[] = [
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
      },
    ],
  },
];
