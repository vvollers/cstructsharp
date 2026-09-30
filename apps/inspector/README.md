# Binary inspector

The inspector demonstrates CStructSharp directly. Every example is a standalone, formatted CStruct definition.
Native `if` and `switch` statements select variants; runtime array counts, bitfields, explicit byte order and typed
pointers describe the stored data. The editor text is the complete layout passed to the library.

**Load & detect** selects the fixed definition of the detected format ([Detection](#detection)). Detection does not
generate fields, discover records for the schema, change its pointer width from file bytes, or merge parse results.
**Load file** preserves the editor text and settings. Sample buttons load their small teaching fixtures;
**Schema · load your file** entries keep the currently loaded file. All examples open pretty-printed.

Files stay as Blob sources. Parsing uses the full source in a worker; the hex panel reads visible windows.
Pointers can target offsets beyond 4 GiB without reading the intervening bytes. Search scans bounded chunks,
and edits compose immutable Blob ranges with undo/redo. Cancel stops parsing; changing the file or example also
cancels outstanding work. Edits are temporary and for inspection only: the app does not save or download edited
files, and the original file stays unchanged. Replacing the file or closing the tab discards the edited copy.

The inspector supports desktop browser windows at least **1200 CSS pixels wide**. Narrower windows show guidance
instead of clipped panels; enlarge the window to return to the same session. Mobile inspection is not supported.

The Safety limits in Operation settings control decoded array, string and total-read budgets. These are independent of file size
and pointer distance. Larger payload budgets can be selected explicitly; results still have to fit available memory.
See the [large-file API guide](../../docs/guides/browser/large-data.md).

## Detection

**Load & detect** identifies the content with [`file-type`](https://github.com/sindresorhus/file-type) and selects
the fixed definition registered for the detected extension in [the schema catalog](src/schema-catalog/index.ts). The same
extension always produces the same definition text and parser settings. Detection may seek through the Blob to
identify the format, but the extension is its only contribution to schema selection: no format-specific scanner
supplies counts, offsets, parser variables or secondary roots. Detection and parsing run locally, and a 15-second
timeout stops stalled detection. Changing the selection cancels pending detection and parsing.

The catalog covers every extension the installed detector reports, plus teaching aliases such as DLL, which shares
the PE definition. A file the detector does not recognize gets a raw-bytes schema.

The first 64 KiB of a loaded file is a hex preview only; the parser always receives the complete Blob. Formats
such as TIFF and PCAP store their byte order in the file, so choose the matching byte order in the settings.

## What the definitions decode

Each catalog entry is a standalone library example. Copy the editor text and use the displayed byte order, pointer
width and root type in any CStructSharp consumer. The inspector passes that text once to the library against the full
source: `if` and `switch` choose stored variants, arrays use earlier counts, and typed pointers follow stored offsets.
There are no discovered regions, input-dependent declarations, injected file offsets, or merged parse trees.

Each entry's `scope` text in the catalog states what it decodes and where it deliberately stops; it also appears as
a comment at the top of the detected definition. Many formats expose only their signature or a fixed header. PDF
text grammar, JPEG entropy decoding, ZIP footer searches, decompression and arbitrary record discovery are never
performed outside the definition. A limit
such as "up to eight PNG chunks" is visible in the definition as eight sequential fields, not hidden in the library.
A detected layout can be broader than its teaching sample: the ZIP sample shows one local header, while the detected
ZIP layout branches on the record signature. A signature match and a successful read do not prove that a file is
valid.

The pointer width describes the stored offset, not the host OS or the file's length. A pointer in a running C program
is usually a virtual-memory address, which means nothing in another process, so file formats store offsets measured
from a defined origin instead. CStructSharp follows a typed pointer by seeking to its target and reading the declared
type, without reading the gap in between; an eight-byte offset can reach beyond 4 GiB. PE shows why the two must not
be confused: a section's raw-data offset is a file offset, but most data-directory addresses are RVAs (relative
virtual addresses in the loaded image), so the PE example keeps RVAs as plain numbers. See
[Pointers and addressing](../../docs/language/pointers-and-addressing.md).

The definitions follow these specifications, whose scope exceeds what the examples implement:
[PE/COFF](https://learn.microsoft.com/en-us/windows/win32/debug/pe-format),
[ELF header](https://gabi.xinuos.com/elf/02-eheader.html),
[ZIP application note](https://pkware.cachefly.net/webdocs/casestudies/APPNOTE.TXT),
[PNG](https://www.w3.org/TR/png-3/), [JPEG T.81](https://www.w3.org/Graphics/JPEG/itu-t81.pdf) with
[JFIF T.871](https://www.itu.int/rec/T-REC-T.871), and [PDF 2.0](https://pdf-issues.pdfa.org/32000-2-2020/clause07.html).

## Schema list icons

[file-type-icons.ts](src/file-type-icons.ts) gives every catalog extension a dedicated or family icon from the
locally bundled VS Code Icons set (through Iconify). The generic file icon is reserved for an extension that has not
been classified yet. An icon follows the file's purpose, not its container: DOCX uses a document icon rather than
ZIP, and HEIC an image icon rather than video. Where the set has no dedicated icon, a family icon stands in: CAD and
mesh files use the 3D solid icon, statistical datasets the data-storage icon, PCAP the network icon, and metadata
formats the configuration icon. The schema filter also searches these family names. Icons are navigational hints
and do not change schema coverage or parsing.

## Development

### Code map and data flow

`App.vue` creates one inspection session, connects file dialogs, and sets the initial dock layout.
The session belongs to that app instance; there is no global UI store.
The header keeps its ready status short; hover over it to inspect the full runtime version/build identity.

| Module                                              | Responsibility                                                                                              |
| --------------------------------------------------- | ----------------------------------------------------------------------------------------------------------- |
| `composables/useInspector.ts`                       | Current schema/source, runtime readiness, file loading and detection; actions that invalidate outdated work |
| `composables/useParseSession.ts`                    | Parse cancellation, success/error results, and JSON/hex selection                                           |
| `composables/useBinarySource.ts`                    | Blob windows, undo/redo, byte editing and search                                                            |
| `components/InspectorDockPanel.vue`                 | Typed adapter for Dockview's nested params; connects session refs/actions to ordinary panel props/events    |
| `components/SchemaPanel.vue`, `SchemaSettings.vue`  | Editor and run action; settings summary around the shared settings dialog                                   |
| `components/BinaryPanel.vue`, `ResultPanel.vue`     | Hex navigation/highlighting and JSON results; neither owns the document                                     |
| `components/InspectorHeader.vue`, `ExampleList.vue` | Runtime/source status and searchable schema catalog                                                         |
| `load-monaco.ts`, `cstruct-language.ts`             | Lazy Monaco setup for the shared `LayoutEditor`: worker, highlighting and language help                     |
| `schema-catalog/`                                   | One registry for file extensions, detection layouts, sample definitions/bytes, and sidebar descriptions     |
| `@cstructsharp/app-shared/wasm/adapter`             | Runtime loading and validation of the browser bridge's result envelope (shared with the explorer)           |

The flow is **panel event → session action → refs → panels**. Document changes cancel pending reads/parses
and clear result selection. File loads publish the preview, full Blob and optional detected schema together;
late completions cannot overwrite newer edits. Parsing always receives the full source. Large binary data
and result trees use shallow refs because they are replaced as snapshots, not edited through Vue proxies.

Selecting a schema advances `schemaRevision`, which resets parser settings without remounting Monaco.
Manual file loads preserve the editor and its settings. The hex panel owns its bounded window and history;
its own edits preserve history, while a different file clears it.

When extending the app, put document transitions in `useInspector`, parser/selection behavior in
`useParseSession`, and presentation in the relevant panel. Add regression tests for async transitions under
`src/composables/`; use `tests/e2e/` for real WASM, editor, docking, and cross-panel behavior.

This organization follows Vue's guidance on [composables](https://vuejs.org/guide/reusability/composables.html)
and [shallow reactivity for large immutable structures](https://vuejs.org/guide/best-practices/performance.html#reduce-reactivity-overhead-for-large-immutable-structures).

### Follow one temporary byte edit

1. `BinaryPanel` sends an edit intent with file-relative byte offsets to `useBinarySource.handleEdit`.
   `editBlob` builds a replacement Blob from unchanged slices and replacement bytes; it does not overwrite the
   disk file. The edit's inclusive end is converted to the exclusive end expected by `Blob.slice`.
2. The composable records the Blob snapshots for undo/redo, then publishes the replacement through `onEdit`.
   `useInspector` owns the current document and invalidates the previous parse and selection.
3. A window load has its own request identity. A late load cannot replace the window belonging to a newer source.
   A file replacement clears history; editing the current file keeps its history. Disposing the scope invalidates
   pending work too.
4. A new parse owns an `AbortController`. `useParseSession` accepts its result only if that controller still belongs
   to the current attempt. Cancellation requests alone are not enough: this identity check rejects late results.
   The UI updates from the accepted result snapshot, not from a stale promise.

To trace failures, start with the composable unit tests and `tests/e2e/product-scope.spec.ts`. Edits remain
inspection-only: replacing or closing the session discards them, and there is no binary export operation.

### Adding or changing a format

Edit the format's entry in `src/schema-catalog/`. Its `extensions` select the detection layout;
`aliases` explicitly share a layout with another extension, such as DLL with EXE. `fields`, `types`,
parser settings and `scope` describe what detection loads. Add an optional `samples` entry beside them
when the format has demonstration bytes; give the sample its extension explicitly. The sidebar is
built from those registrations, so there is no separate ID-to-extension or description map to update.

The eight formats with teaching samples each have their own file (`bmp.ts`, `riff.ts`, `zip.ts`, `png.ts`,
`jpeg.ts`, `pe.ts`, `ico.ts`, `tar.ts`); the detection-only formats are grouped by kind (`images.ts`, `audio.ts`,
`video.ts`, `archives.ts`, `documents.ts`, `fonts.ts`, `programs.ts`, `data.ts`, `models.ts`). Each file exports its
registrations as `formats`; `index.ts` combines them in a fixed order (BMP first, since the inspector opens with the
first sample) and builds the lookups, so a new file also needs a line there. The engine corpus test
(`tests/CStructSharpTests/Engine/InspectorSchemaCatalog.cs`) reads these files as plain literals: keep
registrations to string, template, number, boolean, array and object literals, and shared constants to top-level
`const`s of the same file or of `types.ts`.

Sample layouts sometimes cover less than the detection layout because their small fixtures teach a
specific feature. Keeping both as explicit variants preserves those examples without an override pass.
Identical declarations, such as the EXE/DLL sample layout and TAR fields, are shared within the catalog.
Catalog tests check detector coverage, unique IDs/extensions, aliases and sample placement. The browser tests
compile every registered layout in the real WASM runtime and parse native variants, distant pointers and raised
budgets.
The managed tests also read this file (`tests/CStructSharpTests/Engine/InspectorSchemaCatalog.cs`) and run every
detection layout and sample through the engine's golden-outcome tests. That reader understands plain object literals,
string and template constants, and `...spread`; it fails, rather than skipping an entry, when the catalog uses other
syntax.

`apps/explorer/src/lessons.ts` belongs to the separate explorer application. It teaches language
features through lessons and expected results; it is not used for the inspector's file-type selection.

### Build and verification

`src/` owns the UI and definitions; the WASM boundary and its types come from `apps/shared`.
From the repository root run `npm run build:wasm`. Install dependencies with `npm ci` at the repository
root (the npm workspace). Then, in this directory, run `npm run build`, `npm run test:unit`, and `npm run test:e2e`.
`npm run copy:wasm` copies and validates existing runtime artifacts without rebuilding them.
`dist/`, `public/wasm/`, and browser reports are ignored output.

`tests/e2e/detected-fixtures.spec.ts` is an optional local audit: with `CSTRUCT_FILE_TYPE_FIXTURES` set to a copy of the
file-type project's fixture directory, it detects each file and checks that its selected schema compiles. CI skips it,
because those fixtures are not redistributed with this repository.
