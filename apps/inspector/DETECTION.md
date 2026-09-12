# File detection and schema coverage

Use **Load & detect**, above **New schema**, to choose a file and select a schema from its contents.
The **Load file** button in Binary Data keeps the current schema, which is useful for comparing files.
Detection and parsing run locally; files are not uploaded.

The installed `file-type` 22.1.0 catalog contains 184 extensions. Each extension has an explicit
registration in [detected-schemas.ts](src/detected-schemas.ts). Related formats share their container
layout, but each loaded file receives its own named CStruct declaration and parser settings.
All 184 types are available in the filterable schema list, along with the existing DLL example.
Search by extension (with or without a leading dot) or format name. The existing sample-backed
buttons still load their tested sample bytes. Entries labelled **Schema · load your file** keep
the currently loaded file, or let you load one afterward. Loading a file specializes an unedited
schema template to the file's variant; edited schemas are preserved.

Detection is a signature hint, not validation. A successful parse means the selected fields were read,
not that the entire file is valid. The comments at the top of each generated schema describe its scope.

## What the schemas read

Text fields use explicit bounded `utf8`, `latin1`, `cp437` or UTF-16 encodings where the
format specifies them. ZIP filenames honor each entry's UTF-8 flag; legacy names use CP437.
Registry filenames retain UTF-16LE `wchar<` arrays. Other `char` arrays preserve raw one-byte
code units without assuming an encoding. See the [text-field audit](SCHEMA-REVIEW.md#text-field-audit) for changes and exceptions.

The [schema-by-schema review](SCHEMA-REVIEW.md) records all 184 detector types and the DLL sample,
including the 110 expanded registrations, their language features and remaining opportunities.

- ZIP containers: named flags/compression and up to 16 complete local entries; empty archives use EOCD.
- PNG/APNG, RIFF, GLB: native chunk alternatives, bounded text and typed metadata. ISO media/QuickTime adds bounded movie-header timestamps, fixed-point rate and mixed-scale matrices; PCAP exposes packet records.
- TIFF: named tag/type enums and first IFD, including BigTIFF widths. PE: optional headers and data directories. ELF: typed program/section table pointers.
- SFNT/WOFF: directories with typed pointers to uncompressed `head` tables when visible.
- FLAC/Ogg/GIF/BMP: packed parameters, metadata/identification fields and selected image header variants. Complete supported Ogg comment packets expose bounded UTF-8 vendor and comment strings.
- SQLite: first B-tree page header and cell offsets. STL: normals and triangle vertices as float arrays.
- WebAssembly: LEB128 section lengths/counts, function/start indexes and bounded UTF-8 custom names. LZ4/Zstandard: frame descriptor variants and first block framing.
- Photoshop, CRX, Blender, FBX, KTX, DICOM, CHM, MIDI, AIFF and ICNS: selected additional metadata or records.
- Other formats retain their documented headers or directories; the review identifies possible next steps.

**Prefix-only coverage:** DWG, InDesign, JMP, MIE, PDF, RTF, PostScript/EPS, XML, iCalendar, vCard,
WebVTT, registry exports and SketchUp. These have explicit format-specific prefix layouts; they do
not decode document/model records. Some other headers, such as Parquet and Avro, are also intentionally
small because their metadata uses a separate serialization format.

Compressed member contents, image/audio/video codecs, encryption, arbitrary text grammars, and all
possible format revisions are not decoded. Header/container support must not be described as complete
file-format support. The registry is an extensible starting point for deeper CStruct layouts.

The first 64 KiB selects bounded record coverage and any preview-dependent layouts. Native
conditions select supported per-record variants from the bytes during parsing. Reload detection when changing
format-defining bytes. Pointer reads still use the full file. Detection itself uses a Blob-backed
tokenizer in a worker and can seek beyond the preview; a 15-second timeout stops stalled detection.
Starting another load or selecting a sample discards the previous detection result.

## Verification

`npm run test:unit` checks registration parity with `supportedExtensions` and variant selection.
`npx playwright test tests/e2e/detection.spec.ts` checks the real file chooser, content-based detection,
schema loading, manual loading and compilation of every registered schema in the WASM runtime.

For an optional compatibility audit, point `CSTRUCT_FILE_TYPE_FIXTURES` at a local copy of the upstream
`file-type` fixture directory and run:

```powershell
npx playwright test tests/e2e/detected-fixtures.spec.ts
```

The audit attaches per-file results and rejects invalid CStruct layouts. Detection fixtures sometimes
contain only a signature or truncated header, so a read error is not automatically a schema defect.
This audit does not establish full format validity or codec support.

## References

- [file-type detector and format list](https://github.com/sindresorhus/file-type)
- [CStruct grammar](../../docs/language/grammar.md) and [pointer behavior](../../docs/language/pointers-and-addressing.md)
- [PNG specification](https://www.w3.org/TR/png-3/)
- [SQLite database header](https://www.sqlite.org/fileformat.html)
- [Kaitai executable format specifications](https://formats.kaitai.io/)
- [Microsoft Shell Link specification](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-shllink/a6c2f32d-2297-4727-bcd3-5d3669573bcb)
