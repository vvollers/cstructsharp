// Loaded lazily through load-monaco.ts by the shared LayoutEditor: sets up Monaco with the shared CStruct language,
// C++, C# and JavaScript highlighting for generated code, JSON (with comments) for parsed results, and the workers.
import * as monaco from "monaco-editor/editor";
import "monaco-editor/features/register.all";
import "monaco-editor/languages/definitions/cpp/register";
import "monaco-editor/languages/definitions/csharp/register";
import "monaco-editor/languages/definitions/javascript/register";
import EditorWorker from "monaco-editor/editor/editor.worker?worker";
import { jsonDefaults } from "monaco-editor/languages/features/json/register";
import JsonWorker from "monaco-editor/languages/features/json/json.worker?worker";
import { registerCStructLanguage } from "@cstructsharp/app-shared/language/register";

jsonDefaults.setDiagnosticsOptions({ allowComments: true, comments: "ignore" });
registerCStructLanguage(monaco);
globalThis.MonacoEnvironment = {
  getWorker: (_moduleId, label) => (label === "json" ? new JsonWorker() : new EditorWorker()),
};
export { monaco };
