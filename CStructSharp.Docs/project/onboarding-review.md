---
title: Review onboarding with new users
description: Run consistent first-use sessions and record evidence before claiming the onboarding targets are met.
---

# Review onboarding with new users

The release maintainer owns the public getting-started routes, example catalog, and release checks. Contributors
changing a public example should update its source, teaching text, expected output, and related lesson together.
The maintainer arranging a release should also arrange this human review. Automated tests cannot measure whether
a new user understands the material.

## Run five first-use sessions

Recruit five volunteers familiar with basic C and C# but new to CStructSharp. Ask their permission to take notes.
Use participant numbers rather than personal details. Record the tested revision, package version, OS, browser,
and installed SDK. Keep prerequisite installation time separate from task time.

Give each participant these tasks without demonstrating the steps:

1. Read the project starting page for two minutes. Explain what input the library needs, what it returns, and
   whether it imports arbitrary C headers.
2. Open the browser lesson, read the header, and change kind from 2 to 3. Target: five minutes.
3. Create a C# console application from the instructions and run the first example. Target: ten minutes.
4. Download and run the WASM starter in a local browser. Target: fifteen minutes.
5. Find a writing or updating example. Repair a deliberately truncated header without assistance.

Alternate the C# and WASM task order across sessions so one route does not always benefit from prior practice.
Ask participants to describe what they expect before running an operation. Record where they pause, which links
they follow, unfamiliar words, copying failures, and every hint they need. Do not silently correct their setup.

## Record results honestly

No human sessions have been completed as part of the automated implementation work. For this onboarding update,
the author waived live sessions because no other reviewers are available and accepted proceeding on the assumption
that the improvements are good. The author can use the tasks above for a self-guided review. The table remains
available for future sessions; an empty result is unmeasured, not a pass.

| Participant | Purpose and limits explained | Browser minutes/help | C# minutes/help | WASM minutes/help | Found update and fixed truncation | Main obstacle |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | Unmeasured | Unmeasured | Unmeasured | Unmeasured | Unmeasured | Unmeasured |
| 2 | Unmeasured | Unmeasured | Unmeasured | Unmeasured | Unmeasured | Unmeasured |
| 3 | Unmeasured | Unmeasured | Unmeasured | Unmeasured | Unmeasured | Unmeasured |
| 4 | Unmeasured | Unmeasured | Unmeasured | Unmeasured | Unmeasured | Unmeasured |
| 5 | Unmeasured | Unmeasured | Unmeasured | Unmeasured | Unmeasured | Unmeasured |

The target is at least four of five completing each task within its time without maintainer help. A small sample
provides useful observations, not statistical certainty. Turn failures into concrete follow-up issues with the task,
observed obstacle, affected page or control, proposed change, and a retest result. Do not replace human evidence
with an automated readability score.

## Keep technical evidence with the release

Run the full documentation check, then validate actual artifacts:

```powershell
./tools/Validate-Documentation.ps1
./tools/Test-OnboardingPackage.ps1 -PackageDirectory ./artifacts/package
./tools/Test-OnboardingBrowser.ps1 -ArchivePath ./artifacts/cstructsharp-wasm-vVERSION.zip
```

Replace VERSION with the packaged version. The package check copies the exact README program and all exported
recipes into a temporary project outside the repository. It tests the starters on .NET 8 and .NET 10 and the full
recipes on .NET 10. The browser check extracts the real ZIP and runs it under `/tools/binary/`, including failures
and a downloaded-file byte comparison. It needs the web project's installed dependencies and Playwright Chromium.

The release workflow runs these artifact checks before publishing their corresponding artifacts. Documentation
editing remains independent of WASM builds. Keep historical compatibility snapshots labeled as historical;
development metadata and published releases answer different version questions.
