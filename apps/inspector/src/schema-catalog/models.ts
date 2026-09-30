/**
 * 3D models and drawings: glTF, Draco, FBX, Blender, STL, AutoCAD and SketchUp.
 */

import type { FormatDefinition } from "./types";

export const formats: FormatDefinition[] = [
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
    if (type == glb_chunk_kind.Json) {
        utf8 json_data[length];
    } else {
        uint8 binary_data[length];
    }
};`,
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
    extensions: ["dwg"],
    family: "AutoCAD drawing",
    scope:
      "DWG version signature. Object and section layouts are proprietary and version-specific.",
    fields: "char version[6];",
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
];
