# The comparison record (see ../SensorReading.cs) as a Kaitai Struct schema: 79 packed little-endian bytes.
# SensorReading.g.cs is compiled from this file by tools/quality/comparison-benchmarks.mjs --regenerate-kaitai.
meta:
  id: sensor_reading
  endian: le
seq:
  - id: id
    type: u4
  - id: timestamp
    type: s8
  - id: position
    type: vec3
  - id: velocity
    type: vec3
  - id: flags
    type: u2
  - id: kind
    type: u1
  - id: value
    type: f8
  - id: samples
    type: s4
    repeat: expr
    repeat-expr: 8
types:
  vec3:
    seq:
      - id: x
        type: f4
      - id: y
        type: f4
      - id: z
        type: f4
