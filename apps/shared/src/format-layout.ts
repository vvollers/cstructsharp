/**
 * Adds indentation and line breaks around declarations: one member per line, four spaces per brace level, and one
 * enum or flag value per line. Quoted text, comments and directives stay whole tokens, so punctuation inside them is
 * not mistaken for the structure of the layout. Formatting changes whitespace only and is idempotent.
 * @param source The layout text.
 * @returns The formatted text.
 */
export function formatLayout(source: string): string {
  const tokens =
    source.match(
      /^[\t ]*#[^\r\n]*|\/\/[^\r\n]*|\/\*[\s\S]*?\*\/|"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'|[{};,()]|\s+|[^\s{};,()"'/]+|./gm,
    ) ?? [];

  const lines: string[] = [];
  let line = "";
  let depth = 0;
  let parentheses = 0;
  const enumScopes: boolean[] = [];

  // Finish the current line using four spaces for each level of nested braces.
  /** Ends the current line at the current indentation. */
  const flush = () => {
    if (line.trim()) lines.push("    ".repeat(depth) + line.trim());
    line = "";
  };

  for (const token of tokens) {
    if (token.trimStart().startsWith("#")) {
      flush();
      lines.push(token.trimStart());
    } else if (token.startsWith("//")) {
      line += token;
      flush();
    } else if (token.startsWith("/*")) {
      line += token;
    } else if (/^\s+$/.test(token)) {
      if (line && !line.endsWith(" ")) line += " ";
    } else if (token === "{") {
      // Remember whether these braces hold enum or flag values: their commas start new lines.
      enumScopes.push(/^\s*(?:enum|flag)\b/.test(line));
      line = line.trimEnd() + " {";
      flush();
      depth++;
    } else if (token === "}") {
      flush();
      depth = Math.max(0, depth - 1);
      enumScopes.pop();
      line = "}";
    } else if (token === ";") {
      line = line.trimEnd() + ";";
      if (!parentheses) flush();
    } else if (token === "," && !parentheses && enumScopes[enumScopes.length - 1]) {
      line = line.trimEnd() + ",";
      flush();
    } else {
      // Parentheses can contain expressions; do not split their contents as separate declarations.
      line += token;
      if (token === "(") parentheses++;
      if (token === ")") parentheses = Math.max(0, parentheses - 1);
    }
  }

  flush();
  return lines.join("\n");
}
