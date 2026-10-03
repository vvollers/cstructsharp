/** Plans bounded managed test runs and validates their TRX evidence. Used by tools/quality/test-managed.mjs. */
import fs from "node:fs";
import os from "node:os";
import { parseArguments } from "./tooling.mjs";
import { findAll, parseXml } from "./xml.mjs";

const projects = {
  runtime: "CStructSharpTests",
  parity: "CStructSharp.Generated.Parity",
  generator: "CStructSharp.Generators.Tests",
  compiler: "CStructSharp.Generators.Modern.Tests",
};

/** Selects explicit suites, frameworks and worker budgets; full runs include Extended tests but not external OptIn corpora. */
export function testPlan(argv, processors = os.availableParallelism()) {
  const options = parseArguments(
    argv,
    {
      full: "flag",
      suite: "string",
      filter: "string",
      "no-build": "flag",
      jobs: "number",
      workers: "number",
      help: "flag",
    },
    { defaults: { full: false, "no-build": false } },
  );
  if (options.help) return { options };
  options.suite ??= options.full ? "all" : "runtime";
  const selected = {
    runtime: ["runtime"],
    parity: ["parity"],
    generator: ["generator", "compiler", "parity"],
    all: ["runtime", "generator", "parity", "compiler"],
  }[options.suite];
  if (!selected) throw new Error("Suite must be runtime, parity, generator, or all.");
  if (options.filter !== undefined && !options.filter.trim())
    throw new Error("Filter cannot be empty.");
  options.jobs ??= processors >= 8 ? 2 : 1;
  if (!Number.isInteger(options.jobs) || options.jobs < 1 || options.jobs > 4)
    throw new Error("Jobs must be an integer in 1..4.");
  options.workers ??= Math.max(1, Math.min(8, Math.floor(processors / options.jobs)));
  if (!Number.isInteger(options.workers) || options.workers < 1 || options.workers > 32)
    throw new Error("Workers must be an integer in 1..32.");
  const category = options.full
    ? "TestCategory!=OptIn"
    : "TestCategory!=OptIn&TestCategory!=Extended";
  const filter = options.filter ? `(${category})&(${options.filter})` : category;
  const suites = [];
  const runs = [];
  for (const name of selected) {
    const project = `tests/${projects[name]}/${projects[name]}.csproj`;
    suites.push({ name, project });
    const frameworks =
      options.full && ["runtime", "parity"].includes(name) ? ["net8.0", "net10.0"] : ["net10.0"];
    for (const framework of frameworks)
      runs.push({ name: `${name}-${framework}`, project, framework, filter });
  }
  return { options, suites, runs };
}

/** Validates one nonempty, fully passing test run and returns its test count and slowest observed cases. */
export function testEvidence(filename) {
  const document = parseXml(fs.readFileSync(filename, "utf8"));
  const counters = findAll(document, "Counters")[0]?.attributes;
  const results = findAll(document, "UnitTestResult");
  // Skipped, aborted and missing results must not silently count as a successful development pass.
  if (
    !counters ||
    !results.length ||
    Number(counters.total) !== results.length ||
    Number(counters.passed) !== results.length ||
    results.some((result) => result.attributes.outcome !== "Passed")
  ) {
    throw new Error(`Incomplete, empty or failed test results: ${filename}`);
  }
  // Preserve all durations in TRX; the summary only highlights where a later investigation should look.
  const cases = results.map((result) => {
    const duration = result.attributes.duration;
    if (!/^\d+:\d{2}:\d{2}(?:\.\d+)?$/.test(duration ?? ""))
      throw new Error(`Invalid test duration: ${filename}`);
    const [hours, minutes, seconds] = duration.split(":").map(Number);
    return { name: result.attributes.testName, seconds: hours * 3600 + minutes * 60 + seconds };
  });
  // Test durations overlap during parallel execution, so they must never be summed as elapsed time.
  cases.sort((a, b) => b.seconds - a.seconds);
  return { count: results.length, slowest: cases.slice(0, 10) };
}

/** Runs at most limit tasks at once; after a failure, finishes active tasks without starting further queued work. */
export async function runBounded(tasks, limit, execute) {
  let next = 0;
  let failed = false;
  const results = [];
  /** Takes the next task until the queue finishes or another worker reports a failure. */
  async function worker() {
    while (!failed && next < tasks.length) {
      const task = tasks[next++];
      try {
        results.push(await execute(task));
      } catch (error) {
        failed = true;
        results.push({ name: task.name, error: error.message });
      }
    }
  }
  // Wait for every active host before reporting failure or returning control to a build command.
  await Promise.all(Array.from({ length: Math.min(limit, tasks.length) }, () => worker()));
  return { results, completed: results.length, failed };
}
