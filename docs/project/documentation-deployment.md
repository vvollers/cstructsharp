---
title: Documentation deployment
description: Validate documentation and deploy the complete project website without creating a library release.
---

# Documentation deployment

The `Documentation` workflow in `.github/workflows/docs.yml` builds the same site as the local validator. Pull
requests and relevant `main` pushes validate the content and retain a review artifact for 14 days. They do not
deploy.

## Build the artifact locally

From the repository root:

```sh
node tools/documentation/validate-documentation.mjs
node tools/documentation/new-documentation-pages-artifact.mjs
```

The first command runs the complete site check. The second packages the already validated static output and checks
its required files, links, repository-subpath behavior, source links, and size. Success creates:

```text
artifacts/documentation/cstructsharp-pages.tar.gz
```

This archive is ignored local output. It must stay below 32 MiB uncompressed and 8 MiB compressed.

## Configure the repository once

A repository administrator must select **GitHub Actions** as the publishing source under **Settings > Pages**. See
GitHub's [publishing-source instructions](https://docs.github.com/en/pages/getting-started-with-github-pages/configuring-a-publishing-source-for-your-github-pages-site).

Keep the `github-pages` environment and add the project's normal branch/reviewer protection before the first public
deployment. The build job needs only `contents: read`. Only the downstream deploy job receives `pages: write` and
`id-token: write`.

## Review a pull-request artifact

After a successful `Documentation` workflow run:

1. Open the run in GitHub Actions.
2. Download `cstructsharp-documentation-<commit>`.
3. Confirm it contains `.nojekyll`, `index.html`, `404.html`, `sitemap.xml`, local search, conceptual pages, and the
   generated API reference.
4. Serve the extracted directory as static files if a browser review of that exact artifact is needed.
5. Check the first-use path, common searches, code copy, navigation, narrow viewport, keyboard focus, light/dark
   themes, long API signatures, and byte diagrams.

The artifact is built only from Git-visible inputs. Ignored local planning files must not be required.

## Authorize deployment

Use the **Publish website** workflow (`.github/workflows/site.yml`) for documentation and frontend updates.
It builds and tests the documentation, explorer, and inspector and includes the project landing page. It keeps
the current library version and does not publish npm or NuGet packages.

Deployment is manual:

1. Confirm that the target commit is on protected `main` and has a successful documentation run.
2. Open the `Publish website` workflow and choose **Run workflow**.
3. Select the reviewed `main` ref.
4. Approve the `github-pages` environment when its protection rules request approval.
5. Wait for the deploy job to report the environment URL.

The deploy job consumes only the complete artifact produced by its successful build job. It shares the release
workflow's concurrency group so those deployments cannot overlap. The `Documentation` workflow validates and uploads review artifacts only.

## README badge data

The complete Pages site includes `/badges/`, which holds JSON endpoints for README images and a page explaining
those measurements. Managed CI produces the `readme-badges` artifact from .NET 10 coverage and test results.
`prepare-site-badges.mjs` downloads that artifact from the latest successful main-branch push run. Website and
release workflows need `actions: read` permission for this step; assembly fails if required badge files are missing.

Coverage measures CStructSharp library lines and branches. Test totals cover the managed .NET 10 run, excluding
Vue and browser tests. The details page links to the source CI run. These values refresh when the complete site is
deployed, including during a full release. The README's CI status badge updates independently through GitHub.

The NuGet size badge measures the package archive: the built package during a release, or the latest GitHub Release
asset during a website-only build. The npm size badge reports unpacked package size, including WASM assets, so the
two sizes measure different things. Inspect the [badge details](https://vvollers.github.io/cstructsharp/badges/)
when interpreting the numbers.

## Verify the live site

The intended documentation URL is `https://vvollers.github.io/cstructsharp/docs/`. The project landing page is at
`https://vvollers.github.io/cstructsharp/`. After deployment, verify:

- home, first-parse guide, language tutorial/manual, and generated API pages;
- custom 404 behavior;
- search for user terms such as “byte order,” “unknown enum,” and “caller-owned output”;
- conceptual edit links to `main` and generated API source links to the exact commit; and
- release-note and documentation-issue links.

The site registers no service worker and owns no browser application cache. GitHub Pages supplies HTTP caching and
replaces the static deployment artifact. Follow GitHub's
[custom workflow requirements](https://docs.github.com/en/pages/getting-started-with-github-pages/using-custom-workflows-with-github-pages)
when the platform or official actions change.
