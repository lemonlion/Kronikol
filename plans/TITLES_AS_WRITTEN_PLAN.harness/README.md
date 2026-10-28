# TITLES_AS_WRITTEN_PLAN harness

Measurements behind `plans/TITLES_AS_WRITTEN_PLAN.md` (#139). Everything here runs against the worktree's own
`src/Kronikol` (5.12.0, `1ee6373a`) through `#:project`, so a probe sees exactly the code it describes. Windows 11,
.NET SDK 10.0.300, Python 3.14.

| File | What it is |
|---|---|
| `harvest.py` | Reads C# sources with regular expressions (nothing is built or run) and writes one line per test method: corpus, attribute, class, method, the display name the test wrote (empty when none). |
| `probe.cs` | A file-based app. `examples` prints what 5.12.0's formatters make of hand-picked names; `corpus` counts the losses over `harvest.py`'s output and lists every changed title; `words` runs the word-list prototype. It takes the internal `FormatXunitDisplayName` and `TitleizeAsOf590` through `AssemblyName=Kronikol.Tests`, the test project's `InternalsVisibleTo`. |
| `examples.txt` | `probe.cs examples` at 5.12.0, with candidates B and C beside 5.12.0's output. |
| `corpus.txt` | `probe.cs corpus` at 5.12.0: the counts quoted in the plan's sections 1 and 3. |
| `words.txt` | `probe.cs words`: the word list prototype on the issue's names and BreakfastProvider's. |
| `rules.cs` | A standalone file-based app with no project reference: verbatim copies of 5.12.0's `FormatScenarioDisplayName`/`Humanize`, of `probe.cs`'s `CandidateC` and of 5.12.0's `Titleize`, with each change the plan's section 4.1 weighs behind a switch. `verify` checks the copies against `names.tsv` and `details.tsv` row for row; `shapes` prints the edge-case table; `variants` writes every title under each variant (`REC` is the rule section 4.1 recommends; `RECSEG` adds Q9's, `ALL` Q10's too). |
| `analyze.py`, `cchanges.py`, `rec-count.py` | Shape counts and each variant's cost (`analyze.txt`), the breakdown of candidate C's changes, and the per-corpus totals of `C`, `REC` and `RECSEG` (`rules-counts.txt`), all read from `variants.tsv`. |
| `hzprobe.cs` | Runs a `Humanizer.dll` loaded by path (2.14.1 and 3.0.10 from the NuGet cache) over names given as arguments, for section 4.1's comparison. |
| `names.tsv`, `details.tsv`, `variants.tsv`, `dump.tsv` | The harvested names (13,342), every title a candidate changes, and `rules.cs`'s outputs. Regenerated in seconds, so not committed (`.gitignore`). |

## Rerun

```bash
cd plans/TITLES_AS_WRITTEN_PLAN.harness
python -I harvest.py names.tsv breakfastprovider=C:/Code/BreakfastProvider \
    kronikol-tests=../../tests kronikol-examples=../../examples
dotnet run --file probe.cs -- examples > examples.txt
dotnet run --file probe.cs -- corpus names.tsv details.tsv > corpus.txt
dotnet run --file probe.cs -- words > words.txt
dotnet run --file rules.cs -- verify names.tsv details.tsv
dotnet run --file rules.cs -- variants names.tsv variants.tsv
python -I analyze.py variants.tsv > analyze.txt
python -I rec-count.py variants.tsv > rules-counts.txt
```

BreakfastProvider was read at its checkout of 2026-10-27 (`C:/Code/BreakfastProvider`, on Kronikol 5.4.1). The first
build of `probe.cs` compiles `src/Kronikol` (about two minutes); later runs take seconds.

## The three rules the probe compares

- **5.12.0**: `ScenarioTitleResolver.Humanize`, splitting at a lower-case letter before a capital and inside a run of
  capitals, then upper-casing the first letter and lower-casing everything else.
- **Candidate B**: the feature formatter's own reader (`StringCasing.Humanize` with 5.9.1's pattern) in sentence case.
  Rejected: it keeps a lone capital after a space (`should return A fresh batch`, 63 of BreakfastProvider's 67
  changes) and splits digits from letters before them (`p 95`, `utf 8`, `x unit 3`).
- **Candidate C**: 5.12.0's splitter plus a break between digits and a capital after them
  (`404When`, `C4False`); a word in capitals (its letters all upper case, two or more of them or a digit among them)
  and the pronoun `I` keep their case; every other word is lower-cased; the first word gets a capital.
- **The recommended rule** (`rules.cs`'s `REC`, the plan's section 4.1): candidate C, but a plural acronym stays whole
  (`URLs`), a digit splits from a capital only where a word starts (`E2E` stays whole), any one-letter capital but
  `A` keeps its case, and a name with no lower-case letter reads as a sentence. It changes 5 of BreakfastProvider's
  titles and 468 of Kronikol's tests' (`rules-counts.txt`).

"A word in capitals" in the counts is read from the identifier split as 5.12.0 splits it. "Pascal acronym" is a word
from a fixed list (`Api`, `Http`, `Json`, `Sql`, `Url` and 41 more, `probe.cs`), so its count is a floor: brand and
product names (`AcmePay`, `ClickHouse`) are not in it.
