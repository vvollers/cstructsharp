/**
 * The words the layout editors offer, built from the canonical language contract
 * (`contracts/language/portable-v1.json`): every primitive and alias spelling, and the keywords, annotations and
 * directives with their summaries. A new spelling in the contract reaches both editors without an app change, and
 * the managed tests compile every vocabulary example, so the editors never offer a word the parser rejects.
 */
import contract from "../../../../contracts/language/portable-v1.json";

/** One word with the one-line description completion and hover show. */
export interface VocabularyWord {
  /** The spelling as typed, for example `uint32`, `@align` or `#pragma`. */
  name: string;
  /** What the word means. */
  detail: string;
}

/**
 * Drops the byte-order variants: `uint16<` and `uint16>` are typed as `uint16` followed by the suffix, so only the
 * unsuffixed spelling is offered.
 * @param spelling A contract spelling.
 * @returns True for a spelling without a trailing `<` or `>`.
 */
function isUnsuffixed(spelling: string): boolean {
  return !/[<>]$/.test(spelling);
}

/**
 * Keeps the first word of each name, so a spelling listed in two contract tables is offered once.
 * @param words The candidate words, in priority order.
 * @returns The words with unique names.
 */
function uniqueByName(words: VocabularyWord[]): VocabularyWord[] {
  const seen = new Set<string>();
  return words.filter((word) => !seen.has(word.name) && seen.add(word.name));
}

/** Keywords, from `struct` to `offsetof`, with their summaries. */
export const keywords: VocabularyWord[] = contract.vocabulary.keywords.map((entry) => ({
  name: entry.spelling,
  detail: entry.summary,
}));

/** Member and composite annotations (`@align`, `@count`); each takes one parenthesized argument. */
export const annotations: VocabularyWord[] = contract.vocabulary.annotations.map((entry) => ({
  name: entry.spelling,
  detail: entry.summary,
}));

/** Preprocessor directives the parser accepts, from `#define` to `#pragma`. */
export const directives: VocabularyWord[] = contract.vocabulary.directives.map((entry) => ({
  name: entry.spelling,
  detail: entry.summary,
}));

/**
 * Every type spelling: the fixed-width, dynamic-width and terminated primitives with their size and result type,
 * then the C and platform alias spellings (`int`, `uint32_t`, `DWORD`, `unsigned long`, ...) with the codec each
 * one means. Alias spellings can contain spaces.
 */
export const types: VocabularyWord[] = uniqueByName([
  ...contract.fixedPrimitives
    .filter((type) => isUnsuffixed(type.spelling))
    .map((type) => ({
      name: type.spelling,
      detail: `${type.bytes} ${type.bytes === 1 ? "byte" : "bytes"}; ${type.clr}. ${type.writerDomain}`,
    })),
  ...contract.dynamicNumericPrimitives.map((type) => ({
    name: type.spelling,
    detail: `${type.clr}; at most ${type.maximumBytes} bytes. ${type.writer}`,
  })),
  ...contract.terminatedPrimitives
    .filter((type) => isUnsuffixed(type.spelling))
    .map((type) => ({
      name: type.spelling,
      detail: `${type.encoding}; ${type.terminator}-terminated text.`,
    })),
  ...contract.aliasSpellings
    .filter((alias) => isUnsuffixed(alias.spelling))
    .map((alias) => ({
      name: alias.spelling,
      detail:
        `Alias of ${alias.canonical}.` +
        ("cLongWidth32" in alias && alias.cLongWidth32
          ? ` With a 32-bit C long it means ${alias.cLongWidth32}.`
          : ""),
    })),
]);

/**
 * The single words that start a type spelling or appear in a multi-word one (`unsigned`, `long`), for syntax
 * highlighting, which colours one word at a time.
 */
export const typeWords: string[] = [...new Set(types.flatMap((type) => type.name.split(" ")))];
