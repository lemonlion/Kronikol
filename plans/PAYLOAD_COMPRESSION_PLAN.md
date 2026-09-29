# Payload compression plan: #85, `V4_PLAN.md` R2

**Written:** 2026-09-29, at 3.34.2 (R1, the relaxed encoder), as `V4_PLAN.md` §3.2 requires before #85 is built.
**Status: executed as 3.35.0 the same day** (§6), green-lit with `V4_PLAN.md` (the owner asked for that plan in full
on 2026-09-29); D7 (in place) is taken. Roadmap row 1c.2. Evidence labels as elsewhere: **RUN**, **READ** (`file:line`), **INFERRED**.

## 1. What ships (a minor: a new option, default off)

- **`ReportConfigurationOptions.CompressTestRunReportPayloads`** (bool, default `false`; `true` at 4.0.0, `V4_PLAN.md`
  R8). When it is on and the data format is JSON, a large payload in `TestRunReport.json` is written as a wrapper
  object in place of its string. The XML and YAML files are not affected (`V4_PLAN.md` §6).
- **Payloads:** each entry of a scenario's `diagrams`, and each interaction's `content` (in `httpInteractions` and
  `background`). Not `internalFlowSegments` or `wholeTestFlow` in the mergeable file (§3.2 of the V4 plan: R3 and R5
  reshape the first, and merge would have to inflate it before `WrapSegmentData`).
- **The rule:** a payload of 512 characters or more is stored compressed, unless that would not make it smaller.
  **RUN** (`V4_PLAN.harness/threshold.py`, level 6): on BreakfastProvider's xUnit lane (R1's report) the saving is
  1,517,571 bytes at 512 against a best of 1,519,192 at 384; on its Docker lane (3.27.1's data) 9,605,262 against
  9,606,967; flat from 256 to 1,024 on both. Six payloads per lane are larger compressed at 512, which the "smaller"
  test keeps plain. Below 512 the gain is a few kilobytes and each small body would stop being readable text in
  the file. Level 6 (.NET's `CompressionLevel.Optimal`) beat level 9 on both lanes.
- **The wrapper:** `{"$h": "b:xxxxxxxx", "$n": <length>, "$z": "<base64 of the gzip of the UTF-8 text>"}`.
  `$h` is the `b:` address the scanner computes today for the plain string (READ `ReportScanner.cs:798-803`), so an
  address does not move when the option does. `$n` is the length the index records for a plain body (UTF-16 code
  units, READ `:653-667`). `$h` and `$n` come first, so `head` and a streaming reader see them before the base64.
  Keys start with `$` because an older scanner reads a plain key inside an interaction as the interaction's own
  field (READ `:555-570`).
- **`formatVersion` 2** when the file holds at least one wrapper; 1 otherwise, so a file the option left unchanged
  stays readable by every tool that reads it today. An older `kronikol query` refuses 2 with "Upgrade
  Kronikol.Tool" (READ `ReportGate.cs:42-48`) instead of answering "body: none", and an older `kronikol merge`
  refuses it (READ `MergeableReportReader.cs:57-65`) instead of crashing on `GetString()` (`:89`).
  `mergeableFormatVersion` does not move.

## 2. Writers

- `GenerateTestRunReportJson` and `GenerateMergeableReportJson` take the flag; one helper, `ReportPayloads`, turns
  a string into the string or the wrapper and counts wrappers, and both write `formatVersion` from the count.
- The pipeline passes `options.CompressTestRunReportPayloads` on both branches (READ `ReportGenerator.cs:486-501`).
  The public `GenerateTestRunReportData` keeps its signature (an optional parameter added to a public method is a
  binary break); the internal overload takes the flag.
- **`kronikol merge`** writes wrappers when any input shard held one: a reader that cannot read the compressed
  shard could not read the run anyway, and a merge of 3.x shards stays readable by 3.x tools. No flag.
- **`kronikol ingest`** takes `--payloads plain|compressed` (default: the library's, plain until 4.0.0), beside
  `--note-format`: ingest has a curated flag set, and without it an ingested report could not opt in.

## 3. Readers

- **The scanner** (`ReportScanner`): a `content` or `diagrams` entry that is an object records the object's byte
  range as the payload's slice, `$h` as its hash and `$n` as its length, and never inflates. A wrapper without
  `$h` or `$n` (written by something else) is inflated once at scan time to compute them.
- **`PayloadReader.Read`**: a slice that starts with `{` is a wrapper; it reads `$z`, decodes base64 and inflates.
  Every verb that reads a payload goes through it (`BodyCache`, `grep`, `note`, `diagram`, `values`, `diff`), so
  none needs its own change.
- **`MergeableReportReader`**: accepts `formatVersion` 1 and 2, and inflates a wrapper wherever it reads a diagram
  or a `content`.
- **`query.py`** (both copies): inflates a wrapper with the standard library (`base64`, `gzip`). Copies already
  in consumers' repositories cannot be reached; they meet the form only where a user opts in, and at 4.0.0 the
  migration page tells everyone to refresh them (`V4_PLAN.md` §3.4).
- **`query.cs`** runs the engine that wrote the report, so it needs nothing.
- **The schema:** `$defs.compressedPayload` (the three keys required, no others), `content` and each `diagrams`
  item as a string or that object, `formatVersion` described with both values. A union, not an `if`/`then` on the
  version: a file under the rule holds both forms by design.
- **The run's own `CLAUDE.md`/`AGENTS.md` and the skill** say what a wrapper is, where they describe the file.

## 4. Proof (from `V4_PLAN.md` §4, R2)

1. Option off: the file is byte-identical to R1's pin (`ReportJsonEncodingTests.The_file_matches_its_pin`,
   unchanged), and a second pin holds the option-on file of the same fixture (with the `$z` strings decoded, since
   gzip output is not a contract).
2. Every verb that reads payload text answers the same on a compressed and an uncompressed copy of one run: `http
   --body`, `http --path`, `body`, `diff`, `values`, `interactions --where`, `grep` (plain, `--regex`,
   `--number`, `--in bodies`), `note`, `diagram`, and the SQL hint in `failures`.
3. `b:` addresses are the same on both copies, and `$h` equals the address of the inflated text.
4. The rule: a 511-character payload stays a string, a 512-character one is wrapped, a 512-character one that
   compresses badly stays a string; `formatVersion` is 1 with no wrapper and 2 with one.
5. An unknown `formatVersion` (3) is refused by query and merge; 2 is accepted.
6. Merge: a compressed shard and a plain one merge; the output is compressed; a merge of plain shards is plain and
   byte-identical to before.
7. The schema validator passes on both forms; a wrapper missing `$z` fails it.
8. `query.py` agrees with the tool on a compressed report (`FallbackScriptTests`).
9. `kronikol ingest --payloads compressed` writes wrappers; the default does not.
10. **Measured:** BreakfastProvider's xUnit lane with the option on, against R1's run (`V4_PLAN.md` R8's gate).

## 5. Not in this plan

- The default flip (4.0.0, `V4_PLAN.md` R8), and the migration page.
- `internalFlowSegments`, `wholeTestFlow`, headers, the XML and YAML formats, whole-file gzip (#85's option 2).
- A cached index.

## 6. Execution log

- **3.35.0 (2026-09-29), as planned.** Measured on BreakfastProvider's xUnit lane with a local build and the option on: 4,344,734 bytes against R1's 5,855,252 (74%) and 3.34.0's 6,461,200 (67%), 203 of 203 passing, 325 payloads compressed, `formatVersion` 2; `summary`, `grep` (7 and 10 hits) and `note` answered as on R1's plain report, and the lane took as long (2 min 50 s both). Each fact of §4 was red before its code (the reader's theory, 28 of 30, before the scanner and the gate knew the wrapper), and twelve guards broken one at a time were each caught (`V4_PLAN.harness/mutate_r2.py`): the threshold, the "smaller" test, the version, the scanner's address and length, the inflate, the gate, the merge reader and writer, `query.py`, the ingest flag, the schema's required keys.
- **Found on the way.** (1) `Kronikol.Query` is compiled for .NET 10 only (`Kronikol.csproj` removes it for the others), so the writer, built for every framework, could not call the scanner's address function: the Release build of every framework failed on net8.0 and net9.0 while every net10.0 test passed. The function is `ReportPayloads.Address` now, and the scanner calls it. (2) The schema facts collected errors from the flat list of a JsonSchema.Net evaluation, which holds the branch of a `oneOf` an instance did not take, so the first union in the schema read as a violation under every payload; they read the hierarchical result below failed nodes now, and `SchemaClosedContractTests` still finds every undeclared key. (3) The run's `CLAUDE.md` and `AGENTS.md` and the skill describe no field of the file, so §3's last bullet needed nothing. (4) `query.py`'s listing names a body's address only when the body is too long to print whole, so its fact uses a long one.
