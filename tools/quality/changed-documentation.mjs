/** Checks comments on changed named declarations. Usage: node tools/quality/changed-documentation.mjs --base <git-ref> --language csharp|script. */
import fs from "node:fs";
import path from "node:path";
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";
import { execFileSync } from "node:child_process";
import { repositoryRoot } from "../lib/tooling.mjs";

/** Runs a read-only Git query from the repository root and returns its text. */
function git(...args) {
  return execFileSync("git", args, { cwd: repositoryRoot, encoding: "utf8", maxBuffer: 32 * 1024 * 1024 });
}

/**
 * Collects changed line intervals, including the following declaration when a comment was deleted.
 * @param diff Git's unified diff text with zero context lines.
 * @returns One-based, inclusive line ranges in the current source, not the removed version.
 */
export function changedRanges(diff) {
  return [...diff.matchAll(/^@@ -\d+(?:,\d+)? \+(\d+)(?:,(\d+))? @@/gm)].map((match) => {
    const count = Number(match[2] ?? 1);
    // A deletion-only hunk names the preceding surviving line, not the declaration after the deletion.
    const start = Math.max(1, Number(match[1]) + (count === 0 ? 1 : 0));
    return { start, end: start + Math.max(1, count) - 1 };
  });
}

/**
 * Uses the app's existing TypeScript and Vue parsers; inspected scripts are never executed.
 * @param entries Source snapshots with file names and changed, one-based inclusive line ranges.
 * @returns Diagnostics for undocumented changed named declarations; malformed scripts throw.
 */
export function inspectScripts(entries) {
  const require = createRequire(path.join(repositoryRoot, "apps/explorer/package.json"));
  const ts = require("typescript");
  const vue = require("@vue/compiler-sfc");
  const issues = [];
  for (const entry of entries) {
    let sources = [entry.source];
    if (entry.file.endsWith(".vue")) {
      const parsed = vue.parse(entry.source, { filename: entry.file });
      if (parsed.errors.length) throw new Error(`${entry.file}: Vue parsing failed: ${parsed.errors.join(", ")}`);
      // Keep original line numbers while excluding the template and style blocks from script parsing.
      sources = [parsed.descriptor.script, parsed.descriptor.scriptSetup].filter(Boolean).map((block) =>
        "\n".repeat(block.loc.start.line - 1) + block.content);
    }
    for (const source of sources) {
      const tree = ts.createSourceFile(entry.file, source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS);
      if (tree.parseDiagnostics.length) {
        throw new Error(`${entry.file}: script parsing failed: ${tree.parseDiagnostics.map((diagnostic) => ts.flattenDiagnosticMessageText(diagnostic.messageText, " ")).join("; ")}`);
      }
      /** Visits named declarations and their comment-bearing owner, then follows nested declarations. */
      function visit(node) {
        let owner = node;
        let named = ts.isClassDeclaration(node) || ts.isFunctionDeclaration(node) || ts.isMethodDeclaration(node) || ts.isConstructorDeclaration(node);
        if ((ts.isVariableDeclaration(node) || ts.isPropertyDeclaration(node) || ts.isPropertyAssignment(node)) &&
            node.initializer && (ts.isArrowFunction(node.initializer) || ts.isFunctionExpression(node.initializer))) {
          named = true;
          if (ts.isVariableDeclaration(node)) owner = node.parent.parent;
        }
        if (named) {
          const comments = ts.getLeadingCommentRanges(source, owner.getFullStart()) ?? [];
          const start = tree.getLineAndCharacterOfPosition(comments[0]?.pos ?? owner.getStart(tree)).line + 1;
          const end = tree.getLineAndCharacterOfPosition(owner.end).line + 1;
          // A changed body also changes its declaration's contract-review scope; untouched siblings stay out.
          const changed = entry.ranges.some((range) => range.start <= end && range.end >= start);
          const documented = comments.some((comment) => {
            const text = source.slice(comment.pos, comment.end);
            return text.startsWith("/**") && text.slice(3, -2).replace(/[\s*]/g, "").length > 0;
          });
          if (changed && !documented) {
            const line = tree.getLineAndCharacterOfPosition(node.getStart(tree)).line + 1;
            issues.push(`${entry.file}:${line}: named declaration needs a non-empty JSDoc/TSDoc comment.`);
          }
        }
        ts.forEachChild(node, visit);
      }
      visit(tree);
    }
  }
  return issues;
}

/** Inspects changed authored files and fails on missing documentation, parser/build failures or an invalid base. */
function main() {
  const args = process.argv.slice(2);
  const base = args[args.indexOf("--base") + 1];
  const language = args[args.indexOf("--language") + 1];
  if (!args.includes("--base") || !args.includes("--language") || !["csharp", "script"].includes(language)) {
    throw new Error("Usage: changed-documentation.mjs --base <git-ref> --language csharp|script");
  }
  git("rev-parse", "--verify", `${base}^{commit}`);
  const tracked = git("diff", "--name-only", "-z", "--no-renames", base).split("\0");
  const untracked = git("ls-files", "--others", "--exclude-standard", "-z").split("\0");
  const entries = [];
  for (const file of new Set([...tracked, ...untracked])) {
    if (!file || /(?:^|\/)(?:artifacts|node_modules|bin|obj|snapshots|third-party)\//.test(file) || /\.g\.cs$/.test(file)) continue;
    if (!(language === "csharp" ? /\.cs$/ : /\.(?:[cm]?[jt]s|vue)$/).test(file)) continue;
    const filename = path.join(repositoryRoot, file);
    if (!fs.existsSync(filename)) continue;
    const source = fs.readFileSync(filename, "utf8");
    const ranges = untracked.includes(file) ? [{ start: 1, end: source.split("\n").length }] :
      changedRanges(git("diff", "--no-ext-diff", "--unified=0", "--no-renames", base, "--", file));
    if (ranges.length) entries.push({ file, source, ranges });
  }
  let issues = [];
  if (language === "script") issues = inspectScripts(entries);
  else if (entries.length) {
    const output = execFileSync("dotnet", ["run", "--file", "tools/quality/CSharpComments.cs"], {
      cwd: repositoryRoot, input: JSON.stringify({ entries }), encoding: "utf8", maxBuffer: 32 * 1024 * 1024,
    });
    const result = output.split(/\r?\n/).find((line) => line.startsWith("DOC-COMMENT-RESULT:"));
    if (!result) throw new Error(`C# checker returned no result: ${output}`);
    issues = JSON.parse(result.slice("DOC-COMMENT-RESULT:".length));
  }
  if (issues.length) throw new Error(issues.join("\n"));
  console.log(`Changed-declaration documentation passed (${entries.length} ${language} files).`);
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) main();
