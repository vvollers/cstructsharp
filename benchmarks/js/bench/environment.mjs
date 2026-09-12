// Environment capture written into every result file (mirrors contracts/performance baselineEvidence fields).
import os from "node:os";
import fs from "node:fs";
import path from "node:path";
import { execFileSync } from "node:child_process";
import { repositoryRoot } from "./fixtures.mjs";

function git(args) {
  try {
    return execFileSync("git", ["-C", repositoryRoot, ...args], { encoding: "utf8", stdio: ["ignore", "pipe", "ignore"] }).trim();
  } catch {
    return null;
  }
}

export function bundleManifest() {
  const file = path.join(repositoryRoot, "artifacts/js-bench", `${process.env.BENCH_BUNDLE ?? "bundle"}-manifest.json`);
  return fs.existsSync(file) ? JSON.parse(fs.readFileSync(file, "utf8")) : null;
}

export function captureEnvironment(extra = {}) {
  const cpus = os.cpus();
  const bundle = bundleManifest();
  let dotnetInfo = null;
  try {
    dotnetInfo = execFileSync("dotnet", ["--version"], { encoding: "utf8" }).trim();
  } catch {
    dotnetInfo = null;
  }
  return {
    capturedAtUtc: new Date().toISOString(),
    revision: git(["rev-parse", "HEAD"]),
    worktreeDirty: (git(["status", "--porcelain"]) ?? "").length > 0,
    os: { platform: os.platform(), release: os.release(), arch: os.arch() },
    cpu: { model: cpus[0]?.model ?? "unknown", count: cpus.length, speedMHz: cpus[0]?.speed ?? null },
    totalMemoryBytes: os.totalmem(),
    node: process.versions,
    dotnetSdk: dotnetInfo,
    wasmBundle: bundle ? { sha256: bundle.bundleSha256, totalFrameworkBytes: bundle.totalFrameworkBytes, builtAtUtc: bundle.builtAtUtc, extraProperties: bundle.extraProperties } : null,
    ...extra,
  };
}
