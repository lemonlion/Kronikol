# Kronikol PR report link

A GitHub Actions composite action that keeps **one comment** on a pull request, linking the Kronikol report
artifact from each workflow run. CI rewrites the comment every time a linked workflow runs, so the newest
report is always one click from the PR. With two lanes, component tests and unit tests, in the repository
`octo/app`, the comment is:

```markdown
<!-- kronikol-report-link -->
## 📊 Kronikol test reports

- 🧪 **Component tests** — 📦 [component-test-reports](https://github.com/octo/app/actions/runs/101/artifacts/9001) 🕒 _(last updated 2026-09-15 13:58 UTC)_ ⏳ _(expires on 2026-09-16 13:58 UTC)_ <!-- kronikol-report-link:component-test-reports run:101 -->
- 🧪 **Unit tests** — 📦 [unit-test-reports](https://github.com/octo/app/actions/runs/102/artifacts/9002) 🕒 _(last updated 2026-09-15 14:05 UTC)_ ⏳ _(expires on 2026-09-16 14:05 UTC)_ <!-- kronikol-report-link:unit-test-reports run:102 -->

> [!TIP]
> 💡 Each link downloads a zip of the latest reports. Open `TestRunReport.html` inside it.

<sub>🤖 CI rewrites this comment every time a linked workflow runs.</sub>
<!-- kronikol-report-link:end -->
```

GitHub shows the heading, one line per lane with its tag hidden, a Tip box and a small footer.

Each artifact owns one line. Several workflows, or several jobs in one workflow, can therefore share the
comment without overwriting each other.

## Use it

The reports have to be uploaded first. Set `PublishCiArtifacts = true` on your `ReportConfigurationOptions`:
the test step then writes `reports-path` and `reports-retention-days` to its outputs for `upload-artifact`
([CI Artifact Upload](https://github.com/lemonlion/Kronikol/wiki/CI-Artifact-Upload)). An output holds one
value, the last one written, so a step that runs several Kronikol test projects uploads only the reports of the
one that finished last. Give each project its own step, upload and lane, or combine the runs with
`kronikol merge <inputs…> --publish-artifacts`, which writes the same two outputs.

Then link the upload from a separate job. Copy this folder to `.github/actions/kronikol-pr-report-link/` in
your repository:

```yaml
name: Tests

on:
  pull_request:

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7

      - uses: actions/setup-dotnet@v6
        with:
          dotnet-version: '10.0.x'

      - name: Test
        id: test
        run: dotnet test

      - name: Upload the Kronikol reports
        if: ${{ !cancelled() }}
        uses: actions/upload-artifact@v7
        with:
          name: component-test-reports
          path: ${{ steps.test.outputs.reports-path }}
          retention-days: ${{ steps.test.outputs.reports-retention-days }}
          if-no-files-found: ignore

  report-link:
    name: Link the Kronikol report on the PR
    needs: [test]
    if: ${{ !cancelled() && github.event_name == 'pull_request' && github.event.pull_request.head.repo.full_name == github.repository }}
    runs-on: ubuntu-latest
    permissions:
      contents: read
      actions: read
      pull-requests: write
    concurrency:
      group: kronikol-report-link-${{ github.event.pull_request.number }}
      queue: max
    steps:
      - uses: actions/checkout@v7
        with:
          sparse-checkout: .github/actions

      - uses: ./.github/actions/kronikol-pr-report-link
        with:
          artifact-name: component-test-reports
          label: Component tests
```

Instead of copying the folder, you can reference it from a Kronikol release tag that contains it, and drop the
checkout step: `uses: lemonlion/Kronikol/templates/github-actions/kronikol-pr-report-link@<tag>`. Pin a tag or
commit rather than `main`, because the action holds `pull-requests: write`. On github.com the copied folder also
runs without a checkout step, from the commit being tested: `uses: $/.github/actions/kronikol-pr-report-link`
(runner 2.336.0 or later, not on GitHub Enterprise Server). The example keeps the checkout, which works everywhere.

**Why each part of the job is there:**

- **A separate job.** The test job keeps a read-only token, and `needs` waits for the upload.
- **`!cancelled()` rather than `always()`.** A failed test run still uploads its report, and that is the report
  most worth linking. A cancelled run has nothing worth pointing at.
- **Only on pull requests from this repository.** A fork's token cannot write comments. Outside a pull request
  the action fails, so that a job which lost this condition doesn't pass while never writing the comment.
- **The concurrency group, with `queue: max`.** Every job that links into the same comment must use the same
  group, including jobs in other workflows, because concurrency groups are shared across a repository's
  workflows. Without it, two lanes finishing together can each read the comment before the other writes, and one
  line is lost. With the default queue a group holds one waiting job, and a third that arrives while one runs and
  one waits cancels the waiting one, whose line keeps its previous link. `queue: max` lets every job wait its
  turn, in an order that does not matter, because an older run never replaces a newer line. GitHub added it on
  2026-05-07, and refuses it beside `cancel-in-progress: true`. actionlint does not know the key yet (1.7.12
  reports "unexpected key "queue""): run it with `-ignore 'unexpected key "queue" for "concurrency" section'`, or
  put that pattern under `paths:` in `.github/actionlint.yaml`.

To add a second lane, give its upload a different artifact name and add the same job to that workflow, with its
own `artifact-name` and `label`.

## Inputs

| Input | Default | Description |
|---|---|---|
| `artifact-name` | *(required)* | The name the run uploaded the reports under. Each line of the comment belongs to one artifact name, so lanes that share a comment need different names. |
| `label` | the artifact name | The name the line is shown under. Lines are ordered by it. |
| `icon` | `🧪` | An emoji shown before the label. |
| `heading` | `📊 Kronikol test reports` | The comment's heading. It belongs to the comment, not to a line, so every lane that shares the comment needs the same one, or the comment shows whichever ran last. |
| `report-file` | `TestRunReport.html` | The file the comment tells a reader to open inside the zip. Like `heading`, one per comment. |
| `comment-key` | `kronikol-report-link` | Identifies the comment. A different key keeps a separate comment, for example one for nightly runs. Letters, digits, `.`, `_` and `-` only. |

## How it behaves

- **Links the newest upload.** It lists this run's artifacts with that name and takes the one uploaded last,
  because a re-run keeps its run id and uploads again. Re-running all jobs replaces the earlier attempt's artifact
  of that name; re-running failed jobs keeps both, and artifact ids do not follow upload order, so the upload time
  decides. Either way the line moves to the re-run's report, which expires a retention period after its own
  upload. The link is `…/actions/runs/<run>/artifacts/<id>`, the URL `actions/upload-artifact` reports.
- **Shows upload and expiry times.** "last updated" is the artifact's upload time and "expires on" its expiry,
  both in UTC. The API documents both as nullable, so a missing one is left out.
- **Never lets an older run replace a newer link.** Each line ends in a hidden
  `<!-- <comment-key>:<artifact> run:<id> -->` tag. Run ids only grow, so a line already written by a newer run
  is left alone, even when an older run finishes after it. A later version adds fields to the tag only after the
  run id, so a lane still on this version goes on reading it.
- **Keeps each line one line.** A `label`, `icon`, `heading` or `report-file` given over several lines is written
  on one, each run of whitespace as one space.
- **Edits only its own comment:** one written by `github-actions[bot]` whose body starts with
  `<!-- <comment-key> -->`. Another bot quoting the marker, or a person's pasted copy of the comment, is never
  edited.
- **Tolerates hand edits.** A comment saved in GitHub's web editor (CRLF line endings), or left with trailing
  whitespace, still parses. The next rewrite puts it back.
- **Leaves the comment alone when there is no artifact.** If the run uploaded no artifact of that name, the
  action warns and changes nothing.

## Limits

- **`GITHUB_TOKEN` only.** The action writes with the workflow token and recognises its comment by the
  `github-actions[bot]` author, so it has no token input.
- **The comment is the action's, down to its end marker.** The next run of any lane rewrites everything above
  `<!-- <comment-key>:end -->` and keeps everything after it as it is. Other content goes after the marker, or in
  a comment of its own with its own `comment-key`.
- **Links download a zip, for a signed-in reader.** The reports directory is uploaded as one zip, which the reader
  downloads to open the report. GitHub serves an artifact only to a signed-in user who can read the repository.
  Viewing reports without a download means publishing them somewhere, for example GitHub Pages.
- **Inputs are written as Markdown.** `label`, `icon` and `heading` go into the comment as they are, so pass fixed
  text, never something a pull request controls, such as its branch name or title. No code can be injected: the
  inputs reach the script as environment variables.
- **Links expire with the artifact.** `CiArtifactRetentionDays` defaults to 1, so a link stops working a day
  after its run unless the workflow runs again. The "expires on" time shows when.
