# Wiki edits for 5.0.0 tags (TAGS_PLAN T4)

Base: the live wiki at `450844e` (`WIKI_BASE.txt`), copied untouched to `wiki-base/`; edits in `wiki/`; diff in
`wiki-tags.patch` (`diff -ruN wiki-base wiki`, 29 files, applies cleanly to the base with `patch -p1`). The live wiki
was only read. `How-To-Guides.md` is CRLF in the live working tree but LF in its commit (`git ls-files --eol`:
i/lf, w/crlf); the copy and the patch are LF, as committed, so `patch -p1` applies the patch to a checkout of
`450844e` (dry run: 29 files, exit 0) without rewriting that file's line endings.

## Pages

- **Tags-and-Attributes.md**: intro rewritten; Happy Path table gains TUnit, NUnit `[TestCase]` (5.0.0 fix), ReqNRoll
  `Examples:` block, LightBDD row without the em-dash, plus a paragraph on the happy-path names (`Happy Path` new) and
  that a framework category or label with such a name marks the happy path. The Category and Labels sections are one
  `## Tags` section: a table per framework (scenario tags, feature tags) for xUnit v3, xUnit v2, NUnit 4, MSTest, TUnit,
  BDDfy, LightBDD, ReqNRoll, Cucumber Messages and tests NDJSON; notes on the `category:` prefix, markers, ASCII case
  fold, feature tags drawn once, rule tags, LightBDD class-level `[ScenarioCategory]`, xUnit v2 capture paths, retries;
  an "In the report" subsection (Tags row, chips, has/has not/off, ANY/ALL and `TagFilterMode`, 30 + "N more",
  `@` search, `data-tags`, link params); a "Before 5.0.0" paragraph linking the migration section.
- **Migrating-to-v5-Tags.md** (NEW, no "Migrating to v5" page exists): written as an `##` section to paste into a
  future Migrating-to-v5 page: what-changed table, removed API, data files and tools (4.x files in 5.x, 5.0.0 files in
  4.x, history, committed Specifications files), report classes and links, what reports show differently (F9, F10,
  F14, F4/F5/F8, F23, F7). Linked only from Tags-and-Attributes (one link to move if it is merged elsewhere).
- **Search-Syntax.md**: basic text search lists tags (scenario's and feature's); `@tag` section rewritten (effective
  tags, Happy Path, `@"two words"` in advanced and legacy queries, a "Before 5.0.0" line); example row `@"Happy Path"`;
  "What Gets Searched" item corrected (tags are searched as text too, which was already true in 4.x).
- **Generated-Reports.md**: Specifications.yml/.xml/.json `Tags`/`tags` paragraph; Specifications.md `Tags:` line; JSON
  example keys to `tags`; Key Fields gains a `tags` row and `attempt` mentions the retry badge; "Category filter"
  bullet replaced by "Tags filter"; toggle-defaults bullet says tag filter modes; Deep links gains `tags=`/`tagx=`/
  `tagmode=` and the 4.x `cats=`/`catmode=`/`__uncategorized__` reading; CTRF `tags` = effective tags, no
  `extra.categories`, author's `retry 2` kept, 4.x generated retry labels dropped on read.
- **Report-Configuration.md**: `CategoryFilterMode` row is `TagFilterMode`; URL-hash grammar says `tagmode` and reads
  a 4.x `catmode`.
- **Querying-Reports.md**: `--label` to `--tag` (with `--label` as the old name) in the flag table and the per-verb
  paragraph; `scenarios --json` items carry `tags`.
- **Marking-New-and-Changed-Tests.md**: tags instead of categories throughout ("Why categories" is "Why tags"); the
  NDJSON and Cucumber recipes add plain `pr-new`/`@pr-new`; the .NET recipes write `.tags`; `--tag pr-` trick; a
  "Before 5.0.0" note that `category:` still works; the checked-with line now says it was the 4.x form that was checked.
- **API-Reference.md**: `Feature.Tags`, `Scenario.Tags`; new `ScenarioTags` row (Classify, Classified, CategoryPrefix);
  `ReqNRollConstants.CategoryTagPrefix` gone.
- **Integration-Cucumber-Messages.md**: mapping rows to `Feature.Tags`/`Scenario.Tags`; attempt row mentions the badge;
  tag conventions table and paragraph rewritten (rule tags reach their scenarios, happy path from own and Examples tags).
- **Ingesting-External-Captures.md**: `tags` row of the tests format rewritten; Retries paragraph uses the badge.
- **Ingesting-From-Jest.md**: "`retry 1` label" is a badge (line 49).
- **Integration-ReqNRoll-TUnit.md, -xUnit2.md**: the glued `||` row split into rows, a blank line before `---`;
  **all three ReqNRoll pages**: rows for any other tag and `@category:`; "Category Tags" section is "Tags" with a
  feature/rule/scenario example.
- **Integration-TUnit.md, Integration-BDDfy-xUnit3.md, Integration-Playwright.md, Framework-Integration-Guides.md,
  How-To-Guides.md, Home.md, _Sidebar.md**: as the plan lists (TUnit `[Category]` and `[HappyPath]` on a class; BDDfy
  tag table rows and xUnit traits; playwright-bdd Gherkin tags; a Tags bullet per framework; "dependency or tag";
  a Home description; a sidebar `Tags` sub-link).
- **Beyond the plan's list**: one tags line each on Integration-xUnit3.md, Integration-xUnit2.md, Integration-NUnit.md,
  Integration-MSTest.md and the three Integration-LightBDD-*.md pages (those adapters read tags for the first time),
  and a **Tags** bullet in Merging-Parallel-Reports.md "How merging works" (intersection, 4.x shards).

## Link checker

`KRONIKOL_WIKI=<copy> PYTHONUTF8=1 python C:/Code/Kronikol-tags/tools/wiki-links/wikilinks.py check`:
base `counts: {'ok': 231, 'page-ok': 675}`, edited copy `counts: {'ok': 260, 'page-ok': 676}`, nothing else printed.

## Questions

1. **The happy-path control.** Tags-and-Attributes, Integration-ReqNRoll-xUnit3 and Generated-Reports still say
   "Happy Paths Only" toggle; on v5 it is the "Happy paths" Show / Only / Hide segments (Option C). Left for the
   Option C wiki pass.
2. **Generated-Reports JSON example** now shows `tags` under `"formatVersion": 1, "kronikolVersion": "3.1.0"`, and the
   Root fields text still describes versions 1 and 2. Move the example to 5.0.0 and 3 when REPORT_FORMAT_PLAN's wiki
   edits land?
3. **Marking-New-and-Changed-Tests recipes** are changed but not re-run on Kronikol.Tool 5.0.0; the last line now says
   the 4.x form was checked. Re-run and restate before publishing.
4. **`--tag` does not exist yet** in the `Kronikol-tags` worktree (VerbTable and QueryOptions have `--label` only;
   T3 adds it). The pages follow the brief.
5. **Code, not wiki:** `IngestPipeline.cs:485` and `:495` still print "with a retry label for each earlier one",
   which no longer holds after Q6. Ingesting-From-Jest.md:428 quotes that output and was left as quoted.
6. **`@category:happy-path`**: Tags-and-Attributes says it is the tag `happy-path`, not the happy path, as
   `ScenarioTags`' doc comment and the T1 commit message say. The brief's "a category or label named like a
   happy-path marker marks the happy path" is written for framework categories and labels only. Confirm.
7. **ScenarioTags** is public with `Comparer`, `Merge`, `Shared`, `Normalise` and `EndpointPrefix` besides `Classify`;
   the new API-Reference row names only `Classify`, `Classified` and `CategoryPrefix`. Document the rest, or are they
   not meant as public API?
8. **Where the migration section lives**: Home.md's "Upgrading from 3.x? Migrating to v4" line and the sidebar's
   "Migrating to v4" entry were not touched.

## Answers (2026-10-20)

1. Left for the Option C session's wiki pass, as agreed with it: the three-way Happy paths control is OPTION_C_PLAN S5/S6's.
2. Left for REPORT_FORMAT_PLAN's wiki edits: the example's version fields are that plan's (the JSON stays at formatVersion
   1, or 2 with `$z`; only the `.krkl` declares 3).
3. Checked on a 5.0.0 build (`recipes/check.py`, `recipes/check_c.py`): each recipe marks exactly the tests it should;
   jq is not installed here, so the edits were made by a script of the same logic, and the page says so.
4. `--tag` exists since T3 (506b46f4); the pages match its help text.
5. Fixed in T1 (8da2c182); Ingesting-From-Jest.md quotes the new text.
6. Confirmed: `@category:happy-path` is the tag `happy-path`.
7. API-Reference's row documents Comparer, Merge, Shared, Normalise and EndpointPrefix.
8. A page of its own, Migrating-to-v5-Tags.md, linked from Tags-and-Attributes; it merges into "Migrating to v5" when
   that page is written for the release.
