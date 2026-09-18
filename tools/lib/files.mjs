/** File-system helpers shared by the Node tools. */
import fs from "node:fs";
import path from "node:path";
import { runCommand } from "./tooling.mjs";

/** Every file below a directory (absolute paths, sorted), optionally filtered by a predicate on the absolute path. */
export function listFiles(directory, predicate = () => true) {
  const files = [];
  const visit = (current) => {
    for (const entry of fs.readdirSync(current, { withFileTypes: true }).sort((a, b) => (a.name < b.name ? -1 : a.name > b.name ? 1 : 0))) {
      const full = path.join(current, entry.name);
      if (entry.isDirectory()) visit(full);
      else if (entry.isFile() && predicate(full)) files.push(full);
    }
  };
  if (fs.existsSync(directory)) visit(directory);
  return files;
}

export const isFile = (file) => fs.existsSync(file) && fs.statSync(file).isFile();
export const isDirectory = (file) => fs.existsSync(file) && fs.statSync(file).isDirectory();
export const toPosix = (file) => file.replaceAll("\\", "/");

/** Tracked plus untracked-but-not-ignored files of a repository, as repository-relative POSIX paths. */
export function repositoryFiles(repositoryRoot) {
  const result = runCommand("git", ["-C", repositoryRoot, "ls-files", "--cached", "--others", "--exclude-standard"]);
  return result.stdout.split("\n").map((line) => line.trim()).filter(Boolean);
}

/** Whether a repository-relative path is ignored by git. */
export function isIgnored(repositoryRoot, relative) {
  return runCommand("git", ["-C", repositoryRoot, "check-ignore", "--quiet", "--", relative], { allowFailure: true }).status === 0;
}

/** Splits text into lines the way the PowerShell tools did (`\r?\n`). */
export const lines = (text) => text.split(/\r?\n/);

/** Text with CRLF normalized to LF. */
export const normalizeNewlines = (text) => text.replaceAll("\r\n", "\n");

/** `.` and other regex metacharacters escaped. */
export const escapeRegex = (text) => text.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
