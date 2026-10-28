# SERVICE_CARD_PLAN harness

Probes and results behind `plans/SERVICE_CARD_PLAN.md` (#191). The first set was measured on 2026-10-26 against the
5.11.0 tool built from `44bcc62a`. The second was made on 2026-10-27 by the plan's three audits (files named `audit-A-`,
`audit-B-` and `audit-C-`) and by its revision (files named `revision-`), with the 5.14.0 tool and source (`8d7d1e1d`).

## What is here

| Path | What it is |
|---|---|
| `probes/ledger_stability.py` | Compares consecutive run lines of one suite on one commit in a history ledger: how often counts, call sets, call order, the run's distinct operations and its edges change between two runs of the same code (plan 2.4) |
| `probes/card-prototype.cs` | Builds the card's behaviour from a decoded data file: `CallNesting`'s rule ported, the product's own `InteractionShape.Template` and `TemplateStatement`, a prototype inference of the test client and the subject. Prints the origin split, the service's calls outside any received call, operation counts and the card's size three ways (plan 2.3) |
| `inputs/examples-5.8.0/` | The three Example.Api component reports (`TestRunReport.krkl` and `Run.json`), written by 5.8.0 |
| `results/ledger-stability-breakfastprovider.txt` | `ledger_stability.py` over BreakfastProvider's ledger |
| `results/card-prototype.txt` | `card-prototype.cs` over four BreakfastProvider lanes and the Example.Api Reqnroll report |
| `results/card-prototype-stand-ins.txt` | `card-prototype.cs` over kronikol-bf's stand-ins: the `nunit`, `tunit`, `lightbdd` and `bddfy` in-memory lanes of the same CI run, and a Jest ingest made with 5.11.0 (kept in `CALL_OPERATION_AND_ORIGIN_PLAN.harness/stand-ins/`) |

Audit A (accuracy), over the same ledger; each prints counts only:

| Path | What it is |
|---|---|
| `probes/audit-A-ops_diff.py`, `results/audit-A-ops-diff.txt` | The run pairs whose distinct operations differ, classified by service and method (plan 2.4) |
| `probes/audit-A-ops_diff_results.py`, `results/audit-A-ops-diff-results.txt` | For those pairs, whether the two runs' scenario results differ |
| `probes/audit-A-same_results.py`, `results/audit-A-same-results.txt` | The run-level rates over the pairs whose scenario results are the same |
| `probes/audit-A-scenario_index.py`, `results/audit-A-scenario-index.txt` | How often a scenario's set of operations, status dropped, changes between two runs (the first draft's scenario index) |

Audit B (deep dives), on BreakfastProvider's CI runs 40290999167 (A) and 40473765203 (B), both `cf47045` on Kronikol
5.4.1, 40491266394 (C, `68a8760` on 5.12.1), and two lanes run twice on this machine at `68a8760` (L1, L2):

| Path | What it is |
|---|---|
| `probes/audit-B-card-behaviour.cs` | The card's `behaviour` as the first draft's 4.3 laid it out, with the parties 5.14.0's `query parties --json` gives; flags select the readings P0 to P4 of plan 2.4 and `--with-background-section` reads the root `background.interactions` |
| `probes/audit-B-download.sh`, `audit-B-decode-all.sh`, `audit-B-parties-all.sh` | Download a run's 18 lane reports, each into its own new folder; decode them with 5.14.0; save 5.14.0's `query parties --json` for each |
| `probes/audit-B-local-lane-twice.sh`, `audit-B-local-pair.sh` | Run one in-memory lane twice on a scratch clone of BreakfastProvider, as CI's memory lane runs it (no containers, no browser), then decode, resolve and compare the pair |
| `probes/audit-B-cards-all.sh`, `audit-B-readings-10pairs.sh`, `audit-B-readings-BC.sh` | Build the cards for every input, the ten same-commit pairs under each reading, and runs B against C on all 18 lanes |
| `probes/audit-B-behaviour_diff.py`, `audit-B-count_drift.py`, `audit-B-parties_summary.py` | Compare two cards member by member, count the operations whose counts moved, summarise the parties envelopes |
| `results/audit-B-cards-all.txt` | Per report (46): subject, origin and cause by both readings and where they disagree, sizes and hashes in the first draft's shape |
| `results/audit-B-parties-summary.txt` | 5.14.0's test and subject rows on 52 reports |
| `results/audit-B-readings-10pairs.txt` | Readings P0 to P4 on the ten same-commit pairs (plan 2.4's table) |
| `results/audit-B-readings-BC.txt` | Readings P1 to P4 across runs B and C on 18 lanes |

Audit C (completeness):

| Path | What it is |
|---|---|
| `probes/audit-C-background_block.py`, `results/audit-C-background-block-lanes.txt` | The data file's root keys and its root `background` calls, in the ten decoded files the plan first measured (claim 15, F16) |
| `probes/audit-C-source_locations.py`, `results/audit-C-source-locations.txt` | Scenario, feature and step source files per lane (claim 14) |
| `probes/audit-C-map_lines.py` | Maps the plan's cited lines from `44bcc62a` to `8d7d1e1d` by content (Appendix A) |
| `results/audit-C-background-probe.txt`, `results/audit-C-background-probe-declared.txt` | 5.14.0's `query summary`, `services` and `parties` on two hand-written reports (below): F16 and section 3's third defect found on the way |
| `results/audit-C-example-parties.txt` | 5.14.0's `query parties`, text and `--json`, on `inputs/examples-5.8.0/ReqNRoll.xUnit3/` (F15) |

The revision, the whole card in the revised shape:

| Path | What it is |
|---|---|
| `probes/revision-card-revised.cs` | Writes the whole `service.json` in the revised 4.3 shape from a decoded data file, its parties envelope, the `Run.json` beside the report and the report file, compact and indented, and checks `behaviourHash` against the file's own bytes. Grown from `audit-B-card-behaviour.cs`; its header lists what it approximates |
| `probes/revision-sizes-revised.sh`, `revision-sizes_table.py`, `results/revision-sizes.txt`, `results/revision-sizes-table.txt` | The card's sizes on run C's 18 lanes, Example.Api's Reqnroll report and the Jest stand-in: compact, indented, each gzipped at level 9, and by part (plan 2.3) |
| `probes/revision-pairs-revised.sh`, `revision-pairs_revised.py`, `results/revision-pairs.txt` | The revised card on the ten same-commit pairs, on runs B against C, and Linux against Windows on one commit (plan 2.4, 4.3) |
| `probes/revision-report_sizes.py`, `results/revision-report-sizes.txt` | The four lanes' `.krkl` against their decoded JSON (claim 1) |
| `probes/revision-schemes.py`, `results/revision-schemes.txt` | Requests per URI scheme, side, method word, authority, path, category and meta type (the message table, 4.2) |

## How the 2026-10-27 files were run

- **The audits' scripts are copied as written.** They name each other without the `audit-A-`, `audit-B-` or `audit-C-`
  prefix, and audit B's hard-code the probe folder (`P=`) and the tool to the worktree they ran in,
  `C:/Code/Kronikol-191-dive` at `8d7d1e1d`. To run one again, put them in one folder under their written names and
  change those paths.
- **The revision's scripts take every folder as an argument** and name each other by their names here. They read audit
  B's downloads, decoded files and parties envelopes, so audit B's fetch, decode and parties steps run first.
- **What they were built against.** `revision-card-revised.cs` and `audit-B-card-behaviour.cs` reference
  `src/Kronikol/Kronikol.csproj` by a relative path, as `card-prototype.cs` does. The revised probe was built against a
  `git archive` copy of `8d7d1e1d` in the session's scratchpad, not in a checkout, so its numbers are 5.14.0's
  templater; built here it takes whatever the branch holds. Every 2026-10-27 measurement that ran the tool ran 5.14.0
  (`--version` prints it).
- **Inputs.** Run A (40290999167): the four lanes fetched on 2026-10-26, decoded again with 5.14.0, and
  kronikol-bf's four stand-ins as that session decoded them; its report artifacts have since expired. Runs B and C:
  `sh probes/audit-B-download.sh <run-id> <dest>`, decoded with 5.14.0 (`audit-B-decode-all.sh`). L1 and L2:
  `audit-B-local-lane-twice.sh` on a scratch clone at `68a8760`, decoded with 5.14.0 (`audit-B-local-pair.sh`). Every
  card probe takes 5.14.0's `query parties --json` for the report as its parties.
- **Not committed:** the downloads, the decoded files, the cards (up to 314 KB each indented) and audit C's two
  hand-written reports. The first, `inputs/background-probe/TestRunReport.json`, is a 5.14.0-shaped report with one
  scenario call (`Caller > Orders Api`) and one call in the root `background.interactions` (`Orders Api > Outbox Store`,
  `attributionSource: Expired`). The second, `inputs/background-probe-declared/TestRunReport.json`, is the same report
  with `serviceId` `orders-api` and a parties block naming `Outbox Store` as a declared `real` party with id
  `outbox-db`. What 5.14.0 printed on each is in `results/audit-C-background-probe*.txt`.

## What is not here, and how to get it again

BreakfastProvider's data files and ledger are not committed (several MB each; the ledger is 19 MB). To fetch them:

```bash
# the four lanes of CI run 40290999167 (cf47045, Kronikol 5.4.1); report artifacts expire, so a later run may be needed
for a in xunit-in-memory-report reqnroll-in-memory-report xunit-external-sut-report reqnroll-in-docker-report; do
  gh run download 40290999167 -R lemonlion/BreakfastProvider -n $a -D bp/$a
done
# all 18 lanes of a later run, each into its own new folder
sh probes/audit-B-download.sh 40491266394 bp
# the ledger
gh api "repos/lemonlion/BreakfastProvider/contents/history.jsonl?ref=kronikol-history" \
  -H "Accept: application/vnd.github.raw" > history.jsonl
```

Then decode each lane and run the probes:

```bash
kronikol decode bp/xunit-in-memory-report -o decoded/xunit-in-memory.json
dotnet run --file probes/card-prototype.cs -- decoded/xunit-in-memory.json cards/xunit-in-memory.card.json
python -I -X utf8 probes/ledger_stability.py history.jsonl
python -I -X utf8 probes/audit-A-scenario_index.py history.jsonl
kronikol query parties bp/xunit-in-memory-report --json --max-bytes 0 > parties/xunit-in-memory.json
dotnet run --file probes/revision-card-revised.cs -- decoded/xunit-in-memory.json parties/xunit-in-memory.json \
  bp/xunit-in-memory-report/Run.json bp/xunit-in-memory-report/TestRunReport.krkl cards/xunit-in-memory
```

The `.cs` probes reference `src/Kronikol/Kronikol.csproj` by a relative path, so they run from this folder in a
checkout. Python's `-I` ignores `PYTHONIOENCODING`; `-X utf8` is what lets it print the ledger's arrows on Windows.

## Peers' scripts cited by the plan

Measured by the sessions writing the sibling plans, on the same decoded files, and cited as PEER:

- kronikol-67 (#190): `parties.py`, `infer.py`, `hosts.py` and `cases.py`, committed with that plan in
  `PARTY_IDENTITY_PLAN.harness/bp-reports/` (5.13.0).
- kronikol-bf (#192): the call-identity rules, the GraphQL counts, the attribution split and the CLI timings, in
  `CALL_OPERATION_AND_ORIGIN_PLAN.harness/`.
