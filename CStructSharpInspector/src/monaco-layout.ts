import * as monaco from "monaco-editor/editor";
import "monaco-editor/features/register.all";
import "monaco-editor/languages/definitions/cpp/register";
import EditorWorker from "monaco-editor/editor/editor.worker?worker";

// This app only ever edits CStruct definitions (approximated with C/C++ syntax highlighting,
// see LayoutEditor.vue) - unlike the explorer, there's no C#/JavaScript "generate code" dialog and no
// read-only JSON Monaco view (the result panel uses json-editor-vue instead), so neither language is
// registered here.
globalThis.MonacoEnvironment = {
  getWorker: () => new EditorWorker(),
};
export { monaco };
