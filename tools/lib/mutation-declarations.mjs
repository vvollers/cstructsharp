/** Qualifies exact reviewed declarations with no mutation opportunities; never qualifies compiler-rejected code. */
import assert from "node:assert/strict";
import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { mutationSource } from "./mutation-partitions.mjs";

/** Loads source- and tool-version-pinned declarations, all of which must remain in the configured mutation scope. */
export function loadNonMutableDeclarations(root, configuredFiles) {
  const policy = JSON.parse(fs.readFileSync(path.join(root, "contracts/quality/mutation-non-mutable.json"), "utf8"));
  const tools = JSON.parse(fs.readFileSync(path.join(root, ".config/dotnet-tools.json"), "utf8"));
  assert.equal(policy.schemaVersion, 1);
  assert.equal(policy.strykerVersion, tools.tools["dotnet-stryker"].version, "Review non-mutable declarations after a Stryker version change");
  assert.ok(Array.isArray(policy.declarations));
  const declarations = new Map();
  for (const entry of policy.declarations) {
    assert.ok(configuredFiles.includes(entry.pattern), `Non-mutable declaration is outside the configured scope: ${entry.pattern}`);
    assert.ok(!declarations.has(entry.pattern), `Duplicate non-mutable declaration: ${entry.pattern}`);
    assert.ok(typeof entry.reason === "string" && entry.reason.trim(), "A non-mutable declaration requires a reviewed reason");
    const source = fs.readFileSync(path.join(root, mutationSource(entry.pattern)), "utf8").replaceAll("\r\n", "\n");
    const hash = crypto.createHash("sha256").update(source).digest("hex");
    assert.equal(hash, entry.sourceSha256, `Review changed non-mutable declaration: ${entry.pattern}`);
    declarations.set(entry.pattern, { source, reason: entry.reason });
  }
  return declarations;
}

/**
 * Accepts only a present, exact-source report with an explicitly empty mutation array for a reviewed declaration.
 * Returns false for ordinary files. Ignored, compiler-rejected and unfinished mutations are not non-applicability.
 */
export function qualifyNonMutableDeclaration(declarations, pattern, reportedFile) {
  const declaration = declarations.get(pattern);
  if (!declaration) return false;
  assert.equal(typeof reportedFile?.source, "string", `Missing declaration source: ${pattern}`);
  assert.equal(reportedFile.source.replaceAll("\r\n", "\n"), declaration.source, `Declaration report source changed: ${pattern}`);
  assert.ok(Array.isArray(reportedFile.mutants) && reportedFile.mutants.length === 0, `Non-mutable declaration must have an explicit empty mutation array: ${pattern}`);
  return true;
}
