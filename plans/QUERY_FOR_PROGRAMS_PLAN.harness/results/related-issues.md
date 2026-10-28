# Open issues that break the same properties (read 2026-10-28)

Each row is an open issue on `lemonlion/Kronikol` where a program cannot get something from `kronikol query` that the
report holds. The property column refers to section 4 of the plan.

| Issue | Opened | What a program cannot do | Property | Roadmap |
|---|---|---|---|---|
| #184 | 2026-10-20 | Name a scenario by the id its producer gave it; count a scenario's diagrams or fetch them together; tell "not there" from a usage error | identity, enumeration, outcomes | not placed |
| #183 | 2026-10-20 | Read every diagnostic: `summary` keeps the first message of each kind | completeness | not placed |
| #182 | 2026-10-20 | Know that `grep --count` left headers and notes out: it prints a bare number | completeness | not placed |
| #118 | 2026-09-30 | Read `grep`'s hits as JSON: the verb refuses `--json` | completeness | not placed |
| #76 | 2026-09-18 | Read a scenario's `sourceFile` and `sourceLine` from `scenarios --json` (the rest of the issue was corrected by its author) | completeness | 7.1, "shippable now, alone" |
| #146 | 2026-10-08 | Read when a call, a step or an assertion happened | completeness | not placed |
| #201 | 2026-10-26 | List or count the background calls (made outside every scenario) | enumeration | not placed |
| #206 | 2026-10-27 | Read what a call's activity diagram holds | enumeration | not placed |
| #151 | 2026-10-08 | Ask a verb for its help: `<verb> --help` is a usage error at exit 2 | outcomes | not placed |
| #114 | 2026-09-30 | Parse `--json` from a first `query.cs` run under central package management: NuGet's NU1507 warning lands in stdout | machine channel | not placed |
| #77 | 2026-09-18 | Select a scenario's test for a re-run: no field holds the runner's own name for it (`TestCaseId`) | identity (a different key: stable across runs, unlike `id`) | 7.3 |
