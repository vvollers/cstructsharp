/** File-specific outer-container layouts. Scans are bounded to the supplied preview. */
export interface ExpandedLayout {
  fields: string;
  types: string;
  littleEndian: boolean;
  pointerSize: number;
}

export interface ExpansionReview {
  scope: string;
  features: string;
}
export const expansionReviews: Record<string, ExpansionReview> = {};
function review(exts: string, scope: string, features: string): void {
  for (const ext of exts.split(" ")) expansionReviews[ext] = { scope, features };
}
review(
  "ttf otf woff",
  "Font directory with typed head table pointers for visible, uncompressed entries. Other tables keep their offsets; glyph and shaping programs remain encoded.",
  "Typed pointers; signed bounds; fixed-point storage; file-selected directory records",
);
review(
  "wasm",
  "Up to 32 complete sections in the preview, specializing LEB128 length widths. Section IDs are named; instruction bodies remain bytes.",
  "Enums; nested structs; file-selected array lengths",
);
review(
  "lz4 zst",
  "Frame descriptor flags and optional dictionary/content-size fields, plus first block framing. Skippable and legacy frames retain separate framing; blocks remain compressed.",
  "Bitfields; optional integer widths; little endian",
);
review(
  "zip epub xpi docx pptx xlsx odt ods odp 3mf vsdx apk potx xltx dotx xltm ott ots otp odg otg xlsm docm dotm potm pptm jar ppsm ppsx key numbers pages",
  "ZIP compression enum, named flags, DOS timestamps and up to 16 complete local entries in the preview. Stops before data-descriptor/ZIP64 streaming boundaries; no decompression.",
  "Enums; packed bitfields; nested structs; count-driven arrays",
);
review(
  "png apng",
  "IHDR plus up to 32 following chunks in the preview, including palettes, gamma, resolution, time and APNG animation/frame control. Pixel data stays compressed.",
  "Enums; nested records; expression-sized arrays; explicit big endian",
);
review(
  "wav avi webp qcp",
  "Up to 32 RIFF chunks in the preview, with WAVE format and WebP extended/lossless metadata; AVI LIST contents remain bytes. Stops before a chunk payload outside the preview.",
  "Nested structs; runtime lengths; padding expressions; bitfields",
);
review(
  "mp4 m4a m4v m4p m4b f4v f4p f4b f4a 3gp 3g2 mov heic avif cr3 jp2 jpm jpx mj2",
  "Up to 32 complete outer boxes in the preview, including brands. Extended-size boxes are handled; zero-to-end boxes expose their header; media data remains encoded.",
  "Nested records; 32/64-bit variants; big endian; arrays",
);
review(
  "elf",
  "ELF header and bounded program/section tables through on-disk offsets, respecting class, byte order and declared entry sizes. Extended numbering remains unexpanded.",
  "Typed pointers; arrays of structs; 32/64-bit layouts",
);
review(
  "exe",
  "DOS header, PE/COFF header, PE32/PE32+ optional header, bounded data directories and sections. RVA values remain RVAs; no incorrect raw-file pointer conversion.",
  "Pointers; nested records; architecture-dependent widths",
);
review(
  "tif cr2 arw dng nef orf rw2",
  "TIFF/BigTIFF first IFD with named tag/type enums. Values remain inline-or-offset because interpretation depends on type and count; no unsafe pointer reinterpretation.",
  "Enums; pointer widths; count-driven arrays",
);
review(
  "sqlite",
  "Database header and first B-tree page header/cell-offset array when present; interior pages include the rightmost child. Cell varints and records remain encoded.",
  "Enums; inline structs; count-driven arrays; big endian",
);
review(
  "stl",
  "Binary STL triangle records with normal and three vertices, capped at 128 triangles for inspection.",
  "Float32; multidimensional arrays; nested structs",
);
review(
  "pcap",
  "PCAP global header plus up to 32 complete packet records in the preview, including timestamps, captured/original lengths and packet bytes.",
  "Runtime arrays; nested packet records; byte-order variants",
);
review(
  "glb",
  "glTF 2 header and up to 32 complete JSON/BIN chunk records in the preview. JSON is shown as text; mesh buffers remain bytes.",
  "Enums; character arrays; nested records",
);
review(
  "flac",
  "STREAMINFO with named bitfields for sample count, depth, channels and sample rate, followed by bounded metadata blocks with typed seek points and raw bytes for other metadata.",
  "64-bit bitfields; nested structs; big endian; arrays",
);
review(
  "ogg oga ogv opus spx ogm ogx",
  "Ogg page flags/lacing table plus codec identification fields for Opus or Vorbis when the first complete packet is present.",
  "Bitfields; signed gain; runtime channel-map arrays; nested records",
);
review(
  "gif",
  "GIF logical-screen bitfields, global palette and first image descriptor/local palette when adjacent. Extension blocks are not skipped speculatively.",
  "Bitfields; RGB arrays; file-selected optional fields",
);
review(
  "bmp",
  "BMP/DIB header with compression enum and V2/V3/V4/V5 channel masks, color-space endpoints, gamma and profile coordinates where declared.",
  "Enums; signed fields; fixed-size arrays; header variants",
);
review(
  "gz tar.gz Z xz mp1 mp2 mp3 ac3 mts flv",
  "Named flag/parameter bitfields replace opaque packed values while preserving on-disk widths. Optional framing is expanded where known; compressed payloads stay encoded.",
  "Low-bit-first bitfields; byte order; nested flags",
);
review(
  "mid aif icns",
  "Complete first MIDI track or ICNS element payload within the preview; AIFF COMM metadata decoded when first. Further codec/event decoding remains out of scope.",
  "Runtime lengths; character tags; sample metadata",
);
review(
  "psd",
  "Image header followed by color-mode data, image-resource bytes and layer/mask block length when the preceding sections fit the preview.",
  "Length-driven arrays; nested length-prefixed sections",
);
review(
  "crx",
  "CRX2 key/signature lengths and payloads or CRX3 signed-header bytes, selected by version. Embedded ZIP remains a separate container.",
  "Version-specific layouts; runtime arrays",
);
review(
  "blend fbx",
  "First Blender block or FBX node record, with word widths selected from the file version/header. Object/DNA and property payloads remain encoded.",
  "32/64-bit variants; nested records; byte order",
);
review(
  "ktx",
  "KTX1 header plus bounded key/value storage and first mip-level length.",
  "Runtime arrays; byte-order marker; size fields",
);
review(
  "jxr",
  "JPEG XR first image-file directory through its stored offset, including typed tag records.",
  "Typed pointers; count-driven arrays; enums",
);
review(
  "dcm",
  "First DICOM file-meta explicit-VR element length/value, distinguishing long and short VR framing. Dataset transfer-syntax decoding remains out of scope.",
  "Character tags; runtime arrays; optional length widths",
);
review(
  "it",
  "Tracker header plus a typed message pointer when present, preserving the order and instrument/sample directories.",
  "Typed string pointer; runtime arrays",
);
review(
  "chm",
  "ITSF version 2/3 section offsets and lengths after the GUIDs. Compressed topic streams remain encoded.",
  "64-bit coordinates; version-specific layout",
);
review(
  "ar deb",
  "Archive entry metadata retained; preview-bounded filenames or the first payload are exposed where ASCII sizes can be decoded unambiguously.",
  "Character arrays; file-specialized lengths; padding",
);

export function expandSchema(
  ext: string,
  input: Uint8Array,
  layout: ExpandedLayout,
): ExpandedLayout {
  const bytes = input.subarray(0, 65536);
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const has = (offset: number, length: number) =>
    Number.isSafeInteger(offset) && offset >= 0 && length >= 0 && offset + length <= bytes.length;
  const u16 = (offset: number, le = layout.littleEndian) => view.getUint16(offset, le);
  const u32 = (offset: number, le = layout.littleEndian) => view.getUint32(offset, le);
  const ascii = (offset: number, length: number) =>
    String.fromCharCode(...bytes.subarray(offset, offset + length));
  const addType = (name: string, fields: string) => {
    layout.types += ` struct ${name} { ${fields} };`;
    return name;
  };
  const replace = (from: string, to: string) => {
    layout.fields = layout.fields.replace(from, to);
  };
  const append = (fields: string) => {
    layout.fields += ` ${fields}`;
  };
  const enumType = (name: string, storage: string, members: string) => {
    layout.types += ` enum ${name} : ${storage} { ${members} };`;
    return name;
  };
  const bits = (name: string, storage: string, fields: string) =>
    addType(
      name,
      fields
        .split(",")
        .map((f) => `${storage} ${f.trim()};`)
        .join(" "),
    );

  if (expansionReviews[ext]?.scope.startsWith("ZIP")) {
    bits(
      "zip_flags",
      "uint16",
      "encrypted:1,compression_options:2,data_descriptor:1,reserved_a:2,strong_encryption:1,reserved_b:4,utf8_names:1,reserved_c:1,masked_header:1,reserved_d:2",
    );
    bits("dos_time", "uint16", "seconds_divided_by_two:5,minutes:6,hours:5");
    bits("dos_date", "uint16", "day:5,month:4,years_since_1980:7");
    enumType(
      "zip_method",
      "uint16",
      "Stored=0,Deflate=8,Deflate64=9,Bzip2=12,Lzma=14,Zstandard=93,Xz=95,Ppmd=98",
    );
    replace("uint16 flags;", "zip_flags flags;");
    replace("uint16 compression;", "zip_method compression;");
    replace(
      "uint16 modified_time; uint16 modified_date;",
      "dos_time modified_time; dos_date modified_date;",
    );
    let offset = 0,
      count = 0;
    while (count < 16 && has(offset, 30) && u32(offset, true) === 0x04034b50) {
      const flags = u16(offset + 6, true),
        size = u32(offset + 18, true);
      const length = 30 + u16(offset + 26, true) + u16(offset + 28, true) + size;
      if (flags & 8 || size === 0xffffffff || !has(offset, length)) break;
      offset += length;
      count++;
    }
    if (count) {
      const header = layout.fields;
      addType("zip_entry", `${header} uint8 compressed_payload[compressed_size];`);
      layout.fields = `zip_entry entries[${count}];`;
    }
  }

  if (ext === "png" || ext === "apng") {
    enumType(
      "png_color",
      "uint8",
      "Grayscale=0,Truecolor=2,Indexed=3,GrayscaleAlpha=4,TruecolorAlpha=6",
    );
    replace("uint8 color_type;", "png_color color_type;");
    addType("png_rgb", "uint8 red; uint8 green; uint8 blue;");
    let offset = 33;
    for (let i = 0; i < 32 && has(offset, 8); i++) {
      const length = u32(offset, false),
        tag = ascii(offset + 4, 4);
      let fields = "uint32 length; char type[4];";
      if (!has(offset, length + 12)) {
        append(`${addType(`png_next_${i}`, fields)} next_chunk;`);
        break;
      }
      const decoders: Record<string, [number, string]> = {
        gAMA: [4, "uint32 gamma_times_100000;"],
        pHYs: [9, "uint32 pixels_per_unit_x; uint32 pixels_per_unit_y; uint8 unit;"],
        acTL: [8, "uint32 frame_count; uint32 play_count;"],
        fcTL: [
          26,
          "uint32 sequence; uint32 width; uint32 height; uint32 x_offset; uint32 y_offset; uint16 delay_numerator; uint16 delay_denominator; uint8 dispose_operation; uint8 blend_operation;",
        ],
        tIME: [7, "uint16 year; uint8 month; uint8 day; uint8 hour; uint8 minute; uint8 second;"],
        sRGB: [1, "uint8 rendering_intent;"],
      };
      if (tag === "PLTE" && length % 3 === 0) fields += " png_rgb colors[length / 3];";
      else if (decoders[tag]?.[0] === length) fields += decoders[tag]![1];
      else fields += " uint8 payload[length];";
      fields += " uint32 crc32;";
      append(`${addType(`png_chunk_${i}`, fields)} chunk_${i};`);
      offset += length + 12;
      if (tag === "IEND") break;
    }
  }

  if (["wav", "avi", "webp", "qcp"].includes(ext) && has(0, 20)) {
    layout.fields = "char signature[4]; uint32 file_size_minus_8; char form_type[4];";
    let offset = 12;
    const end = Math.min(bytes.length, 8 + u32(4, true));
    for (let i = 0; i < 32 && offset + 8 <= end; i++) {
      const length = u32(offset + 4, true),
        tag = ascii(offset, 4);
      let fields = "char type[4]; uint32 length;";
      if (offset + 8 + length > end) {
        append(`${addType(`riff_next_${i}`, fields)} next_chunk;`);
        break;
      }
      let consumed = 0;
      if (tag === "fmt " && ext === "wav" && length >= 16) {
        enumType(`wave_format_${i}`, "uint16", "Pcm=1,IeeeFloat=3,ALaw=6,MuLaw=7,Extensible=65534");
        fields += ` wave_format_${i} format; uint16 channels; uint32 sample_rate; uint32 byte_rate; uint16 block_alignment; uint16 bits_per_sample;`;
        consumed = 16;
        if (length >= 18) {
          fields += " uint16 extension_size;";
          consumed = 18;
        }
        if (length >= 40 && u16(offset + 8, true) === 0xfffe) {
          fields += " uint16 valid_bits_per_sample; uint32 channel_mask; uint8 subformat_guid[16];";
          consumed = 40;
        }
      } else if (tag === "avih" && length >= 56) {
        fields +=
          " uint32 microseconds_per_frame; uint32 maximum_bytes_per_second; uint32 padding_granularity; uint32 flags; uint32 frame_count; uint32 initial_frames; uint32 stream_count; uint32 suggested_buffer_size; uint32 width; uint32 height; uint32 reserved[4];";
        consumed = 56;
      } else if (tag === "VP8X" && length >= 10) {
        fields +=
          " uint8 reserved_low:1; uint8 animation:1; uint8 xmp:1; uint8 exif:1; uint8 alpha:1; uint8 icc:1; uint8 reserved_high:2; uint8 reserved[3]; uint8 width_minus_one_le[3]; uint8 height_minus_one_le[3];";
        consumed = 10;
      } else if (tag === "VP8L" && length >= 5) {
        fields +=
          " uint8 signature; uint32 width_minus_one:14; uint32 height_minus_one:14; uint32 alpha_used:1; uint32 version:3;";
        consumed = 5;
      }
      fields += ` uint8 payload[length - ${consumed}]; uint8 padding[length & 1];`;
      if (offset + 8 + length + (length & 1) > end) break;
      append(`${addType(`riff_chunk_${i}`, fields)} chunk_${i};`);
      offset += 8 + length + (length & 1);
    }
  }

  if (ext === "elf" && has(0, 52) && (bytes[4] === 1 || (bytes[4] === 2 && has(0, 64)))) {
    const wide = bytes[4] === 2,
      word = wide ? "uint64" : "uint32";
    layout.pointerSize = wide ? 8 : 4;
    const tableFields: [string, number, string, number][] = [
      [
        "program",
        wide ? 54 : 42,
        wide
          ? "uint32 kind; uint32 flags; uint64 offset; uint64 virtual_address; uint64 physical_address; uint64 file_size; uint64 memory_size; uint64 alignment;"
          : "uint32 kind; uint32 offset; uint32 virtual_address; uint32 physical_address; uint32 file_size; uint32 memory_size; uint32 flags; uint32 alignment;",
        wide ? 56 : 32,
      ],
      [
        "section",
        wide ? 58 : 46,
        `uint32 name_offset; uint32 kind; ${word} flags; ${word} address; ${word} offset; ${word} size; uint32 link; uint32 info; ${word} alignment; ${word} entry_size;`,
        wide ? 64 : 40,
      ],
    ];
    for (const [name, at, fields, minimum] of tableFields) {
      const size = u16(at),
        count = u16(at + 2);
      if (count > 0 && count < 0xffff && size >= minimum && size <= 4096) {
        addType(`elf_${name}`, `${fields} uint8 extension[${size - minimum}];`);
        addType(`elf_${name}_table`, `elf_${name} entries[${Math.min(count, 128)}];`);
        replace(`${word} ${name}_headers_offset;`, `elf_${name}_table *${name}_headers;`);
      }
    }
  }

  if (ext === "exe" && has(0, 64)) {
    const at = u32(60, true);
    if (has(at, 26) && ascii(at, 4) === "PE\0\0") {
      const magic = u16(at + 24, true),
        wide = magic === 0x20b;
      const minimum = wide ? 112 : 96,
        size = u16(at + 20, true);
      if ((magic === 0x10b || wide) && size >= minimum && has(at + 24, minimum)) {
        const word = wide ? "uint64" : "uint32";
        const count = Math.min(
          u32(at + 24 + minimum - 4, true),
          Math.floor((size - minimum) / 8),
          16,
        );
        addType("pe_data_directory", "uint32 rva_or_file_offset; uint32 size;");
        addType(
          "pe_optional",
          `uint16 magic; uint8 linker_major; uint8 linker_minor; uint32 code_size; uint32 initialized_data_size; uint32 uninitialized_data_size; uint32 entry_point_rva; uint32 code_base_rva; ${wide ? "" : "uint32 data_base_rva;"} ${word} image_base; uint32 section_alignment; uint32 file_alignment; uint16 os_major; uint16 os_minor; uint16 image_major; uint16 image_minor; uint16 subsystem_major; uint16 subsystem_minor; uint32 win32_version; uint32 image_size; uint32 headers_size; uint32 checksum; uint16 subsystem; uint16 dll_characteristics; ${word} stack_reserve; ${word} stack_commit; ${word} heap_reserve; ${word} heap_commit; uint32 loader_flags; uint32 directory_count; pe_data_directory directories[${count}]; uint8 remaining[${size - minimum - count * 8}];`,
        );
        layout.types = layout.types.replace(
          "uint8 optional_header[optional_header_size];",
          "pe_optional optional_header;",
        );
      }
    } else if (has(at, 4)) {
      // file-type's EXE detector also recognizes DOS-only and NE executables.
      replace("pe_header *pe;", "uint32 new_header_offset;");
    }
  }

  if (["tif", "cr2", "arw", "dng", "nef", "orf", "rw2", "jxr"].includes(ext)) {
    enumType(
      "tiff_tag_id",
      "uint16",
      "ImageWidth=256,ImageLength=257,BitsPerSample=258,Compression=259,Photometric=262,StripOffsets=273,SamplesPerPixel=277,RowsPerStrip=278,StripByteCounts=279,XResolution=282,YResolution=283,Software=305,DateTime=306,ExifIfd=34665,GpsIfd=34853",
    );
    enumType(
      "tiff_value_type",
      "uint16",
      "Byte=1,Ascii=2,Short=3,Long=4,Rational=5,SByte=6,Undefined=7,SShort=8,SLong=9,SRational=10,Float=11,Double=12,Ifd=13,Long8=16,SLong8=17,Ifd8=18",
    );
    if (ext === "jxr") {
      addType(
        "tag",
        "tiff_tag_id id; tiff_value_type data_type; uint32 count; uint32 value_or_offset;",
      );
      addType("ifd", "uint16 entry_count; tag entries[entry_count]; uint32 next_directory;");
      replace("uint32 first_directory;", "ifd *first_directory;");
    } else
      layout.types = layout.types.replace(
        "uint16 id; uint16 data_type;",
        "tiff_tag_id id; tiff_value_type data_type;",
      );
  }

  if (ext === "stl") {
    addType("stl_triangle", "float32 normal[3]; float32 vertices[3][3]; uint16 attribute;");
    const count = has(80, 4) ? Math.min(u32(80, true), 128) : 1;
    append(`stl_triangle triangles[${count}];`);
  }
  if (ext === "sqlite" && has(100, 8) && [2, 5, 10, 13].includes(bytes[100]!)) {
    enumType("btree_kind", "uint8", "InteriorIndex=2,InteriorTable=5,LeafIndex=10,LeafTable=13");
    const interior = bytes[100] === 2 || bytes[100] === 5;
    append(
      `struct { btree_kind type; uint16 first_freeblock; uint16 cell_count; uint16 cell_content_offset; uint8 fragmented_bytes; ${interior ? "uint32 rightmost_child;" : ""} uint16 cell_offsets[cell_count]; } first_page;`,
    );
  }
  if (ext === "pcap") {
    addType(
      "capture_packet",
      "uint32 timestamp_seconds; uint32 timestamp_fraction; uint32 captured_length; uint32 original_length; uint8 data[captured_length];",
    );
    let offset = 24,
      count = 0;
    while (count < 32 && has(offset, 16)) {
      const length = u32(offset + 8);
      if (!has(offset + 16, length)) break;
      offset += 16 + length;
      count++;
    }
    if (count) append(`capture_packet packets[${count}];`);
  }

  if (ext === "glb" && has(0, 20) && u32(4, true) === 2) {
    layout.fields = "char signature[4]; uint32 version; uint32 total_length;";
    enumType("glb_chunk_kind", "uint32", "Json=1313821514,Binary=5130562");
    let offset = 12;
    const end = Math.min(bytes.length, u32(8, true));
    for (let i = 0; i < 32 && offset + 8 <= end; i++) {
      const length = u32(offset, true),
        json = u32(offset + 4, true) === 0x4e4f534a;
      if (offset + 8 + length > end) {
        append("uint32 next_chunk_length; glb_chunk_kind next_chunk_type;");
        break;
      }
      append(
        `${addType(`glb_chunk_${i}`, `uint32 length; glb_chunk_kind type; ${json ? "char" : "uint8"} data[length];`)} chunk_${i};`,
      );
      offset += 8 + length;
    }
  }
  if (ext === "gif") {
    replace(
      "uint8 packed_flags;",
      "uint8 palette_size_code:3; uint8 sorted:1; uint8 color_resolution_minus_one:3; uint8 global_palette_present:1;",
    );
    const at = 13 + (has(10, 1) && bytes[10]! & 128 ? 3 * (1 << ((bytes[10]! & 7) + 1)) : 0);
    if (has(at, 10) && bytes[at] === 0x2c) {
      append(
        "uint8 image_separator; uint16 left; uint16 top; uint16 image_width; uint16 image_height; uint8 local_palette_size_code:3; uint8 reserved:2; uint8 local_sorted:1; uint8 interlaced:1; uint8 local_palette_present:1;",
      );
      if (bytes[at + 9]! & 128) {
        addType("gif_local_rgb", "uint8 red; uint8 green; uint8 blue;");
        append(`gif_local_rgb local_palette[${1 << ((bytes[at + 9]! & 7) + 1)}];`);
      }
    }
  }
  if (ext === "bmp" && has(14, 4)) {
    enumType(
      "bitmap_compression",
      "uint32",
      "Rgb=0,Rle8=1,Rle4=2,Bitfields=3,Jpeg=4,Png=5,AlphaBitfields=6",
    );
    replace("uint32 compression;", "bitmap_compression compression;");
    const size = u32(14, true);
    if ([52, 56, 108, 124].includes(size))
      append("uint32 red_mask; uint32 green_mask; uint32 blue_mask;");
    if ([56, 108, 124].includes(size)) append("uint32 alpha_mask;");
    if ([108, 124].includes(size))
      append(
        "uint32 color_space; int32 endpoints_xyz[3][3]; uint32 gamma_red; uint32 gamma_green; uint32 gamma_blue;",
      );
    if (size === 124)
      append(
        "uint32 rendering_intent; uint32 profile_offset; uint32 profile_size; uint32 reserved;",
      );
  }
  if (ext === "flac") {
    replace("uint8 metadata_flags;", "uint8 metadata_type:7; uint8 last_metadata:1;");
    replace(
      "uint64 stream_parameters;",
      "uint64 total_samples:36; uint64 bits_per_sample_minus_one:5; uint64 channels_minus_one:3; uint64 sample_rate:20;",
    );
    let at = 42;
    if (has(4, 1) && !(bytes[4]! & 128))
      for (let i = 0; i < 16 && has(at, 4); i++) {
        const kind = bytes[at]! & 127,
          length = bytes[at + 1]! * 65536 + bytes[at + 2]! * 256 + bytes[at + 3]!;
        if (!has(at + 4, length)) break;
        let payload = `uint8 data[${length}];`;
        if (kind === 3 && length % 18 === 0) {
          if (!layout.types.includes("struct flac_seek"))
            addType(
              "flac_seek",
              "uint64 sample_number; uint64 stream_offset; uint16 frame_samples;",
            );
          payload = `flac_seek points[${length / 18}];`;
        }
        append(
          `${addType(`flac_metadata_${i}`, `uint8 type:7; uint8 last:1; uint8 length_be[3]; ${payload}`)} metadata_${i};`,
        );
        if (bytes[at]! & 128) break;
        at += 4 + length;
      }
  }
  if (["ogg", "oga", "ogv", "opus", "spx", "ogm", "ogx"].includes(ext)) {
    replace(
      "uint8 header_flags;",
      "uint8 continued_packet:1; uint8 beginning_of_stream:1; uint8 end_of_stream:1; uint8 reserved:5;",
    );
    if (has(26, 1) && has(27, bytes[26]!)) {
      const at = 27 + bytes[26]!;
      let length = 0;
      let complete = false;
      for (const lace of bytes.subarray(27, at)) {
        length += lace;
        if (lace < 255) {
          complete = true;
          break;
        }
      }
      if (
        complete &&
        !(bytes[5]! & 1) &&
        length >= 19 &&
        has(at, length) &&
        ascii(at, 8) === "OpusHead"
      ) {
        append(
          "char codec[8]; uint8 opus_version; uint8 channels; uint16 pre_skip; uint32 input_sample_rate; int16 output_gain_q8; uint8 mapping_family;",
        );
        if (bytes[at + 18] !== 0 && length >= 21 + bytes[at + 9]!)
          append("uint8 stream_count; uint8 coupled_count; uint8 channel_map[channels];");
      } else if (
        complete &&
        !(bytes[5]! & 1) &&
        length >= 30 &&
        has(at, length) &&
        ascii(at + 1, 6) === "vorbis" &&
        bytes[at] === 1
      ) {
        append(
          "uint8 packet_type; char codec[6]; uint32 vorbis_version; uint8 channels; uint32 sample_rate; int32 maximum_bitrate; int32 nominal_bitrate; int32 minimum_bitrate; uint8 small_block_power:4; uint8 large_block_power:4; uint8 framing;",
        );
      }
    }
  }
  if (ext === "crx" && has(0, 12)) {
    if (u32(4, true) === 2) {
      replace(
        "uint32 header_length_or_public_key_length;",
        "uint32 public_key_length; uint32 signature_length; uint8 public_key[public_key_length]; uint8 signature_bytes[signature_length];",
      );
    } else if (u32(4, true) === 3)
      replace(
        "uint32 header_length_or_public_key_length;",
        "uint32 header_length; uint8 signed_header[header_length];",
      );
  }
  if (ext === "ktx" && has(60, 4) && has(64, u32(60) + 4))
    append("uint8 key_value_data[key_value_bytes]; uint32 first_mip_size;");
  if (ext === "psd" && has(26, 4)) {
    const color = u32(26, false);
    if (has(30, color + 4)) {
      append(
        "uint32 color_data_length; uint8 color_data[color_data_length]; uint32 image_resources_length;",
      );
      const resource = u32(30 + color, false),
        wide = u16(4, false) === 2;
      if (has(34 + color, resource + (wide ? 8 : 4)))
        append(
          `uint8 image_resources[image_resources_length]; uint${wide ? 64 : 32} layer_mask_length;`,
        );
    }
  }
  if (ext === "fbx" && has(23, 4)) {
    const word = u32(23, true) >= 7500 ? "uint64" : "uint32";
    append(
      `${addType("fbx_node", `${word} end_offset; ${word} property_count; ${word} property_bytes; uint8 name_length; char name[name_length];`)} first_node;`,
    );
  }
  if (ext === "blend" && has(0, 12)) {
    layout.littleEndian = bytes[8] === 118;
    const word = bytes[7] === 45 ? "uint64" : "uint32";
    append(
      `${addType("blend_block", `char code[4]; uint32 length; ${word} old_memory_address; uint32 dna_index; uint32 count;`)} first_block;`,
    );
  }
  if (ext === "chm" && has(4, 4) && [2, 3].includes(u32(4, true))) {
    append(
      "uint64 section_zero_offset; uint64 section_zero_length; uint64 directory_offset; uint64 directory_length;",
    );
    if (u32(4, true) === 3) append("uint64 data_offset;");
  }
  if (ext === "dcm" && has(136, 4)) {
    const longVr = ["OB", "OD", "OF", "OL", "OV", "OW", "SQ", "UC", "UR", "UT", "UN"].includes(
      ascii(136, 2),
    );
    append(
      `${longVr ? "uint16 reserved; uint32" : "uint16"} value_length; uint8 value[value_length];`,
    );
  }
  if (ext === "it" && has(54, 6) && u16(54, true) > 0) {
    addType("tracker_message", `char text[${u16(54, true)}];`);
    replace("uint32 message_offset;", "tracker_message *message;");
  }
  if (["ar", "deb"].includes(ext) && has(0, 68)) {
    const text = ascii(48, 10).trim();
    if (/^\d+$/.test(text) && has(68, Number(text)))
      append(`uint8 first_member[${Number(text)}]; uint8 member_padding[${Number(text) & 1}];`);
  }
  if (ext === "mid" && has(8, 6) && u32(4, false) >= 6) {
    const at = 8 + u32(4, false);
    if (has(at, 8) && ascii(at, 4) === "MTrk" && has(at + 8, u32(at + 4, false)))
      append(
        `uint8 header_extension[${at - 14}]; char track_type[4]; uint32 track_length; uint8 events[track_length];`,
      );
  }
  if (ext === "icns" && has(12, 4) && u32(12, false) >= 8 && has(16, u32(12, false) - 8))
    append("uint8 first_element[first_element_length - 8];");
  if (ext === "aif" && has(12, 8) && ascii(12, 4) === "COMM" && u32(16, false) >= 18 && has(20, 18))
    append(
      "uint16 channels; uint32 sample_frames; uint16 sample_size; uint8 sample_rate_extended80[10];",
    );

  if (
    "mp4 m4a m4v m4p m4b f4v f4p f4b f4a 3gp 3g2 mov heic avif cr3 jp2 jpm jpx mj2"
      .split(" ")
      .includes(ext) &&
    has(0, 8)
  ) {
    let at = 0,
      fields = "";
    for (let i = 0; i < 32 && has(at, 8); i++) {
      const size = u32(at, false),
        tag = ascii(at + 4, 4),
        header = size === 1 ? 16 : 8;
      if (!has(at, header)) break;
      // A zero size extends to the actual EOF, not the end of our preview.
      if (size === 0) {
        fields += "uint32 final_box_size; char final_box_type[4];";
        break;
      }
      const length = size === 1 ? Number(view.getBigUint64(at + 8, false)) : size;
      if (!Number.isSafeInteger(length) || length < header) break;
      let content = `uint32 size; char type[4]; ${header === 16 ? "uint64 extended_size;" : ""}`;
      if (!has(at, length)) {
        fields += `${addType(`box_${i}`, content)} next_box;`;
        break;
      }
      if (tag === "ftyp" && length >= header + 8 && (length - header) % 4 === 0) {
        addType(`brand_${i}`, "char code[4];");
        content += `char major_brand[4]; uint32 minor_version; brand_${i} compatible_brands[${(length - header - 8) / 4}];`;
      } else content += `uint8 payload[${length - header}];`;
      fields += `${addType(`box_${i}`, content)} box_${i};`;
      at += length;
    }
    if (fields) layout.fields = fields;
  }
  if (["gz", "tar.gz"].includes(ext)) {
    replace(
      "uint8 flags;",
      "uint8 text:1; uint8 header_crc_present:1; uint8 extra_present:1; uint8 name_present:1; uint8 comment_present:1; uint8 reserved:3;",
    );
  }
  if (ext === "Z")
    replace("uint8 flags;", "uint8 maximum_code_bits:5; uint8 reserved:2; uint8 block_mode:1;");
  if (ext === "xz")
    replace(
      "uint8 stream_flags[2];",
      "uint8 reserved; uint8 check_type:4; uint8 reserved_flags:4;",
    );
  if (ext === "flv")
    replace(
      "uint8 flags;",
      "uint8 video_present:1; uint8 reserved_low:1; uint8 audio_present:1; uint8 reserved_high:5;",
    );
  if (["mp1", "mp2", "mp3"].includes(ext))
    replace(
      "uint32 frame_header;",
      "uint32 emphasis:2; uint32 original:1; uint32 copyright:1; uint32 mode_extension:2; uint32 channel_mode:2; uint32 private_bit:1; uint32 padding:1; uint32 sample_rate_index:2; uint32 bitrate_index:4; uint32 no_crc:1; uint32 layer:2; uint32 mpeg_version:2; uint32 sync:11;",
    );
  if (ext === "ac3") {
    replace(
      "uint8 sample_rate_and_frame_size;",
      "uint8 frame_size_code:6; uint8 sample_rate_code:2;",
    );
    replace("uint8 bitstream_id_and_mode;", "uint8 bitstream_mode:3; uint8 bitstream_id:5;");
  }
  if (ext === "mts") {
    replace(
      "uint16> transport_flags_and_pid;",
      "uint16> pid:13; uint16> transport_priority:1; uint16> payload_unit_start:1; uint16> transport_error:1;",
    );
    replace(
      "uint8 adaptation_and_continuity;",
      "uint8 continuity_counter:4; uint8 adaptation_control:2; uint8 scrambling_control:2;",
    );
  }
  if (["ttf", "otf", "woff"].includes(ext)) {
    const woff = ext === "woff",
      at = woff ? 44 : 12,
      stride = woff ? 20 : 16;
    const count = has(woff ? 12 : 4, 2) ? u16(woff ? 12 : 4, false) : 0;
    if (count > 0 && count <= 128 && has(at, count * stride)) {
      addType(
        "font_head",
        "uint16 major_version; uint16 minor_version; int32 revision_fixed16; uint32 checksum_adjustment; uint32 magic; uint16 flags; uint16 units_per_em; int64 created; int64 modified; int16 x_min; int16 y_min; int16 x_max; int16 y_max; uint16 style; uint16 smallest_ppem; int16 direction_hint; int16 loca_format; int16 glyph_data_format;",
      );
      let directory = "";
      for (let i = 0; i < count; i++) {
        const entry = at + i * stride;
        const isHead =
          ascii(entry, 4) === "head" &&
          (woff
            ? u32(entry + 8, false) === u32(entry + 12, false) && u32(entry + 12, false) >= 54
            : u32(entry + 12, false) >= 54);
        const pointer = isHead ? "font_head *header;" : "uint32 offset;";
        directory += `${addType(`font_entry_${i}`, woff ? `char tag[4]; ${pointer} uint32 compressed_length; uint32 original_length; uint32 checksum;` : `char tag[4]; uint32 checksum; ${pointer} uint32 length;`)} table_${i};`;
      }
      replace("table_record tables[table_count];", directory);
    }
  }
  if (ext === "wasm") {
    enumType(
      "wasm_section_id",
      "uint8",
      "Custom=0,Type=1,Import=2,Function=3,Table=4,Memory=5,Global=6,Export=7,Start=8,Element=9,Code=10,Data=11,DataCount=12,Tag=13",
    );
    let at = 8;
    for (let i = 0; i < 32 && has(at, 2); i++) {
      let length = 0,
        width = 0,
        complete = false;
      for (; width < 5 && has(at + 1 + width, 1); width++) {
        const byte = bytes[at + 1 + width]!;
        length += (byte & 127) * 2 ** (7 * width);
        if (!(byte & 128)) {
          width++;
          complete = true;
          break;
        }
      }
      if (!complete || length > 0xffffffff || !has(at + 1 + width, length)) break;
      append(
        `${addType(`wasm_section_${i}`, `wasm_section_id id; uint8 length_leb128[${width}]; uint8 payload[${length}];`)} section_${i};`,
      );
      at += 1 + width + length;
    }
  }
  if (["lz4", "zst"].includes(ext) && has(0, 4)) {
    const magic = u32(0, true);
    if (magic >= 0x184d2a50 && magic <= 0x184d2a5f)
      layout.fields = "uint32 signature; uint32 skippable_length;";
    else if (ext === "lz4" && magic === 0x184c2102)
      layout.fields = "uint32 signature; uint32 first_block_length;";
    else if (ext === "lz4" && magic === 0x184d2204 && has(4, 2)) {
      replace(
        "uint8 flags;",
        "uint8 dictionary_present:1; uint8 reserved:1; uint8 content_checksum:1; uint8 content_size_present:1; uint8 block_checksum:1; uint8 independent_blocks:1; uint8 version:2;",
      );
      replace(
        "uint8 block_descriptor;",
        "uint8 reserved_low:4; uint8 block_maximum_code:3; uint8 reserved_high:1;",
      );
      if (bytes[4]! & 8) append("uint64 content_size;");
      if (bytes[4]! & 1) append("uint32 dictionary_id;");
      append("uint8 header_checksum; uint32 first_block_size:31; uint32 uncompressed_block:1;");
    } else if (ext === "zst" && magic === 0xfd2fb528 && has(4, 1)) {
      const flag = bytes[4]!,
        single = !!(flag & 32),
        code = flag >> 6;
      replace(
        "uint8 frame_descriptor;",
        "uint8 dictionary_size_code:2; uint8 content_checksum:1; uint8 reserved:1; uint8 unused:1; uint8 single_segment:1; uint8 content_size_code:2;",
      );
      if (!single) append("uint8 window_mantissa:3; uint8 window_exponent:5;");
      const dictionary = [0, 1, 2, 4][flag & 3]!;
      if (dictionary) append(`uint${dictionary * 8} dictionary_id;`);
      const size = code === 0 ? (single ? 1 : 0) : [0, 2, 4, 8][code]!;
      if (size)
        append(`uint${size * 8} ${size === 2 ? "content_size_minus_256" : "content_size"};`);
      // Three bytes are retained because the language has no uint24 storage type.
      append("uint8 first_block_header_le[3];");
    }
  }
  return layout;
}
