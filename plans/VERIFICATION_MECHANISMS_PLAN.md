# VERIFICATION_MECHANISMS_PLAN — the defects an audit structurally cannot find

**Status: investigation complete, NOT green-lit.** Successor to `LLM_FIRST_PLAN.md`, which is being
implemented in this same working tree by another session as of 2026-09-12 19:4x. Nothing here conflicts
with that work and nothing here should start before it lands — §6 states the sequencing and the two
files it would contend for.

**A row is owed in `PLANS_STATUS.md`.** This plan does not add it: that file is tracked and in flight.

---

## 0. The one sentence

`LLM_FIRST_PLAN` found 144 findings across eighteen survey lanes and 108 adversarial verifications, and
it **could not have found the defect that shipped in commit `338231b`** — *"the comparer that made
`Failures.md` point at the wrong scenario"* — because that defect is invisible in any single artifact.
This plan builds the mechanisms that find that class, and it is written on the premise that **an audit
is a one-time sweep with a shelf life measured in hours** (the last one went stale in nine minutes when
M3 landed), while an invariant does not go stale.

## 1. The blind spot, stated precisely

The audit checked **each artifact against expectations**. It almost never checked **two artifacts
against each other**.

The comparer bug has exactly that shape. `Failures.md` was internally plausible — a real address, a real
error message, correct markdown. `TestRunReport.json` was internally plausible. Only the *pairing*
between them was wrong. No inspection of either file alone can see it, which is why 108 verifications
pointed at falsifiers did not trip over it and one person writing the code did.

**This is not a criticism of the audit's diligence.** It is a statement about what its method can
resolve. The corollary matters more than the instance: **every defect that lives in the relationship
between two outputs is currently found only by a human noticing.**

## 2. What is measured, and what is assumed

Everything in this section was executed on 2026-09-12 between 19:38 and 19:52, against
`Kronikol.dll` 19:07:51 / working tree at commit `338231b`.

| # | Claim | Depth | Evidence |
|---|---|---|---|
| V1 | **No test resolves a printed address back into the report it came from.** Every `RoundTrip`-named test in the repo is *within one artifact* | **RUN** | `grep -rn "round.trip\|RoundTrip" --include=*.cs tests` → `FeatureSynthesizerTests:137` (JSON→object→JSON), `IngestAttachmentTests:65`, `InteractionRecordTests:47,106,158` (NDJSON writer→reader). None crosses artifacts |
| V2 | **`coverlet.collector` is installed on 60 of 77 test projects — including `Kronikol.Tests` — and no workflow ever collects it** | **RUN** | `grep -rl coverlet --include=*.csproj` → 60; `find -name '*Tests*.csproj'` → 77; `grep -n "\-\-collect\|XPlat\|cobertura\|lcov\|opencover" .github/workflows/*.yml` → **no matches**. Coverage is a dependency that does nothing |
| V3 | **No property-based or fuzz tooling exists** | **RUN** | `grep -rn "FsCheck\|Bogus\|AutoFixture\|PropertyBased" --include=*.csproj` → nothing |
| V4 | **The Kronikol4J parity oracle is one-directional.** Java reads pinned `/parity/*` resources and asserts its own output matches; the .NET side never compares against anything | **RUN** | `ReportDataParityTest.java:121`, `ComponentDiagramReportGoldenTest.java:37`, `CiSummaryGeneratorTest.java:109` all `getResourceAsStream("/parity/…")`. 59 parity resources, 45 of them HTML |
| V5 | **There is no parity-fixture regeneration path in either repo**, so no one can diff a golden even when they want to | **RUN** | `grep -rln parity` over the .NET repo's `*.csproj`/`*.ps1`/`*.sh` → nothing; no script in the port. This is what blocked `LLM_FIRST_PLAN` §14.3 item 16 |
| V6 | **Two scenario fields are exercised zero times by the whole fixture corpus**: `rule` 0 of 1,349 scenarios, `categories` 0 | **RUN** | Census over 37 `TestRunReport.json`, positive control passing (CiPreview.Mixed = 15 `result:"Failed"`) |
| V7 | **The last four releases were all driven by production use, not by analysis** | **RUN** | `git log`: 3.0.83 *"all user-reported against one production report"*, 3.0.84 *"user-reported against a production report"*, 3.0.85, 3.0.86 *"user-reported"* |
| V8 | The mechanisms below would have caught the `338231b` comparer defect | **UNVERIFIED — this is the plan's central bet** | M1's invariant 3 is written to fail on exactly that pairing, but the defect is fixed, so it cannot be demonstrated against a red build. **Falsifier: revert `338231b`'s comparer change in a scratch branch and confirm the new test reddens.** Do this before believing M1 works |

**V7 is the uncomfortable one and it belongs in the plan rather than in a footnote.** Production use has
a better recent record at finding defects than a 144-finding audit did. The two find *different
populations* — the audit found the schema failing 16/16 validators, 313 phantom errors in `services`,
and the digest collapse; production found rendering and payload-fidelity bugs. Neither substitutes for
the other, and the audit is the more expensive. **This plan is justified by the class it closes, not by
a claim that analysis beats usage.**

## 3. M1 — Cross-artifact invariants (the whole point of the plan)

One test class, run over every report the suite already generates. No new fixtures.

**The invariants:**

1. **Every address resolves.** Each `sN`, `sN/iM`, `sN/K`, `b:hash` printed in `Failures.md`,
   `Failures.jsonl`, `Reports/CLAUDE.md` and `Reports/AGENTS.md` names something that exists in the
   report beside it.
2. **Every address resolves to the *right* thing.** The error message printed beside address `sN`
   equals the JSON's `features[].scenarios[]` entry at that ordinal, after the digest's own
   normalisation.
3. **Address and identity agree.** The `stableId` printed for an entry is the `stableId` of the
   scenario that entry's `sN` address names. **This is the `338231b` defect**, and invariant 2 alone
   would not have caught it if the comparer reordered both sides consistently.
4. **Every `#sid-` link lands.** Each `TestRunReport.html#sid-<id>` emitted in any text artifact
   corresponds to a `data-stable-id` attribute in the HTML beside it. (`LLM_FIRST_PLAN` C54 measured
   that a miss is *totally silent* — identical DOM, `scrollY` 0, zero console events — so nothing at
   runtime will ever tell you.)
5. **The digest's counts match the report's.** "N further failures" plus the worked examples equals the
   report's failed-scenario count.

**Why these five and not more:** each one is a relationship the product asserts *in print* to an agent
that will act on it. Invariant 4 is load-bearing precisely because its failure mode is silence.

**Cost:** a digest parser (the formats are stable and pinned), plus the assertions. The reports already
exist in `bin/Debug/net10.0/Reports/` for every example project.

**Sequencing note:** `LLM_FIRST_PLAN`'s M2 rewrites `Failures.md`'s content (A1–A5). These invariants
are about *structure*, not wording, so they should survive it — but write them **after** M2 lands, or
write them against the format M2 defines, not today's.

## 4. M2 — Collect the coverage that is already installed

V2 is the cheapest finding in this document: 60 projects carry `coverlet.collector` and nothing ever
runs it.

- Add `--collect:"XPlat Code Coverage"` to the CI test invocation and publish the report.
- Then use it for **one specific question**, not as a percentage target: *which product paths does the
  fixture corpus never exercise?*

V6 is the worked example. `rule` at 0 of 1,349 scenarios and `categories` at 0 is this repo's
**recurring bug class** — a path gated on input no corpus exercises — and it was found by hand twice
(once in the Kronikol4J coverage audit, once tonight). Coverage finds it on every run, mechanically.

**Explicitly not a coverage-percentage goal.** A number invites gaming and would produce tests that
execute lines without asserting behaviour. The deliverable is a *list of unexercised paths*, reviewed.

## 5. M3 — Make the port a bidirectional oracle

Today (V4) the port asserts its output matches 59 pinned `.NET` artifacts. The .NET side asserts
nothing. So two independent implementations exist and only one of them can ever notice a disagreement,
and only after the bytes are already published.

- **A .NET test that regenerates the parity inputs and diffs them against the same pinned resources.**
  A .NET change that moves those bytes then fails *here*, in the repo that caused it, instead of
  surfacing later as port breakage.
- This also closes V5, which is a real gap independent of everything else: **nobody can currently diff
  a golden even when they want to.** It is what blocked `LLM_FIRST_PLAN` §14.3 item 16, leaving §9's
  release note resting on inference — *42 goldens carry the element, the element gains an attribute,
  therefore the bytes move* — rather than on a diff.

**The trap, measured:** the .NET unit-test outputs are **not** the parity fixtures. `Attachments.html`
is 460 KB titled `Test`; `report-attachments.html` is 276 KB titled `Kronikol Run`. Diffing that pair
measures two different reports. M3 must generate the parity inputs deliberately, not repurpose existing
test output.

## 6. Sequencing, and the two files this contends for

**Nothing here starts before `LLM_FIRST_PLAN`'s M0–M2 land.** Reasons, in order of force:

1. M2 rewrites `Failures.md`; M1's parser would be written against a format about to change.
2. `LLM_FIRST_PLAN` M1 is on a deadline — it is the breaking-shape release and the window closes when
   3.1.0 is tagged (`Directory.Build.props` is 3.0.86, latest tag v3.0.86, so it is **still open**).
   Nothing in this plan is time-sensitive and it must not compete for that window.
3. This plan's own M3 re-pins goldens, which `LLM_FIRST_PLAN` §9 is already re-pinning once. Doing both
   in one cycle is one parity pass instead of two.

**Files this would contend for:** `.github/workflows/ci.yml` (M2) and the Kronikol4J parity resources
(M3). Both are being touched by the current work. M1 contends for nothing — it is new test files only.

## 7. Non-goals

- **Finding everything.** Not achievable, and the phrase should not appear in a plan. The goal is to
  make one *class* of defect discoverable by a mechanism that runs without anyone remembering to look.
- **Replacing production feedback.** V7 says plainly that recent defects came from real use. This plan
  narrows what reaches production; it does not claim to close the channel.
- **A coverage percentage.** See §4.
- **Auditing again.** If the outcome of `LLM_FIRST_PLAN` is only "those findings got fixed", the next
  comparer bug needs another audit. The point of this plan is that it should fail a test instead.

## 8. Open questions

1. **Does M1 belong in `Kronikol.Tests` or its own project?** Recommend `Kronikol.Tests` — it already
   has the report-generation helpers, and a new project is a new CI allowlist entry, which this repo
   has a documented history of rotting.
2. **Should the invariants run over the Example.Api reports in CI, or only over reports the unit suite
   generates?** Recommend both, but the unit-suite ones are the gate: they run everywhere, including on
   machines without the example app.
3. **Is M4 (property/fuzz over `Feature[]`) worth it?** Deliberately left out of the milestones above.
   It has the highest ceiling and the worst cost-to-first-finding, and M1 plus M2 should be measured
   before adding a third mechanism. Revisit once M1 has run for a release cycle and the count of
   defects it caught is known.

## 9. How to verify this plan is worth executing

Before building any of it, run V8's falsifier: **revert `338231b`'s comparer change on a scratch
branch, write M1's invariant 3, and confirm it reddens.** If it does not, M1 is mis-specified and the
rest of the plan is resting on the same error class it was written to close.
