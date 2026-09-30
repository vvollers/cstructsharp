// Loaded lazily through load-monaco.ts by the shared LayoutEditor: sets up Monaco with the shared CStruct language (highlighting, completion and
// hover) and the editor worker. Schema validation still happens in WASM when the user runs it.
import * as monaco from "monaco-editor/editor";
import "monaco-editor/features/register.all";
import EditorWorker from "monaco-editor/editor/editor.worker?worker";
import { registerCStructLanguage } from "@cstructsharp/app-shared/language/register";

registerCStructLanguage(monaco);

// Monaco asks for a worker when an editor needs background work. Vite supplies its bundled URL.
// Register the factory during this one-time module load, before LayoutEditor creates an editor.
globalThis.MonacoEnvironment = {
  getWorker: () => new EditorWorker(),
};

export { monaco };
