/**
 * Registers the CStruct layout language with a Monaco instance: syntax highlighting, completion and hover. Both
 * editors call it with their own Monaco import, so they highlight and suggest the same words, all taken from the
 * language contract through `vocabulary.ts`, plus the names the document itself declares (`symbols.ts`). Layout
 * validation still happens in the WebAssembly runtime when the user runs an operation.
 */
import type * as Monaco from "monaco-editor/editor";

import { CSTRUCT_LANGUAGE_ID } from "./id";
import { collectSymbols, type CStructSymbolKind } from "./symbols";
import {
  annotations,
  directives,
  keywords,
  typeWords,
  types,
  type VocabularyWord,
} from "./vocabulary";

/**
 * Escapes a word for use inside a regular expression.
 * @param word The literal word.
 * @returns The escaped text.
 */
function escapeRegExp(word: string): string {
  return word.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

/**
 * Builds the Monarch tokenizer. Directives and annotations are matched as whole prefixed words before identifiers,
 * so `#pragma` and `@count` get their own colours.
 * @returns The Monarch language definition.
 */
function monarchLanguage(): Monaco.languages.IMonarchLanguage {
  const directiveNames = directives.map((word) => escapeRegExp(word.name.slice(1))).join("|");
  const annotationNames = annotations.map((word) => escapeRegExp(word.name.slice(1))).join("|");
  return {
    defaultToken: "",
    tokenPostfix: ".cstruct",
    keywords: keywords.map((word) => word.name),
    typeKeywords: typeWords,
    operators: [
      "!",
      "&&",
      "||",
      "==",
      "!=",
      "<=",
      ">=",
      "|",
      "&",
      "<<",
      ">>",
      "+",
      "-",
      "~",
      "*",
      "/",
      "%",
      "=",
      "<",
      ">",
    ],
    symbols: /[!=><~?:&|+\-*/^%]+/,
    tokenizer: {
      root: [
        [new RegExp(`#\\s*(?:${directiveNames})\\b`), "keyword.directive"],
        // Monarch reads "@name" in a rule as a reference to a definition such as @keywords; "@@" is its escape
        // for a literal "@" character.
        [new RegExp(`@(?:${annotationNames})\\b`.replace("@", "@@")), "keyword.annotation"],
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
        [/[;,.:]/, "delimiter"],
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

/**
 * The editing behaviour of the language: comments, bracket matching and auto-closing pairs.
 * @returns The language configuration.
 */
function languageConfiguration(): Monaco.languages.LanguageConfiguration {
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

/**
 * Registers the language, its tokenizer, completion and hover with a Monaco instance. Call it once per instance.
 * @param monaco The application's Monaco module.
 */
export function registerCStructLanguage(monaco: typeof Monaco): void {
  const { CompletionItemKind, CompletionItemInsertTextRule } = monaco.languages;

  /**
   * Maps a declared name's kind to its completion icon.
   * @param kind The symbol kind.
   * @returns The completion item kind.
   */
  function completionKindFor(kind: CStructSymbolKind): Monaco.languages.CompletionItemKind {
    switch (kind) {
      case "struct":
      case "union":
        return CompletionItemKind.Struct;
      case "enum":
        return CompletionItemKind.Enum;
      case "typedef":
        return CompletionItemKind.Interface;
      case "define":
        return CompletionItemKind.Constant;
    }
  }

  monaco.languages.register({ id: CSTRUCT_LANGUAGE_ID, aliases: ["CStruct", "cstruct"] });
  monaco.languages.setLanguageConfiguration(CSTRUCT_LANGUAGE_ID, languageConfiguration());
  monaco.languages.setMonarchTokensProvider(CSTRUCT_LANGUAGE_ID, monarchLanguage());

  // Completion: the language's words, then the names this document declares.
  monaco.languages.registerCompletionItemProvider(CSTRUCT_LANGUAGE_ID, {
    triggerCharacters: ["@", "#"],
    /** Offers the vocabulary words and the document's declared names at the cursor. */
    provideCompletionItems(model, position) {
      const word = model.getWordUntilPosition(position);
      // A typed "@" or "#" is not part of Monaco's word, so the replaced range starts one column earlier.
      const prefix = model.getLineContent(position.lineNumber).charAt(word.startColumn - 2);
      const startColumn =
        prefix === "@" || prefix === "#" ? word.startColumn - 1 : word.startColumn;
      const range = {
        startLineNumber: position.lineNumber,
        endLineNumber: position.lineNumber,
        startColumn,
        endColumn: word.endColumn,
      };
      /** A completion for one vocabulary word, sorted after its group's prefix. */
      const plain = (
        entry: VocabularyWord,
        kind: Monaco.languages.CompletionItemKind,
        sortPrefix: string,
      ) => ({
        label: entry.name,
        kind,
        detail: entry.detail,
        insertText: entry.name,
        // Single words sort before multi-word aliases such as "unsigned long long int".
        sortText: `${sortPrefix}${entry.name.includes(" ") ? "z" : "a"}${entry.name}`,
        range,
      });
      const suggestions: Monaco.languages.CompletionItem[] = [
        ...keywords.map((entry) => plain(entry, CompletionItemKind.Keyword, "1")),
        ...types.map((entry) => plain(entry, CompletionItemKind.TypeParameter, "2")),
        ...directives.map((entry) => plain(entry, CompletionItemKind.Keyword, "3")),
        ...annotations.map((entry) => ({
          label: entry.name,
          kind: CompletionItemKind.Snippet,
          detail: entry.detail,
          insertText: `${entry.name}(\${1:N})`,
          insertTextRules: CompletionItemInsertTextRule.InsertAsSnippet,
          sortText: `3${entry.name}`,
          range,
        })),
        ...collectSymbols(model.getValue()).map((symbol) => ({
          label: symbol.name,
          kind: completionKindFor(symbol.kind),
          detail: symbol.detail,
          documentation: symbol.documentation,
          insertText: symbol.name,
          sortText: `0${symbol.name}`,
          range,
        })),
      ];
      return { suggestions };
    },
  });

  // Hover: a language word's description, or a declared name's signature and member count.
  monaco.languages.registerHoverProvider(CSTRUCT_LANGUAGE_ID, {
    /** Describes the word under the cursor, including its @ or # prefix. */
    provideHover(model, position) {
      const word = model.getWordAtPosition(position);
      if (!word) return null;
      const range = new monaco.Range(
        position.lineNumber,
        word.startColumn,
        position.lineNumber,
        word.endColumn,
      );
      const line = model.getLineContent(position.lineNumber);
      const prefix = line.charAt(word.startColumn - 2);
      const spelled = prefix === "@" || prefix === "#" ? `${prefix}${word.word}` : word.word;
      const vocabularyWord = [...keywords, ...types, ...annotations, ...directives].find(
        (entry) => entry.name === spelled,
      );
      if (vocabularyWord) {
        return {
          range,
          contents: [{ value: `**${vocabularyWord.name}**` }, { value: vocabularyWord.detail }],
        };
      }

      const symbol = collectSymbols(model.getValue()).find((entry) => entry.name === word.word);
      return symbol
        ? { range, contents: [{ value: `**${symbol.detail}**` }, { value: symbol.documentation }] }
        : null;
    },
  });
}
