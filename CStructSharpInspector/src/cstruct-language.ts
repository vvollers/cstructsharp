// Registers a dedicated Monaco language for the CStruct DSL (previously approximated with the built-in
// "c" language for highlighting only, see monaco-layout.ts) - a Monarch tokenizer, bracket/comment
// configuration, and completion/hover providers covering the language's keywords, primitive type
// spellings, and any struct/union/enum/typedef/#define names the user has already declared in the current
// document (see cstruct-symbols.ts's collectSymbols).
//
// collectSymbols is a lightweight regex-based scanner, not a real parser reusing the WASM boundary's own
// grammar (which only ever accepts/returns the whole definition as one opaque string, see
// cstruct-wasm.ts) - good enough for symbol-name completion and hover text, not full semantic validation.
// Real diagnostics still only appear when the user hits Run.
import * as monaco from "monaco-editor/editor";
import {
  collectSymbols,
  CSTRUCT_KEYWORD_DOCS,
  CSTRUCT_KEYWORDS,
  CSTRUCT_PRIMITIVE_TYPES,
  type CStructSymbolKind,
} from "./cstruct-symbols";

export const CSTRUCT_LANGUAGE_ID = "cstruct";

const monarchTypeKeywords = new Set<string>();
for (const { name } of CSTRUCT_PRIMITIVE_TYPES) {
  for (const word of name.split(" ")) monarchTypeKeywords.add(word);
}

function getLanguageConfiguration(): monaco.languages.LanguageConfiguration {
  return {
    comments: { lineComment: "//", blockComment: ["/*", "*/"] },
    brackets: [
      ["{", "}"],
      ["(", ")"],
      ["[", "]"],
    ],
    autoClosingPairs: [
      { open: "{", close: "}" },
      { open: "(", close: ")" },
      { open: "[", close: "]" },
      { open: '"', close: '"' },
    ],
    surroundingPairs: [
      { open: "{", close: "}" },
      { open: "(", close: ")" },
      { open: "[", close: "]" },
    ],
  };
}

function getMonarchLanguage(): monaco.languages.IMonarchLanguage {
  return {
    defaultToken: "",
    tokenPostfix: ".cstruct",
    keywords: CSTRUCT_KEYWORDS,
    typeKeywords: Array.from(monarchTypeKeywords),
    operators: ["|", "&", "<<", ">>", "+", "-", "~", "*", "/", "=", "<", ">"],
    symbols: /[=><~?:&|+\-*/^%]+/,
    tokenizer: {
      root: [
        [/#\s*define\b/, "keyword.directive"],
        // Monarch treats "@word" inside a rule's own regex as an attribute reference (that's how
        // "@keywords"/"@symbols" below work) - "@@" is Monarch's own escape for a literal "@" character.
        [/@@align\b/, "keyword.annotation"],
        [/@@/, "annotation"],
        [
          /[a-zA-Z_]\w*/,
          {
            cases: {
              "@keywords": "keyword",
              "@typeKeywords": "type",
              "@default": "identifier",
            },
          },
        ],
        { include: "@whitespace" },
        [/0[xX][0-9a-fA-F_]+[uUlL]*/, "number.hex"],
        [/0[bB][01_]+[uUlL]*/, "number.binary"],
        [/0[oO][0-7_]+[uUlL]*/, "number.octal"],
        [/\d[\d_]*[uUlL]*/, "number"],
        [/[{}()[\]]/, "delimiter.bracket"],
        [/@symbols/, { cases: { "@operators": "operator", "@default": "" } }],
        [/[;,.]/, "delimiter"],
      ],
      whitespace: [
        [/[ \t\r\n]+/, ""],
        [/\/\*/, "comment", "@comment"],
        [/\/\/.*$/, "comment"],
      ],
      comment: [
        [/[^/*]+/, "comment"],
        [/\*\//, "comment", "@pop"],
        [/[/*]/, "comment"],
      ],
    },
  };
}

function completionKindFor(kind: CStructSymbolKind): monaco.languages.CompletionItemKind {
  switch (kind) {
    case "struct":
    case "union":
      return monaco.languages.CompletionItemKind.Struct;
    case "enum":
      return monaco.languages.CompletionItemKind.Enum;
    case "typedef":
      return monaco.languages.CompletionItemKind.Interface;
    case "define":
      return monaco.languages.CompletionItemKind.Constant;
  }
}

monaco.languages.register({ id: CSTRUCT_LANGUAGE_ID, aliases: ["CStruct", "cstruct"] });
monaco.languages.setLanguageConfiguration(CSTRUCT_LANGUAGE_ID, getLanguageConfiguration());
monaco.languages.setMonarchTokensProvider(CSTRUCT_LANGUAGE_ID, getMonarchLanguage());

monaco.languages.registerCompletionItemProvider(CSTRUCT_LANGUAGE_ID, {
  triggerCharacters: ["@"],
  provideCompletionItems(model, position) {
    const word = model.getWordUntilPosition(position);
    const range = {
      startLineNumber: position.lineNumber,
      endLineNumber: position.lineNumber,
      startColumn: word.startColumn,
      endColumn: word.endColumn,
    };
    const suggestions: monaco.languages.CompletionItem[] = [];

    for (const keyword of CSTRUCT_KEYWORDS) {
      suggestions.push({
        label: keyword,
        kind: monaco.languages.CompletionItemKind.Keyword,
        detail: CSTRUCT_KEYWORD_DOCS[keyword],
        insertText: keyword,
        range,
      });
    }
    for (const type of CSTRUCT_PRIMITIVE_TYPES) {
      suggestions.push({
        label: type.name,
        kind: monaco.languages.CompletionItemKind.Keyword,
        detail: type.detail,
        insertText: type.name,
        sortText: type.name.includes(" ") ? `z${type.name}` : `a${type.name}`,
        range,
      });
    }
    suggestions.push({
      label: "@align",
      kind: monaco.languages.CompletionItemKind.Snippet,
      detail: "Override this declarator's alignment (aligned mode only)",
      insertText: "@align(${1:N})",
      insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
      range,
    });

    for (const symbol of collectSymbols(model.getValue())) {
      suggestions.push({
        label: symbol.name,
        kind: completionKindFor(symbol.kind),
        detail: symbol.detail,
        documentation: symbol.documentation,
        insertText: symbol.name,
        range,
      });
    }

    return { suggestions };
  },
});

monaco.languages.registerHoverProvider(CSTRUCT_LANGUAGE_ID, {
  provideHover(model, position) {
    const word = model.getWordAtPosition(position);
    if (!word) return null;
    const range = new monaco.Range(
      position.lineNumber,
      word.startColumn,
      position.lineNumber,
      word.endColumn,
    );

    const primitive = CSTRUCT_PRIMITIVE_TYPES.find((type) => type.name === word.word);
    if (primitive) {
      return { range, contents: [{ value: `**${primitive.name}**` }, { value: primitive.detail }] };
    }
    const keywordDoc = CSTRUCT_KEYWORD_DOCS[word.word];
    if (keywordDoc) {
      return { range, contents: [{ value: `**${word.word}**` }, { value: keywordDoc }] };
    }
    const symbol = collectSymbols(model.getValue()).find((entry) => entry.name === word.word);
    if (symbol) {
      return {
        range,
        contents: [{ value: `**${symbol.detail}**` }, { value: symbol.documentation }],
      };
    }
    return null;
  },
});
