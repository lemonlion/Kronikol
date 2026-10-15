# Attachment merge plan (#175)

**Status:** written 2026-10-14 at 4.20.2 (`3240c983`) by kronikol-ec, and swept on 2026-10-15 at 4.22.1 (`85f849f7`) at
the owner's request. The sweep re-ran every measurement on 4.22.1, measured the background and hook cases that had only
been read from the code, and found F9: the failures digest reads only a scenario's own attachments, so R1 now carries
the readers too (section 10 has the sweep's log). Green-lit in full by the owner on 2026-10-15 ("Can you implement the
plan in full"), every question in section 9 as recommended. **Executed as 4.24.0**, one minor release (Q14); section 10
has the implementation's log.

**Issue:** [#175](https://github.com/lemonlion/Kronikol/issues/175), "A playwright-bdd failure lists its trace, video,
screenshot and error context twice, because the merge matches attachments by a name only one side gives an extension".

**Harness:** `plans/ATTACHMENT_MERGE_PLAN.harness/`. `probe/` is a real playwright-bdd 9.2.0 run on @playwright/test
1.62.1 in a browser, with Playwright's own artefacts on, `retries: 1`, and the wiki's own reporter recipe writing the
tests file. `measure.sh` ingests it with four merge rules against four variants of the tests file
(`results/matrix.txt`), and `results/measurements-2026-10-14.txt` holds the rest: the issue's sizes, memory, placement,
`query failures`, the test suite's own leftovers. `prototype.patch` is the rule in section 4, as measured. Windows 11,
.NET SDK 10.0.300, node 25.9.0.

Line numbers are to `85f849f7` (4.22.1) unless a line says otherwise. Nothing under `src/Kronikol/Ingestion/` changed
between `3240c983` and `85f849f7`, so the Ingestion lines are the same at both. Wiki lines are on `origin/master`
`32dc356`, where the pages cited are unchanged since `60daf39`.

---

## 1. Summary

The issue is right about what happens and about its cause. It understates the problem, though, and neither of its
two suggestions fixes it: the first leaves most of the duplicates and loses artefacts in a common case, and the second
mixes up artefacts.

The underlying problem is that the merge has no notion of an attachment's identity. Every artefact of a playwright-bdd
run is described twice: the reporter's tests file names the file Playwright wrote, and the Cucumber messages embed the
same bytes. `CucumberFeatureMerger.MergeAttachments` (L110-116) decides that two of these are the same artefact by
comparing a display label, and each part of how it does that fails on its own:

| What the merge does | Measured on the probe (4.20.2) | Effect |
|---|---|---|
| Compares a label that Kronikol rewrites on one side only (`CucumberFeatureSynthesizer.cs` L1123) | Playwright's `trace` meets `trace.zip`, and the same for `video`, `screenshot`, `error-context` | The issue: four built-in artefacts listed and stored twice (F1) |
| Compares the scenario's own list only (L122-126) | A step's `step-shot.png`, and the wiki's recommended `AfterStep` screenshot on every step | Every attachment made inside a step is listed and stored twice, extension or not (F2) |
| Keeps the reporter's copy without checking that its file exists | Tests file kept, `test-results/` gone (Playwright empties it on its next run): 26 dead links, the messages' bytes thrown away | The issue's first suggestion makes this worse (F3) |
| Pairs by name alone | Two artefacts named `trace` (a step's own and Playwright's) | Once step lists are compared, a name-only rule swaps them (F4) |
| Decodes and writes every copy before deciding, into a temp folder nothing deletes (L1137-1146) | 2,929 folders (52 MB) on this machine, 69.7 MB per ingest at the issue's sizes | Disk filled by copies that are never used (F6) |
| Leaves the tests file's `kronikol-bypass` and `kronikol-test-id` alone | `kronikol-bypass` listed as a text file | The wiki says these never appear in a report (F7) |
| (The readers) `Failures.md`, `Failures.jsonl` and `query failures` read only the scenario's own list | Today a step's screenshot reaches them only as the duplicate. Without it, `Failures.md` loses the failing step's screenshot, and with `--include-hooks` it lists nothing | Fixing the merge alone would take evidence out of the digest agents read first (F9) |

On the probe's four scenarios, 4.20.2 lists 41 entries where 25 artefacts exist (`results/matrix.txt`). At the
issue's sizes (a 58 MB trace and a 7.5 MB video attached in a failing step), `attachments/` holds 139.4 MB where
69.7 MB is the run, which reproduces the issue's 76 to 141 MB.

**R1, one release** (a minor only because `Failures.jsonl` gains one member, Q14), gives an attachment an identity that
both sides agree on and uses it across the whole scenario.
Each attachment of the tests file is paired, one to one and in order, with a copy anywhere the messages put one (the
scenario, a Gherkin step, a background or hook step). A pair needs the same name, where the messages' side may give its
producer's own name or the one Kronikol displays. When the reporter's file can be read, it also needs the same bytes.
The pair keeps the reporter's file and name when that file exists, and the messages' copy when it does not, and it sits
where the messages put it. The two signals are never attachments. The decoded copies the report does not use are
deleted, and so is the staging folder once the report is written. The failures digest and `query failures` then read
attachments wherever they sit, each labelled with its step, so nothing they list today drops out of them (4.7).

On a prototype of that rule: 25 entries, 166,354 B in `attachments/` against 280,605 B (41% less). With the reporter's
files gone, it keeps 23 files with 2 dead links (the two text bodies the messages never carried), where 4.20.2 keeps 15
with 26. At the issue's sizes it stores 69.7 MB, leaves nothing in the temp folder, and takes 1.3 to 1.4 s where
4.20.2 takes 1.2 s, with the same peak memory. All 414 existing Cucumber and ingest facts pass on it; the one fact that
called the old signature is rewritten (6.2, fact 18). On 4.22.1 the prototype applies unchanged and the matrix gives the
same counts (`results/matrix-4.22.1.txt`). The prototype does not include the readers' change: its `Failures.md` for
the failing scenario lists 6 entries where 4.22.1 lists 14. Of the 8 gone, 4 are the duplicates R1 is meant to remove
and 4 are artefacts made inside steps (`step-shot.png` and three `after-step.png`, the failing step's own among them).
That measurement is why 4.7 is part of R1.

## 2. What the issue says, checked

| Claim | Verdict | Evidence |
|---|---|---|
| A playwright-bdd run gives every attachment twice: a file named by the tests file's `attachment` record, and base64 in the messages | **True.** playwright-bdd's `message` reporter always embeds (`toEmbeddedAttachment`, `dist/reporter/cucumber/message.js` L29). Only its HTML reporter can externalise, so `url` never reaches the messages file | `results/summary-probe.txt`: every file attachment is `BASE64`, every `url` is empty |
| `MergeAttachments` (L110-116) keeps the first and matches by name | **True**, case-insensitive. It also drops *every* messages copy whose label any tests name equals, so one tests `note` would remove two messages `note`s | L114-115 |
| The changelog promises the reporter's attachments win | **True**, `[3.0.45]` (`CHANGELOG.md` L8537), the release that added the merge (`f46ebdb6`, 2026-08-22). The existing fact's comment says why: "seen live: 58 files for 29 screenshots". 3.0.45 fixed that symptom for names with an extension only | `CucumberFeatureMergerTests.cs` L150-170 |
| Playwright names its built-ins `trace`, `video`, `screenshot`, `error-context` | **True** | `summary-probe.txt` |
| The tests file keeps those names and `Materialise` gives the messages' copy an extension (L1123) | **True, and both sides start from the same name.** playwright-bdd sets the attachment's `fileName` to Playwright's own name (`TestStepAttachments.js`, `fileName: pwAttachment.name`). The difference is made by Kronikol alone | L1119-1123 |
| A failed scenario lists the ten names quoted | **Reproduced.** The probe's first run listed the issue's ten names in the issue's order, with the step's own screenshot also listed twice | section 3.2 |
| `attachments/` holds each of those files twice (`trace.zip` and `0110-trace.zip`) | **True**: `trace.zip` is the reporter's file, `NNNN-trace.zip` the decoded copy | `extract_attachments.py` on the probe's report |
| Attachments named with an extension are matched and kept once | **True only in the scenario's own list.** One made inside a step, extension or not, is kept twice: the reporter's copy on the scenario, the messages' copy on the step | `step-shot.png`, `after-step.png` (3.2) |
| 58 MB trace, 7.5 MB video, `attachments/` from 76 to 141 MB | **Reproduced at that scale**: 69.7 MB to 139.4 MB | measurements, section 3 |
| Suggestion 1: compare names with the media type's extension on both sides | **Needed, not enough.** Measured as written (`issue-1` in the matrix): 41 entries become 37 and step duplicates stay. With the reporter's files gone it keeps 11 files where 4.20.2 keeps 15, because the four built-ins' messages copies, the only bytes left, are now dropped too | `matrix.txt` |
| Suggestion 2: match on content (size and hash) | **Wrong as a key, right as a check.** Screenshots of an unchanged page have the same bytes whatever their name, so pairing by bytes put the failure screenshot on a Gherkin step. As a check on a name pair, it is what keeps two artefacts with one name apart (3.4, 3.5) | measurements, section 7 |
| Report written by 4.6.0, `kronikol ingest` 4.19.1 the same | **Same on 4.20.2 and 4.22.1**, and on every version since 3.0.45 | `git log -S MergeAttachments`; `results/matrix-4.22.1.txt` |

## 3. Findings

### 3.1 F1: Kronikol makes the two names differ

`AttachmentWriter.Materialise` (L1117-1153) names the messages' copy `name + ExtensionFor(mediaType)` when the name has
no extension (L1123), so that, its comment says (L1121-1122), the report renders a screenshot inline. That reason did
not hold even in the commit that wrote it (`f46ebdb6` added both): `FileAttachment.IsInlineImage` (`FileAttachment.cs`
L31-42) reads the media type first, a Cucumber attachment always carries one (`mediaType` is required in @cucumber/messages; `fileName` and `url`
are optional), and the HTML uses `Name` only as link text and `alt` (the `href` is the file, `ReportGenerator.cs`
L2266-2280). The extension is cosmetic, and it is the only thing that makes `trace` and
`trace.zip` different. The tests side's name comes from the reporter's `name` (`FeatureSynthesizer.cs` L348-354),
which the wiki recipe sets to Playwright's.

### 3.2 F2: only the scenario's own list is compared

`CarryOver` (L122-131) passes `target.Attachments`, the scenario's own list, to the merge. The synthesis puts an
attachment on the Gherkin step whose `testStepId` it carries (L414-419, L484-489), and moves only hook attachments to the
scenario when hooks are dropped (L501-503). On the tests side, every attachment of a scenario the messages own lands
on the scenario, because the pipeline removes that scenario's `step` records before the tests model is built (L416), so
a `step` index has nothing to resolve to (`FeatureSynthesizer.cs` L324-334). A copy on a step therefore never meets its
twin.

Measured, with the wiki's `AfterStep` per-step screenshot (`Integration-Playwright.md` L129 recommends it) and a step that
attaches `step-shot.png`: the failing scenario lists 18 entries for 10 artefacts, and every one made inside a step is
listed twice. A `Background:` step's attachment is pinned to the background step and listed twice the same way (measured
in the sweep, `probe/features/background.feature`: 18 entries for 10). With `--include-hooks` it is worse: the hooks
keep what they attached (L514-524), so even `screenshot-start.png`, which the scenario's list pairs without hooks,
is kept twice, and the scenario lists 20 entries for 10 (measured in the sweep).

### 3.3 F3: the reporter's copy wins when it is not there

The tests file names a path. Nothing checks that it exists before the merge throws the messages' copy away, and the
copier then keeps a missing file's original path (`ReportGenerator.cs` L6138-6142, #148). That happens whenever the
reporter's files are gone when the ingest runs: Playwright empties `test-results/` at the start of its next run
(found by accident in the harness), and a CI job that uploads only the two NDJSON files leaves them behind.

Measured (`gone` rows): 4.20.2 lists 41 entries, has 15 files and 26 dead links. Every extension-named copy in the
scenario's own list was thrown away. The issue's first suggestion makes it 11 files, because it also throws away the
built-ins' copies, the only bytes left. The prototype keeps 23 files, and its 2 dead links are the two `note` JSON
bodies that playwright-bdd sends as text, which the synthesis does not write out by default (`WriteTextAttachments` is
off, by design).

### 3.4 F4: one name, two artefacts

Playwright allows two attachments with one name. At the issue's scale the failing step attaches its own 58 MB `trace`
and 7.5 MB `video`, and Playwright adds its own `trace` (7.5 MB) and `video` on failure. A rule that pairs by name alone,
once it looks beyond the scenario's list, pairs the step's `trace` with Playwright's: the step then shows Playwright's
7.5 MB trace and the scenario the 58 MB one (measured, `KRONIKOL_175_VERIFY=0`). Checking the bytes of each pair keeps
each where it was attached.

### 3.5 F5: bytes cannot be the key

Every screenshot of an unchanged page has the same bytes whatever it is called: the probe's `screenshot`,
`screenshot-start.png`, `screenshot-end.png`, `step-shot.png` and `after-step.png` share one SHA-256, and two attempts'
`error-context.md` match too. Pairing by bytes alone (the issue's second suggestion), run on the tests file with no
names, put the failure screenshot on a Gherkin step and an after-step screenshot on the scenario. Limited to bytes that
occur once on each side, and with the name rule accepting the displayed name (4.1), it changed nothing on any variant.
So bytes are a check on a name pair and never a key.

The prototype's first cut had a defect of the same family: a pair written back into its step stayed a candidate, so the
three identical `after-step.png` screenshots all paired with the first step and the other two steps kept their decoded
copies (measurements, section 7a). A paired slot must never be offered again (fact 3).

### 3.6 F6: every copy is decoded and written first, and the folder is never deleted

`Materialise` decodes and writes each base64 body (L1143) into `CucumberSynthesisOptions.AttachmentsDirectory`, which
nothing in the product sets, so into `%TEMP%/kronikol-cucumber-attachments/<guid>` (L1137-1138). It writes the copies the
merge will drop as well, and nothing deletes the folder. The report copies what it links into `attachments/`
(`ReportGenerator.cs` L6052), so the folder is dead weight once the report is written.

Measured: 2,929 such folders (52 MB) on this machine before the probe ran; 69.7 MB left per ingest at the issue's sizes;
23 folders per run of the Cucumber and ingest facts (13 with the prototype, which deletes only the pipeline's own).
On an ephemeral CI runner this costs disk during the job only; on a developer machine it accumulates.

### 3.7 F7: the signals come back through the tests file

The synthesis reads `kronikol-test-id` and `kronikol-bypass` as signals and never writes them out (L32-36, L401-410). The
wiki's reporter recipe skips the test id but writes every other attachment, so a step that attaches `kronikol-bypass`
reaches the report as a text file named `kronikol-bypass` (measured, the probe's fourth scenario). The wiki says these
attachments "never appear in the report, whatever that option says" (`Integration-Cucumber-Messages.md` L213-214).

### 3.8 F8: documentation and comments that are wrong today

- `Integration-Cucumber-Messages.md` L210-212: the extension is "what makes the report render a screenshot inline"
  (false, 3.1); L213-214: the two signals "never appear in the report" (false with `--tests`, 3.7); the staging folder is
  described with no word that it stays behind (3.6). L185-197 ("Two sources, one report") lists attachments among what
  the tests file contributes and says nothing about the messages' copies.
- `Integration-Playwright.md` L129: "Kronikol's `step` index puts them on the matching step row". The recipe it follows
  (L107-113) writes no `step`, though Playwright's `TestStep.attachments` would let it (`testReporter.d.ts` L829). With
  `--cucumber-messages` the step comes from the messages instead (4.2).
- `CucumberFeatureMerger.cs` L19-21 (the class remarks) and L105-109 (the method's summary); the code comment at L1121-1122.
- `Generated-Reports.md` L436-437 says the digest lists "attachments"; it never listed a step's (F9).

### 3.9 F9: the failures digest reads only the scenario's own list (found in the sweep)

`FailuresDigestGenerator` builds a failure's attachments from `scenario.Attachments` alone (L285), and writes them to
`Failures.md` (L781-785) and `Failures.jsonl` (L969). `kronikol query failures` prints the scenario's first four
(`QueryCommand.Narrative.cs` L136, L205; the cap is #161). Only `query steps` prints a step's attachments (L335). No
reader of the digest ever sees an attachment made inside a step, in any lane: the tests file's `step` index
(`FeatureSynthesizer.cs` L340-344), the Cucumber messages, LightBDD's step attachments, a report `kronikol merge` read
back. The debugging skill tells agents to read screenshots from `failures` output (`SKILL.md` L163), so this is the
path that matters.

In the combined lane the gap is hidden today by the duplicates: the reporter's copy of a step's screenshot sits on the
scenario, so the digest lists it. Measured on the probe's failing scenario:

| Build | `Failures.md` attachments | `query failures` (first four) |
|---|---|---|
| 4.22.1 | 14: every artefact, plus the 4 duplicated built-ins | `screenshot-start.png, after-step.png, step-shot.png, after-step.png`: no failure artefact |
| Prototype (merge only) | 6: the scenario's hooks' and fixtures' artefacts. `step-shot.png` and the three `after-step.png` are gone, the failing step's own screenshot among them | `screenshot-start.png, screenshot, screenshot-end.png, video` |
| 4.22.1 with `--include-hooks` | 10: the reporter's copies, which sit on the scenario | (as above) |
| Prototype with `--include-hooks` | **0**: every pair sits on a hook step, and the scenario's own list is empty | none |

So the merge fix alone moves evidence out of the digest, and with `--include-hooks` empties it. The model cannot tell a
hook step from another (`ScenarioStep` has `IsAssertion` and no hook marker), so the readers have to take attachments
from the whole tree, not from "the hooks". The data files (JSON, YAML, XML) already carry step attachments. Run
rotation keeps each retained run's `attachments/` (`RunRotation.cs` L185-201), so every kept run holds today's
duplicates as well.

### 3.10 Found on the way, not in this plan

- **Memory.** The messages reader keeps every body in memory until the synthesis runs. Peak working set: 75 MB for the
  0.5 MB probe, 1,380 MB for the 185.8 MB issue-scale file, about seven times its size. A suite with ten failures of the
  issue's size would need over 10 GB. playwright-bdd's embedding is what makes the file that large; streaming each body
  to disk as it is read would cap it (Q9).
- **The recipe's `step` index** (3.8, second bullet), for the tests-only lane (Q11).

## 4. Design

### 4.1 The rule

For each scenario the messages own, `CarryOver` passes the tests model's scenario attachments to a new
`MergeAttachments(Scenario target, FileAttachment[] fromTests, CucumberSynthesisResult cucumber)`, which:

1. Drops every tests attachment named like a signal: the test id attachment's name
   (`CucumberSynthesisOptions.TestIdAttachmentName`, `kronikol-test-id` from the CLI) or `kronikol-bypass`, compared as
   the synthesis compares them (case-insensitive). Only for scenarios the messages own: elsewhere they are files as the
   tests contract describes them (Q7).
2. Collects the candidates: every attachment the messages placed in the scenario, in reading order. That is the
   scenario's list first (which already holds hook attachments when hooks are dropped, and orphans, L614-619), then each
   background step, step and kept hook step, with their sub-steps.
3. For each tests attachment in its order, takes the first candidate not yet paired such that:
   - neither side is a URL (a link is never a copy of a file);
   - the tests name equals, case-insensitively, the messages attachment's own `fileName` or the name the synthesis
     displays for it (its own plus the media type's extension, L1123). The second form is what a reporter that names a
     record by its file writes (Playwright's `trace.zip`), and today's rule matches it by accident;
   - when the tests file exists, both have the same length and SHA-256. When it does not, the name is enough.
4. Makes the pair one entry: the tests name, the tests file when it exists (and deletes the decoded copy) or else the
   messages' copy, and the media type of the file that survives (the tests record's, or the messages' when it has
   none; the messages' when its copy survives). When the reporter's files are gone, two artefacts with one name pair
   by order alone, and may cross: each survivor is then the messages' copy in its own place, and its own media type
   keeps an image from being labelled as a zip.
5. Leaves every unpaired attachment of either side where it was. When a name pair fails only on bytes (both files exist
   and differ), both are kept, and one diagnostic counts them (Q4): the merge counts them on its result (an internal
   property, as `DefaultedResultCount` and `UnmatchedTestsFileBypasses` are), and the pipeline reports the count as
   `DiagnosticKind.Other`.

A slot that took its pair is never offered again (3.5). The comparison stays case-insensitive, as today. Both
producers measured pass Playwright's name through unchanged, so nothing needs that, and the mutation is recorded as
equivalent on real inputs (6.4).

### 4.2 Where the pair goes, and in what order

The pair goes where the messages put it. On a step (Gherkin, background or kept hook) it takes the messages copy's
position there and leaves the scenario's list. In the scenario's list it takes the tests entry's position. The
scenario's list is then the tests entries in their order, paired or not, followed by the messages' unpaired entries in
theirs, which is today's order. The tests side cannot do better: its `step` index is never resolved for a scenario the
messages own (3.2). So a per-step screenshot appears on its step once, instead of on the scenario and the step (Q2).

### 4.3 What the synthesis records

The merge needs each candidate's own name, its bytes' length and hash, and the file the synthesis wrote. Today a
`FileAttachment` carries only the displayed name, the path and the media type. `FileAttachment` is a public record, and
a new member there would be public surface (a minor) and would change its equality and its JSON. So the synthesis keeps
an internal side table on the result, keyed by reference:
`internal IReadOnlyDictionary<FileAttachment, CucumberAttachmentOrigin> AttachmentOrigins` with
`CucumberAttachmentOrigin(string? OriginalName, long? Length, byte[]? Sha256, string? WrittenPath)`. The hash is taken
from the bytes `Materialise` has already decoded, so it costs no extra read. A `url` attachment records its name only,
and the merge hashes its file when it needs to. The tests side's files are hashed at most once each, and only when a
same-named candidate exists. Also `internal IReadOnlySet<string> SignalAttachmentNames`, from the options, so the merge
uses the synthesis's names.

### 4.4 Deleting what the report does not use

- The merge deletes the decoded file of each copy that a reporter's file replaced (its `WrittenPath`). The folder then
  holds only copies the report links.
- `IngestPipeline.Run` gives the synthesis a staging folder of its own (`AttachmentsDirectory`, the same temp location as
  today) and deletes it in a `finally` once the report is written, unless an entry of the result still links into it.
  That happens when the generator could not copy a file, which it reports as an `AttachmentFailure` diagnostic, and the
  report keeps the original path. Deleting the folder then would turn a recorded failure into a dead link.
- A library caller of `CucumberFeatureSynthesizer.Build` keeps today's contract: its folder, its files.

Not done: deciding before decoding, so that duplicates are never written. It would need the tests records inside the
synthesis, or every body held until the merge. At the issue's sizes, writing and then deleting 69.7 MB is part of an
ingest that took 1.3 s.

### 4.5 What does not change

- **No public API.** `CucumberFeatureMerger.Merge`, `CucumberSynthesisOptions` and `FileAttachment` are untouched. The old
  `MergeAttachments` overload was internal.
- **A messages-only ingest** has no tests attachments to pair, so its entries, display names included, are as today.
  A tests-only ingest, and every scenario the messages do not own, are untouched.
- **The copier**, rendering, and the data files' format. The digest's format changes (4.7), in every lane.
- **Kronikol4J:** `kronikol4j-cucumber` is an in-process Cucumber-JVM plugin with no attachment handling and no messages
  importer.

What a reader of a combined ingest sees change (the changelog's behaviour notes): each artefact once; a step's
attachment on its step rather than the scenario; a Playwright built-in once, under the run's name (`trace`, not also
`trace.zip`); a messages copy linked where the reporter's file was missing; no `kronikol-bypass` or `kronikol-test-id`
entry for a scenario the messages own; `attachments/` one file per artefact; and no staging folder left in the temp
directory. In every lane: `Failures.md`, `Failures.jsonl` and `query failures` list the attachments made inside steps
(4.7).

### 4.6 Options considered

| Rule | Probe entries | Reporter's files gone: files / dead links | Why not |
|---|---|---|---|
| Today (4.20.2 and 4.22.1) | 41 | 15 / 26 | the issue, F2, F3 |
| The issue's suggestion 1: names with the extension on both sides, scenario list, reporter always wins | 37 | 11 / 26 | step duplicates stay; drops the only bytes of four more artefacts |
| Name only, whole scenario, the copy that exists wins | 25 | 23 / 2 | swaps same-named artefacts (F4) |
| The issue's suggestion 2: bytes as the key | not run as the only key; tried on the nameless variant | pairs nothing: there are no reporter's bytes to compare | misplaces identical screenshots (F5); limited to unique bytes it adds nothing |
| The messages win: keep the decoded copy, drop the reporter's | not built | (23 / 2 by construction) | loses the run's names, links every artefact from a decoded copy, reverses 3.0.45's documented rule, and still needs the same pairing to know which reporter entries to drop |
| Pairs stay on the scenario (the reporter's place), the messages' step copies dropped | not built | (23 / 2 by construction) | keeps the digest as it is without 4.7, but takes every per-step screenshot off its step row, where 4.22.1 shows it today (as the duplicate); the wiki promises the step row (`Integration-Playwright.md` L129) |
| **This plan** | **25** | **23 / 2** | |

### 4.7 The readers: the digest lists attachments wherever they sit

Without this, 4.2 takes step evidence out of the digest (F9). With it, the digest lists what it lists today, minus the
duplicates, in every lane:

- **`Failures.md` and `Failures.jsonl`:** a failure's attachments are the scenario's own, then each step's in tree order
  (background steps, steps, kept hooks, sub-steps), each step's labelled with the step's `sN/k` address, as the digest
  already labels the failing step. In `Failures.jsonl` each attachment object gains a `step` member with that address,
  absent for the scenario's own; the JSON shape is otherwise unchanged (Q14). No cap is added. The digest's rule is that
  attachments are paths, one line each (L40-44), and its budget limits how many failures are detailed
  (`MaxDetailedFailures`), not an entry's length. So a scenario with a screenshot after each of 30 steps gains 30
  lines. Listing only the failing step's and its context steps' would miss the hooks' artefacts under
  `--include-hooks` (they come after the failing step), which is why every step is listed (Q15).
- **`kronikol query failures`:** the failing step's block prints that step's attachments under its error and location,
  as `query steps` prints them (L335). The scenario's lines stay the scenario's own first four (#161 owns that cap and
  its missing "and N more"). With `--include-hooks`, hook artefacts reach `Failures.md` and `query steps`, not `query
  failures`; the model has no hook marker to pick them out by (Q15).
- **Unchanged:** the HTML (it draws step attachments on their rows already), the data files, `query steps`.

## 5. Releases

| Release | Content | Bump | Depends on |
|---|---|---|---|
| **R1** | 4.1-4.4: the pairing rule, placement, the signals, deleting unused copies and the staging folder; 4.7: the digest and `query failures` read attachments wherever they sit | **minor**, recommended (Q14): `Failures.jsonl`'s attachment objects gain a `step` member, new surface a script can read, and CLAUDE.md says the highest-ranking change decides the bump (precedent: `callsScope` arrived in the 3.2.0 minor). Without the member, a patch | nothing |

One release (Q8): the merge must not ship without 4.7 (F9), and 4.7 alone would list today's duplicates twice in the
digest. The number is claimed with "taking X now" at push time. On 2026-10-15, kronikol-51 is releasing #173 (R0 a
patch, then three minors) and has offered to fit around this plan's number once it is green-lit.

## 6. Tests (TDD: each written red against the release before R1, 4.22.1 today, then made green)

Every finding, its fix and the facts that hold it:

| Finding | Fix | Facts (6.2) | Mutations (6.4) |
|---|---|---|---|
| F1 the names differ | 4.1, step 3: either form of the messages' name | 1, 3, 16, 17 | M1 |
| F2 the scenario's list only | 4.1, step 2 and 4.2: the whole scenario, the messages' placement | 2, 3, 10, 11, 17 | M6, M7 |
| F3 the reporter's copy wins when missing | 4.1, step 4 | 5 | M4, M5, M15 |
| F4 one name, two artefacts | 4.1, step 3: the byte check; step 5 | 4, 6 | M2 |
| F5 bytes are not a key; a paired slot offered again | 4.1: no bytes-only pass; a slot pairs once | 3 | M3 |
| F6 decoded first, never deleted | 4.4; the suite's own folders (6.3) | 14, 15 | M11, M12 |
| F7 the signals | 4.1, step 1 | 7 | M8 |
| F8 the documentation | section 7 | none (docs) | none |
| F9 the digest reads the scenario's list only | 4.7 | 16, 19, 20, 21, 22 | M14, M16 |
| Guards: text bodies, links, order, retries | unchanged behaviour | 8, 9, 12, 13 | M9, M10 |

### 6.1 Fixture

`tests/Kronikol.Tests/TestData/Cucumber/playwright-bdd-9.2-attachments/`, cut from the harness probe's real run: its
messages file (the failing, flaky and bypass scenarios, both attempts), its tests file with paths made relative to the
folder (resolved by `AttachmentsBase`), and the files those records name. The whole probe is 483 KB of messages and
318 KB of files, so the cut stays under 0.5 MB. Absolute paths in the messages are rewritten to `C:\fixture`, as the
existing golden fixture's are (`CucumberFixtures.cs`). The cut also takes `probe/features/background.feature`'s run (a
`Background:` step with the `AfterStep` hook; playwright-bdd pins that attachment to the background step, measured in
the sweep), which also serves `--include-hooks`. Facts that need a shape the probe lacks (two artefacts with one name
and different bytes, a link, a missing file) build their files in a per-fact temp folder.

### 6.2 Facts

Unit facts go in `CucumberFeatureMergerTests.cs`, pipeline facts in `CucumberIngestPipelineTests.cs`, CLI facts beside
`CucumberBypassIngestTests.cs` (through `IngestCommand.Run`, so the same files compile on the release before R1), and the paint facts in
a new `tests/Kronikol.Tests.EndToEnd/IngestedAttachmentTests.cs` beside `IngestedBypassTests.cs`.

1. `Playwrights_own_artefacts_are_listed_once_under_the_runs_names` (pipeline, fixture): the failing scenario lists
   `screenshot`, `video`, `error-context` and `trace` once each, under those names, linked to the reporter's files.
   Red on 4.22.1: `screenshot.png`, `error-context.md`, `trace.zip` and `video.webm` as well.
2. `A_steps_attachment_is_listed_once_on_its_step`: `step-shot.png` on the step that attached it and not on the
   scenario. Red on 4.22.1: on both.
3. `Per_step_screenshots_with_one_name_and_the_same_bytes_land_one_on_each_step`: each of the three steps holds one
   `after-step.png`, linked to the reporter's three files in order. Red on 4.22.1 (six entries), and red on the
   prototype's first cut (measurements 7a).
4. `Two_artefacts_with_one_name_stay_where_each_was_attached` (unit): a hook's `trace` in the scenario's list and a
   step's own `trace` with other bytes. Each keeps its own bytes and place. Red under mutation M2 (measured: the swap).
5. `When_the_reporters_file_is_gone_the_messages_copy_is_linked` (pipeline, the fixture's tests file pointed at a
   folder that does not exist): every artefact the messages carried has a file in `attachments/`, under the reporter's
   name, where the messages put it; the only paths outside the report are the two `note` bodies. Red on 4.22.1: every
   entry from the tests file is a dead path, and the extension-named copies in the scenario's list are gone (26 dead
   links on the four-scenario probe). Each kept entry carries its own file's media type (a unit sibling crosses two
   same-named artefacts of different types: an image keeps `image/png`).
6. `A_name_pair_with_different_bytes_keeps_both_and_says_so` (unit): both kept, and one diagnostic counting them (Q4).
7. `The_signals_are_not_attachments_of_a_scenario_the_messages_own` and
   `The_signals_stay_attachments_of_a_scenario_the_messages_do_not_own`. Red on 4.22.1: the first lists `kronikol-bypass`.
8. `Text_bodies_only_the_tests_file_has_are_kept_in_order`: both `note` entries, in order. A guard, green on 4.22.1.
9. `A_link_is_never_paired`: a tests `http://` entry named like a messages attachment; both kept. A guard.
10. `With_hooks_kept_a_hooks_attachment_is_listed_once_on_its_hook_step` (`--include-hooks`): ten entries for ten
    artefacts, the hooks' and fixtures' on their hook steps. Red on 4.22.1: twenty (measured).
11. `A_background_steps_attachment_is_listed_once_on_the_background_step`. Red on 4.22.1: on the scenario too
    (measured).
12. `The_scenario_list_keeps_the_reporters_order_then_the_messages_own`. A guard against M10.
13. `A_retried_scenario_pairs_its_last_attempts_attachments`: the flaky scenario's four entries are its retry's files.
    A guard: green on 4.22.1 too, with and without `attempt` on the records (measured, `noattempt`).
14. `The_decoded_copies_a_reporters_file_replaced_are_deleted` (unit, `AttachmentsDirectory` set): after `Merge` the
    folder holds only the copies the result links. Red on 4.22.1.
15. `An_ingest_leaves_no_staging_folder` and `...unless_the_report_still_links_into_it`. The root is taken through an
    internal `IngestRequest.DecodedAttachmentsRoot`, so a fact never counts a temp folder that parallel facts share. Red
    on 4.22.1.
16. `The_ingest_cli_writes_one_file_per_artefact` (`IngestCommand.Run`, fixture): `attachments/` has one file per
    artefact, `Failures.md` lists each once, and `kronikol query failures` prints four different names for the failing
    scenario (F9). Red on 4.22.1.
17. Playwright, `IngestedAttachmentTests`: the ingested fixture's page paints, per scenario, the expected entries in
    `.scenario-attachments`, and per step row its own. Every `img.attachment-image` has loaded (`naturalWidth > 0`).
    Red on 4.22.1. Under the E2E rules in `CLAUDE.md` (`PollingInterval = 200`, `.First` where many match, no network
    mocking).
18. The existing `The_reporters_attachments_win_over_the_messages_copies_of_the_same_artefact` is rewritten with real
    files. Its paths do not exist, so under the new rule they would read as a missing reporter's file and keep the
    messages' copy, the opposite of its assertion.

The readers (4.7). 19 and 20 go in a new `tests/Kronikol.Tests/Reports/DigestStepAttachmentTests.cs` beside
`DigestCallScopeTests.cs` and the other digest facts, through `FailuresDigestGenerator` on a report model, so every
lane's shape is covered without an ingest. Both are red on 4.22.1 for any lane, since the digest never read a step.

19. `Failures_md_lists_the_attachments_made_inside_steps_with_their_step`: a failing scenario with one attachment of
    its own, one on the failing step and one on an earlier step lists all three, the step ones after the scenario's,
    each with its `sN/k`.
20. `Failures_jsonl_attachments_carry_their_step`: the same model; each step attachment's object has `step`, and the
    scenario's has none.
21. `Query_failures_prints_the_failing_steps_attachments_under_it` (`QueryCommand.Run`): the lines follow the failing
    step's location, and the scenario's lines are unchanged. Red on 4.22.1.
22. `With_hooks_kept_the_digest_still_lists_the_failure_artefacts` (pipeline, the fixture with `--include-hooks`):
    `Failures.md` lists `screenshot`, `trace`, `video` and `error-context` with their hook step. Red on the merge
    without 4.7 (measured: none listed).

Added while implementing (section 10):

23. `A_test_the_messages_do_not_own_that_never_ended_is_still_said_to_have_defaulted` (`CucumberIngestPipelineTests`):
    the merge built its result afresh and dropped the tests file's `DefaultedResultCount`, so a combined ingest never
    reported `ResultDefaulted` for a test outside the Gherkin run. Red on 4.22.2.
24. `A_reporters_file_keeps_the_reporters_media_type` (unit): the other half of 4.1 step 4, held by M22. A guard.

Fact 21 also reads `query failures --json`: each failing step's object carries the step's `attachments` (4.7's open
decision, taken in section 10).

### 6.3 Existing facts

On the prototype, 414 of 414 Cucumber and ingest facts pass with fact 18's original excluded (415 pass on 4.20.2). No
other fact pins today's merge. The golden fixture's "nothing else changes" guard (`CucumberBypassTests.cs` L530)
checks the synthesis alone, which R1 changes only by recording the side table. No digest fact asserts its attachment
lines today (`grep` over `tests/Kronikol.Tests` for the `Attachments:` heading and the `attachments` member), so 4.7
breaks none; 19 to 22 are the first.

Test hygiene (Q10): `CucumberFixtures.Build` and the facts that call the synthesis directly pass an
`AttachmentsDirectory` under a per-fact temp root and delete it, which removes the 13 folders a run still leaves.

### 6.4 Proofs and mutations

Copy the new facts onto the release before R1 (4.22.1 today) with the new helpers stubbed, and confirm each red fact fails on its own assertion.
Mutations to kill, each named with the fact that catches it:

| | Mutation | Caught by |
|---|---|---|
| M1 | compare the displayed name only (today's key) | 1, 3 |
| M2 | no byte check | 4 |
| M3 | a paired slot offered again | 3 |
| M4 | the reporter's copy always survives | 5 |
| M5 | the messages' copy always survives | 1 (the link), 14 |
| M6 | every pair to the scenario's list | 2, 3, 10 |
| M7 | the walk skips background, hook or sub-steps | 10, 11 |
| M8 | no signal filter / the filter on every scenario | 7, both facts |
| M9 | a link paired | 9 |
| M10 | the messages' unpaired entries first | 12 |
| M11 | replaced copies not deleted | 14 |
| M12 | the staging folder kept / deleted while linked | 15, both facts |
| M13 | an ordinal (case-sensitive) name comparison | none: both producers pass the name through, so the mutant is equivalent on real inputs; recorded with that reason |
| M14 | the digest reads the scenario's own list only (today's) | 19, 20, 22 |
| M15 | the merged entry takes the tests' media type when the messages' copy survives | 5's sibling |
| M16 | `query failures` prints the failing step's attachments in the scenario's capped lines instead | 21 |
| M17 | the kept-twice count never made | 6 |
| M18 | the merge drops the tests file's defaulted count (4.22.2's) | 23 |
| M19 | `query failures --json` leaves out the failing step's attachments | 21 |
| M20 | `Failures.jsonl` leaves out `step` | 20 |
| M21 | `Failures.md` leaves out the step's address | 19, 22 |
| M22 | a reporter's file takes the messages' media type | 24 |

M7 runs as three mutants: the background steps skipped (M7a), the hook steps skipped (M7b) and the sub-steps skipped
(M7c). M7c is equivalent on real inputs: the synthesis gives a Gherkin step no sub-steps, and the merger grafts its
assertion rows after it has merged the attachments. The walk keeps the sub-steps for a synthesis that one day writes
them. `r1/mutations.py` is the run, and `r1/mutations.txt` its result.

### 6.5 Checks

The core suite after the version bump. The full Playwright suite, serialized with peers ("starting Playwright" /
"Playwright done"). `release.slnf` in Release for every target. The harness re-run on the built tool, where
`measure.sh`'s prototype rows must equal the release's. The prototype's three `KRONIKOL_175_*` switches and its bytes-only
second pass are not part of R1.

## 7. Documentation and ledger

- **Wiki** `Integration-Cucumber-Messages.md`:
  - "Two sources, one report" (L185-197): attachments are matched with the messages' copies (same name, the producer's
    own or with the extension; same bytes when the reporter's file can be read); the reporter's file and name are kept
    when the file exists, else the messages' copy; the pair sits where the messages put it.
  - "Attachments" (L205-216): the staging folder is deleted after the report; the extension is for the label only (the
    media type decides how it renders); the two signals never appear, now true with `--tests` as well.
- **Wiki** `Integration-Playwright.md` L129: with `--cucumber-messages` the messages place a step's attachment on its
  Gherkin step. The sentence about the recipe's `step` index is corrected (Q11).
- **Wiki** `Ingesting-External-Captures.md` L334-337: one sentence that attachments are matched with the messages'
  copies, with a link to the section above.
- **XML docs and comments**: `CucumberFeatureMerger` remarks (L19-21) and the method's summary;
  `CucumberSynthesisOptions.AttachmentsDirectory` (L16-22): `kronikol ingest` deletes its own folder after the report;
  the comment at L1121-1122.
- **Wiki** `Generated-Reports.md`, "Failures.md and Failures.jsonl" (L432-500): the attachments are the scenario's and
  each step's, the step's with its address, and the `Failures.jsonl` members table gains `step` on attachment objects.
- **CHANGELOG:** the release's bump (Q14) and which part moved, with the behaviour changes in 4.5 listed.
- **README:** no change; it does not describe the Cucumber lane's attachments.
- **Skill reference**, both copies (`.claude/skills/kronikol-test-debugging/` and `templates/skills/kronikol-test-debugging/`):
  `references/commands.md` L140 says `failures` prints "any attachments"; it gains "the failing step's under it". The
  line in `src/Kronikol/Reports/agent-instructions.md` (L133, written into every report's agent files) is generic and
  stays.
- **Kronikol4J ledger:** one line, nothing to port (4.5).

## 8. Coordination

- Announced to the seven live sessions on 2026-10-14. kronikol-51 (#173), kronikol-71 (#174) and kronikol-22 (#78, #124)
  touch none of R1's files: `CucumberFeatureMerger.cs`, `CucumberFeatureSynthesizer.cs` (the writer and the result),
  `IngestPipeline.cs` (`Run` and L400-403), and the tests and wiki pages above.
- #161 (`query failures` caps attachments at four, with no note) and #148 (a missing attachment keeps its path with no
  diagnostic) have no plan. R1 changes what both see, with fewer duplicates in the first four and fewer dead links, and
  fixes neither.
- kronikol-51 is implementing #173 (2026-10-15): the three tap extensions and their tests, a doc summary in
  `src/Kronikol/Reports/DiagnosticEntry.cs` (where `DiagnosticKind` lives), and the tap and diagnostics wiki pages. R1's
  diagnostic (Q4) uses the existing `DiagnosticKind.Other`, so R1 needs no edit there.
- R1's `QueryCommand.Narrative.cs` change sits beside #161's cap (L136, L205) without changing it.
- `PLANS_STATUS.md`: this plan's row only. No `ROADMAP.md` row taken (Q12).

## 9. Questions for the owner

| # | Question | Recommendation |
|---|---|---|
| Q1 | The bump for the merge itself (4.1-4.4) | **Patch.** Nothing new to call; the behaviour changes are called out (CLAUDE.md's rule for a fix that changes observable behaviour). The release's bump is Q14's |
| Q2 | Where a pair goes: where the messages put it (a step), or the scenario as the reporter put it | **Where the messages put it, together with 4.7 (Q13).** The tests side cannot place it for these scenarios (3.2), and the wiki already promises the step row. Without 4.7 this choice takes step evidence out of the digest (F9) |
| Q3 | The pair's name: the reporter's (`trace`) or the displayed one (`trace.zip`) | **The reporter's.** It is the run's own name, 3.0.45's rule, and the linked file keeps its extension |
| Q4 | Same name, both files present, different bytes | **Keep both, and one diagnostic counting them.** Neither copy can be known to be the stale one, and the diagnostic explains two entries with one name |
| Q5 | Bytes as a key (the issue's second suggestion) | **No.** It misplaces identical screenshots (F5), and limited to unique bytes it changed nothing |
| Q6 | When the reporter's file is missing | **Link the messages' copy.** Today's rule leaves a dead link while the bytes are in hand (F3) |
| Q7 | The signals from the tests side: drop them for scenarios the messages own, for every scenario, or not at all | **For scenarios the messages own.** Elsewhere the tests contract says they are files |
| Q8 | One release, or the cleanup (4.4) separately | **One.** The merge deletes what it replaces, and the cleanup uses the same result |
| Q9 | The reader's memory (3.10, about seven times the messages file) | **A separate issue**, streaming each body to disk as it is read. Out of #175's scope |
| Q10 | The test suite's own staging folders (13 a run after R1) | **Fix in R1's tests**: no product change |
| Q11 | The recipe's `step` index (`Integration-Playwright.md` L129) | **Correct the sentence in R1's wiki edit.** Extending the recipe to compute the index from `TestStep.attachments` is a separate docs change, and it needs a recipe run first |
| Q12 | A `ROADMAP.md` row | None taken. Your call |
| Q13 | The readers (4.7): in R1, or not at all (keep pairs on the scenario instead, 4.6's last row) | **In R1.** Step placement is what 4.22.1's HTML already shows (as the duplicate) and what the wiki promises; the digest change also closes F9 for every other lane |
| Q14 | `Failures.jsonl`'s new `step` member, and with it the release's bump | **Add it, and call the release a minor.** A script can then tell a step's screenshot from the scenario's. Dropping the member keeps R1 a patch, and `Failures.md` still shows the step |
| Q15 | Hook artefacts under `--include-hooks` in `query failures` (4.7), and the digest's length for long scenarios | **Leave both as 4.7 has them.** They are in `Failures.md` and `query steps`. A hook marker on `ScenarioStep` would be public surface for one verb, and a cap belongs to #161 |

## 10. Log

- 2026-10-14: written at 4.20.2 (`3240c983`) in worktree `C:/Code/Kronikol-175`. The probe reproduced the issue's
  list in the issue's order, then F2 to F7. A prototype measured the rule in section 4 against the issue's two
  suggestions and two other rules (`results/matrix.txt`). Its first cut had the defect in 3.5, found by the probe's
  identical per-step screenshots and fixed before the measurements quoted here. The issue's sizes measured with a 58 MB
  `trace` and a 7.5 MB `video` attached inside a step. Announced to kronikol-51, -71, -44, -88, -17, -4b and -50;
  kronikol-71 (#174) and kronikol-22 (#78, #124) announced their own scopes, neither overlapping.
- 2026-10-15, the sweep (the owner asked to check the plan for completeness and accuracy and to dive deep where a dive
  might change it). The worktree moved to `85f849f7` (4.22.1): 14 commits, none under `src/Kronikol/Ingestion/`; line
  numbers re-pinned. The prototype applies unchanged to 4.22.1 and the matrix gives the same counts
  (`results/matrix-4.22.1.txt`). What the dives found:
  - **F9 (changes the plan):** the digest and `query failures` read only a scenario's own attachments. The merge alone
    took the failing step's own screenshot out of `Failures.md` (14 entries to 6), and under `--include-hooks` left it
    empty. 4.7 added to R1, with Q13 to Q15, facts 19 to 22 and M14, M16; the bump question moved to Q14.
  - **Measured what was read from the code:** a `Background:` step's attachment (on the background step; twice on
    4.22.1) and `--include-hooks` (20 entries for 10 on 4.22.1, 10 on the prototype). Facts 10 and 11 now say so.
  - **Design refinement:** a merged entry takes the media type of the file that survives (4.1 step 4, M15). Without it,
    two same-named artefacts that cross when the reporter's files are gone could label an image as a zip.
  - **Confirmed:** playwright-bdd 8.5.1 names and embeds attachments as 9.2.0 does, so the rule holds for 8.x;
    `mediaType` is required in @cucumber/messages, `fileName` and `url` optional; the HTML draws step attachments on
    their rows with the steps section open by default; `DiagnosticKind` has `Other` for Q4, so the plan needs no edit to
    the file #173's release changes; no existing digest fact asserts attachment lines; the skill's `commands.md` has two
    copies.
- 2026-10-15 and 16, R1 implemented (the owner: "Can you implement the plan in full", so every question in section 9 as
  recommended), on 4.22.2 (`9b7886d9`, #173's R0, which touches none of R1's files), and rebased before the release
  onto 4.22.3 (`609ba8f3`, #165's audit), whose edits to the digest (the clusters paragraph) and to the skill's
  `commands.md` (another section) leave R1's lines as they were. What differs from the plan, and
  what the implementation found:
  - **A bug found on the way (fact 23):** `CucumberFeatureMerger.Merge` built its result afresh and dropped the tests
    file's `DefaultedResultCount`, so a combined ingest never reported `ResultDefaulted` for a test outside the Gherkin
    run that started and never ended, and reported it `Passed`. Fixed in R1: the merged result counts the scenarios
    whose result defaulted.
  - **The fixture** is a fresh run of the probe, all but `@big`, with `PROBE_SMALL=1` (a 48x27 page and a trace
    without screenshots, snapshots or sources): 338 KB where the default page made 1.2 MB, with every artefact still
    made under its name and media type. `cut_fixture.py` turns this machine's paths into `C:\fixture` inside each
    `trace.zip` as well, and re-encodes the messages' copy from the same new bytes. The fixture folder is `-text` in
    `.gitattributes`: a checkout that converted the line endings of its text artefacts would fail every byte check.
  - **Where the facts are:** the pipeline and CLI facts on the fixture are a class of their own,
    `CucumberAttachmentIngestTests`, beside `CucumberBypassIngestTests.cs`, rather than more of
    `CucumberIngestPipelineTests.cs`; fact 21 is in `DigestStepAttachmentTests` and builds its report through the tests
    file's `step` index, the lane that needs no Cucumber.
  - **4.7's open decision, `query failures --json`:** each failing step's object carries `attachments` (`{ name, path }`),
    as the text prints them under the step; the scenario's `attachments` stay its first four. `Failures.jsonl`'s
    `formatVersion` stays 1: `step` is a member added, as `calls[].error` was in 4.3.0.
  - **Fact 16's numbers:** the background scenario fails with the failing scenario's message, so `Failures.md` works
    through one failure of their cluster and lists its 10 attachments, each once; `attachments/` holds 35 files.
  - **Q10:** `CucumberFixtures.Decoding` gives each fact that synthesises messages itself a folder under one root per
    test process, deleted when the process exits, as `BareOrigin`'s git home is. The Cucumber and ingest facts leave
    no staging folder, where they left 23 a run on 4.22.2 (13 with the merge alone).
  - **Red proof** (`r1/red-4.22.2.txt`): the new and changed facts copied onto 4.22.2 with the
    `IngestRequest.DecodedAttachmentsRoot` seam alone (`r1/red-stubs.diff`): 19 fail, each on its own assertion, the
    Playwright fact among them, and the guards pass (8, 9, 12, 13, 15b, 18, 7b).
  - **Mutations** (`r1/mutations.txt`): 26 mutants, section 6.4's sixteen with M7 run as three, M8 and M12 as two
    each, and M17 to M22: 24 killed. M7c and M13 survive, equivalent on real inputs as section 6.4 says. The code the
    mutants ran on differs from the release's only in a constant's visibility, a doc comment and 4.22.3's clusters
    paragraph.
  - **Version:** R1 was built and measured as 4.23.0 and released as 4.24.0, since #173's R1 took 4.23.0 first; the
    harness's `results/*-4.24.0.txt` say so in their headers.
  - **Checks:** After the version bump the core suite passes 7,645 with 2 skipped, the full Playwright suite passes 1,041 with 28 skipped when run alone, and `release.slnf` builds in Release for every target.
