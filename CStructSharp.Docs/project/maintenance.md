---
title: Documentation maintenance
description: Keep user, language, API, contributor, search, and deployment information accurate as the project changes.
---

# Documentation maintenance

Documentation is part of a public change, not cleanup to postpone until after the code is finished. Maintainers own
the tracked `CStructSharp.Docs` site together with the source and tests that support its claims.

## Version policy

The site describes current supported preview/main behavior. The project version and reviewed compatibility files
identify the release candidate. Add documentation version selection only when two maintained release lines require
different instructions or reference pages.

## Update the right material

| When this changes | Review and update |
| --- | --- |
| Public signature, default, ownership rule, exception, or limit | XML comments, generated API check, affected guide/recipe/example, API compatibility, release notes |
| Layout token, grammar, primitive, expression, placement, path, or operation | Tutorial/manual, EBNF, Portable JSON, feature matrix, valid/invalid fixtures, both-framework tests |
| Build, dependency, test, package, or repository structure | Project overview, setup, repository map, build/test pages, contributor entry points |
| Navigation, search, theme, template, or asset | TOCs, links, likely search terms, browser/accessibility cases, artifact size |
| Workflow, Pages, release, or publication policy | Deployment/release pages, action pins, artifact and authorization validators |
| Release version | Changelog, package notes, version agreement, install guidance, outbound links, final artifact |

## Editorial rules

- Write for a developer with basic C and C#/Java knowledge; introduce low-level or repository-specific terms before
  relying on them.
- Begin with the user's task or the contributor's decision. Explain why a step exists, where to run it, and what
  success looks like.
- Use concrete bytes and values. Show how the code connects to the format.
- Display C# from the compiled documentation runner or another executable fixture. Keep examples internally
  consistent.
- State ownership, stream-position, limit, and partial-output behavior where it affects API choice.
- Link to one maintained explanation instead of repeating the same paragraph on many pages.
- Keep planning notes, prompts, private ledgers, generated logs, and internal reasoning out of the published site.
- Use terms such as *baseline*, *reference data*, or *compatibility rules* only when they help the reader; avoid
  process-heavy language in beginner guides.
- If source and tests cannot settle a required fact, use `[NEEDS CLARIFICATION: ...]` instead of guessing.

## Review cadence

Every pull request runs structure, API/language, example, browser, search, and accessibility checks. Run the external
link checker after changing outbound links and review each time-limited exception by its recorded date.

At each release candidate:

1. Walk the install/first-parse and common recipe paths as a new reader.
2. Check the language tutorial and exact references.
3. Follow contributor setup on a clean machine or isolated checkout.
4. Review desktop/narrow layouts, keyboard use, code copy, tables, byte diagrams, and both themes.
5. Test likely search terms and revise wording when the intended page is hard to find.
6. Build and inspect the Pages artifact before authorizing deployment.

See the [release notes](https://github.com/vvollers/CStructSharp/blob/main/CHANGELOG.md). Report inaccurate steps,
missing explanations, or search failures with the
[documentation issue form](https://github.com/vvollers/CStructSharp/issues/new?labels=documentation&title=Documentation%3A%20).
