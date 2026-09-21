/** Regression checks for generated-code performance reporting. Usage: node --test tools/quality/benchmark-trigger.test.mjs. */
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";

// The workflow deliberately uses a simple quoted path list; test its actual entries rather than a second list of globs.
test("generator and shared build changes schedule advisory PR performance reporting", () => {
  const source = fs.readFileSync(new URL("../../.github/workflows/benchmark-drift.yml", import.meta.url), "utf8");
  const block = source.match(/  pull_request:\n    paths:\n((?:      - "[^"\n]+"\n)+)/)?.[1];
  assert.ok(block, "Expected explicit PR paths in the benchmark workflow");
  // Extract only the trigger's patterns, not unrelated YAML lists.
  const patterns = [...block.matchAll(/"([^"\n]+)"/g)].map((match) => match[1]);
  for (const changed of [
    "src/CStructSharp.Generators/LayoutEmitter.Readers.cs",
    "src/CStructSharp.Generators/CStructSharp.Generators.csproj",
    "src/CStructSharp.Core/Parsing/LayoutParser.cs",
    "src/CStructSharp/CStructOperations.Async.cs",
    "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props",
    "global.json", "NuGet.Config", "nuget.config", "CStructSharp.NonWeb.sln",
  ]) {
    // One changed file must be enough to schedule the report.
    assert.ok(patterns.some((pattern) => path.matchesGlob(changed, pattern)), changed);
  }
  assert.match(source, /schedule:/);
  assert.match(source, /workflow_dispatch:/);
  assert.match(source, /continue-on-error: true/);
  assert.doesNotMatch(source, /--strict\s/);
});
