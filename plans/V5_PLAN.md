# V5 Plan: the report data file and Option C (5.0.0)

**About the name.** Until 2026-10-18 `V5_PLAN.md` was the plan for Mermaid CI summaries and removing Server and Local
rendering. That plan is now [`V6_PLAN.md`](V6_PLAN.md) (`ROADMAP.md` D39). A plan, doc comment or wiki page written
before that date that names `V5_PLAN.md`, or puts the Mermaid CI summaries, the Server, Local, IKVM and C4 removal,
the options nothing reads, the toggle-default unification or the maybes "in v5" or "at 5.0.0", means v6. Toolbar
Option C is the exception: it stays at 5.0.0.

This file is the index of 5.0.0, which the owner set on 2026-10-18 as three pieces of work released together from the
branch `v5` (`ROADMAP.md` D39, stage 1e), and made four on 2026-10-19 with one list of tags (D40):

| Piece | Plan | Where it stands |
|---|---|---|
| The report data file: `TestRunReport.krkl`, a compressed file only Kronikol's tool reads, by default; no schema written beside it by default; net8.0 and net9.0 dropped; collapsed sub-steps drawn on first expand | [`REPORT_FORMAT_PLAN.md`](REPORT_FORMAT_PLAN.md) | Draft. Its 4.x releases (the `sizes` verb, the reader, the writer behind an option, the net11.0 builds) ship from `main` first |
| Option C: the report redesign, emitted in its final shape, with `--kron-*` and `--dg-*` tokens as the theming surface | `OPTION_C_PLAN.md`, on the local branch `v5` (`5594a857`) | In progress; its S0 was done on 2026-10-18 |
| The dependency filter combines the chosen dependencies with OR by default | commit `9452ddb5`, on `v5` | Done on the branch |
| Tags: a feature's labels and a scenario's labels and categories become one list, every adapter reading its framework's own tags, one Tags row in the report, `tags` in every output | [`TAGS_PLAN.md`](TAGS_PLAN.md) | Built on the branch `v5-tags`, which `v5` fast-forwards to: T2, T1 and T3 done; T4 (the wiki, the Kronikol4J port) under way |

The version moves to 5.0.0 when the branch is released, not before. Until then the branch's changelog collects its
entries under `[Unreleased]`, and the branch stays local until the owner decides (`OPTION_C_PLAN.md` Q8). A wiki page,
"Migrating to v5", goes with the release.
