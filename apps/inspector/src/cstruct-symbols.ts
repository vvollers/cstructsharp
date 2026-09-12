// Pure DSL vocabulary/scanner logic for CStruct intellisense (see cstruct-language.ts for the Monaco
// wiring that consumes it). Kept free of any monaco-editor import so it can be unit-tested directly
// without pulling in Monaco's browser-only runtime.

export const CSTRUCT_KEYWORDS = [
  "if",
  "else",
  "switch",
  "case",
  "default",
  "struct",
  "union",
  "enum",
  "typedef",
  "const",
  "volatile",
  "restrict",
];

export const CSTRUCT_KEYWORD_DOCS: Record<string, string> = {
  if: "Includes a field group when its integer predicate is nonzero.",
  else: "Includes the alternative field group when the if predicate is zero.",
  switch: "Selects a tagged field group without fall-through.",
  case: "A switch tag followed by : { fields }.",
  default: "Fallback switch group: default: { fields }.",
  struct: "Declares a named sequential composite type.",
  union: "Declares a named overlapping composite type - every field starts at offset 0.",
  enum: "Declares a named integral enum. Defaults to 1-byte unsigned backing unless `: type` is given.",
  typedef: "Declares an alias for an existing type, or names an inline struct/union.",
  const:
    "Layout-neutral qualifier - accepted before a type or after a pointer star, then discarded.",
  volatile:
    "Layout-neutral qualifier - accepted before a type or after a pointer star, then discarded.",
  restrict:
    "Layout-neutral qualifier - accepted before a type or after a pointer star, then discarded.",
};

// Canonical (non-suffixed) primitive/type spellings, from docs/language/primitive-types.md.
// A `<`/`>` endianness suffix is typed by the user afterward and isn't offered as a separate entry.
export const CSTRUCT_PRIMITIVE_TYPES: { name: string; detail: string }[] = [
  { name: "byte", detail: "uint8 alias - 1 byte, unsigned 0..255" },
  { name: "uint8", detail: "1 byte, unsigned 0..255" },
  { name: "int8", detail: "1 byte, signed -128..127" },
  { name: "bool", detail: "1 byte boolean - any nonzero byte reads as true" },
  { name: "_Bool", detail: "bool alias" },
  { name: "char", detail: "1 byte raw code unit, U+0000..U+00FF" },
  {
    name: "utf8",
    detail: "UTF-8 byte code unit; utf8 text[N] decodes exactly N bytes as a string",
  },
  { name: "wchar", detail: "2 byte UTF-16 code unit" },
  { name: "latin1", detail: "Byte-counted strict Latin-1 text buffer; scalar/index is a raw byte" },
  { name: "cp437", detail: "Byte-counted strict CP437 text buffer; scalar/index is a raw byte" },
  {
    name: "utf16le",
    detail: "Byte-counted strict UTF-16LE text buffer; scalar/index is a raw byte",
  },
  {
    name: "utf16be",
    detail: "Byte-counted strict UTF-16BE text buffer; scalar/index is a raw byte",
  },
  {
    name: "uleb128_32",
    detail: "Unsigned 32-bit LEB128; dynamic byte width, alignment 1; canonical writes",
  },
  {
    name: "uleb128_64",
    detail: "Unsigned 64-bit LEB128; dynamic byte width, alignment 1; canonical writes",
  },
  {
    name: "sleb128_32",
    detail: "Signed 32-bit LEB128; dynamic byte width, alignment 1; canonical writes",
  },
  {
    name: "sleb128_64",
    detail: "Signed 64-bit LEB128; dynamic byte width, alignment 1; canonical writes",
  },
  { name: "fixed16_16", detail: "Signed 32-bit storage / 65536; exact Double" },
  { name: "ufixed16_16", detail: "Unsigned 32-bit storage / 65536; exact Double" },
  { name: "fixed2_30", detail: "Signed 32-bit storage / 1073741824; exact Double" },
  { name: "ufixed8_8", detail: "Unsigned 16-bit storage / 256; exact Double" },
  { name: "uuid", detail: "16-byte network-order identifier; alignment 1" },
  { name: "guid", detail: "16-byte Windows GUID; alignment 1" },
  { name: "int24", detail: "3 bytes, alignment 1, signed -8388608..8388607 (supports < / >)" },
  { name: "uint24", detail: "3 bytes, alignment 1, unsigned 0..16777215 (supports < / >)" },
  { name: "int16", detail: "2 bytes, signed -32768..32767 (supports < / > endianness suffix)" },
  { name: "uint16", detail: "2 bytes, unsigned 0..65535 (supports < / > endianness suffix)" },
  { name: "int32", detail: "4 bytes, signed (supports < / > endianness suffix)" },
  { name: "uint32", detail: "4 bytes, unsigned (supports < / > endianness suffix)" },
  { name: "int64", detail: "8 bytes, signed (supports < / > endianness suffix)" },
  { name: "uint64", detail: "8 bytes, unsigned (supports < / > endianness suffix)" },
  { name: "float32", detail: "4 byte IEEE-754 binary32 (supports < / > endianness suffix)" },
  { name: "float64", detail: "8 byte IEEE-754 binary64 (supports < / > endianness suffix)" },
  { name: "float", detail: "float32 alias" },
  { name: "double", detail: "float64 alias" },
  { name: "short", detail: "int16 alias" },
  { name: "ushort", detail: "uint16 alias" },
  { name: "int", detail: "int32 alias" },
  { name: "uint", detail: "uint32 alias" },
  { name: "long", detail: "int64 alias - always 64-bit, unlike native C" },
  { name: "ulong", detail: "uint64 alias - always 64-bit, unlike native C" },
  { name: "signed", detail: "int32 alias" },
  { name: "unsigned", detail: "uint32 alias" },
  { name: "signed int", detail: "int32 alias" },
  { name: "unsigned int", detail: "uint32 alias" },
  { name: "signed short", detail: "int16 alias" },
  { name: "unsigned short", detail: "uint16 alias" },
  { name: "signed long", detail: "int64 alias" },
  { name: "unsigned long", detail: "uint64 alias" },
  { name: "long long", detail: "int64 alias" },
  { name: "signed long long", detail: "int64 alias" },
  { name: "unsigned long long", detail: "uint64 alias" },
  { name: "signed char", detail: "int8 alias" },
  { name: "unsigned char", detail: "uint8 alias" },
  { name: "int8_t", detail: "int8 alias" },
  { name: "uint8_t", detail: "uint8 alias" },
  { name: "int16_t", detail: "int16 alias" },
  { name: "uint16_t", detail: "uint16 alias" },
  { name: "int32_t", detail: "int32 alias" },
  { name: "uint32_t", detail: "uint32 alias" },
  { name: "int64_t", detail: "int64 alias" },
  { name: "uint64_t", detail: "uint64 alias" },
  { name: "ascii_string_zero", detail: "NUL-terminated ASCII string" },
  { name: "cstring", detail: "ascii_string_zero alias" },
  { name: "ascii_string_newline", detail: "LF-terminated ASCII string" },
  { name: "utf8_string_zero", detail: "NUL-terminated UTF-8 string" },
  { name: "utf8_string_newline", detail: "LF-terminated UTF-8 string" },
  {
    name: "unicode_string_zero",
    detail: "NUL-terminated UTF-16 string (supports < / > endianness suffix)",
  },
  { name: "string", detail: "unicode_string_zero alias" },
  {
    name: "unicode_string_newline",
    detail: "LF-terminated UTF-16 string (supports < / > endianness suffix)",
  },
];

export type CStructSymbolKind = "struct" | "union" | "enum" | "typedef" | "define";

export interface CStructSymbol {
  kind: CStructSymbolKind;
  name: string;
  detail: string;
  documentation: string;
}

function stripCommentsAndStrings(source: string): string {
  return source.replace(
    /\/\/[^\n]*|\/\*[\s\S]*?\*\/|"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'/g,
    (match) => match.replace(/[^\n]/g, " "),
  );
}

function findMatchingBrace(text: string, openIndex: number): number {
  let depth = 0;
  for (let i = openIndex; i < text.length; i++) {
    if (text[i] === "{") depth++;
    else if (text[i] === "}") {
      depth--;
      if (depth === 0) return i;
    }
  }
  return -1;
}

function countTopLevelFields(body: string): number {
  // Conditional braces group declarations without introducing a result member.
  // Composite braces do introduce a member; skip their children when counting
  // the enclosing type. This is a tolerant editor scan, not layout validation.
  const compositeBraces: boolean[] = [];
  let compositeDepth = 0;
  let count = 0;
  let prefix = "";
  for (const token of body.matchAll(/[A-Za-z_]\w*|[{};]|[^\s]/g)) {
    const value = token[0];
    if (value === "{") {
      const composite = /\b(struct|union|enum)\b/.test(prefix);
      compositeBraces.push(composite);
      if (composite) compositeDepth++;
      prefix = "";
    } else if (value === "}") {
      const composite = compositeBraces.pop();
      if (composite) compositeDepth--;
      prefix = composite ? "member" : "";
    } else if (value === ";") {
      if (compositeDepth === 0 && prefix.length > 0) count++;
      prefix = "";
    } else {
      prefix += ` ${value}`;
    }
  }
  return count;
}

function describeFields(body: string): string {
  const fields = pluralize(countTopLevelFields(body), "field");
  return /\b(if|switch)\s*\(/.test(body)
    ? `${fields} declared across all branches; active fields depend on the data`
    : fields;
}

function pluralize(count: number, noun: string): string {
  return `${count} ${noun}${count === 1 ? "" : "s"}`;
}

/**
 * Best-effort scan for declared struct/union/enum/typedef names and #define constants, used to offer them
 * as completions and hover text anywhere else in the document. Not a real parser: it works directly on the
 * comment/string-stripped source text rather than a token stream, so it can be fooled by pathological input
 * (e.g. braces inside a skipped construct) - acceptable for editor sugar, not for validating the layout.
 */
export function collectSymbols(source: string): CStructSymbol[] {
  const text = stripCommentsAndStrings(source);
  const symbols: CStructSymbol[] = [];
  const seen = new Set<string>();
  const add = (symbol: CStructSymbol): void => {
    if (seen.has(symbol.name)) return;
    seen.add(symbol.name);
    symbols.push(symbol);
  };

  const compositeRe = /\b(struct|union)\s+([A-Za-z_]\w*)\s*(?:@align\s*\([^)]*\)\s*)?\{/g;
  for (let match = compositeRe.exec(text); match; match = compositeRe.exec(text)) {
    const [whole, kind, name] = match as unknown as [string, "struct" | "union", string];
    const openBrace = match.index + whole.length - 1;
    const closeBrace = findMatchingBrace(text, openBrace);
    if (closeBrace === -1) continue;
    const fieldDescription = describeFields(text.slice(openBrace + 1, closeBrace));
    add({ kind, name, detail: `${kind} ${name}`, documentation: fieldDescription });

    const isTypedefBody = /\btypedef\s+(struct|union)\s*$/.test(text.slice(0, match.index));
    const aliasMatch = /^\s*([A-Za-z_]\w*)\s*;/.exec(text.slice(closeBrace + 1));
    if (isTypedefBody && aliasMatch) {
      add({
        kind: "typedef",
        name: aliasMatch[1]!,
        detail: `typedef ${kind} ${name} ${aliasMatch[1]}`,
        documentation: `Alias for ${kind} ${name} (${fieldDescription})`,
      });
    }
  }

  // The tag name is optional (LANG grammar: `[ identifier ]`) - the common real-world form is a
  // completely anonymous `typedef struct { ... } Name;` with no tag at all. An anonymous struct/union with
  // no typedef prefix is instead an ordinary nested field (`struct outer { struct { ... } inner; };`,
  // "inner" is a field name, not a type) and isn't a symbol worth completing/hovering as a type.
  const anonymousCompositeRe = /\b(struct|union)\s*(?:@align\s*\([^)]*\)\s*)?\{/g;
  for (
    let match = anonymousCompositeRe.exec(text);
    match;
    match = anonymousCompositeRe.exec(text)
  ) {
    const [whole, kind] = match as unknown as [string, "struct" | "union"];
    if (!/\btypedef\s*$/.test(text.slice(0, match.index))) continue;
    const openBrace = match.index + whole.length - 1;
    const closeBrace = findMatchingBrace(text, openBrace);
    if (closeBrace === -1) continue;
    const aliasMatch = /^\s*([A-Za-z_]\w*)\s*;/.exec(text.slice(closeBrace + 1));
    if (!aliasMatch) continue;
    const fieldDescription = describeFields(text.slice(openBrace + 1, closeBrace));
    add({
      kind: "typedef",
      name: aliasMatch[1]!,
      detail: `typedef ${kind} ${aliasMatch[1]}`,
      documentation: `Alias for an anonymous ${kind} (${fieldDescription})`,
    });
  }

  const enumRe = /\benum\s+([A-Za-z_]\w*)\s*(?::\s*([A-Za-z_]\w*)\s*)?\{/g;
  for (let match = enumRe.exec(text); match; match = enumRe.exec(text)) {
    const [whole, name, storage] = match;
    const openBrace = match.index + whole.length - 1;
    const closeBrace = findMatchingBrace(text, openBrace);
    if (closeBrace === -1) continue;
    const members = text
      .slice(openBrace + 1, closeBrace)
      .split(",")
      .map((part) => part.trim().split("=")[0]?.trim())
      .filter((value): value is string => !!value);
    add({
      kind: "enum",
      name: name!,
      detail: `enum ${name}${storage ? ` : ${storage}` : ""}`,
      documentation: members.length ? `Values: ${members.join(", ")}` : "No values declared",
    });
  }

  const typedefAliasRe = /\btypedef\s+([A-Za-z_]\w*[<>]?)\s*((?:\*\s*)*)([A-Za-z_]\w*)\s*;/g;
  for (let match = typedefAliasRe.exec(text); match; match = typedefAliasRe.exec(text)) {
    const [, underlying, stars, name] = match;
    add({
      kind: "typedef",
      name: name!,
      detail: `typedef ${underlying}${stars ? stars.replace(/\s+/g, "") : ""} ${name}`,
      documentation: `Alias for ${underlying}${stars?.trim() ? " pointer" : ""}`,
    });
  }

  const defineRe = /#\s*define\s+([A-Za-z_]\w*)\s+([^\n]+)/g;
  for (let match = defineRe.exec(text); match; match = defineRe.exec(text)) {
    const [, name, expression] = match;
    add({
      kind: "define",
      name: name!,
      detail: `#define ${name}`,
      documentation: `= ${expression!.trim()}`,
    });
  }

  return symbols;
}
