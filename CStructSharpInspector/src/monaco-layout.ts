import * as monaco from "monaco-editor/editor";
import "monaco-editor/features/register.all";
import EditorWorker from "monaco-editor/editor/editor.worker?worker";
import { CSTRUCT_LANGUAGE_ID } from "./cstruct-language";

// This app only ever edits CStruct definitions, using the dedicated "cstruct" language registered by
// ./cstruct-language.ts (Monarch tokenizer, keyword/type/symbol completion, hover) - unlike the explorer,
// there's no C#/JavaScript "generate code" dialog and no read-only JSON Monaco view (the result panel uses
// json-editor-vue instead), so no other language needs registering here.
globalThis.MonacoEnvironment = {
  getWorker: () => new EditorWorker(),
};
export { monaco, CSTRUCT_LANGUAGE_ID };
