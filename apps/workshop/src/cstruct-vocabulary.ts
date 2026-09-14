import contract from "../../../contracts/language/portable-v1.json";

export const cstructKeywords = [
  "struct",
  "union",
  "enum",
  "typedef",
  "const",
  "volatile",
  "restrict",
  "if",
  "else",
  "switch",
  "case",
  "default",
];

// Consume the repository contract directly so new runtime spellings reach the workshop editor.
export const cstructTypes = [
  ...contract.fixedPrimitives.map((type) => ({
    name: type.spelling,
    detail: `${type.bytes} bytes; ${type.clr}. ${type.writerDomain}`,
  })),
  ...contract.terminatedPrimitives.map((type) => ({
    name: type.spelling,
    detail: `${type.encoding}; ${type.terminator}-terminated text.`,
  })),
  ...contract.dynamicNumericPrimitives.map((type) => ({
    name: type.spelling,
    detail: `${type.clr}; at most ${type.maximumBytes} bytes. ${type.writer}`,
  })),
];
