import { cstructKeywords, cstructTypes } from "./cstruct-vocabulary";

export function registerCStructLanguage(monaco: typeof import("monaco-editor/editor")) {
  const id = "cstruct";
  monaco.languages.register({ id, aliases: ["CStruct"] });
  monaco.languages.setLanguageConfiguration(id, {
    comments: { lineComment: "//", blockComment: ["/*", "*/"] },
    brackets: [
      ["{", "}"],
      ["[", "]"],
      ["(", ")"],
    ],
    autoClosingPairs: [
      { open: "{", close: "}" },
      { open: "[", close: "]" },
      { open: "(", close: ")" },
    ],
  });
  monaco.languages.setMonarchTokensProvider(id, {
    keywords: cstructKeywords,
    types: [...new Set(cstructTypes.flatMap((type) => type.name.replace(/[<>]$/, "").split(" ")))],
    tokenizer: {
      root: [
        [/\/\/.*$/, "comment"],
        [/\/\*/, "comment", "@comment"],
        [/#define\b/, "keyword"],
        [
          /[A-Za-z_]\w*/,
          { cases: { "@keywords": "keyword", "@types": "type", "@default": "identifier" } },
        ],
        [/0[xX][\da-fA-F_]+|0[bB][01_]+|0[oO][0-7_]+|\d[\d_]*[uUlL]*/, "number"],
        [/[{}()[\]]/, "@brackets"],
        [/[!<>=&|+*~/-]+/, "operator"],
        [/[;,:]/, "delimiter"],
      ],
      comment: [
        [/[^/*]+/, "comment"],
        [/\*\//, "comment", "@pop"],
        [/[/*]/, "comment"],
      ],
    },
  });
  monaco.languages.registerCompletionItemProvider(id, {
    provideCompletionItems(model, position) {
      const word = model.getWordUntilPosition(position);
      const range = new monaco.Range(
        position.lineNumber,
        word.startColumn,
        position.lineNumber,
        word.endColumn,
      );
      return {
        suggestions: [
          ...cstructTypes.map((type) => ({
            label: type.name,
            insertText: type.name,
            detail: type.detail,
            kind: monaco.languages.CompletionItemKind.TypeParameter,
            range,
          })),
          ...cstructKeywords.map((keyword) => ({
            label: keyword,
            insertText: keyword,
            kind: monaco.languages.CompletionItemKind.Keyword,
            range,
          })),
        ],
      };
    },
  });
  monaco.languages.registerHoverProvider(id, {
    provideHover(model, position) {
      const word = model.getWordAtPosition(position);
      const type = cstructTypes.find((type) => type.name === word?.word);
      return type ? { contents: [{ value: `**${type.name}** — ${type.detail}` }] } : null;
    },
  });
}
