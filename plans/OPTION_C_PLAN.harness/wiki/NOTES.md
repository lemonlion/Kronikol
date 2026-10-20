# The 5.0.0 wiki, as one series

`series/` holds the wiki's 5.0.0 pages as ten patches on the live wiki's `master` at `7295fb3` (its head on
2026-10-20). Nothing in it is pushed: the wiki describes 5.0.0 only once 5.0.0 ships, and pushing is the owner's call.

| Patch | From | What |
|---|---|---|
| `0001` | kronikol-89, `plans/TAGS_PLAN.harness/wiki/wiki-tags-on-b5c9a27.patch` (the same tree) | Labels and categories are tags (TAGS_PLAN T4) |
| `0002` to `0007` | kronikol-39's local wiki clone, branch `v5-on-tags`, `145c8b1..ddef9ff` | The report data file (REPORT_FORMAT_PLAN): `TestRunReport.krkl`, the schema when asked, sub-steps drawn on first open |
| `0008` | this plan, S14 | The report's 5.0.0 pages: Generated Reports, Report Configuration, Report Themes (new), Migrating to v5 (new, with kronikol-39's data file fragment), Migrating to v5: the report (new), and ten more pages |
| `0009` | kronikol-89, its fix of the tags page (2026-10-20) | Tags and Attributes: where `endpoint:/x` sets a feature's endpoint, and that NUnit, MSTest, xUnit and TUnit take it from `[Endpoint]` |
| `0010` | `WASM_ENGINE_PLAN.md` | The engine 5.0.0 draws with: PlantUML Browser Rendering (a new section, The Wasm engine), Large Response and Diagram Handling, Diagnostics and Debugging, Report Configuration |

## Applying it

```bash
git clone https://github.com/lemonlion/Kronikol.wiki.git && cd Kronikol.wiki
git am -3 <repo>/plans/OPTION_C_PLAN.harness/wiki/series/*.patch
```

Applied to `7295fb3` in that order, the series gives exactly the tree it was exported from (checked on 2026-10-20, with
all ten: tree `94a3e5ac`).
`7295fb3` is `b5c9a27` and one line, a 4.x fix the link check found while the series was assembled: Merging Parallel
Reports linked Querying Reports' compressed reports section by an anchor ending `4290`, from before the section
shipped as 4.30.0. kronikol-39 put it on the live wiki the same day, and the series was exported again on it.
Where `master` has moved, `git am -3` merges each patch; a conflict is resolved for the page's newer text, keeping
what the patch adds. The commit subjects say "LOCAL ONLY"; reword them at publication if the history should not.

## Before publishing

- The release's own wiki edits (version numbers, the changelog's dates) go on top.
- `Migrating to v5` merges the three parts' tables into one, with one snippet that keeps the 4.x behaviour; a part
  that changes later changes its rows there too.
- Every `[[...]]` and Markdown link and its anchor resolved on 2026-10-20, on every page of the series' tree. The
  check is `python -I <repo>/plans/OPTION_C_PLAN.harness/wiki/linkcheck.py *.md`, run in the wiki's folder; it
  reports a few false hits, all in inline code or at an explicit `<a name>` anchor.
