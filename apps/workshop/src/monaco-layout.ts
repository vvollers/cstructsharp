import * as monaco from "monaco-editor/editor";
import "monaco-editor/features/register.all";
import "monaco-editor/languages/definitions/cpp/register";
import "monaco-editor/languages/definitions/csharp/register";
import "monaco-editor/languages/definitions/javascript/register";
import EditorWorker from "monaco-editor/editor/editor.worker?worker";
import { jsonDefaults } from "monaco-editor/languages/features/json/register";
import JsonWorker from "monaco-editor/languages/features/json/json.worker?worker";
import { registerCStructLanguage } from "./cstruct-language";

jsonDefaults.setDiagnosticsOptions({ allowComments: true, comments: "ignore" });
registerCStructLanguage(monaco);
globalThis.MonacoEnvironment = {
  getWorker: (_moduleId, label) => (label === "json" ? new JsonWorker() : new EditorWorker()),
};
export { monaco };
