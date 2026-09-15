# Binary inspector

The inspector demonstrates CStructSharp directly. Every example is a standalone, formatted CStruct definition.
Native `if` and `switch` statements select variants; runtime array counts, bitfields, explicit byte order and typed
pointers describe the stored data. The editor text is the complete layout passed to the library.

**Load & detect** uses `file-type` to identify a format and selects its fixed definition. Detection does not generate
fields, discover records for the schema, change its pointer width from file bytes, or merge multiple parse results.
**Load file** preserves the editor text and settings. Sample buttons load their small teaching fixtures;
**Schema · load your file** entries keep the currently loaded file. All examples open pretty-printed.

Files stay as Blob sources. Parsing uses the full source in a worker; the hex panel reads visible windows.
Pointers can target offsets beyond 4 GiB without reading the intervening bytes. Search scans bounded chunks,
and edits compose immutable Blob ranges with undo/redo. Cancel stops parsing; changing the file or example also
cancels outstanding work. No edits are written to disk until the user downloads a result.

The settings dialog controls decoded array, string and total-read budgets. These are independent of file size
and pointer distance. Larger payload budgets can be selected explicitly; results still have to fit available memory.
See the [schema coverage and catalog audit](SCHEMA-REVIEW.md),
[detection behavior](DETECTION.md), and the [large-file API guide](../../docs/guides/browser/large-data.md).

## Development

### Code map and data flow

`App.vue` creates one inspection session, connects file dialogs, and sets the initial dock layout.
The session belongs to that app instance; there is no global UI store.

| Module                                                       | Responsibility                                                                                              |
| ------------------------------------------------------------ | ----------------------------------------------------------------------------------------------------------- |
| `composables/useInspector.ts`                                | Current schema/source, runtime readiness, file loading and detection; actions that invalidate outdated work |
| `composables/useParseSession.ts`                             | Parse cancellation, immutable result snapshots, and JSON/hex selection                                      |
| `composables/useBinarySource.ts`                             | Blob windows and undo/redo; delegates byte operations to `blob-hex.ts`                                      |
| `components/InspectorDockPanel.vue`                          | Typed adapter for Dockview's nested params; connects session refs/actions to ordinary panel props/events    |
| `components/SchemaPanel.vue`, `SchemaSettings.vue`           | Editor and run action; parser settings and their dialog                                                     |
| `components/BinaryPanel.vue`, `ResultPanel.vue`              | Hex navigation/highlighting and JSON results; neither owns the document                                     |
| `components/InspectorHeader.vue`, `ExampleList.vue`          | Runtime/source status and searchable schema catalog                                                         |
| `components/LayoutEditor.vue`, `monaco-layout.ts`            | Monaco lifecycle and language integration                                                                   |
| `detected-schemas.ts`, `standalone-layouts.ts`, `formats.ts` | Detector profiles, extended native layouts, and teaching samples                                            |
| `wasm/cstruct-wasm.ts`                                       | Runtime loading and validation of the browser bridge's result envelope                                      |

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

### Build and verification

`src/` owns the UI and definitions; `wasm/` holds declarations copied from the runtime package.
From the repository root run `node tools/packaging/publish-wasm.mjs`. Then, in this directory, run
`npm ci`, `npm run build`, `npm run test:unit`, and `npm run test:e2e`.
`npm run copy:wasm` copies and validates existing runtime artifacts without rebuilding them.
`dist/`, `public/wasm/`, and browser reports are ignored output.
