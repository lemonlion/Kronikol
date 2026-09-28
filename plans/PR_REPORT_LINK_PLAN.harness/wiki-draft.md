# Draft for S6: the wiki's CI-Artifact-Upload page (plan §4.6 step 1)

Drafted 2026-09-27 against the wiki at `86a77c1`, so the owner can read the words before the plan is green-lit.
S6 writes them after the merge, when the action is on `main`; the sentence marked **(S3)** waits for S3's
check 5, and the link and the tag wait for what S6 records. The page's own style (its "Note:" blocks, its dashes)
is left as it is; the new text follows this repository's plain style.

## 1. The page's GitHub example (under "Example Workflow")

- `actions/checkout@v5` → `@v7`, `actions/setup-dotnet@v5` → `@v6`, `actions/upload-artifact@v5` → `@v7`, as the
  action's README after S2 (F7).
- The upload step's `if: always()` → `if: ${{ !cancelled() }}`, and the note below the example becomes:

> **Note:** The `id: test` is what lets the upload step read the outputs the library writes. `if: ${{ !cancelled() }}`
> uploads the reports when tests fail, which is when they matter most, and skips the upload when the run was
> cancelled. GitHub advises it over `always()`, which runs on a cancelled run too and can hold a workflow until it
> times out.

- A paragraph after the note (F12):

> **One reports directory per step.** The step's `reports-path` output holds one directory. A `dotnet test` over a
> solution runs each Kronikol test project in its own process, each writes its own `reports-path` line, and the
> output keeps the last one written, so the upload carries only the project that finished last. Run each project
> in a step of its own, each with its own upload and artifact name, or combine the runs with
> `kronikol merge <inputs…> --publish-artifacts`, which writes the same two outputs for the merged report.

## 2. A new section after "Combining with CI Summary"

> ### Link the report on the pull request
>
> An artifact is found from the workflow run's page, a few clicks away from the pull request that caused it. The
> [`kronikol-pr-report-link`](https://github.com/lemonlion/Kronikol/tree/main/templates/github-actions/kronikol-pr-report-link)
> action keeps one comment on the pull request instead: a line for each lane, linking its newest report, with
> when it was uploaded and when it expires. Every run rewrites the comment, so the newest report is always one
> click from the PR.
>
> It runs in a job of its own after the job that uploads. The action's README has the job to copy and says why
> each part is there:
>
> - **its own job**, so the test job keeps a read-only token, and `needs` waits for the upload;
> - **`actions: read` and `pull-requests: write`**, to list the run's artifacts and to write the comment;
> - **pull requests from the repository only**, because a fork's token cannot write comments;
> - **one concurrency group, with `queue: max`, for every job that links into the comment, in every workflow**, so
>   the jobs take turns and none is cancelled while it waits.
>
> Copy the folder into `.github/actions/` or reference it from a Kronikol release tag (the first is
> `v<first tag>`). A lane that is re-run links its newest upload **(S3: what a re-run's upload does, from check 5)**.
> The links download a zip, need a GitHub sign-in, and expire with the artifact: `CiArtifactRetentionDays`
> defaults to one day.

## 3. The page's Limitations list gains nothing

The action's limits stay in its README, which the section links.
