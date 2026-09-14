// Format structural whitespace only; quoted text, comments, and directives stay intact.
export function formatLayout(source: string): string {
  const tokens =
    source.match(
      /^[\t ]*#[^\r\n]*|\/\/[^\r\n]*|\/\*[\s\S]*?\*\/|"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'|[{};()]|\s+|[^\s{};()"'/]+|./gm,
    ) ?? [];
  const lines: string[] = [];
  let line = "";
  let depth = 0;
  let parentheses = 0;
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
      line = line.trimEnd() + " {";
      flush();
      depth++;
    } else if (token === "}") {
      flush();
      depth = Math.max(0, depth - 1);
      line = "}";
    } else if (token === ";") {
      line = line.trimEnd() + ";";
      if (!parentheses) flush();
    } else {
      line += token;
      if (token === "(") parentheses++;
      if (token === ")") parentheses = Math.max(0, parentheses - 1);
    }
  }
  flush();
  return lines.join("\n");
}
