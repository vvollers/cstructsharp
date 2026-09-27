import { describe, expect, it } from "vitest";

import contract from "../../../../contracts/language/portable-v1.json";
import { annotations, directives, keywords, typeWords, types } from "./vocabulary";

/** Names of a word list. */
const names = (words: { name: string }[]): string[] => words.map((word) => word.name);

describe("editor vocabulary", () => {
  it("offers every unsuffixed primitive and alias spelling in the language contract", () => {
    const offered = new Set(names(types));
    const spellings = [
      ...contract.fixedPrimitives,
      ...contract.dynamicNumericPrimitives,
      ...contract.terminatedPrimitives,
      ...contract.aliasSpellings,
    ]
      .map((entry) => entry.spelling)
      .filter((spelling) => !/[<>]$/.test(spelling));
    expect(spellings.filter((spelling) => !offered.has(spelling))).toEqual([]);
  });

  it("offers every keyword, annotation and directive in the contract vocabulary", () => {
    expect(names(keywords)).toEqual(contract.vocabulary.keywords.map((entry) => entry.spelling));
    expect(names(annotations)).toEqual(
      contract.vocabulary.annotations.map((entry) => entry.spelling),
    );
    expect(names(directives)).toEqual(
      contract.vocabulary.directives.map((entry) => entry.spelling),
    );
  });

  it("includes the words either editor used to miss", () => {
    for (const name of ["int48", "uint48", "int128", "uint128", "float16", "int", "uint32_t"]) {
      expect(names(types)).toContain(name);
    }
    expect(names(annotations)).toContain("@count");
    expect(names(directives)).toContain("#pragma");
  });

  it("offers each word once, with a description, and no byte-order variants", () => {
    const all = [...keywords, ...annotations, ...directives, ...types];
    expect(new Set(names(all)).size).toBe(all.length);
    expect(all.filter((word) => word.detail.trim().length === 0)).toEqual([]);
    expect(names(types).filter((name) => /[<>]$/.test(name))).toEqual([]);
  });

  it("splits multi-word aliases into single highlight words", () => {
    expect(typeWords).toContain("unsigned");
    expect(typeWords).toContain("long");
    expect(typeWords.filter((word) => word.includes(" "))).toEqual([]);
  });
});
