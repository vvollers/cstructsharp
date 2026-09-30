/**
 * Documents and text formats: PDF, compound files, e-books, calendars, contacts and subtitles.
 */

import type { FormatDefinition } from "./types";

export const formats: FormatDefinition[] = [
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
    extensions: ["indd"],
    family: "InDesign",
    scope: "InDesign format signature. Page/object records are not decoded.",
    fields: "uint8 signature[16];",
    coverage: "prefix",
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
];
