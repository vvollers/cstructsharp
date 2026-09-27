// The document symbol scan behind completion and hover: declared struct, union, enum and typedef names and #define
// constants. It has no monaco-editor import, so it runs and is unit-tested without Monaco's browser-only runtime.

/** The kind of a declared name: a composite, an enum, a typedef alias or a #define constant. */
export type CStructSymbolKind = "struct" | "union" | "enum" | "typedef" | "define";

/** A declared name with the one-line signature and the description completion and hover show. */
export interface CStructSymbol {
  kind: CStructSymbolKind;
  name: string;
  detail: string;
  documentation: string;
}

/**
 * Blanks comments and string literals, keeping line breaks, so the scan sees only declarations at their original
 * offsets.
 * @param source The layout text.
 * @returns The text with comments and strings replaced by spaces.
 */
function stripCommentsAndStrings(source: string): string {
  return source.replace(
    /\/\/[^\n]*|\/\*[\s\S]*?\*\/|"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'/g,
    (match) => match.replace(/[^\n]/g, " "),
  );
}

/**
 * Finds the brace that closes the one at an index.
 * @param text The stripped text.
 * @param openIndex The index of an opening brace.
 * @returns The index of its closing brace, or -1 when it is not closed.
 */
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

/**
 * Counts the members a composite body declares, including every conditional branch but not the members of nested
 * composites.
 * @param body The text between a composite's braces.
 * @returns The member count.
 */
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

/**
 * Describes a composite body by its member count, noting when conditions decide which members exist.
 * @param body The text between a composite's braces.
 * @returns For example "3 fields".
 */
function describeFields(body: string): string {
  const fields = pluralize(countTopLevelFields(body), "field");
  return /\b(if|switch)\s*\(/.test(body)
    ? `${fields} declared across all branches; active fields depend on the data`
    : fields;
}

/**
 * Formats a count with a singular or plural noun.
 * @param count The count.
 * @param noun The singular noun.
 * @returns For example "1 field" or "2 fields".
 */
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
  /** Records a symbol unless its name was already found. */
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
