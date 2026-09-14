#!/usr/bin/env node
// Turns a perf "--children -g none --sort sym" listing (profile-dotnet.sh writes <scenario>.inclusive.txt) into a
// compact top-N table of managed/native frames, normalized to the scenario's own root frame so the percentages
// read as "share of the benchmarked operation" rather than "share of the process".
// Usage: node benchmarks/profiling/summarize-perf.mjs <scenario> [outdir] [topN]
import fs from "node:fs";
import path from "node:path";

const scenario = process.argv[2];
const outdir = process.argv[3] ?? "artifacts/profiles";
const topN = Number(process.argv[4] ?? 12);
const lines = fs.readFileSync(path.join(outdir, `${scenario}.inclusive.txt`), "utf8").split("\n");
const rows = [];
for (const line of lines) {
  const m = line.match(/^\s*([\d.]+)%\s+([\d.]+)%\s+\[\.\]\s+(.*)$/);
  if (!m) continue;
  const symbol = m[3];
  if (/^0x[0-9a-f]+$/.test(symbol)) continue;
  if (/__libc_start_main|hostfxr_main|corehost_main|coreclr_execute_assembly|Program::Main|ProfileDriver::RunFor|ReportStubBlock|Func`1.*Invoke/.test(symbol)) continue;
  rows.push({ children: Number(m[1]), self: Number(m[2]), symbol });
}
const shorten = (s) => s
  .replace(/\[(OptimizedTier1|OptimizedTier1OSR|QuickJitted|Instrumented Tier1|Tier0)\]$/, "")
  .replace(/instance |class |valuetype |int32 |int64 |bool |void |object |string |uint8 /g, "")
  .replace(/\[System\.Runtime\]|\[System\.Collections\]|\[System\.Private\.CoreLib\]|\[System\.Linq\]|\[System\.Memory\]/g, "")
  .replace(/\[CStructSharp\] /g, "")
  .replace(/System\.__Canon/g, "T")
  .replace(/\(.*$/, "()")
  .slice(0, 110);
const root = rows[0]?.children ?? 100;
const table = rows.slice(0, topN).map((r) => ({
  inclusiveOfRoot: (100 * r.children / root).toFixed(1) + "%",
  inclusiveOfProcess: r.children.toFixed(1) + "%",
  self: r.self.toFixed(1) + "%",
  frame: shorten(r.symbol),
}));
const markdown = [
  `#### ${scenario} (perf, ${rows.length} symbolized frames ≥ 0.5 %; root frame = \`${shorten(rows[0]?.symbol ?? "?")}\`)`,
  "",
  "| incl. of root | incl. of process | self | frame |",
  "| ---: | ---: | ---: | --- |",
  ...table.map((t) => `| ${t.inclusiveOfRoot} | ${t.inclusiveOfProcess} | ${t.self} | \`${t.frame}\` |`),
  "",
].join("\n");
fs.writeFileSync(path.join(outdir, `${scenario}.top.md`), markdown);
process.stdout.write(markdown);
