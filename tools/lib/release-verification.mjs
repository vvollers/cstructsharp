/** Shared release source-gate identity checks; publication and recovery must accept only complete original evidence. */
import assert from "node:assert/strict";

export const sourceVerificationJobs = Object.freeze([
  "Managed verification / Build and test",
  "Managed verification / Managed tests on windows-latest",
  "Managed verification / Managed tests on macos-latest",
  "Explorer verification / Lint and test the WASM bridge and Vue frontend",
  "Inspector verification / Lint and test the binary inspector frontend",
  "Documentation verification / Validate and package documentation",
]);

/** Requires one successful instance of every shared source gate on the manifest's exact commit; missing/skipped gates fail. */
export function verifySourceJobs(jobs, sourceSha) {
  for (const name of sourceVerificationJobs) {
    const matches = jobs.filter((job) => job.name === name);
    assert.equal(matches.length, 1, `Expected exactly one source gate: ${name}`);
    assert.equal(matches[0].conclusion, "success", `Source gate did not pass: ${name}`);
    assert.equal(matches[0].head_sha, sourceSha, `Source gate ran on a different commit: ${name}`);
  }
}
