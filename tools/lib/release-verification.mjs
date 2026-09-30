/**
 * Release source-gate identity checks: publication and recovery accept only complete original evidence.
 *
 * The jobs a release run must have passed are read from `.github/workflows/release.yml` at the release's source
 * commit: every job the `publish` job needs, directly or through other jobs. A job that calls a reusable workflow
 * (`uses: ./.github/workflows/<file>.yml`) stands for every job of that workflow, named the way GitHub names them
 * (`<caller name> / <called job name>`), and a matrix job stands for each of its combinations. Renaming a job or
 * changing a matrix therefore changes the expected evidence automatically.
 *
 *   import { expectedReleaseJobs, verifyReleaseJobs } from "../lib/release-verification.mjs";
 */
import assert from "node:assert/strict";
import { jobDisplayNames, readWorkflow } from "./workflow-yaml.mjs";

/** The release workflow, relative to the repository root. */
export const releaseWorkflowPath = ".github/workflows/release.yml";

/** The release job whose (transitive) needs are the publication gates. */
export const publishJobId = "publish";

/**
 * Lists the names of the jobs of one workflow, expanding matrices and calls of local reusable workflows.
 * @param {string} root The repository root.
 * @param {string} workflowPath The workflow file, relative to the root.
 * @param {string[] | undefined} selectedIds The job ids to include, in any order; undefined selects every job.
 * @param {string[]} callers The workflow paths already on the call stack, which rejects a call cycle.
 * @returns {string[]} The job names as the Actions API reports them for a run of this workflow.
 */
function workflowJobNames(root, workflowPath, selectedIds, callers = []) {
  assert.ok(!callers.includes(workflowPath), `Reusable workflow cycle: ${[...callers, workflowPath].join(" -> ")}`);
  const workflow = readWorkflow(root, workflowPath);
  const jobs = workflow.jobs ?? {};
  const names = [];
  for (const [id, job] of Object.entries(jobs)) {
    if (selectedIds && !selectedIds.includes(id)) continue;
    if (job.uses === undefined) {
      names.push(...jobDisplayNames(id, job));
      continue;
    }
    // A local reusable workflow's jobs appear in the caller's run, prefixed by the calling job's name.
    assert.match(job.uses, /^\.\/\.github\/workflows\/[\w.-]+\.ya?ml$/, `${workflowPath}: job '${id}' must call a local reusable workflow.`);
    assert.equal(job.strategy, undefined, `${workflowPath}: job '${id}' calls a workflow from a matrix, which the release check does not model.`);
    const called = job.uses.slice(2);
    const calledWorkflow = readWorkflow(root, called);
    assert.ok(calledWorkflow.on && Object.hasOwn(calledWorkflow.on, "workflow_call"), `${called} must declare workflow_call to be used by ${workflowPath}.`);
    for (const name of workflowJobNames(root, called, undefined, [...callers, workflowPath])) names.push(`${job.name ?? id} / ${name}`);
  }
  return names;
}

/**
 * Derives the job names a release run must have completed successfully before publication.
 * @param {string} root The repository root, checked out at the release's source commit.
 * @param {string} [workflowPath] The release workflow, relative to the root.
 * @returns {string[]} The expected job names, each unique, in workflow order.
 * @throws {Error} When the workflow lacks the publish job, needs an unknown job, or cannot be read.
 */
export function expectedReleaseJobs(root, workflowPath = releaseWorkflowPath) {
  const jobs = readWorkflow(root, workflowPath).jobs ?? {};
  assert.ok(jobs[publishJobId], `${workflowPath} has no '${publishJobId}' job.`);

  // Collect every job the publish job waits for, through any chain of `needs`.
  const required = new Set();
  const pending = [publishJobId];
  while (pending.length > 0) {
    const needs = jobs[pending.pop()].needs ?? [];
    for (const id of Array.isArray(needs) ? needs : [needs]) {
      assert.ok(jobs[id], `${workflowPath}: '${id}' is needed but not defined.`);
      if (!required.has(id)) {
        required.add(id);
        pending.push(id);
      }
    }
  }

  const names = workflowJobNames(root, workflowPath, [...required]);
  assert.equal(new Set(names).size, names.length, `${workflowPath}: two required jobs share a name, so their evidence would be ambiguous.`);
  return names;
}

/**
 * Requires one successful instance of every expected job on the manifest's exact commit; a missing, duplicate,
 * failed, cancelled or skipped job fails.
 * @param {{name: string, conclusion: string | null, head_sha: string}[]} jobs The original run's jobs from the API.
 * @param {string} sourceSha The source commit recorded in the release manifest.
 * @param {string[]} expectedNames The names from {@link expectedReleaseJobs}.
 * @throws {assert.AssertionError} When any expected job lacks exactly one successful run on that commit.
 */
export function verifyReleaseJobs(jobs, sourceSha, expectedNames) {
  assert.ok(expectedNames.length > 0, "No release gates were derived from the workflow.");
  for (const name of expectedNames) {
    const matches = jobs.filter((job) => job.name === name);
    assert.equal(matches.length, 1, `Expected exactly one release gate: ${name}`);
    assert.equal(matches[0].conclusion, "success", `Release gate did not pass: ${name}`);
    assert.equal(matches[0].head_sha, sourceSha, `Release gate ran on a different commit: ${name}`);
  }
}
