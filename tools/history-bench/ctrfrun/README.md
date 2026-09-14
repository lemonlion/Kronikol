# ctrfrun — running `github-test-reporter`'s own insights arithmetic

`run-insights.ts` and `sort-reports.ts` are **verbatim copies** from
`ctrf-io/github-test-reporter` (`src/ctrf/core/src/methods/`, fetched 2026-09-12). The only edits are
to the two import lines: `from "ctrf"` → `./shim.ts` (type-only aliases, since the types are erased
anyway) and the `.js` specifier → `.ts`. **No logic is touched** — the point is to run their code,
not a transcription of it.

```bash
node --experimental-transform-types drive.ts    # producer flaky vs retries
node --experimental-transform-types drive2.ts   # a genuinely FLIPPING test
```

`--experimental-transform-types`, not `--experimental-strip-types`: `sort-reports.ts` declares a
TypeScript `enum`, which strip-only mode rejects.

Refresh the copies before trusting an old result:

```bash
gh api repos/ctrf-io/github-test-reporter/contents/src/ctrf/core/src/methods/run-insights.ts \
  --jq '.content' | base64 -d > run-insights.ts   # then re-apply the two import edits
```

## What it established (CROSS_RUN_HISTORY_PLAN §0.2, §8.6)

| Producer | `flakyRate` | `failRate` | `insights.extra.totalResultsFlaky` |
|---|---|---|---|
| Plain CTRF, test flips 2-of-6 | **0** | 0.3333 | 0 |
| Same, plus `flaky: true` | **0** | 0.3333 | **6** |
| Never fails, `retries: 2` | **0.6667** | 0 | 5 |

A test that never fails scores 0.67 flaky; a test failing a third of the time scores 0. **`flakyRate`
measures retry volume, not flakiness** — so the free chain reports zero flakiness forever for any
suite that does not retry, which is the default for xUnit and ReqNRoll.

Also established: a producer-set `insights` is **overwritten**; a producer-set `test.extra`
**survives** into the enriched report; and `totalResultsFlaky` **is** emitted under `insights.extra`,
so a custom Handlebars template can render it even though no built-in rate reads it.
