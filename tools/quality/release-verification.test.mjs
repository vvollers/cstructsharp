/** Checks exact-source release eligibility without publishing. Usage: node --test tools/quality/release-verification.test.mjs. */
import assert from "node:assert/strict";
import test from "node:test";
import { sourceVerificationJobs, verifySourceJobs } from "../lib/release-verification.mjs";

// Every selected source gate must be present once, successful, and tied to the immutable manifest commit.
test("release source gates fail closed for missing, failed, skipped, duplicate or wrong-source evidence", () => {
  const sha = "a".repeat(40);
  const jobs = sourceVerificationJobs.map((name) => ({ name, conclusion: "success", head_sha: sha }));
  verifySourceJobs(jobs, sha);
  for (let index = 0; index < jobs.length; index++) {
    assert.throws(() => verifySourceJobs(jobs.filter((_, position) => position !== index), sha), /exactly one/);
    assert.throws(() => verifySourceJobs([...jobs, jobs[index]], sha), /exactly one/);
    for (const conclusion of ["failure", "skipped", "cancelled", null]) {
      const changed = jobs.map((job, position) => position === index ? { ...job, conclusion } : job);
      assert.throws(() => verifySourceJobs(changed, sha), /did not pass/);
    }
    const wrongSource = jobs.map((job, position) => position === index ? { ...job, head_sha: "b".repeat(40) } : job);
    assert.throws(() => verifySourceJobs(wrongSource, sha), /different commit/);
  }
});
