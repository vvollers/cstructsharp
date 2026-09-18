#!/usr/bin/env node
/**
 * Turns a Cobertura report into a per-file risk report (JSON + CSV) and enforces optional minimum coverage and
 * maximum risk-file counts.
 *
 *   node tools/quality/coverage-risk.mjs --coverage-path <coverage.cobertura.xml> --output-directory <dir>
 *     [--minimum-line-percent N] [--minimum-branch-percent N] [--maximum-critical-risk-files N] [--maximum-high-risk-files N]
 */
import fs from "node:fs";
import path from "node:path";
import { assertCondition, main, parseArguments, repositoryRoot, runCommand } from "../lib/tooling.mjs";
import { childrenNamed, findAll, parseXml } from "../lib/xml.mjs";

const options = parseArguments(
  process.argv.slice(2),
  {
    "coverage-path": "string",
    "output-directory": "string",
    "minimum-line-percent": "number",
    "minimum-branch-percent": "number",
    "maximum-critical-risk-files": "number",
    "maximum-high-risk-files": "number",
  },
  { defaults: { "minimum-line-percent": 0, "minimum-branch-percent": 0, "maximum-critical-risk-files": 2147483647, "maximum-high-risk-files": 2147483647 } },
);
assertCondition(options["coverage-path"] && options["output-directory"], "Options --coverage-path and --output-directory are required.");

const round6 = (value) => Math.round(value * 1e6) / 1e6;

await main(() => {
  const coveragePath = path.resolve(options["coverage-path"]);
  fs.mkdirSync(options["output-directory"], { recursive: true });
  const outputDirectory = path.resolve(options["output-directory"]);
  const document = parseXml(fs.readFileSync(coveragePath, "utf8"));

  // Merge every class's lines per file: a file with several classes reports a line once per class.
  const fileLines = new Map();
  for (const classNode of findAll(document, "class")) {
    const filename = (classNode.attributes.filename ?? "").replaceAll("\\", "/");
    if (!filename.trim()) continue;
    if (!fileLines.has(filename)) fileLines.set(filename, new Map());
    const lines = fileLines.get(filename);
    for (const linesNode of childrenNamed(classNode, "lines")) {
      for (const lineNode of childrenNamed(linesNode, "line")) {
        const number = Number(lineNode.attributes.number);
        const hits = Number(lineNode.attributes.hits);
        let branchesCovered = 0;
        let branchesValid = 0;
        const condition = /\((\d+)\/(\d+)\)/.exec(lineNode.attributes["condition-coverage"] ?? "");
        if (/^true$/i.test(lineNode.attributes.branch ?? "") && condition) {
          branchesCovered = Number(condition[1]);
          branchesValid = Number(condition[2]);
        }
        const existing = lines.get(number);
        if (existing) {
          existing.hits = Math.max(existing.hits, hits);
          existing.branchesCovered = Math.max(existing.branchesCovered, branchesCovered);
          existing.branchesValid = Math.max(existing.branchesValid, branchesValid);
        } else {
          lines.set(number, { hits, branchesCovered, branchesValid });
        }
      }
    }
  }

  const fileReports = [...fileLines].map(([file, lines]) => {
    const values = [...lines.values()];
    const linesValid = values.length;
    const linesCovered = values.filter((line) => line.hits > 0).length;
    const branchesValid = values.reduce((sum, line) => sum + line.branchesValid, 0);
    const branchesCovered = values.reduce((sum, line) => sum + line.branchesCovered, 0);
    const lineRate = linesValid === 0 ? 1 : linesCovered / linesValid;
    const branchRate = branchesValid === 0 ? 1 : branchesCovered / branchesValid;
    const uncoveredLines = linesValid - linesCovered;
    const uncoveredBranches = branchesValid - branchesCovered;
    const riskBand =
      lineRate < 0.6 || (branchesValid >= 10 && branchRate < 0.5) ? "critical"
      : lineRate < 0.75 || (branchesValid >= 10 && branchRate < 0.65) ? "high"
      : lineRate < 0.9 || (branchesValid >= 10 && branchRate < 0.8) ? "medium"
      : "low";
    return {
      file,
      riskBand,
      riskScore: uncoveredLines + 2 * uncoveredBranches,
      linesCovered,
      linesValid,
      lineRate: round6(lineRate),
      branchesCovered,
      branchesValid,
      branchRate: round6(branchRate),
      uncoveredLines,
      uncoveredBranches,
    };
  });
  fileReports.sort((a, b) => b.riskScore - a.riskScore || (a.file < b.file ? -1 : a.file > b.file ? 1 : 0));

  const sum = (key) => fileReports.reduce((total, report) => total + report[key], 0);
  const totalLinesValid = sum("linesValid");
  const totalLinesCovered = sum("linesCovered");
  const totalBranchesValid = sum("branchesValid");
  const totalBranchesCovered = sum("branchesCovered");
  const revision = runCommand("git", ["-C", repositoryRoot, "rev-parse", "HEAD"], { allowFailure: true }).stdout.trim() || null;
  const dirty = runCommand("git", ["-C", repositoryRoot, "status", "--porcelain"], { allowFailure: true }).stdout.trim().length > 0;
  const summary = {
    files: fileReports.length,
    linesCovered: totalLinesCovered,
    linesValid: totalLinesValid,
    lineRate: totalLinesValid === 0 ? 1 : round6(totalLinesCovered / totalLinesValid),
    branchesCovered: totalBranchesCovered,
    branchesValid: totalBranchesValid,
    branchRate: totalBranchesValid === 0 ? 1 : round6(totalBranchesCovered / totalBranchesValid),
    criticalRiskFiles: fileReports.filter((report) => report.riskBand === "critical").length,
    highRiskFiles: fileReports.filter((report) => report.riskBand === "high").length,
  };
  const report = {
    schemaVersion: 1,
    generatedAtUtc: new Date().toISOString(),
    revision,
    worktreeDirty: dirty,
    source: path.basename(coveragePath),
    riskFormula: "uncoveredLines + (2 * uncoveredBranches); bands are informational and are not CI gates",
    summary,
    files: fileReports,
  };
  const jsonPath = path.join(outputDirectory, "coverage-risk.json");
  const csvPath = path.join(outputDirectory, "coverage-risk.csv");
  fs.writeFileSync(jsonPath, `${JSON.stringify(report, null, 2)}\n`);
  const columns = Object.keys(fileReports[0] ?? { file: "" });
  const csvValue = (value) => (typeof value === "string" ? `"${value.replaceAll('"', '""')}"` : String(value));
  fs.writeFileSync(csvPath, [columns.map(csvValue).join(","), ...fileReports.map((row) => columns.map((column) => csvValue(row[column])).join(","))].join("\n") + "\n");

  console.log(`Coverage risk report written to ${jsonPath}`);
  console.log(`Files: ${fileReports.length}; line rate: ${summary.lineRate}; branch rate: ${summary.branchRate}`);

  const linePercent = 100 * summary.lineRate;
  const branchPercent = 100 * summary.branchRate;
  assertCondition(linePercent >= options["minimum-line-percent"], `Line coverage ${Math.round(linePercent * 100) / 100}% is below the required ${options["minimum-line-percent"]}%.`);
  assertCondition(branchPercent >= options["minimum-branch-percent"], `Branch coverage ${Math.round(branchPercent * 100) / 100}% is below the required ${options["minimum-branch-percent"]}%.`);
  assertCondition(summary.criticalRiskFiles <= options["maximum-critical-risk-files"], `Coverage has ${summary.criticalRiskFiles} critical-risk files; at most ${options["maximum-critical-risk-files"]} are allowed.`);
  assertCondition(summary.highRiskFiles <= options["maximum-high-risk-files"], `Coverage has ${summary.highRiskFiles} high-risk files; at most ${options["maximum-high-risk-files"]} are allowed.`);
});
