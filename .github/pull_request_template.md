## Summary

Describe the user-visible or maintainer-visible outcome and the smallest shared change that produces it.

## Evidence

- [ ] A focused regression failed first for the intended reason.
- [ ] Relevant tests pass on both managed target frameworks.
- [ ] A warning-free non-Web build and affected validators pass.
- [ ] Exact commands, counts, limitations, and artifact impact are recorded.
- [ ] No unrelated generated output or local planning/evidence files are included.

## Documentation impact

- [ ] Public API comments, guides, examples, and compatibility contracts are updated when caller behavior changed.
- [ ] The language manual, EBNF, Portable contract, matrix, and fixtures are updated when layout behavior changed.
- [ ] Project/contributor documentation is updated when build, dependency, test, workflow, or release behavior changed.
- [ ] Search/navigation/browser/accessibility/Pages gates pass for documentation presentation changes.
- [ ] `CHANGELOG.md`, package release notes, and documentation links agree when release-facing behavior changed.
- [ ] If no documentation changed, the summary explains why no published behavior or workflow changed.

Publication, deployment, tags, releases, commits, and pushes remain separately authorized actions.
