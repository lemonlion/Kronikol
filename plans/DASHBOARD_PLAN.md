# DASHBOARD_PLAN.md — a static dashboard over the history ledgers

**Status:** written 2026-09-14; **NOT green-lit**, nothing implemented. Grew out of one question: could a
static page, published to GitHub Pages by a workflow and fed from the history branch of one or more
repositories, compete with the hosted test dashboards? The answer below is yes on one axis and no on
another, and the plan says which is which before it says how.

Interacts with `CROSS_RUN_HISTORY_PLAN.md` (§0 sets the premise this plan continues; §14 is the contract
this plan stays inside and proposes one sentence for, §8 here), `MCP_PLAN.md` (§10 there: a hosted
Kronikol service is by construction a credential sink; the same argument decides where the token lives
here), `PLATFORM_FOUNDATIONS_PLAN.md` (F3, raw facts in and interpretation inside: the view this plan adds
is interpretation and is computed on the .NET side only), `TOOLBAR_REDESIGN_PLAN.md` (the `--kron-*` token
names; a new page adopts them from its first byte) and `V4_PLAN.md` (nothing here waits for v4, nothing
here is breaking).

---

## 1. The answer in one page

The data is already there. Every repository that runs the cross-run history recipe has an orphan
`kronikol-history` branch holding one append-only JSONL ledger, and each run line carries, per scenario,
the result, the attempt, the duration, the dependency call count, the interaction-shape fingerprint and
the error cluster key, plus the run's branch, commit, provider, CI run URL and timestamp. A dashboard is
a client-side function of files that are being written today. No capture work and no schema work on the
capture side.

Two things about the question's framing change what gets built:

1. **"Live" means the page reads the branch, not that a workflow redeploys on every run.**
   `raw.githubusercontent.com` serves a public branch with `Access-Control-Allow-Origin: *`, a five-minute
   cache and a `304` on `If-None-Match` (§2, measured). A page on Pages can fetch any number of public
   ledgers on load and poll every minute at the cost of a `304` each. No token, no redeploy, no API.
2. **Compete on what the ledger holds, not on the charts.** Pass rate, flakiness and duration are table
   stakes: Allure ships them as static HTML with history on a `gh-pages` branch, the CTRF reporter reads
   them from artifacts, and Codecov, Trunk, Buildkite and Datadog all sell them (§3). A me-too page loses.
   The ledger has two things none of them have a concept for, per-scenario dependency call counts and
   interaction-shape fingerprints, and the page leads with those: behaviour drift across commits, call
   volume per scenario, failure clusters and their age, across every repository at once.

Two constraints the plan states up front rather than discovers:

- **A Pages site for a private repository is public unless the organisation is on GitHub Enterprise
  Cloud** (§2). The output is therefore one self-contained HTML file that deploys to Pages by default and
  to any static host without change. For private sources the page renders a snapshot the workflow baked
  in; it never carries a token.
- **The page renders, it never judges.** Every verdict on it is computed by `HistoryAnalyzer` on the .NET
  side when the view is written. The rules moved four times on 2026-09-14 alone (3.12 to 3.15); a second
  implementation in JavaScript would be wrong within a day. Consequently the page does not read the
  ledger. It reads a derived **view** file the tool writes beside it (§4.2).

Where this cannot compete, and says so: assignment, role-based access, ticketing, alerting on first
failure, retention beyond the window. Those need accounts and a server. `CROSS_RUN_HISTORY_PLAN.md` §0
drew that line and this plan does not move it.

---

## 2. Checked, not assumed (2026-09-14)

Every platform fact this plan rests on was measured or read this day. Where a fact could not be found
in the source consulted, the row says so.

| Claim | What was found | How |
|---|---|---|
| A public history branch is fetchable from a browser on another origin | `Access-Control-Allow-Origin: *`, `Cross-Origin-Resource-Policy: cross-origin`, `Cache-Control: max-age=300`, `Content-Encoding: gzip`, weak `ETag`, `Vary: Authorization,Accept-Encoding`, served by Fastly (`X-Cache: HIT`) | `curl -D -` with an `Origin` header against `raw.githubusercontent.com/lemonlion/Kronikol/kronikol-history/history.jsonl` |
| A poll that finds nothing new costs nothing | `If-None-Match` with the served ETag returned `304`, 0 bytes | `curl -w "%{http_code} %{size_download}"` |
| The REST API is not a polling path for a page | unauthenticated `X-RateLimit-Limit: 60` per hour | `curl -D - https://api.github.com/rate_limit` |
| GitHub Pages caching and compression | `Cache-Control: max-age=600`, `Content-Encoding: gzip`, `Access-Control-Allow-Origin: *`, `Server: GitHub.com` | `curl -D - https://pages.github.com/` |
| Pages limits | "Published GitHub Pages sites may be no larger than 1 GB." "GitHub Pages sites have a *soft* bandwidth limit of 100 GB per month." "GitHub Pages sites have a *soft* limit of 10 builds per hour. This limit does not apply if you build and publish your site with a custom GitHub Actions workflow." | docs.github.com, GitHub Pages limits |
| Private Pages | "To publish a GitHub Pages site privately, your organization must use GitHub Enterprise Cloud." Private or internal repositories only; not organisation sites. | docs.github.com, enterprise-cloud, changing the visibility of your GitHub Pages site |
| Deploying from a workflow | `permissions: pages: write, id-token: write`; the artifact comes from `actions/upload-pages-artifact`; a dedicated job with the `github-pages` environment | `actions/deploy-pages` README |
| Telling another repository's workflow to run | `repository_dispatch` runs "the workflow file [that] exists on the default branch"; "OAuth app tokens and personal access tokens (classic) need the repo scope to use this endpoint"; the `GITHUB_TOKEN`'s "permissions are limited to the repository that contains your workflow", and "`workflow_dispatch` and `repository_dispatch` events always create workflow runs" even when the `GITHUB_TOKEN` fires them | docs.github.com, events that trigger workflows; REST repos; GITHUB_TOKEN concept page |
| A schedule as the fallback trigger | "The shortest interval you can run scheduled workflows is once every 5 minutes." "The `schedule` event can be delayed during periods of high loads … some queued jobs may be dropped." | docs.github.com, events that trigger workflows |
| GitHub has no native test dashboard | community discussion #163123, "[Feature Request] Native Test Results Dashboard for GitHub Actions (like Azure DevOps)", opened 2025-06-17, still open, 17 upvotes, 13+ comments, an automated acknowledgement and no staff reply | github.com/orgs/community/discussions/163123 |
| This repository's ledger | 41 lines: 1 header, 4 rosters, 36 runs over 4 suites; 71,724 bytes raw, 4,520 gzipped (4,596 on the wire) | `git show origin/kronikol-history:history.jsonl`, `gzip -9`, `curl -I` |
| BreakfastProvider's ledger, 18 lanes | 127 lines: 1 header, 18 rosters, 108 runs; 1,419,891 bytes raw, 226,011 gzipped (225,670 on the wire); 171 to 203 positions per roster; every run green so far | same, against `lemonlion/BreakfastProvider` |
| What a run line is made of (BreakfastProvider, 7,436 bytes average) | `shapeSet` 29.2%, `shapeOrdered` 29.2%, `errors` 13.3% (an all-`null` array, one `null` per position, on every green run), `durations` 7.7%, `deps` 6.5%, `calls` 5.7%, `results` 2.7%, `attempts` 2.7%, `url` 1.0%. The 18 roster lines are 561,940 bytes, 40% of the file, 79,817 gzipped | python over the ledger, `json.dumps` per field |
| What a view weighs (§4.2: no fingerprints, sparse errors, a cluster table, a `shapeChanged` bit string) | BreakfastProvider: 765,047 bytes raw, 116,228 gzipped; the run part 1,879 bytes raw and 337 gzipped per run; rosters 79,817 gzipped. Kronikol: about 22 KB raw, 3.1 KB gzipped. Projection at 1,000 runs with 18 rosters: about 2.4 MB raw, about 420 KB gzipped | python, the view built from the real ledgers |
| `deps` is per run, not per scenario | one distinct list per run line (`["Caller>Breakfast Provider"]`), 171 positions | the run line |
| The E2E suite opens pages from disk and has no local server | `Page.GotoAsync(GenerateReport("AdvSearchAnd.html"))`; the project references Playwright 1.59.0 and xunit.v3 3.2.2 only. Chromium refuses `fetch()` of a relative URL from a `file://` page, so a page that must work from disk cannot fetch its data: it inlines it (§4.3) | `tests/Kronikol.Tests.EndToEnd`, browser behaviour pinned by an M1 test |
| The report already inlines compressed payloads | `decompressGzipBase64` in `src/Kronikol/Reports/report-decompress-helper.js`, gzip + base64 through `DecompressionStream`; "the ONE definition … in a report" | source |
| The report already has the history palette | `HistoryHtml.Colour`: passed `#228b22`, failed `#bf0000`, skipped `#949494`, absent `#e6e6e6`, unknown `#c8c8c8`, other `#b8a000`; classes `history-sparkline`, `history-bar-pass`, `history-bar-fail`, `history-bar-duration`, `history-bar-partial`, `history-verdict-*` | `src/Kronikol/History/HistoryHtml.cs` |
| Analysis defaults the view must carry | `Window` 50, `MinRuns` 5, `FlakyRate` 0.1, `SlowerBy` 1.5 (`HistoryAnalysisOptions`); the tool's `DefaultWindow` 50 | `HistoryVerdicts.cs`, `HistoryCommand.cs` |

Competitors, read the same day (§3 draws on these rows):

| Product | History store | Page | What was read |
|---|---|---|---|
| Allure Report 3 | one `history.jsonl` (`historyPath`), a line appended per generation; trend charts, a History tab per test | static HTML, published to Pages by community actions | allurereport.org, history and retries |
| html-reporter-github-pages (Marketplace) | keeps N whole reports on `gh-pages`, `keep_reports` default 20, "There is a 5GB limit on Git Repo"; a folder per run `<subfolder>/<tool>/<workflow>/<env>/<run_number>/index.html`; a PAT with `repo` for another repository | the reports themselves; no cross-run page | its Marketplace listing |
| ctrf-io/github-test-reporter | workflow artifacts read back through the API: `max-workflow-runs-to-check` default 400, `max-previous-runs-to-fetch` default 100, `previous-results-max` 10; same branch for push events, same pull request otherwise | job summary and PR comment only; "does not mention any published dashboard" | its README |
| Codecov Test Analytics | hosted; "the only test result file format we support is JUnit XML at the moment"; 60-day retention; an upload token | hosted Tests tab | docs.codecov.com |
| Trunk Flaky Tests | hosted; detection, quarantine, PR comments, Jira and Linear tickets | hosted | docs.trunk.io |
| Datadog Test Optimization | hosted; instrumentation libraries (.NET among them) or JUnit XML upload; collects "test names and durations", environment variables, git history and CODEOWNERS | hosted | docs.datadoghq.com/tests |
| benchmark-action/github-action-benchmark | append-only JSON on `gh-pages` (`dev/bench`), a chart page rendered from it, about 2,700 public repositories | static page from a data branch: the exact shape of this plan, for performance numbers | `CROSS_RUN_HISTORY_PLAN.md` §0.1 (checked 2026-09-12) |

---

## 3. Competitive position

**What every product on the table has:** per-test pass and fail over time, flakiness, duration, a
"new versus persistent" split, some notion of quarantine. Kronikol has all of those in the ledger and on
the report since 3.9.0 to 3.11.0. A dashboard that shows them is necessary and not sufficient.

**What only Kronikol has, because only Kronikol captures it:**

- **Per-scenario dependency call counts and the set of distinct calls**, fingerprinted per run
  (`InteractionShape`, rule 3). The `behaviour-changed` verdict, earned once a scenario's count has been
  constant over `MinRuns` runs, exists nowhere else. On a dashboard it becomes a drift timeline over
  commits: which scenarios started talking to something new, stopped reading a cache, doubled their
  writes, and in which commit, across every repository.
- **Error clusters as keys**, so a cluster's first-seen and last-seen run, its scenario count and,
  across repositories, the same dependency failing in three services at once, are text equality over the
  ledgers, not a product feature.
- **The interpretation is committed.** A pull-request checkout already carries `main`'s history, and the
  dashboard is one more reader of the same file. Nothing has to be configured on a vendor's side to make
  the numbers agree with the report.
- **Zero infrastructure and zero credentials in the page.** The competition's moat is the org-wide private
  dashboard behind accounts. This plan's version of org-wide is one workflow reading N branches.

**What other frameworks' results can do here:** `kronikol history import --from-ctrf` and `--from-allure`
(3.10.0) put JUnit-shaped results into a ledger, so a repository with no Kronikol capture can still be a
source of pass, fail, duration and flakiness on the same dashboard. It gets no drift and no calls; the
panels say "not captured" rather than showing zero.

**Where this loses and should not try:** identity and workflow (assignment, comments, approval to mute,
audit), alerting (Slack on first failure, "tell me when this unmutes"), retention beyond the window, and
private hosting on the free GitHub tiers. The honest positioning is interaction-level test history with
no infrastructure: free on Pages for public repositories, and on whatever static host a team already
has for private ones.

---

## 4. Design

### 4.1 Principles

1. **The ledger is the only truth.** The view (§4.2) is derived from it and can be regenerated from it at
   any time, so rewriting the view is safe in a way rewriting the ledger never is (the `SIGKILL` argument
   in `HistoryFormat`'s remarks applies to the ledger alone).
2. **The page renders and never judges.** Verdicts, evidence strings, cluster membership and drift events
   are written into the view by `HistoryAnalyzer`. The JavaScript has no rule in it that could disagree
   with `Failures.md`.
3. **No token in the page, ever.** A public source is fetched by the browser because it is public. A
   private source is baked in by the workflow that holds the token. The page never calls
   `api.github.com`.
4. **One self-contained file**, as the report is: no CDN, no web font, no external script or stylesheet,
   no third-party library unless vendored as an embedded resource. It opens from disk, from Pages, from
   any host.
5. **Ledger content is test data, not instructions.** Scenario names, feature names, cluster text,
   evidence and dependency names become text nodes only; URLs are validated before they become links;
   the page ships a hash-based Content Security Policy it computes at build time.

### 4.2 The view: `history.view.json`

A pure function of one ledger and one `HistoryAnalysisOptions`, written by the tool and read by the page.

```jsonc
{
  "viewVersion": 1,
  "generator": "3.x.y",
  "generatedAt": "2026-09-14T22:40:00Z",
  "repository": { "name": "lemonlion/BreakfastProvider", "provider": "GitHubActions",
                  "url": "https://github.com/lemonlion/BreakfastProvider" },
  "window": 200,
  "analysis": { "minRuns": 5, "flakyRate": 0.1, "slowerBy": 1.5, "slowerMinMs": 0, "shapeVersion": 3 },
  "streams": ["main"],
  "suites": [
    {
      "name": "xunit-in-docker",
      "rosters": { "f96d6540f70b11b8": { "ids": [], "names": [], "features": [], "sources": [] } },
      "runs": [
        { "id": "gh:34881911876:1", "at": "2026-09-14T18:40:58Z", "stream": "main",
          "commit": "3787de7c…", "url": "https://github.com/lemonlion/BreakfastProvider/actions/runs/34881911876",
          "roster": "f96d6540f70b11b8", "partial": false, "shards": 1,
          "results": "PPPF…", "attempts": "----…",
          "durations": [43, 2, 1], "calls": [3, 3, 3],
          "shapeChanged": "0010…",
          "errors": { "3": 0 },
          "deps": ["Caller>Breakfast Provider"] }
      ],
      "scenarios": [
        { "id": "97dd85d9115293c4", "verdict": "flaky", "since": "gh:…", "evidence": "…", "p95Ms": 123, "last": "P" }
      ],
      "clusters": [
        { "key": "…", "text": "first 300 characters of the message", "firstSeen": "gh:…", "lastSeen": "gh:…", "scenarios": 3 }
      ],
      "events": [
        { "run": "gh:…", "kind": "behaviour-changed", "scenario": "97dd85d9115293c4", "evidence": "calls 3 in gh:… to 14 now, constant over the last 7 runs" }
      ]
    }
  ]
}
```

Decisions inside the shape, each with the measurement that made it:

- **No fingerprints.** `shapeSet` and `shapeOrdered` are 58% of a BreakfastProvider run line and the page
  never compares them. `shapeChanged` is one character per position: `1` where this run's set
  fingerprint differs from the previous run on the same stream under the same shape rule, `0` where it is
  the same, and the key is absent where there is no comparable previous run (first run of a stream, or a
  `shapeVersion` change, exactly as `HistoryAnalyzer` refuses to compare across rules). It is texture for
  the strips; the verdicts in `events` are the truth, and the schema description says so.
- **Sparse errors.** `errors` in the ledger is one `null` per position on every green run, 13.3% of the
  file. The view writes a position-to-cluster-index map and omits the key when the run had no failures.
- **A cluster table.** The ledger repeats a cluster's key (up to 200 characters) on every run that hits
  it and its text in `errorText`; the view holds each once with the head of its text.
- **Rosters once per hash**, only those an included run references. On a small ledger the rosters
  dominate (BreakfastProvider: 40% raw, 80 KB gzipped for 18 lanes); §7 has the dedupe to apply if M0's
  measurement keeps them above 30%.
- **A window of its own.** The gate's window is 50 because a verdict needs recent evidence; a chart wants
  more. `window` is runs per stream per suite, default 200, and the analysis inside still uses
  `HistoryWindow` for the verdicts, so the pills agree with the report.
- **Verdicts as the analyzer emits them.** `scenarios[].verdict` and `events[]` come from
  `HistoryAnalyzer` with the same options the report uses. One test in M0 runs the analyzer and the view
  builder on the same ledger and asserts equality. There is no second source.
- **`repository`** is derived from the run URL when the provider is GitHub Actions
  (`https://github.com/<owner>/<repo>/actions/runs/<id>`), otherwise null unless `--repository` names it.
- **Deterministic.** Same ledger, same options, same generator: identical bytes except `generatedAt`. The
  M0 tests pin it, and the dashboard build is deterministic on top of it.

Measured weight (§2): 116 KB gzipped for BreakfastProvider's 108 runs against 226 KB for the ledger; 337
bytes gzipped per additional run; about 420 KB gzipped at 1,000 runs. Nothing on that curve troubles a
page load, a Pages limit or a browser.

Where it lives: `src/Kronikol/History/HistoryView.cs` (the model and `Build`), `HistoryViewJson.cs`
(writer and reader, ASCII-escaped like the ledger) and `history.view.schema.json` as an embedded
resource, with a closed-contract test in the shape of `SchemaClosedContractTests`. The library side, not
the tool, so a report could embed it one day and Kronikol4J can port it.

### 4.3 The page

One `index.html` emitted by `kronikol dashboard build`. The data is inlined, gzip + base64, decoded with
the report's `decompressGzipBase64` (one definition, reused, not copied). One inline `<script>`, one
inline `<style>`, both hashed into the CSP (§4.7). It renders from the inlined snapshot immediately and
then, for sources marked live, refreshes in place (§4.4).

Panels, in the order that puts the differentiators first:

1. **Behaviour drift.** A commit axis per repository and stream; every `behaviour-changed` event as a
   marker; a density strip per run (how many positions had `shapeChanged`). Click a marker: the
   scenario, the evidence string, the run link, the commit link.
2. **Calls per scenario.** Sortable by most calls, biggest change, most changes; each row a small series
   with the changed runs marked. The scenario that went from three calls to fourteen is the first row.
3. **Failure clusters.** Each cluster as a bar from first seen to last seen, the scenarios it touched and,
   with more than one source, the repositories it appears in (key equality). Ordered by age.
4. **Runs.** Per suite, a strip of runs: stacked pass, fail, skip; partial runs hatched; total duration
   as a line; click a run to open its CI page.
5. **Scenarios.** The roster with the report's sparkline and colours (`HistoryHtml.Colour` becomes a
   shared palette the page and the report both read), the verdict pill, p95 and last result; filters by
   verdict, suite, stream and text; sort by any column.
6. **Durations.** The `slower` verdicts and the p95 trend of the slowest scenarios.
7. **Dependencies.** Per suite, which dependencies each run talked to and the run in which one appeared
   or disappeared. Run-level only: the ledger records `deps` per run, not per scenario (§2), so the
   scenario-by-dependency heat map waits for a ledger change (§5, M3, and §9 question 6).

Header: source, suite, stream and window selectors; for every source, "snapshot from <time>" and, when
live, "refreshed <time>" or "source unreachable, showing the snapshot".

**Rendering is hand-rolled SVG and CSS.** The panels are bars, strips, polylines, markers and a table;
none needs axis or zoom machinery; the report already draws its sparkline and bars this way. Every byte
of script is Kronikol's, there is no licence text in the file and nothing to pin or audit, and the
Java port can follow. Fallback, decided at M2 and not before: if brushing or a crosshair prove
necessary, vendor uPlot (about 45 KB minified, MIT) as an embedded resource. Never a CDN.

**Theme and tokens.** Light and dark by `prefers-color-scheme`, overridable; the custom property names
come from `TOOLBAR_REDESIGN_PLAN.md` (`--kron-*`: accent, accent-soft, surface and the rest) so the day
v4 lands on the report the two share one vocabulary. Relative units, no horizontal page scroll; the
table scrolls inside itself.

**Budget.** A 1,000-run, 200-scenario view renders its first panel in under 300 ms on a CI runner,
measured under contention the way the render-perf budgets are (`ContentionScale`).

### 4.4 Two delivery modes, one page

- **Snapshot.** What is inlined at build time. Works from `file://`, from Pages, from any host, offline.
  Freshness is the last build plus up to ten minutes of Pages cache on `index.html` itself.
- **Live refresh.** For each source with a `view` URL, the page fetches it on load and every 60 seconds
  with `cache: "no-cache"`, which lets the browser revalidate with `If-None-Match`; raw answers `304`
  (§2) and the DOM is left alone. A `200` replaces that source's data in place, no reload, no flicker. A
  failed fetch (CORS on a private branch, `404`, offline) keeps the snapshot and marks the source.
  Freshness is at most the five-minute raw cache.
- **Never `api.github.com`.** Sixty unauthenticated calls an hour is not a polling budget, and an
  authenticated call would put a token in the page.

Private repositories are snapshot-only by construction: their branch is not fetchable without a token,
and the token stays in the workflow.

### 4.5 CLI

```
kronikol history view   [<fragments-or-ledger>] --history FILE --out FILE [--window N] [--repository owner/name]
                        [--min-runs N] [--flaky-threshold R] [--slower-by R] [--slower-min-ms N]
kronikol history record <fragments> --history FILE [--view FILE]      # writes the view in the same invocation
kronikol dashboard build --source NAME=PATH-or-URL ... --out DIR|FILE [--title TEXT] [--live]
kronikol dashboard build --sources sources.json --out DIR|FILE
kronikol dashboard build --history FILE --out DIR|FILE                 # single repository: view, then page
```

`history view` and `record --view` sit in `HistoryCommand`'s switch beside `show` and `record`; the
analysis flags are the ones `gate` already parses. `dashboard` is a new entry in `Commands.Table`, which
`CommandTableTests` covers without a change (every command answers its own help, the table matches the
help). Exit codes as everywhere: 0, 2 for usage. `--live` writes each source's URL into the page's
manifest; a source given as a path is snapshot-only unless `--source NAME=PATH@URL` names both.

`sources.json`:

```json
[
  { "name": "lemonlion/Kronikol",          "view": "https://raw.githubusercontent.com/lemonlion/Kronikol/kronikol-history/history.view.json" },
  { "name": "lemonlion/BreakfastProvider", "view": "https://raw.githubusercontent.com/lemonlion/BreakfastProvider/kronikol-history/history.view.json" },
  { "name": "acme/private-service",        "file": "./sources/private-service/history.view.json" }
]
```

Drift guards: `SkillDriftTests` reads every `--flag` inside a code span of `commands.md` as a query flag,
so the dashboard and view flags are documented on the wiki and in `history --help`, and in `commands.md`
only in prose, exactly as the `history` flags are today.

### 4.6 Workflow recipes

Written to be executed at M2 and recorded in §10, as the history recipe was.

**(a) A source repository: one flag.** The history job that folds fragments into the branch adds
`--view "$WT/history.view.json"` to `kronikol history record` and `history.view.json` to `git add`.
The push loop already rebases when two lanes record at once, and the ledger union-merges; a rewritten
view conflicts. **The rule: a view is regenerated from the ledger it sits beside, never merged.** On a
conflict the loop runs `kronikol history view --history history.jsonl --out history.view.json`,
`git add history.view.json`, `GIT_EDITOR=true git rebase --continue`, and pushes again. BreakfastProvider's
eighteen lanes make this path run, not merely exist.

**(b) A dashboard repository for N sources.**

```yaml
on:
  repository_dispatch: { types: [kronikol-history-updated] }
  schedule: [ { cron: "17 * * * *" } ]     # the fallback; may be delayed under load
  workflow_dispatch:
permissions: { contents: read, pages: write, id-token: write }
concurrency: { group: pages, cancel-in-progress: true }
jobs:
  build:
    steps:
      - uses: actions/checkout@v5
      - uses: actions/checkout@v5            # one per PRIVATE source; public sources are fetched by the page
        with: { repository: acme/private-service, ref: kronikol-history, sparse-checkout: history.view.json,
                path: sources/private-service, token: ${{ secrets.SOURCES_READ_TOKEN }} }
      - uses: actions/setup-dotnet@v5
        with: { dotnet-version: 10.0.x }
      - run: dotnet tool install -g Kronikol.Tool --version <pinned>
      - run: kronikol dashboard build --sources sources.json --live --out site
      - uses: actions/upload-pages-artifact@v4
        with: { path: site }
  deploy:
    needs: build
    environment: { name: github-pages, url: ${{ steps.deployment.outputs.page_url }} }
    steps:
      - id: deployment
        uses: actions/deploy-pages@v4
```

A source repository tells the dashboard to rebuild with
`gh api repos/<org>/<dashboard>/dispatches -f event_type=kronikol-history-updated`, using a token that can
write to the dashboard repository: the source's own `GITHUB_TOKEN` is limited to the source repository
(§2), so this is a fine-grained PAT or a GitHub App token held as a secret in the source. A dashboard
whose sources are all public needs no dispatch at all: the page refreshes itself.

**(c) Kronikol's own dogfood.** A `dashboard.yml` in this repository publishing
`https://lemonlion.github.io/Kronikol/` from its own view with BreakfastProvider as the second, live,
public source. Requires Pages to be enabled on the repository with the source set to GitHub Actions,
which is a settings change only the owner can make (§9, question 1).

**(d) Private sources.** Snapshot mode; the page says "snapshot from <time>" for them. Hosting the file
privately is the team's choice of static host: Pages on Enterprise Cloud, Azure Static Web Apps with
its built-in authentication, Cloudflare Pages behind Access, S3 and CloudFront behind SSO, or an
internal web server. The output does not change.

### 4.7 Security

- **Untrusted text.** Everything from a ledger is written with `textContent`. A URL becomes an `href`
  only if it parses as `https:` and its host is the provider's (`github.com` for GitHub Actions) or the
  source's declared repository host; otherwise it is shown as text. Links carry `rel="noopener"`.
- **Content Security Policy** as a `<meta http-equiv>` the build computes:
  `default-src 'none'; script-src 'sha256-<inline script>'; style-src 'sha256-<inline style>';
  img-src data:; connect-src <origins of the live sources>; base-uri 'none'; form-action 'none'`.
  A unit test recomputes both hashes from the emitted bytes; an E2E test names a scenario with a script
  injection payload and asserts nothing ran and the console recorded no CSP violation.
- **No secret in the artifact.** The build fails if the site contains a string shaped like a GitHub token
  (`ghp_`, `github_pat_`, `ghs_`); the workflow's token is read-only on sources and never reaches the
  page. A unit test feeds a view containing such a string in a scenario name and asserts the build
  refuses.
- **The page makes no request in snapshot mode.** Pinned by an E2E test counting requests beyond the
  document itself.

### 4.8 Across repositories

No verdict arithmetic crosses a repository. Grouping across sources is text equality only: cluster keys,
dependency names, stream names. Each source carries its own `analysis` block and the header shows it, so
"flaky" on one source and "flaky" on another can mean different thresholds and the page says so rather
than pretending to a single rule.

---

## 5. Milestones

One minor release per milestone, numbers assigned at release time, every package on the same version,
the plugin manifests bumped with `Directory.Build.props`, the twelve template pins moved to the previous
published release, a CHANGELOG entry that says which part of the version moved and why, and the wiki
updated in the same session. Every slice is test-first.

### M0 — the view

Library: `HistoryView`, `HistoryViewJson`, `history.view.schema.json`. Tool: `history view`, `history
record --view`. Dogfood: `ci-summary-preview.yml`'s history job writes and commits the view with the
rebase-regenerate rule. Wiki: a "The view" section on `Cross-Run-History.md`.

Tests written first:

- `HistoryViewTests`: identical bytes for the same ledger and options (except `generatedAt`); the window
  keeps the last N runs per stream per suite and drops rosters nothing references; an all-`null` errors
  array yields no `errors` key and a run with two failures maps both positions to cluster indices; a
  cluster's text head appears once at 300 characters; `shapeChanged` is present only between consecutive
  same-stream runs under the same `shapeVersion`, absent for a stream's first run and across a rule
  change; `scenarios[].verdict`, `since`, `evidence` and `events[]` equal `HistoryAnalyzer`'s output for
  the same options on the same ledger; a partial run is marked; `repository` is derived from a GitHub
  Actions run URL and null for an unknown provider without `--repository`; the size guard: a synthetic
  18-suite, 171-position, 108-run ledger yields a view no larger than 130 KB gzipped and no more than
  400 bytes gzipped per additional run (the BreakfastProvider measurement plus ten percent).
- `HistoryViewJsonTests`: round-trip; ASCII-escaped; `viewVersion` is the first key; the schema is
  closed and an undeclared key at any level is a violation; a view of a higher `viewVersion` is refused
  by the reader with a message naming both versions.
- `HistoryCommandTests`: `history view` usage, exit codes, `--out` default beside the ledger, the ledger
  resolved from `--history`, then `KRONIKOL_HISTORY`, then `.kronikol/history.jsonl` as every other verb
  resolves it; `record --view` writes the ledger and the view in one invocation and a re-run is
  idempotent; `--window 0` and a non-number are usage errors. Tool tests pass `_ => null` as the
  environment (the three-argument `Run` reads the real one).
- Drift: `SkillDriftTests` unchanged and green with the new flags in prose only.

### M1 — the page, snapshot mode, one repository

Tool: `DashboardCommand`, `Commands.Table` entry, `src/Kronikol.Tool/Dashboard/dashboard.html`,
`dashboard.css`, `dashboard.js` as embedded resources, the shared palette (`HistoryPalette` read by
`HistoryHtml` and by the build). All seven panels, the differentiators (1 to 3) before the strips (4 to
6), panel 7 at run level. CSP hashes. Theme.

Tests written first:

- `DashboardBuildTests`: one file out; N sources inlined; both CSP hashes match the inline bytes; no
  `http:` or `https:` reference in a `src` or `href` of a script, style, font or image (the report's
  self-contained guard, reused); a token-shaped string in a view aborts the build with exit 2 naming the
  source; identical output for identical inputs except the timestamp; `--title`; a missing source path
  exits 2 naming it; `--history FILE` builds the view then the page.
- Playwright, `tests/Kronikol.Tests.EndToEnd/Dashboard*Tests.cs`, opened from disk like the report:
  renders with zero requests beyond the document; the runs strip shows every run with pass and fail
  segments whose counts match the view; a partial run is hatched; the scenario table filters by verdict,
  suite and text and sorts by p95 and by name; a `behaviour-changed` event is a marker on the drift
  timeline and clicking it shows the evidence and a link whose `href` is the run URL; the calls panel
  shows a scenario's series with its changed run marked; two scenarios sharing a cluster key appear
  under one cluster bar with first and last seen; a dependency that appears in run 6 is marked in panel
  7; dark theme applies when the browser prefers it; a scenario named with a script-injection payload
  renders as text and the console shows no CSP violation; every control is reachable by keyboard; the
  1,000-run, 200-scenario synthetic view renders its first panel under 300 ms scaled by contention.
  House rules apply: `PollingInterval = 200` on every `WaitForFunctionAsync`, no `Force = true`, `.First`
  or `.Nth` on any multi-match selector, no network mocking.

### M2 — many repositories, live refresh, the recipes

Tool: `--sources`, `--live`, the manifest, the fetch loop with in-place replacement and the unreachable
banner. Repo: `.github/workflows/dashboard.yml` publishing Kronikol's own Pages site with
BreakfastProvider as a live source; the source-repository one-flag change on BreakfastProvider's history
job; a reusable workflow template under `templates/` for a dashboard repository. Library: the
`HistoryDashboardUrl` option and `dashboard: <url>` on the pointer's `history:` line and in the CI
summary, when set. Wiki: `Dashboard.md`, linked from Home and the sidebar; a README section.

Tests written first:

- Unit: the manifest carries name, URL and mode per source; sources render in a stable order; each
  source's `analysis` block is shown; a `file` source is never marked live; a `view` URL that is not
  `https:` is a usage error.
- Playwright, live mode, against a **real local static server**: Kestrel through a
  `Microsoft.AspNetCore.App` framework reference in the E2E project, bound to `127.0.0.1:0` with the port
  read back after binding, never free-port-then-bind (the `ClosedPort` lesson). The page is served from
  one origin and the view from another so CORS is exercised, and the server sends
  `Access-Control-Allow-Origin: *` and honours `If-None-Match` as raw does. Tests: the page picks up a
  replaced view within one interval without reload; a `304` leaves the DOM node identity unchanged; a
  `404` keeps the snapshot and shows the banner; a source without the CORS header keeps the snapshot and
  shows the banner; the request count over three intervals with an unchanged view is three `304`s.
- Workflow verification, logged in §10: the Pages URL serving; BreakfastProvider fetched live from the
  page; two BreakfastProvider lanes recording concurrently and the view regenerated on the rebase;
  dispatch-to-deploy latency measured once.

### M3 — per-scenario dependencies (optional, needs a ledger change)

The scenario-by-dependency heat map and "which scenarios touch Cosmos" need a per-position dependency
list on the run line, a ledger format change (`HistoryFormat.Version` 2, with the reader accepting 1).
Only if M2's users ask for the panel. While in the format, the all-`null` `errors` array (13% of a green
ledger) goes sparse in the same version.

---

## 6. What this deliberately does not build

- **No server, database, account or push.** The page holds no token, never calls `api.github.com`, and
  keeps no state. Kronikol itself still opens no socket; the fetching is the browser's or the platform's.
- **No verdict in JavaScript.** Every pill, evidence string and event on the page is the .NET analyzer's.
- **No cross-repository verdict arithmetic.** Grouping across sources is text equality.
- **No editing from the page.** Quarantine and aliases stay committed files; the page may link to a
  prefilled issue or pull request URL, which is a link and not state.
- **No retention beyond the window.** A team wanting two years wants a warehouse; CTRF and OTLP export
  exist for that.
- **No CDN, web font, external script or stylesheet**, and no chart library unless vendored (§4.3).
- **No report content beyond the ledger.** The dashboard does not render diagrams or captures; the run
  URL is the hand-off to the report artifact.

---

## 7. Risks, each with its number

- **Staleness.** Raw caches five minutes; Pages caches `index.html` ten. A snapshot can be fifteen
  minutes behind a run, a live source five. The header timestamps make it visible; nothing claims
  real time.
- **Growth.** 337 bytes gzipped per run; the window caps a source at `window × streams × suites` runs.
  BreakfastProvider at 18 lanes and a window of 200 is 3,600 runs, about 1.2 MB gzipped in the worst
  case. M2 measures a page load on a runner and lowers the default window if it exceeds two seconds.
- **Rosters dominate small ledgers.** 40% raw and 80 KB gzipped on BreakfastProvider today. If M0's
  measurement keeps them above 30%, names and features are written once per stable id with per-roster
  id lists, a view-only change.
- **Visibility.** On the free and Team plans a private repository's Pages site is public. Stated in the
  wiki page's first paragraph, in `dashboard build --help`, and in the page header for snapshot sources.
- **Two lanes recording at once.** Eighteen on BreakfastProvider. The regenerate rule (§4.6a) is
  exercised there at M2, not assumed.
- **A texture read as a verdict.** `shapeChanged` bits invite a reader to count them. The page draws them
  as a strip and never as a number, and the schema's description of the field says what it is not.
- **Pages disabled or the deploy failing** leaves the previous site up; the page's own timestamps show
  the age. No silent staleness.

---

## 8. One sentence for `CROSS_RUN_HISTORY_PLAN.md` §14

Today: "No server, database, hosted dashboard or account. Kronikol itself opens no socket …"

Proposed, applied when this plan is green-lit and not before: "No server, database, account or push.
Kronikol itself opens no socket … A static page that reads the ledger's derived view from a git branch
(`DASHBOARD_PLAN.md`) is not a hosted dashboard in this sense: it holds no state, needs no account, and
the only fetching is the browser's, from a public branch, or the platform's own tooling; the three
things this section keeps out stay out of it."

---

## 9. Open questions for the owner

1. **Enable Pages on `lemonlion/Kronikol`** (source: GitHub Actions) for the dogfood in M2, and give
   BreakfastProvider's history job the `--view` flag so it is the second, live, public source?
2. **Where the reference multi-repository dashboard lives:** Kronikol's own Pages site with
   BreakfastProvider as a source (recommended: one repository to keep green), or a separate
   `lemonlion/kronikol-dashboard` repository that exercises the dispatch path end to end?
3. **Default view window:** 200 runs per stream per suite?
4. **Rendering:** hand-rolled SVG (recommended), or vendor uPlot from the start?
5. **Names:** `history.view.json`, `kronikol history view`, `kronikol dashboard build`?
6. **Panel 7 at scenario level** needs ledger Version 2 (M3). Defer until asked for?
7. **Should `history record` write the view by default** once M2 lands, so a consumer adds nothing? Costs
   one rewritten file per record and a regeneration on each rebase.
8. **`dashboard: <url>` on the pointer line** behind a `HistoryDashboardUrl` option: worth the option?

---

## 10. Verification log

Empty until the plan is green-lit. Entries follow `CROSS_RUN_HISTORY_PLAN.md` §12.1: what shipped, in
which version and commit, what was measured and where it differed from the plan.
