# Detection and standalone schemas

**Load & detect** identifies the content using `file-type` and selects a fixed definition from
[schema-catalog.ts](src/schema-catalog.ts). The same extension always produces identical source and parser
settings. Detection can seek through a Blob to identify its format, but its only contribution to schema selection
is the extension. No format-specific scanner supplies counts, offsets, parser variables or secondary roots.
Detection and parsing run locally. A 15-second timeout stops stalled detection.

**Load file** replaces the binary source while keeping the definition and settings, including unedited examples.
Use it to demonstrate how one native `if`/`switch` layout handles different inputs. **Load & detect** deliberately
selects a new definition. Selecting a sample loads its fixture; selecting a schema-only entry preserves the file.
Changing selection cancels pending detection and parsing.

The catalog covers all 184 extensions supported by the installed detector, plus the DLL teaching sample.
The nine sample-backed examples retain their small managed-library fixtures. Detected layouts can be broader;
for example, the ZIP sample demonstrates a local header, while the detected ZIP layout branches on the signature
and can also handle an empty archive. A signature match and a successful field read do not establish file validity.

The first 64 KiB loaded into the UI is a hex preview only. The parser receives the complete Blob. Definitions
open pretty-printed and can be copied into ordinary CStructSharp consumers with the displayed parser settings.
Formats such as TIFF and PCAP require the user to choose the matching byte order. Pointer width describes the
stored offset, not the host OS or the file's length.

See the [schema coverage and catalog audit](SCHEMA-REVIEW.md).
Some formats expose only their signature or a fixed header. PDF text grammar, JPEG entropy decoding, ZIP footer
searches, decompression and arbitrary record discovery are not silently performed outside the definition.

## Verification

`npm run test:unit` checks detector parity and formatting. Browser tests compile every registered layout in WASM,
parse native variants with identical definitions, check distant pointers and configurable budgets, and exercise
manual/detected file loading. For the optional upstream fixture audit, set `CSTRUCT_FILE_TYPE_FIXTURES` to a local
`file-type` fixture directory and run `npx playwright test tests/e2e/detected-fixtures.spec.ts`.
Truncated detection fixtures may legitimately produce read errors; that audit checks layout compilation.

References: [file-type](https://github.com/sindresorhus/file-type),
[CStruct grammar](../../docs/language/grammar.md), [pointers](../../docs/language/pointers-and-addressing.md).
