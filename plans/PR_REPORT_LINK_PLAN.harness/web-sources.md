# Web sources for PR_REPORT_LINK_PLAN §1.6

Gathered on 2026-09-27 by a research agent in the session that wrote the plan, from official pages, release
notes and source code: tags with `git ls-remote` and blobless clones, quotes taken from the raw HTML. The plan's
author relayed them and did not re-read every page. Each answer carries the agent's own mark: VERIFIED (the
source was read and says so), PARTLY VERIFIED, or UNVERIFIED (no source found, or sources conflict). Re-check
anything a step depends on before executing it (plan §4.0).

## 1. Concurrency with three jobs: VERIFIED

The default, `queue: single`, keeps one pending job and cancels it when another arrives. `queue: max`, shipped
2026-05-07, keeps up to 100 pending jobs, first in first out, and cannot be combined with `cancel-in-progress: true`.

- <https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/control-workflow-concurrency>
  - "`single` (default): At most one job or workflow run can be `pending` in the concurrency group. When a new job
    or workflow run is queued, any existing pending job or workflow run in the same group is canceled and replaced."
  - "`max`: Up to 100 jobs or workflow runs can be `pending`… When the queue is full, any additional jobs or
    workflow runs are canceled."
  - "The combination of `queue: max` and `cancel-in-progress: true` is not allowed and will result in a workflow
    validation error."
  - "…processed in first-in-first-out (FIFO) order according to the time each one started waiting… ordering is not
    guaranteed."
- <https://github.blog/changelog/2026-05-07-github-actions-concurrency-groups-now-allow-larger-queues/>
  - "Increased queuing can be enabled by adding `queue: max` to the concurrency block in YAML when
    cancel-in-progress is false or not set."

## 2. Group scope: VERIFIED

Group names are shared by every workflow in a repository, and are case-insensitive. Same page as 1:

- "If you have multiple workflows in the same repository, concurrency group names must be unique across workflows
  to avoid canceling in-progress jobs or runs from other workflows. Otherwise, any previously in-progress or
  pending job will be canceled, regardless of the workflow."
- "The concurrency group name is case insensitive."

## 3. actions/upload-artifact: VERIFIED (how GitHub serves an HTML file: UNVERIFIED)

- Latest major v7: v7.0.0 released 2026-02-26; the `v7` tag points at v7.0.1 (2026-04-10); node24.
- `archive: false` first appears in v7.0.0 (absent from the v4, v5 and v6 `action.yml`). One file only; the `name`
  input is ignored; downloading one with download-artifact needs v8.
  - `action.yml`: "When `archive` is `false`, only a single file can be uploaded. The name of the file will be used
    as the artifact name (ignoring the `name` parameter)."
- Clicking the link downloads the file unzipped, and the browser shows it when it renders the type natively:
  - <https://github.blog/changelog/2026-02-26-github-actions-now-supports-uploading-and-downloading-non-zipped-artifacts/>:
    "If your browser supports viewing the file type natively, you can view files directly in your browser. This
    is great for simple HTML files (without links to CSS or JS), images, or markdown."
  - The docs.github.com artifact pages checked do not mention it yet. No source was found on the content type, a
    content security policy or a sandbox.
- Outputs: `artifact-id` ("can be used as input to other APIs to download, delete or get more information about
  an artifact"), `artifact-url` ("only works for requests Authenticated with GitHub. Anonymous downloads will be
  prompted to first login"; the README's example is `https://github.com/example-org/example-repo/actions/runs/1/artifacts/1234`),
  and `artifact-digest`.
- `overwrite: true`: "If true, an artifact with a matching name will be deleted before a new one is uploaded. If
  false, the action will fail if an artifact for the given name already exists." The README adds: "this will give
  the Artifact a new ID, the previous one will no longer exist."
- Source: <https://github.com/actions/upload-artifact> (README and `action.yml` on `main`).

## 4. Re-runs and artifact names: UNVERIFIED (no official page; the evidence conflicts)

No docs.github.com page says what happens when a later attempt uploads an artifact under a name an earlier
attempt used.

- GitHub's v4 announcement: "there cannot be multiple v4 artifacts with the same name, in the same workflow run",
  and downloads reach "…the current workflow run and any previous run attempts."
  <https://github.blog/news-insights/product-news/get-started-with-v4-of-github-actions-artifacts/>
- GitHub's toolkit, `packages/artifact/src/internal/client.ts` in actions/toolkit: "It is possible to have
  multiple artifacts with the same name in the same workflow run by using … @actions/artifact < v2 or it is a
  rerun." Its `latest` list option exists for this ("In the case of reruns, this can be useful to avoid
  duplicates"), and download-artifact calls `listArtifacts({latest: true})`.
- A pre-v4 GitHub Support answer (2022), community discussion #17854: "Any time a user presses 're-run all jobs'
  the original artifacts will be removed and replaced…"; for partial re-runs, "They are simply moved into the new
  attempt of the run."
- Two third-party reports from September 2026 disagree: NSTA1/Orleans.Lattice#2924 shows the list API returning
  two same-name artifacts, one per attempt; BenSheridanEdwards/StyleProof#688 reports "an artifact with this name
  already exists".
- No re-run of `ci-summary-preview.yml` was found among lemonlion/Kronikol's last ~500 runs, so it could not be
  checked here.
- The `name` query parameter, VERIFIED wording: "The name field of an artifact. When specified, only artifacts with
  this name will be returned." Whether the match is exact is not stated. The endpoint also has a newer `direction`
  parameter (asc or desc, default desc). <https://docs.github.com/en/rest/actions/artifacts>

## 5. Latest major tags: VERIFIED (`git ls-remote`, tag dates, `runs.using` in each `action.yml`)

| Action | Latest major | Released | Runtime |
|---|---|---|---|
| actions/github-script | v9 (the tag exists) | v9.0.0 2026-04-09 | node24 (v8 was node24 too) |
| actions/checkout | v7 | v7.0.0 2026-06-17, v7.0.1 2026-07-17 | node24 |
| actions/setup-dotnet | v6 | v6.0.0 2026-07-15 | node24 |
| actions/upload-artifact | v7 | see 3 | node24 |
| actions/download-artifact | v8 | v8.0.0 2026-02-23, v8.0.1 2026-03-11 | node24 |

- github-script v9's breaking changes (release notes): `require('@actions/github')` "will fail at runtime", because
  the package is now ESM-only; `getOctokit` is now injected, so declaring it yourself is a SyntaxError. The PR's
  script does neither.
- checkout v7's breaking change (CHANGELOG): "Block checking out fork PR for pull_request_target and
  workflow_run"; opting back in needs `allow-unsafe-pr-checkout: true`. The PR's README uses `pull_request`.

## 6. The token's permissions: VERIFIED

- Creating and updating a pull request comment: `pull-requests: write` alone is enough. Both endpoint pages: 'The
  fine-grained token must have at least one of the following permission sets: "Issues" repository permissions
  (write) "Pull requests" repository permissions (write)'. The "Permissions required for GitHub Apps" page lists
  both endpoints under both, noting "Multiple permissions are required, or a different permission may be used."
  Listing comments needs read on either.
- Listing a run's artifacts: '"Actions" repository permissions (read)', declared explicitly, because "If you
  specify the access for any of these permissions, all of those that are not specified are set to none."
- Fork pull requests: read-only whatever the `permissions` block says, but for an admin setting: "if the workflow
  was triggered by a pull request event other than pull_request_target from a forked repository, and the Send
  write tokens to workflows from pull requests setting is not selected, the permissions are adjusted to change any
  write permissions to read only."
- Sources: <https://docs.github.com/en/rest/issues/comments>,
  <https://docs.github.com/en/rest/authentication/permissions-required-for-github-apps>,
  <https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax>

## 7. The comment's author: VERIFIED only by combining two statements

No page says in one sentence that a comment made with `GITHUB_TOKEN` has the login `github-actions[bot]`.

- "The GITHUB_TOKEN secret is a GitHub App installation access token."
  <https://docs.github.com/en/actions/concepts/security/github_token>
- "An installation token identifies the app as a GitHub App bot account, such as @jenkins[bot]."
  <https://docs.github.com/en/apps/oauth-apps/building-oauth-apps/differences-between-github-apps-and-oauth-apps>
- The actions/checkout README uses `github-actions[bot]` and `41898282+github-actions[bot]@users.noreply.github.com`,
  for commits rather than comments.

## 8. The Marketplace: VERIFIED

<https://docs.github.com/en/actions/how-tos/create-and-publish-actions/publish-in-github-marketplace>

- "Each repository must contain a single action metadata file (action.yml or action.yaml) at the root.
  Repositories may include other actions metadata files in sub-folders, but they will not be automatically listed
  in the marketplace."
- "The action must be in a public repository."
- "The name in the action's metadata file must be unique. The name cannot match an existing action name published
  on GitHub Marketplace. The name cannot match a user or organization on GitHub, unless the user or organization
  owner is publishing the action… cannot match an existing GitHub Marketplace category."
- `branding` is optional (the metadata syntax reference: "branding Optional").
- No current rule against workflow files in the repository: the page has none, and actions/checkout, which has
  `.github/workflows/*.yml`, is listed at v7.0.1. Third-party summaries still repeat the old rule.

## 9. An action in a subdirectory: VERIFIED

- Workflow syntax: "{owner}/{repo}/{path}@{ref} A subdirectory in a public GitHub repository at a specific branch,
  ref, or SHA", for example `actions/aws/ec2@main`; "Using the commit SHA of a released action version is the
  safest for stability and security."
- New, 2026-07-30: `uses: $/path/to/action` "resolves to your workflow's own repository at the exact commit that is
  running, with no checkout required". It takes no `@ref`, is not available on GitHub Enterprise Server, needs
  runner 2.336.0 or later, and works inside composite action steps.
  <https://github.blog/changelog/2026-07-30-reference-same-repository-actions-with-self-repository-syntax/>

## 10. nuget.org and relative links: VERIFIED (the docs for images, the source code for links)

- Images: "Images with relative local paths and images hosted from unsupported domains will not be rendered and
  will produce a warning on the readme file preview and package details page that is only visible to the package
  owners." <https://learn.microsoft.com/en-us/nuget/nuget-org/package-readme-on-nuget-org>
- Links: the docs say nothing; NuGetGallery's source (`main` at `e5977c4`, 2026-09-25) clears them.
  `src/NuGetGallery/Services/MarkdownService.cs`: "// Allow only http or https links in markdown." A link that is
  neither absolute http(s) nor a `#` fragment gets `linkInline.Url = string.Empty`, and
  `PackageHelper.TryPrepareUrlForRendering` requires `UriKind.Absolute`. So `[x](github-actions/foo)` renders with
  an empty address.

## 11. Alerts in comments: PARTLY VERIFIED

- The syntax docs define `> [!TIP]` without listing where it renders, and say "Alerts cannot be nested within
  other elements."
  <https://docs.github.com/en/get-started/writing-on-github/getting-started-with-writing-and-formatting-on-github/basic-writing-and-formatting-syntax>
- A maintainer's update on github/docs#27919 (closed): the syntax "is supported in Issues, Pull requests,
  Discussions, Gists, Releases, and Markdown files." No test comment was rendered.

## 12. Comment size: PARTLY VERIFIED

- The REST docs state no limit ("body string Required The contents of the comment.").
- A GitHub staff answer (2020), <https://github.com/orgs/community/discussions/27190>: "…a maximum value length of
  262,144. This equals a limit of 65,536 4-byte unicode characters."
- The error users saw in 2024: a 422, "Body is too long (maximum is 65536 characters)".

## 13. Notifications on edit: creating VERIFIED, editing UNVERIFIED

- The create endpoint says "This endpoint triggers notifications."; the update endpoint has no such sentence, and
  the notifications docs say nothing about edits.
- Only an unofficial 2014 report (isaacs/github#310) supports "edits don't notify": "when someone edits to add an
  @mention, and that person does not receive an email."

## 14. Dependabot pull requests: VERIFIED

The token starts read-only, and a `permissions` block raises it, so a job that comments with
`pull-requests: write` works.

- "…runs that are triggered by Dependabot from push, pull_request, pull_request_review, or
  pull_request_review_comment events are treated as if they were opened from a repository fork… they receive a
  read-only GITHUB_TOKEN and do not have access to any secrets…"
- "By default, GitHub Actions workflows triggered by Dependabot get a GITHUB_TOKEN with read-only permissions. You
  can use the permissions key in your workflow to increase the access for the token" (its example uses
  `pull-requests: write`).
- <https://docs.github.com/en/code-security/dependabot/troubleshooting-dependabot/troubleshooting-dependabot-on-github-actions>
- The 2021-10-06 changelog: Dependabot runs "respect the permissions specified in your workflows… default token
  permissions will remain read-only." The workflow syntax page still has an older-sounding line ("…therefore use a
  read-only GITHUB_TOKEN"); the Dependabot pages are the explicit ones. Only Dependabot secrets are available.

## 15. `always()` against `!cancelled()`: VERIFIED

<https://docs.github.com/en/actions/reference/workflows-and-actions/expressions>

- "always Causes the step to always execute, and returns true, even when canceled. The always expression is best
  used at the step level or on tasks that you expect to run even when a job is canceled."
- "Warning: Avoid using always for any task that could suffer from a critical failure, for example: getting
  sources, otherwise the workflow may hang until it times out. If you want to run a job or step regardless of its
  success or failure, use the recommended alternative: `if: ${{ !cancelled() }}`"
