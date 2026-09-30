/**
 * Databases, data files and system records: SQLite, packet captures, columnar data, shortcuts, registry files and
 * OpenPGP.
 */

import type { FormatDefinition } from "./types";

export const formats: FormatDefinition[] = [
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
    extensions: ["mie"],
    family: "Meta information encapsulation",
    scope: "MIE framing and signature prefix. Nested metadata records are not decoded.",
    fields: `uint8 framing[4];
char signature[4];`,
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
    extensions: ["reg"],
    family: "Registry export",
    scope: "UTF-16 registry-export header only. Registry keys and values are text records.",
    fields: `uint16 byte_order_mark;
wchar< version_header[35];`,
    coverage: "prefix",
  },
  {
    extensions: ["pgp"],
    family: "OpenPGP",
    scope: "First OpenPGP packet-header octet only. Lengths and packet bodies are not decoded.",
    fields: "uint8 packet_header;",
  },
];
