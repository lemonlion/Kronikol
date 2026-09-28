# The doorstep: the repository's homepage and description

**Date:** 2026-09-27, third pass finished 2026-09-28 · **Repo version:** 3.31.3 (`fa866f8`) when written,
3.31.5 (`4aa1e1e`) at the first same-day update, 3.31.9 (`e4c9e36`) at the second, 3.31.10 (`28e4460`) at the
third, 3.32.4 (`1b74a56`) at execution · **Status: EXECUTED 2026-09-28** (the owner asked for the plan in full,
so D24 is taken, §11; the record is §12 and §13, and the audit of 2026-09-29 is §14). The defect S0 found (F28) was fixed in 3.31.10. 3.31.10 was published on 2026-09-28 from
`28e4460` (`571a98d` with a test fix; CI green on both, all 34 checks). The session that made it could not: its
environment refused the tag `v3.31.10` and the wiki commit (HTTP 403, where branch pushes worked), so a local
session pushed both at the owner's word, with the Kronikol4J entries (Appendix B). D24 was the owner's to take, most of it in their own words, since the
roadmap gives this text to "the owner's hand"; they took it on 2026-09-28 (§11). Roadmap item **2.1** (stage 2, track C). No package changes, so **no version bump**, and
nothing in it is outreach (rule 9).

**Updated three times.** Once GitHub was reconnected, a session ran the parts of S0 it could reach
through GitHub's API and BreakfastProvider's source. Once the environment had full network access, a session
reached the live demo, the card image and nuget.org's search (§3, §12). Between the two, BreakfastProvider
moved to 3.31.4 (`INTERNAL_FLOW_BLOB_PLAN.md` S5, deployed 11:57 UTC), so S1 now waits only on D24 and the
owner's traffic reading. After 3.31.10, a third pass (2026-09-27 and 28):
- matched the drafts against what the demo draws (F29);
- made Q5's deep link concrete and proved it (F30);
- timed the first diagram without the network (F31);
- predicted each draft on the card (F32);
- read the package README nuget.org shows (F33);
- checked the plan's REFERENCE rows against GitHub's documentation (F34) and, where that is silent, GitHub's
  shipped script (F23);
- linted and extended S4's guard (§7).

**Executed 2026-09-28** by a local session whose `gh` is the owner's (admin on the repository). So the settings, the
traffic and GitHub's search, which the plan left to the owner's hands because the cloud sessions that wrote it had
no such access, were within reach, each at the owner's word (§13). S1, S2, S3, S4 and Q5 are done, and the demo is on
3.32.4, where F30's deep link lands. Found on the way:
- GitHub's card cuts the description at a character cap between 116 and 123, so it cut draft C, the owner's first
  choice. The owner chose a 111-character trim of C, which the card prints whole (F35).
- A logged-out phone visitor is shown neither field: the block F23 read in GitHub's script is not drawn (F36).
- Over a real network the demo's 3.31.9 drew the deep link's diagram off the screen on a desktop and not at all on a
  phone (F37). On 3.32.4 it lands on the screen at both widths (§12).

The roadmap's row, whole: "**The doorstep.** The repository's homepage field is empty (RUN): point it at
the live BreakfastProvider report. Rewrite the description from mechanism to outcome; the direction
report's draft is 'see what your integration test actually did: every HTTP call, SQL query and message,
as a sequence diagram'. About an hour. Passive: it changes what the fortnight's 31 visitors see and
invites no more. Outward-facing text: the owner's hand, and no em-dashes or other tells."

It is the first rung of the distribution ladder in "Where Kronikol Goes Next" (roadmap D.1), headed "Put
the demo where people land". That rung had three parts: the homepage field, the description, and the demo
hoisted to the README's first line with the real report as its first image. The roadmap kept the first
two here and put the third into 13.1, the README as a landing page. What this plan adds to the row:

- **The premises hold** (RUN, 2026-09-27, read again after 3.31.9). The homepage is empty, the description
  is word for word the one the direction report quoted on 2026-09-12, and the repository has twenty topics.
- **The description is read in more places than the About box.** GitHub puts it in the repository page's
  title, which is the line a search engine shows, in the link card's tags, and in the card's image, which
  prints it whole (RUN, F15; a web search returns the title word for word, F17). About 35 characters of it
  survive a search result's cut (F26), so the outcome has to lead (§2). On a phone, GitHub's script draws it in
  the page's header, above the file list (READ, F23), and every draft prints whole on the card (F32). Both were
  wrong: a logged-out phone is shown neither field (F36), and the card cuts a description at 116 to 123
  characters (F35).
- **The demo is another repository's output, and it is live.** BreakfastProvider's CI rebuilds the site on
  every push and every night: a landing page, 18 reports and two API viewers. All of it answered on
  2026-09-27, the six reports the landing page links are green, and the six docker reports it does not link
  carry three known failures each (RUN, F20). Nothing in either repository checks the URL (READ), so
  choosing it is also choosing what can break (F6). S4 offers a guard, and a session has dry-run it (§7).
- **The demo is current since 11:57 UTC.** That morning it was on 3.29.0, seventeen releases back.
  `INTERNAL_FLOW_BLOB_PLAN.md` S5 moved all 30 of BreakfastProvider's pins to 3.31.4 (`9f242d4`), the site
  redeployed, and every report now says 3.31.4 (RUN, F5). The releases since are fixes and a smaller file.
- **A report's first screen has no diagram.** At both widths the first screen is the summary, and all 67
  features start collapsed: the first diagram is three taps from the landing page (a report, a feature, a
  scenario), and on a phone a screen of scrolling as well (RUN, F21). The report already has what a demo
  needs, a `#sid-` link that opens one scenario with its diagram in view (F22), which the landing page
  could use (Q5). **Found on the way, and fixed in 3.31.10:** that link, Next Failure and a failure-cluster
  link landed past their scenario. A smooth scroll aimed across features the browser had not drawn yet ran
  to the bottom of any long report, and on a phone the report folded its filters away after the scroll had
  started (RUN, F28). Q5's link is now concrete. It opens a scenario whose diagram is one HTTP call, one SQL insert
  and one message, and it lands there once the demo is on 3.31.10 (RUN, F30). Without the network, the diagram
  is drawn 2.1 to 3.5 s after the link on a desktop and 3.5 to 3.7 s at phone width. Reached by the three taps,
  it is drawn 0.1 to 0.5 s after the last one, because the engine loads while the visitor looks (RUN, F31).
- **The demo shows what the drafts say.** The xUnit lane's 203 diagrams draw 540 HTTP calls, 255 SQL queries,
  214 other database queries and 258 messages, and 53 of them hold all three of draft B's nouns. So every noun
  B, C and D use is on the screens the homepage leads to (RUN, F29).
- **The draft over-claims by one word.** "Every HTTP call" holds only for calls through a tracked client
  that are attributed to a test, and section 0 of the roadmap rules out a document that states a
  falsehood (F9). "Self-contained", the direction report's word for the file, is not true of the default
  report either: the page fetches the PlantUML engine from jsDelivr when it opens, and without it shows a
  render error where each diagram would be (RUN, F10).
- **The old name is a larger doorstep on nuget.org than the new one.** The TestTrackingDiagrams packages
  have 2.5 million downloads to Kronikol's 0.9 million, rank above Kronikol's on every search they share,
  are not deprecated, and send their project link to the old repository address (RUN, F24, F25).
  Deprecating them is already 13.4's; this plan only records it. The README Kronikol's own packages carry to
  nuget.org links no demo and opens with "Effortlessly" (READ, F33).
- **Fixed in this plan's commit:** the README's link to the BreakfastProvider repository had a doubled
  slash, and both links in that sentence spelt the name "BreakFastProvider" (F11).
- **What a session can check, and what only the owner can.** A session:
  - read the repository's fields, page, card and script;
  - read GitHub's documentation;
  - read BreakfastProvider's source;
  - read nuget.org's records and search;
  - read the live demo;
  - drew copies of two live reports in Chromium at both widths, and timed one (§12).

  Left for the owner:
  - traffic (the traffic API needs a permission the session's token does not have, F12, F34);
  - GitHub's own search (outside the session's repository scope);
  - a look at the About block on a phone (GitHub's script says what it holds and where, F23);
  - the first diagram's time on a real phone and network (the browser's own part is measured, F31);
  - the glance for anything private (not run by a session).

  The session's GitHub tools have no call that edits a repository's settings, so S1 and S2 are the owner's
  hands whatever D24 says.

---

## 0. Summary

Two repository fields change. Nothing in `src/`, no package, no report byte, no Kronikol4J ledger entry.

| Slice | What | Who | Time | Needs |
|---|---|---|---|---|
| S0 | Look first: the demo at two widths and the version that wrote it, the repository page as a logged-out visitor sees it, and the numbers only the owner can read. **Run by a session on 2026-09-27 as far as it reaches** (§3, §12) | the owner, for what is left | 10 min left | a browser and a phone; push access |
| S1 | Set the homepage | owner | 2 min | Q1, and S0's traffic reading first (F12). The demo's version is no longer a condition: it is 3.31.4 (F5) |
| S2 | Write and set the description | owner | 30 min | Q2, and S0 step 5's look at a phone first (F23) |
| S3 | *Optional.* The README's first line points at the demo | the owner's words, a session's commit | 10 min | Q3 |
| S4 | *Optional.* A weekly check that the homepage answers, the reports it links are green, and a deep link on it names a scenario its report has | a session | 45 min | Q4, after S1 |
| S5 | Record the before and after, strike the roadmap row, update the index | a session | 10 min | S1, S2 |

The roadmap's hour is S0, S1, S2 and S5. S3 and S4 are proposals, each a yes or no in D24. The demo's
upgrade is in neither the hour nor this plan, and it is done: S0 first found the demo on 3.29.0, and
`INTERNAL_FLOW_BLOB_PLAN.md` S5 moved its pins to 3.31.4 the same day (F5). S2 never waited: the words
change nothing the demo shows. The defect of F28 was Kronikol's, not this plan's, and it shipped as a patch
of its own, 3.31.10, the same day. The demo gets it with its next pin move, and Q5's deep link waits for that.

## 1. What was checked

Basis marks as in the roadmap, plus **REFERENCE**: GitHub's documented or well-known behaviour, not
checked today. Every REFERENCE row that the plan leans on is checked in S0. The third pass checked them
against GitHub's documentation (F34) and, where that is silent, against GitHub's shipped script (F23). The
rows those settle now say READ. What stays REFERENCE is either documented nowhere or not GitHub's to document:
- that Pages keeps a site after a failed deploy (F6);
- where a search engine cuts a title (F26);
- how long an unfurler keeps a card (§2).

Pages over https were read with `curl`, which trusts this environment's proxy. Chromium does not, so the
reports were drawn from copies fetched with `curl` and opened from `file://`. That draws the page as a
visitor gets it, except that the engine from jsDelivr cannot load, so no diagram is drawn. For the timing
(F31), a copy was served at the report's own address and the engine at jsDelivr's, from memory, through
Playwright's request interception with the bytes and headers jsDelivr sent (its integrity check passed). No
request left the machine, so those times are the browser's own work, without a network's.

| # | Finding | Basis |
|---|---|---|
| F1 | The homepage field is empty. `has_pages` is false: Kronikol has no Pages site of its own, so the homepage can only point elsewhere. The dashboard plan's `lemonlion.github.io/Kronikol/` is not built (roadmap 9.3; it answers 404) | RUN: `api.github.com/repos/lemonlion/Kronikol`, 2026-09-27, again after 3.31.9 |
| F2 | The description is `A mechanism for tracking the request-responses in your tests and converting them into PlantUML diagrams.`, 104 characters. It names a mechanism, uses a word nobody searches for ("request-responses") and gives the drawing tool as the outcome. It does not say .NET, that the result is an interactive report, that capture is automatic, or that databases and message brokers are drawn as well as HTTP | RUN, same call |
| F3 | Twenty topics, which is GitHub's maximum ("Add no more than 20 topics", F34). The direction report calls them "done right", and adding one means removing one, so this plan changes none. `plantuml`, `plantuml-diagrams`, `plantuml-generator` and `sequence-diagrams` keep those words findable if the description drops them | RUN, same call. READ: GitHub's docs |
| F4 | The demo is `https://lemonlion.github.io/BreakfastProvider/`, a Pages site built by the `deploy-pages` job of BreakfastProvider's `ci-main.yml` on every push to `main` and daily at 03:00 UTC. Its root is a landing page the job writes inline, titled `Breakfast Provider — Kronikol`: a hero ("Breakfast Provider", "Component Test Reports & API Documentation", a "Powered by Kronikol" badge), six cards linking `reports/<framework>/TestRunReport.html` (the in-memory lanes of ReqNRoll, LightBDD, BDDfy, xUnit, TUnit and NUnit), the OpenAPI and AsyncAPI viewers under `api/`, and the two source repositories. The twelve docker and external-SUT reports (`reports/docker/<framework>`, `reports/docker-sut/<framework>`) are published and linked from nowhere. The live root is the job's text byte for byte, bar a final newline. The nightly is `cron: "0 3 * * *"`, but its runs have recorded between 08:04 and 08:49 UTC on each of the last four days, so the site changes then, not at 03:00. That fits GitHub delaying scheduled runs at the start of an hour (F34, INFERRED as the cause). At 07:05 UTC on 2026-09-28 the site was still the deploy of 11:57 the day before (`last-modified`) | READ: BreakfastProvider's `ci-main.yml` at `9f242d4` (a clone), and its `kronikol-history` branch. RUN: the live root and its headers |
| F5 | The demo is on 3.31.4. On the morning of 2026-09-27 BreakfastProvider's `main` was `0d29537` (2026-09-23), with all 30 pins at 3.29.0 (27 package references and three `Kronikol.Tool --version` steps), seventeen releases back. `INTERNAL_FLOW_BLOB_PLAN.md` S5 moved all 30 to 3.31.4 in `9f242d4`, `CI: Main` went green twice, and the site redeployed at 11:57 UTC: each of the 18 reports carries `Kronikol v3.31.4`, and each digest names `Kronikol 3.31.4+81f77b58` (F20). So the fixes a visitor would meet on a first look are live: the toolbar at every width (3.29.2), parameter tables and detail panels at tablet and phone widths (3.29.3), long tokens scrolling the page sideways (3.29.4), the note header's contrast and a link's hover colour (3.30.0), and a report 37% smaller (3.31.4). The five releases since are fixes (3.31.5 to 3.31.8; 3.31.6 was tagged and never published) and a smaller file again (3.31.9: the xUnit lane's download 1.16 to 0.90 MB). Moving the demo to them is the next consumer release's business, not a condition of S1 | RUN: the clone, a `git grep` of the pins at both commits, the live site. PLAN: `INTERNAL_FLOW_BLOB_PLAN.md` §11.1. READ: CHANGELOG 3.31.5 to 3.31.9 |
| F6 | Nothing watches the link. None of this repository's four workflows mentions `github.io`, and none of BreakfastProvider's ten does either: its `post-deployment-tests.yml` tests the API server, not the Pages site. `deploy-pages` needs all 18 lane jobs and both unit-test jobs, with no `always()`, so a lane job that fails holds the deploy back and the last site stays up (READ; that Pages keeps serving it is REFERENCE, and GitHub's docs do not say, F34). A lane job fails only on a failure its history ledger has not seen before (`kronikol history gate` in `_tests.yml`): a failure that persists is known on its second run, and from then on it is published, as the docker lanes' three are (F20). A renamed lane, a moved path, a renamed repository or Pages switched off would send every visitor to a 404. For the renamed repository GitHub's docs say so: a rename redirects "all existing information, with the exception of project site URLs" | READ: both repositories' workflows, BreakfastProvider's `ci-main.yml` and `_tests.yml` at `9f242d4`; GitHub's docs on renaming a repository. INFERRED for the other failure modes |
| F7 | The README links the demo twice, both inline: the first paragraph's words "rich interactive HTML reports" (`README.md:12`), and a bold sentence under Example Output (`:33`) saying the picture beneath it is "just a very simple static example". That picture is an image uploaded to GitHub, and it links to a plantuml.com server URL, so the README's one picture of the product sends a click to the site the ad audit measured (roadmap D.3). GitHub renders both demo links, like 62 of the README's 77 links out of GitHub, with `rel="nofollow"`, so they lend the demo no search weight | READ. RUN for the `rel` values, on the repository page's HTML |
| F8 | Tells are already at the door. The direction report's own draft has an em dash (`see what your integration test actually did — every HTTP call, SQL query and message, as a sequence diagram`; the roadmap's row already turned it into a colon). The README's opening paragraphs have three (`README.md:10`, `:14`, `:16`), and the demo's landing page title one (F4) | READ; RUN for the live title |
| F9 | "Every" cannot be proved. A call is drawn when it goes through a client that has its extension and is attributed to a test. A client without one is not drawn, and identity-less background work that names no document still lands nowhere (roadmap Appendix C). The nouns can be proved (§5.4) | READ: `src/` (62 packages), `README.md:14`, roadmap Appendix C |
| F10 | "Self-contained" is not true of the default report. The default rendering is `BrowserJs` (`ReportConfigurationOptions.cs:133`), and since 3.31.1 that page fetches the PlantUML engine from `cdn.jsdelivr.net` when it opens: `@plantuml/core@1.2026.8`'s `plantuml.js` (3,947,570 bytes, 1,078,370 as jsDelivr sends it) and `viz-global.js` (1,445,436 bytes, about 0.6 MB sent), which a browser then keeps for a year (`max-age=31536000, immutable`). A page that cannot fetch them shows, where each diagram would be, "Render error: PlantUML engine unavailable: failed to load https://cdn.jsdelivr.net/npm/@plantuml/core@1.2026.8/plantuml.js". Keep the word, and "offline", out of the description | READ: `ReportConfigurationOptions.cs:133`, `TrackingDefaults.PlantUmlJsCdnBase`. RUN: the engine's sizes and headers; the render error, drawn by a copy of a live report in a browser that cannot reach jsDelivr |
| F11 | `README.md:33` linked the BreakfastProvider repository as `https://github.com/lemonlion//BreakfastProvider/`, with a doubled slash, and spelt the name "BreakFastProvider" in both of its links. **Fixed in this plan's commit** (documentation only, no bump). Whether GitHub resolved the doubled slash is still unknown: this environment's proxy refuses to forward such a path (`Request path could not be canonicalized`), with full network access too. The corrected form is right either way | READ; RUN for the proxy's answer |
| F12 | Traffic (views, unique visitors, referring sites, popular content) is shown to people with push access, for the last 14 days. Referring sites and popular content update daily, and the API returns only the top ten of each (READ, F34). The traffic API refuses the session's token (403, `Resource not accessible by integration`) and an anonymous call (403). Its endpoints need the Administration permission (read) from an app's or a fine-grained token (READ, F34), which this session's evidently lacks (INFERRED). The direction report's baseline, read 2026-09-12: 31 unique visitors in 14 days; referrers GitHub 7, NuGet 2, Google 1, DuckDuckGo 1, ChatGPT 1. A before-reading has to be taken by the owner on the day of S1, or it is gone | PLAN (the report). READ: GitHub's docs. RUN for the refusals |
| F13 | A search across all of GitHub is outside this session's repository scope, so the GitHub half of the search baseline is the owner's. The web half was taken with the session's search tool (F17). A second engine, DuckDuckGo's HTML page, answered with a bot challenge, and a session does not go round one. nuget.org's half is F24 | RUN |
| F14 | A GitHub release is published for each tag (the latest, v3.31.9, at 13:46 UTC on 2026-09-27), so the About column's release box is current. Recorded so nobody re-checks it | RUN: `api.github.com/repos/lemonlion/Kronikol/releases/latest` |
| F15 | The repository page's title is `GitHub - lemonlion/Kronikol: <description> · GitHub`; `og:title` and `twitter:title` are the same without ` · GitHub`, and `og:description`, `twitter:description`, `og:image:alt` and the meta description are `<description> - lemonlion/Kronikol`. `og:image` is GitHub's generated card (`opengraph.githubassets.com/<hash>/lemonlion/Kronikol`, `twitter:card` `summary_large_image`), so no custom social preview is set, which answers S0 step 2. The card itself is a 1200 × 600 PNG: the name, the whole description on three lines of 44 to 49 characters, the owner's avatar, and four counts (2 contributors, 1 "used by", 17 stars, 1 fork) | RUN: the page's HTML and the card image, 2026-09-27 |
| F16 | nuget.org's latest Kronikol is 3.31.9. Its project URL is `https://github.com/lemonlion/Kronikol` (`Directory.Build.props:10`), so a NuGet visitor's project link lands on this doorstep too. Its description (`src/Kronikol/Kronikol.csproj:9`) is mechanism-first as well, and opens with the old name: "Kronikol, formerly TestTrackingDiagrams. Autogenerate PlantUML sequence diagrams from your component and acceptance tests. Tracks HTTP calls, database operations (Cosmos DB, SQL via EF Core), Redis commands, events/messages, and arbitrary method calls, then converts them into searchable HTML reports and structured data files." Package metadata, so out of scope (§10). The `Kronikol` package has 127,720 downloads, and the 62 Kronikol packages 893,458 | RUN: nuget.org's registration index and search API; READ |
| F17 | Web search, before (the session's search tool, US results, 2026-09-27). For "Kronikol", the Java port's repository comes first, titled "A Java port of the .Net Kronikol project that tracks your test flow at run time and turns it into interactive diagrams", then this repository under today's title, then release pages and issue #78. For the five phrasings of S0 step 3, Kronikol is in none of the results: "reqnroll report" returns ReportPortal, Allure and Reqnroll's own reporting pages, and "sequence diagram tests .NET" returns papers. The demo is not found by name: "BreakfastProvider Kronikol test reports" returns BreakfastProvider's repository first, described as "An example API showcasing technologies I've created (TTD/InMemoryEmulators)", then this repository's issues and the repository itself, and no page of the Pages site. "TestTrackingDiagrams" returns the old address, `github.com/lemonlion/TestTrackingDiagrams`, first, under today's description | RUN |
| F18 | The landing page's first look, drawn from its source (the text the job writes, which is the live page, F4) in Chromium through Playwright: nothing scrolls sideways at 1280 × 800 or at 390 × 844, and the first report card's top sits at 367 px and 468 px, inside the first screen at both. Kronikol appears only in the "Powered by Kronikol" badge, the source card and the footer, and the first paragraph is insider language (the history ledger, a sparkline, `$flaky`, `Failures.md`, `ctrf-report.json`) | RUN |
| F19 | A trap for whoever measures at phone width: headless Chromium's `--window-size` lays a page out no narrower than 500 px and crops the screenshot, so a 390 px check needs a real viewport (Playwright's, or DevTools' device mode). The first attempt here showed the landing page clipped at 390 px; measured with a real viewport, nothing overflows | RUN |
| F20 | The live site, 2026-09-27, as `CI: Main` run `36316716498` left it (11:57 UTC). The root (6,206 bytes, `max-age=600`) and all 18 reports answer 200 over https with no cookies. The root without its final slash answers 301 to the slash form; `lemonlion.github.io/` and `.../BreakfastProvider/demo/` answer 404. The six linked in-memory reports are 3.9 to 5.5 MB, 0.91 to 1.23 MB as Pages sends them (xUnit 4,962,030 and 1,179,176 bytes); the docker ones 3.5 to 6.2 MB (1.0 to 2.1 MB), the external-SUT ones 1.7 to 2.4 MB (0.40 to 0.48 MB). Beside each report sit its `Failures.md`, `Failures.jsonl`, `CLAUDE.md`, `AGENTS.md`, `TestRunReport.json` (6.4 MB for xUnit) and `ctrf-report.json`. The digests open `# No failures` on the six in-memory and the six external-SUT lanes, and `# Failures` with 3 of 178 to 205 scenarios on each docker lane: the three Orders scenarios the consumer's Azure emulator has failed since `7a0fb9b` (PLAN: `INTERNAL_FLOW_BLOB_PLAN.md` §11.1). The API viewers, the icon and the Kronikol link on the root answer 200; its two BreakfastProvider links are outside what this environment's proxy serves | RUN |
| F21 | A report's first look, drawn from copies of the live xUnit and ReqNRoll reports in Chromium through Playwright (1280 × 800; and 390 × 844 as a phone, with the CPU slowed four times): nothing scrolls sideways at either width (a wide parameter table scrolls inside its own box). The first screen is the title, the folded Features Summary and the execution summary ("Passed", 67 features, 203 scenarios) at 390 px, with the CI box and the filters beside them at 1280 px. All 67 features start collapsed, and so does each scenario, so no diagram is on a report's first screen at either width. The first diagram is three taps from the landing page (a report, a feature, a scenario), and at 390 px the first feature is a screen below the top. How long it takes to draw was measured without the network (F31), since the browser here cannot fetch the engine from jsDelivr (F10); over a real network it is the owner's to time | RUN |
| F22 | Deep links. A report opened at `TestRunReport.html#sid-<stableId>` (or `#scenario-<slug>`) opens every section around that scenario and scrolls it to the middle of the screen, the scenario opened (`report-url-hash-function.js`). On a copy of the live xUnit report at 1280 × 800, `#sid-83cb467c01ca1187` landed with the scenario's title and diagram box in the first screen. The id hashes the suite, the feature and scenario names, the outline and the example values (`ScenarioStableId.Compute`), so it survives reruns and releases and moves on a rename; an id that matches nothing leaves the visitor at the top of the report | READ; RUN for the landing. At 390 px, and far down a long report, see F28 |
| F23 | The About block on a phone. The repository page's HTML holds one visible copy of the description, in the sidebar's About section, which carries `hide-sm hide-md`: GitHub's stylesheet hides those at 543.98 px and below and from 544 to 767.98 px, so below 768 px. The HTML served to a phone's user agent is the same and holds no other copy, so whatever a phone shows is drawn by the page's script, which the browser here cannot load. **The script says what it draws** (its `code-view` bundle, read 2026-09-27, when the sidebar had become GitHub's React one and the page carried its data as `sidebarAbout`). Below 768 px, the overview's header draws a block of its own, `repo-mobile-details`, in the header's `show-whenNarrow` part, above the file list. It holds the description, cut at 350 characters with an ellipsis, then the licence, then the website. Both blocks show the website as its address without `https://` (the final slash stays), in bold beside a link icon. Both open it with `rel="noopener noreferrer"`, and the sidebar's adds `nofollow`, so a visitor it sends arrives with no referrer. §2's "above the file list" is read, not seen: the owner's phone look in S0 step 5 confirms it. **Measured 2026-09-28, it does not: a logged-out phone is shown neither field (F36)** | RUN: the page's HTML for a desktop and a phone user agent, GitHub's stylesheets. READ: GitHub's shipped script |
| F24 | nuget.org's search, before (its search API, stable versions, 2026-09-27). The three problem phrasings find nothing at all: its search matches words, not questions. "integration test diagram" and "sequence diagram tests" return the old name's package first and Kronikol's second; "plantuml" has `TestTrackingDiagrams.PlantUml.Ikvm` second; the first Kronikol or TestTrackingDiagrams package is sixth for "sequence diagram", ninth for "reqnroll report" and fortieth for "test report html"; "living documentation" has neither in its first hundred | RUN |
| F25 | The old name on nuget.org. The 56 `TestTrackingDiagrams` packages have 2,520,149 downloads against the 62 Kronikol packages' 893,458 (the main packages 463,560 and 127,720). Their last version is 2.37.4 (2026-05-17), none is deprecated, and their project URL is `github.com/lemonlion/TestTrackingDiagrams`, which reaches this repository only through GitHub's redirect for a renamed repository. GitHub's docs describe that redirect, and the one thing that ends it: a new repository created under the old name (READ). This environment's proxy answers only for the session's own repositories, so the redirect itself was not followed. Deprecating them is 13.4's (`LLM_FIRST_PLAN.md` §14.3's outward steps) | RUN: nuget.org's search API and registration index. READ: GitHub's docs on renaming a repository |
| F26 | How much of the description a search result keeps. Measured with Arial's metrics at 20 px (the metric-compatible Liberation Sans here), a title cut at 600 px (REFERENCE: the width a desktop result is commonly cut at) holds the 29-character prefix (255 px) and 34 to 38 characters of each draft, then an ellipsis. Today's keeps "A mechanism for tracking the reques", and draft B "See what your .NET integration tests". A phone result wraps the title onto a second line instead (REFERENCE) | RUN for the measurement; REFERENCE for the cut |
| F27 | The README's links, 2026-09-27: 119 of its 152 answered 200 (59 badges, none drawing an error; 57 nuget.org pages; ctrf.io; the plantuml.com link; the demo). The 29 wiki links name a page and a heading that exist in the wiki at `86a77c1`. The CI badge and workflow link are refused by this environment's proxy (the workflow is `.github/workflows/ci.yml`), and the image and the BreakfastProvider link are outside what it serves. Nothing is broken | RUN |
| F28 | **A defect, fixed in 3.31.10: a `#sid-` link, Next Failure and a failure-cluster link landed past their scenario.** Two causes. Every feature and scenario is drawn with `content-visibility: auto`, whose placeholder is 500 px for a feature (about 532 px with its padding, where a closed feature is about 60) and 150 px for a scenario, until the browser first draws it. The three jumps scrolled smoothly, and a smooth scroll is aimed once, across those placeholders: as it passed them they were drawn at their true size, and it ran on to the bottom of the page. On the live xUnit report a link into the 40th of 67 features ended with the scenario 1,766 px above the screen at 1280 px and 1,267 px at 390 px. And at 768 px and below, `report-init-script.js` called `parse_url_hash()` first and only then folded the filter panel (559 px there) away, so even a link into the first feature ended 220 px above the screen at 390 × 844; `parse_url_hash` itself reveals its anchor last so that "the scroll has to land on the layout the filters produced", and the init script broke that rule. 3.31.10 routes the three jumps through `jump_into_view(target, block)` in `report-url-hash-function.js`, which jumps (`behavior: 'instant'`) and re-aims for up to ten frames while the target's top moves (the features it lands among are drawn in the frames after it, and Safari has no scroll anchoring to absorb that), and calls `parse_url_hash()` last in the init handler. On a copy of the live report with the change, links into the first, 40th and last features land with the scenario's title on screen, -15 to 85 px from the middle, at both widths. The keyboard and table-reference jumps move a short way and stay smooth. Tests: four Playwright facts, each red on 3.31.9 (a link into the 30th of 60 features; a link into the first feature at 390 px; Next Failure and a cluster link into the 30th feature). Report output changes, so the Kronikol4J ledger owes the entry of Appendix A | RUN: copies of the live report, as found and with the change; the new facts on 3.31.9 and 3.31.10. READ: `stylesheets.css`, `report-init-script.js`, `report-url-hash-function.js` |
| F29 | **What the demo draws, against the drafts' nouns.** The xUnit lane's 67 features hold 203 scenarios, each with a diagram, making 1,315 calls. HTTP: 540, the tests' requests to the service and its calls to four downstream services (Cow, Goat, Supplier, Kitchen). SQL: 255, that is EF Core's inserts, selects, updates and deletes against two databases the diagrams label SQL Server (220), then Spanner 12, ClickHouse 12 and BigQuery 11. Other databases: 214 (Cosmos DB 198, MongoDB 16). gRPC: 48. Messages: 258, publishes to Pub/Sub, Kafka and Event Grid and consumes from Kafka, Event Hubs and Pub/Sub. 53 of the 203 diagrams hold an HTTP call, a SQL query and a message, and 30 hold all five kinds. The ReqNRoll lane is the same service under another framework (205 scenarios, 1,277 calls, 51 with B's three nouns). So every noun B, C and D use is on the screens the homepage leads to, and "SQL queries" is as safe there as "queries" | RUN: both lanes' `TestRunReport.json`, read with `kronikol query interactions --group-by` and a script that counts per scenario (neither file was opened). READ: BreakfastProvider's package references at `9f242d4` |
| F30 | **Q5's deep link, chosen and proved.** `reports/xunit/TestRunReport.html#sid-fb8c79cbd78d82b0` opens "Adding a new inventory item should return the created item": the test's `POST /inventory`, the service's insert into the Breakfast Database, and its `InventoryItemAddedEvent` published to Pub/Sub. That is three calls among four participants, which reads on a phone, and B's three nouns in one picture. It is in the 30th of 67 features, far enough down for F28. On the demo's 3.31.4 the link ends at the bottom of the page, the title 2,673 px above the screen at 1280 px and 2,029 px at 390 px. With 3.31.10's change the title lands 274 px and 347 px from the top, and the diagram's box starts on screen, at both widths. Drawn by the engine's real bytes, its diagram holds the four participants and both calls, and no error (F31). The id is the lane's: the ReqNRoll lane's copy of the scenario is `f57238fb856d675a`, because the id hashes the suite. It moves if the feature or the scenario is renamed, which leaves a visitor at the top of the report (F22), so S4's guard checks it (§7). For breadth instead, 30 scenarios draw all five kinds, the smallest in 22 calls among 11 participants ("Valid order should be created and an event published", `#sid-a02ed988a93eb069`), too wide for a first look on a phone | RUN: `kronikol query flow` on the lane's data; copies of the live report, as found and with 3.31.10's change, in Chromium at 1280 × 800 and 390 × 844 |
| F31 | **The first diagram's time, without the network.** The xUnit report was served at its own address and the engine's two files with the bytes and headers jsDelivr sent, all from memory (§1). The deep link used the copy with 3.31.10's change, the taps the live bytes. Deep link at 1280 × 800, four runs: first paint 0.27 to 0.51 s, page loaded 1.07 to 1.60 s, engine ready 1.13 to 2.41 s, the scenario's diagram 2.05 to 3.52 s after the link was opened. Deep link at 390 × 844 with the CPU slowed four times, three runs: first paint 0.68 to 0.71 s, loaded 2.05 to 2.37 s, engine ready 2.20 to 2.51 s, the diagram 3.50 to 3.67 s. The three taps (a feature opened, then the scenario scrolled to and tapped): the diagram 0.11 to 0.47 s after the tap at 1280 px and 0.20 to 0.24 s at 390 px, because the engine starts loading when the page opens and is ready before a visitor has found a scenario. The engine ran in its worker, as a visitor's does, and its integrity check passed. Two things this leaves out. The phone profile slows the page, not the engine's worker (0.8 to 1.3 s for a run's three diagrams under either profile), so a real phone draws later. And the network adds the report (1.18 MB as sent) and, on a first visit, the engine (about 1.7 MB): at least 2.3 s at 10 Mbit/s, and at least 14 s at the 1.6 Mbit/s of Lighthouse's slow-4G profile (REFERENCE for the profile). The owner's S0 reading is that network part, on a real phone | RUN: Playwright's Chromium 1194 on this machine's four cores. INFERRED for the network's share, from the sizes |
| F32 | **Each draft on the card, predicted.** The card's description is set in Inter, at about 31.6 px on a 48 px line pitch: of six sans-serif faces, Inter reproduces today's three line widths (765, 683 and 145 px, measured on the card) within 4 px. Today's breaks put the text box between 765 and 838 px wide. At either width every draft prints whole, A in two or three lines and B, C and D in three, as today's does, so no draft needs a fourth line, which the card has not been seen to draw. **Wrong for C: the card also cuts at a character cap, between 116 and 123 (F35)** | RUN: the card's pixels; the metrics of Google Fonts' Inter, Mona Sans, Hubot Sans, Noto Sans and Roboto, and of Liberation Sans. INFERRED: the wrap |
| F33 | **The README nuget.org shows.** Every package but `Kronikol.Templates` ships `nuget-readme.md` (`Directory.Build.props:17`), not `README.md`, so F11's fix does not reach nuget.org, and no demo link does either. `Kronikol.Templates` ships `templates/README.md` (`templates/Kronikol.Templates.csproj:11`), which the audit found (§14). The file links the wiki eleven times and the demo not at all. It opens with the old name's note and then "Effortlessly autogenerate **PlantUML sequence diagrams**", a word §5.3's check flags, and it holds 14 em dashes. NuGet sent 2 of the 12 referrals on 2026-09-12 (F12), and the `Kronikol` package has 127,720 downloads (F16) | READ |
| F34 | **GitHub's documentation, checked** (docs.github.com and the gh manual, 2026-09-27). Topics: "Add no more than 20 topics" (F3). Search: "When you omit this qualifier, only the repository name, description, and topics are searched" (§2). Traffic: push access, 14 days, referring sites and popular content updated daily and listed ten at most, and the API needs the Administration permission (read) (F12). Scheduled workflows run only on the default branch. They "can be delayed during periods of high loads", which "include the start of every hour", and under enough load some are dropped. The shortest interval is 5 minutes. A failure is notified to the user who created the workflow, or to whoever last changed its cron line or re-enabled it. "In a public repository, scheduled workflows are automatically disabled when no repository activity has occurred in 60 days" (§7). `gh repo edit` takes `--homepage` (`-h`, which is not help there) and `--description` (`-d`) (§4, §5.5). A description has no documented length limit: the REST and GraphQL references give none (§5.1). Pages documents no redirect but GitHub's own (HTTP to HTTPS, and a custom domain's `www`), and no analytics (H3, §8). A renamed repository redirects everything but its Pages site (F6, F25). Nothing documented counts clicks on the homepage link, and no documented notification covers a settings edit (§8, §9). A custom social preview is best at 1280 × 640 px, under 1 MB (§10). Documented nowhere: whether Pages keeps a site after a failed deploy (F6) | READ: GitHub's docs and the gh manual |
| F35 | **The card cuts the description at a character cap.** Set on 2026-09-28, draft C (124 characters) came back on GitHub's regenerated card in three lines ending "drawn as sequence diagrams in an HTML…": 116 characters kept, cut back to a word boundary, then an ellipsis. The old 104 characters printed whole (F15), so the cap lies between 116 and 123 characters. F32 modelled the wrap alone and predicted C whole. A (107) and B (114) are under the cap; D (120) may not be. The owner chose a 111-character trim of C (§11), which the card prints whole on three lines. The page title, `og:description` and `twitter:title` carry the text whole either way | RUN: the page's HTML and the card's image, before and after the trim |
| F36 | **A logged-out phone visitor sees neither field.** In Chromium 149 at 390 × 844, logged out, with the user agents of iOS Safari and Android Chrome and with script on and off, the repository page shows no description, licence, website, About heading or star count anywhere. The sidebar's About is hidden below 768 px (hidden at 767 px, shown at 768 px). The `repo-mobile-details` block F23 read in GitHub's script (`code-view-7a9e6bc38f73174e.js`: the description cut at 349 characters and an ellipsis, then the licence, the website and the counts) is in the bundle, but nothing draws it: its container in the header is empty, also after a tap from a folder page back to the root. The first screen is the header, the repository's name and tabs, the branch and Code buttons and the file list, and the README starts at its foot. So F23's "above the file list" does not hold for this visitor, and on a phone the README's first line (S3) is the demo link a visitor meets. A signed-in phone was not checked | RUN: Playwright with Chromium 149, 2026-09-28 21:37 to 22:03 UTC, screenshots at both widths |
| F37 | **The first diagram over a real network, on the demo's 3.31.9.** This machine's connection (the report's first byte in 21 to 38 ms, jsDelivr from a London edge), a fresh browser each run, three runs. The deep link at 1280 × 800: first paint 76 to 436 ms, loaded 404 to 416 ms, engine ready 646 to 673 ms, and the scenario's diagram drawn after 1.0 s but off the screen, because 3.31.9 ends the scroll at the foot of the page (F28: the title 2,677 px above the screen). At 390 × 844 with the CPU slowed four times: loaded 823 to 860 ms, engine ready 950 to 988 ms, and the diagram not drawn in 30 s, because the report draws a diagram only within 200 px of the screen and the scroll ended 2,454 px past it. By the three taps at 390 px, the first scenario's diagram shows 12 to 14 ms after the last tap (it is drawn at load) and F30's 138 to 211 ms after it. Sent: the report, 918,334 bytes (2.67 MB decoded), and on a first visit `plantuml.js` 1,079,094 and `viz-global.js` 574,193 bytes (brotli), about 2.57 MB in all. Chromium slows the page's thread, not the engine's worker, so a real phone draws later. After the demo's move to 3.32.4: §13 | RUN: Playwright 1.61 |

## 2. Where the two fields show

The case for writing the description carefully is that it is read in five places, and in two of them it is
cut short. The title, the card's tags and the card's image were read on 2026-09-27 (F15), and the cut was
measured (F26). The phone's block was read from GitHub's script (F23), and the search fields from its docs (F34);
S0 step 5 looks at the phone before S2.

| Where | Homepage | Description | Note |
|---|---|---|---|
| The About column (desktop) or its phone form | yes, as the address without `https://`, in bold beside a link icon (READ, F23) | yes, whole: the phone's block cuts at 350 characters (READ, F23). A logged-out phone is shown neither field (RUN, F36) | The desktop copy is in the sidebar, which GitHub hides below 768 px. Below that, GitHub's script has a block for the page's header, above the file list, with the description, the licence and the website (READ, F23), and a logged-out phone is shown none of it (RUN, F36). Either link opens the homepage with `rel="noopener noreferrer"` |
| The page title: browser tab, bookmarks, search engine result | no | yes: `GitHub - lemonlion/Kronikol: <description> · GitHub` (RUN, F15) | 29 characters go to the prefix, and about 35 of the description survive a desktop result's cut (F26) |
| The link card GitHub draws for a shared repository link (Slack, Teams, X, LinkedIn) | no | yes, unless a custom social preview is set, and none is (RUN, F15) | The tags carry it, and the image prints it on three lines beside the owner's avatar and four counts (RUN, F15), whole up to a cap of 116 to 123 characters (RUN, F35). Unfurlers cache cards for days, so a change shows late |
| GitHub's repository search results and profile lists | the direction report says the homepage shows in search results (PLAN) | yes | Default repository search matches the name, the description and the topics (READ, F34) |
| The REST API | `homepage` | `description` | RUN. How S1, S2 and S4 are verified |

What the homepage display means for the URL: the About column shows the address itself, less its scheme
(F23), so `lemonlion.github.io/BreakfastProvider/` reads as a project's site and a lane report's path reads as
a file path. Nothing in the About column says "demo", which is one argument for S3.

nuget.org shows neither field. A package page shows the package's own description and project URL, which
ship in a package and so in a release (§10). The old name's packages send theirs to the old address (F25).

## 3. S0: look first (the owner, about 20 minutes)

In a logged-out browser window unless the step says otherwise. Each step writes one row of §12.

**Run by a session on 2026-09-27, as far as it could reach:** step 2 whole; step 3's web and nuget.org
halves; step 4 but for the glance for anything private, with the first diagram timed without the network
(F31); step 5's title, card tags and card image, with what the phone's About block holds read from GitHub's
script (F23). **Left for the owner:** step 1, step 3's GitHub half, step 4's time on a real phone and
network and its glance, and a look at step 5's About block on a phone. **Taken by a local session on 2026-09-28**
(§13): step 1, step 3's GitHub half, step 4's glance and its time over a real network, and step 5's phone look in a
browser (F36). A real phone stays the owner's.

1. **Traffic, before** (logged in, Insights → Traffic). The 14-day unique visitors and views, the
   referring sites, and the popular content. F12: this is the only day it can be read.
2. **The social preview** (logged in, Settings → General → Social preview). Is a custom image set? If not,
   the card GitHub draws carries the description, and S2 changes the card too. **Answered 2026-09-27:**
   none is set, and the generated card prints the description whole (F15).
3. **Search, before.** On GitHub's repository search and on one web search engine, note Kronikol's
   position for the phrasings people search with. The direction report's: *see what my integration test
   actually did*, *sequence diagram from EF Core queries*, *what HTTP calls did my test make*. Add
   *sequence diagram tests* and *reqnroll report*. "Not in the first page" is a valid entry. **Taken
   2026-09-27:** the web (F17) and nuget.org (F24). GitHub's is the owner's.
4. **The demo's first look**, at 1280 × 800 and 390 × 844, for the root and for the two or three lanes that
   are candidates (an in-memory lane carries the internal-flow popups, PLAN: `INTERNAL_FLOW_BLOB_PLAN.md`
   §1.5):

   | Check | Pass | Found 2026-09-27 |
   |---|---|---|
   | It answers | 200 over https, logged out | **Yes:** the root and all 18 reports, with no cookies. Use the address with its final slash; the other form costs a 301 (F20) |
   | It says what it is | the first screen names Kronikol and says what the page holds | In part. The hero reads "Breakfast Provider", "Component Test Reports & API Documentation" and "Powered by Kronikol": Kronikol is named, but not as what the page shows, and the first paragraph is insider language (F18) |
   | It shows breadth | the root names the test frameworks, so a visitor finds their own (roadmap §0: a visitor who finds their case missing does not return) | Yes: six framework cards. MSTest, which Kronikol supports, has no lane, and the twelve docker and external-SUT reports are linked from nowhere, which suits the docker six while they carry their three failures (F20) |
   | It reaches a diagram | a report in one click from the root; a drawn diagram on a report's first screen, with the time it took on the owner's connection noted | Half. A report is one click away (F18), but its first screen holds no diagram at either width, and the first is two taps further in, a feature and a scenario (F21). A `#sid-` link lands on one (F22), at a phone's width and far down a long report only from 3.31.10 (F28), which the demo does not have yet; F30 names the link. Without the network, the first diagram is drawn 2.1 to 3.5 s after that link at 1280 px and 3.5 to 3.7 s at 390 px, and 0.1 to 0.5 s after the last of the three taps (F31). The network's part is the owner's: the report (1.18 MB for xUnit) and, once, the engine (about 1.7 MB) |
   | Nothing is broken | no failing scenario, no render-error picture, nothing clipped or scrolling sideways at 390 px | The six linked reports are green, and the two drawn scroll nothing sideways at either width (F20, F21). `popup-smoke.js` drew every popup, with no console error, on the six in-memory lanes and the xUnit docker lane of the published 3.31.4 site (PLAN: `INTERNAL_FLOW_BLOB_PLAN.md` §11.1). With the engine's real bytes served from memory, F30's scenario drew its four participants and both calls, with no error (F31). Over a real network, a drawn diagram needs a browser that reaches jsDelivr: the owner's |
   | It is current | the version the lane's `Failures.md` names (`Kronikol <version>.` on a green run, `Written by Kronikol <version>.` when something failed), which sits beside the report | **Yes, since 11:57 UTC: 3.31.4** (F5, F20) |
   | Nothing leaks | open two notes and the headers of one request. The site is already public and linked from the README, so this is a glance, not an audit | **Glanced 2026-09-28 by a session, nothing private** (§13): the xUnit report's data, its page and the 622 compressed blocks inside it decoded show no credential, key, token, private address or real person, and the first scenario's notes and headers hold trace ids, `Server: Kestrel` and made-up bodies |

5. **Where the description shows** (§2): the tab title, the About column at both widths, and the card
   (paste the repository URL into any unfurling chat box, or read the page's `og:image`). If any row of
   §2 is wrong, correct §2 before S2, because the brief leans on it. **Done 2026-09-27:** the title, the
   card's tags and image (F15), where the desktop copy sits, and what GitHub's script draws on a phone
   (F23). **Left:** a look at a phone, where the script puts the description in the page's header, above
   the file list, with the licence and the website under it (F23).

## 4. S1: the homepage (the owner, about 2 minutes)

| Option | For | Against |
|---|---|---|
| **H1. The site's root**, `https://lemonlion.github.io/BreakfastProvider/` | The URL that already exists and is linked from the README. It survives lanes being renamed or added. It reads well in the About column. It names the frameworks, so it shows breadth before the first click, which is section 0's worry (F18) | One click short of the artifact, and three taps short of a diagram (F21). Its title carries an em dash, and its hero names Kronikol only as "Powered by" (F4, F18, Q5) |
| **H2. One lane's report** | Lands inside the artifact: the direction report's "thirty seconds inside a real report and it is obvious". It lands on the summary, though, not a diagram, unless the URL carries a `#sid-` fragment (F21, F22) | A long URL that reads as a file path, longer with a fragment. It is tied to a lane path that BreakfastProvider's CI owns, so it is the first thing to break (F6). About 1.2 MB before the page shows and 1.7 MB more for the first diagram, on a phone's network (F10, F20). One framework, when the visitor may use another |
| **H3. A stable alias**, such as `https://lemonlion.github.io/BreakfastProvider/demo/`, written by BreakfastProvider's deploy job as a page that forwards to the chosen lane | H2's landing with H1's stability. The lane can change without the homepage changing | A change in another repository's workflow. Pages documents no redirect but its own (F34), and the demo is uploaded by `actions/upload-pages-artifact` and published by `actions/deploy-pages` (READ: `ci-main.yml:1027`, `:1033`), so no Jekyll plugin runs and the alias is a page the job writes. `/demo/` answers 404 today (F20) |
| H4. The wiki, or nuget.org | | The direction report's whole point is the artifact, not more prose. Rejected |

**Recommended: H1, if the root passes S0's first look** (it answers, it says what it is, it names the
frameworks, a report is one click away at both widths). If it fails, **H2 with the lane S0 rates best**,
and then H3 as the form to settle on, so that the homepage never holds a lane path.

**What S0 found (2026-09-27).** Live, the root passes every check a session can run: it answers, it names
the frameworks, a report is one click away at both widths, and the reports it links are green, current
(3.31.4) and scroll nothing sideways (F5, F18, F20, F21). It says what it is only in part, and a diagram is
three taps in. Both are the landing page's to fix, in BreakfastProvider (Q5), and neither is a reason to
hold the link back, since today the About column gives its visitors no link at all. So H1 stands, and S1
waits for nothing but D24 and the owner's traffic reading (S0 step 1).

```bash
gh repo edit lemonlion/Kronikol --homepage "https://lemonlion.github.io/BreakfastProvider/"
curl -s https://api.github.com/repos/lemonlion/Kronikol | python3 -c "import json,sys; print(json.load(sys.stdin)['homepage'])"
```

Or the gear beside About on the repository page → Website. Kronikol has no Pages site of its own (F1), so
the dialog's option to use one does not apply. Keep the final slash (F20). Both flags are the gh manual's
(READ, F34); its short form of `--homepage` is `-h`, which is not help there, so the long form is safer.

**Order.** S1 need not wait for S2. The link changes no words, and the 31 visitors are better off with a link
than without one while the owner writes the description. S4, if taken, comes after S1: with the field empty,
its first check fails.

## 5. S2: the description (the owner, about 30 minutes)

### 5.1 The brief

The row says "from mechanism to outcome". Turned into tests a draft can pass or fail:

1. **The outcome leads.** Something a visitor wants (to see what their test did) comes before anything
   about how, inside the first 35 or so characters that a search result keeps (§2, F26).
2. **The platform is named early.** ".NET" is the first thing a visitor needs to know ("does this work for
   me?"), and the launch is a .NET launch (D18). Nothing claims Java: Kronikol4J is a separate repository
   with its own front door (D10).
3. **The artifact is named.** A sequence diagram in an interactive HTML report is what the demo shows.
   Naming it makes the homepage link the obvious next click.
4. **Every word is true of the product today** (§5.4). No universal quantifier without a proof (F9), no
   "self-contained" or "offline" (F10).
5. **No tells** (§5.3). The owner's list governs; §5.3 is a starting list and a check for it.
6. **Short.** GitHub documents no cap on a description (F34), and its phone block cuts one at 350 characters
   (F23), far above every draft. The limit that matters is the card and the phone block; the drafts below run
   107 to 124 characters, the card prints today's 104 whole on three lines (F15), and it would print each
   draft whole in two or three (F32). **Measured on 2026-09-28: the card cut C at 116 characters (F35), so 116 or
   fewer.**

### 5.2 Raw material

Drafts to cut from, not text to paste: the row asks for the owner's hand. Each was measured by the check
in §5.3 (RUN), and what a search title keeps by the width measurement of F26 (RUN).

| # | Draft | Chars | What a 600 px search title keeps (F26) | Tells | Words to prove |
|---|---|---:|---|---|---|
| today | A mechanism for tracking the request-responses in your tests and converting them into PlantUML diagrams. | 104 | `A mechanism for tracking the reques` | none | none |
| A | See what your integration test actually did: every HTTP call, SQL query and message, as a sequence diagram. | 107 | `See what your integration test actuall` | none | every |
| B | See what your .NET integration tests actually did: the HTTP calls, SQL queries and messages, as sequence diagrams. | 114 | `See what your .NET integration tests` | none | none |
| C | See what your .NET tests actually did: their HTTP calls, queries and messages, drawn as sequence diagrams in an HTML report. | 124 | `See what your .NET tests actually di` | none | none |
| D | Sequence diagrams of the HTTP calls, database queries and messages your .NET tests make, in one interactive HTML report. | 120 | `Sequence diagrams of the HTTP call` | none | none |
| set | See what your .NET tests actually did: HTTP calls, queries and messages as sequence diagrams in an HTML report. | 111 | `See what your .NET tests actually di` | none | none |

- **A** is the roadmap's form of the direction report's draft. It fails brief 2 and brief 4 ("every").
- **B** keeps the report's framing and fixes both. **Recommended as the starting point** for the owner's
  rewrite. It is also the only draft whose kept part ends on a whole phrase.
- **C** names the report as well as the diagram (brief 3), at the cost of ten characters, and says
  "queries" so databases other than SQL ones are covered.
- **D** is plain rather than outcome-first. It leads with the words people search for ("sequence
  diagrams") but gives up the contrast the direction report and 13.1 build on: your tests pass, and here
  is what they did.
- **Set on 2026-09-28:** the owner chose C, then, once the card cut it (F35), the trim in the last row,
  which drops "their" and "drawn" and keeps the rest of C.

The demo keeps each draft's promise. Its xUnit lane draws 540 HTTP calls, 255 SQL queries, 214 queries to other
databases and 258 messages, and 53 of its 203 diagrams hold all three of B's nouns (F29). On the card, each
draft prints whole (F32).

A choice inside every draft: "SQL queries" is concrete and is what people type; "queries" or "database
queries" is true of Cosmos DB, MongoDB, DynamoDB, Redis and the rest as well (§5.4). The demo draws both
kinds (F29), so either is true of what the homepage shows.

### 5.3 The tells check

The check behind the table. It prints the length, the first 64 characters of the search title (about what a
600 px cut keeps, F26), anything from the starting list of tells (dashes used as punctuation, the
typographic ellipsis and quotes, an exclamation mark, and the words that mark generated copy), and every
word that makes a claim about all cases, which then needs a line in §5.4. Run it on the final text before
S2 sets it.

```python
# python3 check.py "<draft>" ["<draft>" ...]
import re, sys

TELLS = re.compile(
    r"[\u2012-\u2015\u2026\u201c\u201d\u2018\u2019!]"
    r"|\b(seamless\w*|effortless\w*|powerful|robust|supercharg\w*|unlock\w*|elevat\w*|empower\w*"
    r"|comprehensive|cutting-edge|game.?chang\w*|leverag\w*|streamlin\w*|delv\w*|harness\w*"
    r"|revolutioni[sz]\w*|next-gen\w*|blazing\w*)\b", re.I)
TO_PROVE = re.compile(
    r"\b(every|each|all|any|always|never|whole|complete|full|self-contained|offline)\b", re.I)

for text in sys.argv[1:]:
    title = "GitHub - lemonlion/Kronikol: " + text
    print(f"{len(text)} chars: {text}")
    print(f"  search title, first 64: {title[:64]}")
    print(f"  tells: {sorted({m.group(0) for m in TELLS.finditer(text)}) or 'none'}")
    print(f"  words to prove: {sorted({m.group(0).lower() for m in TO_PROVE.finditer(text)}) or 'none'}")
```

A regular expression catches characters and words. It does not catch rhythm: a triple of parallel
phrases, "not just X but Y", a question the text answers itself. Those are the owner's ear, which is why
the row gives the text to the owner.

### 5.4 The truth check

Every noun a draft uses, what makes it true today, and where the demo the homepage links shows it.

| Word | True because | Basis | On the demo's xUnit lane (RUN, F29) |
|---|---|---|---|
| .NET | Every package in `src/` is .NET. The Java port is Kronikol4J, another repository | READ | A .NET service's tests |
| tests, integration tests | The core package and the adapters for xUnit 2 and 3, NUnit 4, MSTest, TUnit, ReqNRoll, LightBDD and BDDfy. The README says "integration/component test" | READ: `src/`, `README.md:12` | 203 scenarios; five more frameworks in the other linked lanes |
| HTTP calls | The first thing the README lists among what is tracked | READ: `README.md:14` | 540 |
| SQL queries | `SqlClient`, `Npgsql`, `MySqlConnector`, `Oracle`, `Sqlite`, `EfCore.Relational`, `Dapper`, three ClickHouse drivers, `BigQuery`, `Spanner` | READ: `src/` | 255 (EF Core, Spanner, ClickHouse, BigQuery) |
| queries, database queries | The above, and `CosmosDB`, `MongoDB` (two), `DynamoDB`, `Redis`, `Elasticsearch`, `Bigtable`, `AtlasDataApi` | READ: `src/` | 469 (the above, Cosmos DB and MongoDB) |
| messages | `ServiceBus`, `EventHubs`, `Kafka`, `SQS`, `SNS`, `EventBridge`, `PubSub`, `MassTransit`, `StorageQueues` | READ: `src/` | 258 (Pub/Sub, Kafka, Event Grid, Event Hubs) |
| sequence diagrams | Each scenario's diagram is a PlantUML sequence diagram; activity and component diagrams are drawn as well | READ: `README.md:12` | One for each of the 203 scenarios |
| HTML report, interactive | "rich interactive HTML reports" | READ: `README.md:12` | The page itself |
| every, each, all | Not provable (F9) | | |
| self-contained, offline | False by default (F10) | | |

### 5.5 Setting it, and checking it

```bash
gh repo edit lemonlion/Kronikol --description "<the owner's text>"
curl -s https://api.github.com/repos/lemonlion/Kronikol | python3 -c "import json,sys; print(json.load(sys.stdin)['description'])"
```

Or the gear beside About on the repository page → Description. Then, logged out, check the tab title and
the About block at both widths, and the card once an unfurler refreshes it.

## 6. S3, optional: the README's first line (Q3)

What is there: the demo link sits inside the first paragraph, and the first picture is the static example
that links out to plantuml.com (F7). The roadmap puts the README's rewrite in 13.1: twenty-five words, the
demo link, one screenshot of the new look, depth below the fold. The screenshot has to wait for the new
look, and so does the rewrite.

One line does not have to wait. Under the title and above the rename note, in the owner's words, something
of this shape: `**[See a live report](https://lemonlion.github.io/BreakfastProvider/)** from a sample
service's test run.` It is as passive as the homepage (it changes what the same visitors see, and
invites no more), it is documentation only, and 13.1 replaces it. A second, smaller change for the same
yes: point the example image's link at the demo instead of plantuml.com, so the README's one picture of
the product no longer sends a click to a third-party ad site.

The same line fits `nuget-readme.md`, the README the packages carry to nuget.org, which links no demo today
(F33). It is documentation, so it needs no version bump of its own; it reaches nuget.org with the next
release. Its opening tells ("Effortlessly", the em dashes) are the README's twin and go with 13.1.

The line points at the root, not at a scenario. A `#sid-` link (F22) would land the reader on a diagram,
but the root survives a renamed scenario and a renamed lane, and before 3.31.10 the deep link landed past
its scenario (F28). The landing page is where a deep link belongs (Q5).

S3 and S4 are the only parts of 2.1 that touch a file (the roadmap's §5 counts 2.1 as settings alone). S3
edits `README.md`, which 2.2's PR #73 edits as well. On 2026-09-27 the roadmap found the PR's `README.md`
merging clean with `main`, with only `CHANGELOG.md` in conflict. Against 3.31.10 that still holds (RUN:
`git merge-tree`; GitHub calls the PR unmergeable for the changelog). So whichever lands second takes a
one-line merge in `README.md`.

## 7. S4, optional: the guard (Q4)

Rule 3: a net goes up before the work it protects. What F6 lists can break the homepage without anyone in
this repository changing anything.

| Where | Catches | Cost |
|---|---|---|
| **A scheduled workflow in this repository** | Everything in F6; an emptied or mistyped homepage field, because it reads the field from the API each time; a red report on the page the homepage links (F20); and a deep link there that names a scenario its report no longer has (F30) | One file, about 35 lines. GitHub notifies a failure to the user who created the workflow, or to whoever last changed its cron line or re-enabled it (READ, F34) |
| A step after `deploy-pages` in BreakfastProvider's `ci-main.yml` | A deploy that drops or moves the lane, in the same run that caused it | Another repository's workflow; it misses Pages switched off or a repository renamed |
| None | | The first sign is a visitor's 404 |

**Recommended: the workflow here.** A sketch:

```yaml
name: Doorstep
on:
  schedule:
    - cron: "17 6 * * 1"
  workflow_dispatch:
permissions:
  contents: read
jobs:
  homepage:
    runs-on: ubuntu-latest
    steps:
      - name: The repository's homepage answers, the reports it links are green, and its deep links resolve
        shell: bash
        env:
          GH_TOKEN: ${{ github.token }}
        run: |
          url=$(gh api "repos/${{ github.repository }}" --jq .homepage)
          test -n "$url" || { echo "::error::The homepage field is empty"; exit 1; }
          code=$(curl -sS -L -o page.html -w '%{http_code}' "$url")
          test "$code" = 200 || { echo "::error::$url answered $code"; exit 1; }
          grep -qi kronikol page.html || { echo "::error::$url answered, but not with a Kronikol page"; exit 1; }
          while read -r report; do
            digest="${url%/}/${report%TestRunReport.html}Failures.md"
            curl -sS -f -o digest.md "$digest" || { echo "::error::$digest did not answer"; exit 1; }
            first=$(head -n 1 digest.md)
            test "$first" = "# No failures" || { echo "::error::$digest reads: $first"; exit 1; }
          done < <(grep -oE 'href="reports/[^"#]+/TestRunReport\.html"' page.html | sed -E 's/^href="//; s/"$//')
          while read -r link; do
            report="${link%%#*}"
            sid="${link#*#sid-}"
            curl -sS -f --compressed -o report.html "${url%/}/$report" || { echo "::error::$report did not answer"; exit 1; }
            grep -q "data-stable-id=\"$sid\"" report.html || { echo "::error::$link names a scenario the report does not have"; exit 1; }
          done < <(grep -oE 'href="reports/[^"#]+/TestRunReport\.html#sid-[0-9a-f]+"' page.html | sed -E 's/^href="//; s/"$//')
```

The second loop reads the landing page's `#sid-` links, which it has none of until Q5's is added. A report
marks each scenario with `data-stable-id` (`ReportGenerator.cs`), so a renamed scenario is caught the week
it moves, where a visitor would otherwise land at the top of the report (F22).

**Dry run, 2026-09-27** (RUN: the step's commands under `set -eo pipefail`, as GitHub's `bash` shell runs
them, with the URL passed in, since the field is still empty). The sketch above:
- against the live root, passed, reading `# No failures` from all six linked digests;
- against `.../BreakfastProvider/demo/`, failed with 404;
- given the root with F30's link added, passed, finding the scenario in the xUnit report;
- given the same link with an id no scenario has, failed ("names a scenario the report does not have");
- given a page that links the docker xUnit lane, failed on that digest's `# Failures` line;
- given a page that links no report, as a lane URL under H2 or H3 would be, passed on the page alone.

Re-run on 2026-09-28 at 07:05 UTC against the live root, and against the root with F30's link: both passed.

actionlint 1.7.12, with shellcheck 0.11.0, passes it. The first sketch's `for report in $(grep ...)` drew
shellcheck's SC2013, which the `while read` loops replace.

Its schedule, against GitHub's documented rules (F34):
- **Timing.** Monday 06:17 UTC keeps it off the start of the hour, when GitHub delays scheduled runs and,
  under enough load, drops some; a dropped run costs one week.
- **Disabling.** In a public repository GitHub disables a scheduled workflow after 60 days without repository
  activity, which Kronikol's pace of releases makes moot. `gh workflow enable` turns it back on.
- **Who is told.** A failure is told to the user who created the workflow or last changed its cron line, so
  that should be the owner: commit it from the owner's account, or change its cron line once after a session
  commits it.

Before it is committed: run it once by `workflow_dispatch` against the real field, then once with the
URL swapped for a path that does not exist, and see it fail. A check that has never failed has not been
shown to work. It checks no version on purpose: releases ship most days, so a check that the demo is on the
latest would be red most of the time, and moving the demo belongs to the release that needs it, as
`INTERNAL_FLOW_BLOB_PLAN.md` S5 did. It adds no test project and no package, so no bump. It is a new file
under `.github/workflows/`, so it collides with no other stage (roadmap §5, track C).

## 8. S5: record (a session, about 10 minutes)

- §12 of this plan: the before-readings of S0 and the text and URL set by S1 and S2.
- `ROADMAP.md`: strike 2.1 with the date and what was set, move D24 into the row, and note S3 and S4's
  answers.
- `PLANS_STATUS.md`: this plan's row.
- The after-reading, 14 days after S1, is the owner's (F12). It is context, not a target: the item invites
  nobody, so no rise in visitors is expected. The number the direction report set, 31 a fortnight becoming
  300, belongs to stage 13, and 13.0 re-reads the usage figures anyway (roadmap §8).

**Done** when: the API returns the chosen homepage and description; a logged-out window shows both at 1280
and 390 px; the homepage answers 200, and a report it links draws a diagram at both widths once a feature
and a scenario are opened (F21); the final text has passed §5.3 and every noun in it has a line in §5.4;
and §12 holds the before-readings.

**What cannot be measured, said plainly.** GitHub documents no count of clicks on the homepage link and no
analytics for a Pages site (READ, F34). The About link also sends no referrer (`noreferrer`, F23), so even an
analytics tool on the demo would count a visitor it sends as a direct one. Whether visitors follow the link
is not observable from either repository. The item's success is binary: the two fields are set, true, free of tells, and the link is
alive.

## 9. Risks

| Risk | Answer |
|---|---|
| The demo shows fixed defects to every visitor (F5) | It did until 11:57 UTC on 2026-09-27; since then it is 3.31.4. The releases since change the file's size, not the first look |
| The link breaks later (F6) | S4, which also catches a deep link on the landing page that stops resolving (F30), or at worst the next time someone opens the About column |
| A bad night: the demo shows a failing run | A new failure holds the deploy back, so the last site stays up; a failure that persists is published from its second run, as the docker lanes' three are (F6, F20). The six linked reports are green today, and S4's digest step would catch one that is not within a week |
| The description over-claims (F9, F10) | §5.4, and the check's words to prove |
| The description reads as generated | §5.3, and the owner's hand |
| It becomes outreach | Nothing here announces anything. No notification GitHub documents covers a settings edit (READ, F34). The topics stay as they are |
| A phone visitor waits on a large download | Measured by size, and without the network by time. A linked report is 0.91 to 1.23 MB as sent, and the first diagram adds the engine, about 1.7 MB once and then kept for a year (F10, F20). The browser's own work to the first diagram is 2.1 to 3.7 s after a deep link (F31). Under H1 the visitor picks a report before paying for one. The network's time is the owner's |
| A visitor's network blocks jsDelivr | Every diagram becomes a render error naming the engine's address (F10). Accepted for the homepage: it is the product's default, and the page says what failed. Drawing the demo without the CDN is BreakfastProvider's configuration, not this plan |
| A visitor finds no diagram on the first screen | Three taps to the first one (F21). Q5's deep link on the landing page (F30 names it) makes it one, once the demo is on 3.31.10 or later (F28) |
| Something private in the demo | It is public today and linked from the README, so pointing the homepage at it adds visitors, not exposure. The owner glances anyway (S0 step 4) |

## 10. Out of scope, and where it lives

| Item | Where |
|---|---|
| The README as a landing page: twenty-five words, one screenshot of the new look, depth below the fold, and the em dashes of its opening | 13.1 |
| A custom social preview image. A screenshot now would show the look that stage 6 and 12.1 replace; until then GitHub's own card carries the new description. GitHub's size for it: 1280 × 640 px for best display, under 1 MB (F34) | 13.1 |
| The topics | Unchanged: at the maximum, and "done right" (F3) |
| nuget.org's package description, project URL and README | Package metadata and packaged documentation, so they ship in a release. The project URL already points here. The description is mechanism-first and opens with the old name (F16). The README links no demo and opens with a tell (F33). If the owner wants the new words there too, they ride along with the next release, and S3's line can go into the README the same way (§6); none is a reason for a release |
| The old name's packages on nuget.org: their deprecation, and the old address they send people to (F25) | 13.4, `LLM_FIRST_PLAN.md` §14.3's outward steps |
| The deep-link defect (F28) | Fixed in 3.31.10. The demo gets it with BreakfastProvider's next pin move, and this plan's Q5 deep link waits for that |
| A report that opens a scenario on arrival | Not proposed. The demo gets it from a `#sid-` link (F22), with no change to the product |
| The Kronikol4J repository's front door | D10, taken 2026-09-22 in its README |
| A Pages site for Kronikol itself | The dashboard plan (9.3), not green-lit |
| Anything that brings people in: a listing, a post, a newsletter | Stage 13 (rule 9) |
| A rename | Not funded (roadmap stage 13) |

## 11. Open questions (D24)

| # | Question | Recommendation |
|---|---|---|
| Q1 | Which URL? | H1, the root, with its final slash. Live, it passes every check a session can run, the demo is on 3.31.4 since 2026-09-27, and what it lacks is the landing page's to fix (Q5), not a reason to wait (§4). Otherwise H2 with the best lane, then H3 so the homepage never holds a lane path |
| Q2 | Which words? | The owner's. Start from B (".NET" early, no claim about every call, a kept part that ends on a whole phrase) and rewrite it by hand; run §5.3 on the result (§5) |
| Q3 | Does the README's first line point at the demo now, or wait for 13.1? | Now, as one line to the root, with the example image linking to the demo. It is as passive as the homepage, and the rewrite and screenshot stay in 13.1 (§6) |
| Q4 | A guard on the homepage? | Yes, the weekly workflow in this repository, reading the reports the homepage links, and any deep link on it, as well as the page (§7). Linted, and dry-run in six cases on 2026-09-27; proved by one failing run before it is kept, and committed after S1 from the owner's account, so its failures reach the owner (F34) |
| Q5 | If H1: the landing page's title (`Breakfast Provider — Kronikol`), a line on that page saying what it is to someone arriving from Kronikol, and a link to one scenario by `#sid-` so a diagram is one tap from the root rather than three (F21, F22). And BreakfastProvider's own description, which still names Kronikol by its old initials (F17) | All of it, in BreakfastProvider (its workflow and its settings), in a commit of its own, in the owner's words as for S2. The deep link once the demo is on 3.31.10 or later (F28): `reports/xunit/TestRunReport.html#sid-fb8c79cbd78d82b0`, one HTTP call, one SQL insert and one message in a diagram that reads on a phone (F30). The title is the browser tab's text for every visitor the homepage sends (F8), the hero names Kronikol only as "Powered by" (F18), and the landing page's source card leads to that description |

**D24 taken 2026-09-28** (the owner asked for the plan in full):
- **Q1** as recommended: the root, with its final slash.
- **Q2** the owner chose draft C, then, once GitHub's card cut it at 116 characters (F35), its 111-character trim:
  `See what your .NET tests actually did: HTTP calls, queries and messages as sequence diagrams in an HTML report.`
- **Q3** yes, the recommended line, in `nuget-readme.md` too.
- **Q4** yes.
- **Q5** yes. The owner asked that BreakfastProvider keep both of its purposes, a demo of Kronikol and of how they do
  component testing in a .NET service, and gave the landing page's title in their own words: `Breakfast Provider: A
  Component Testing Approach Using Kronikol`. They chose the recommended line, and chose to move the demo to 3.32.4
  for the deep link. Their answer named the title alone, so the repository's description changes only the old
  initials: TTD becomes Kronikol.

## 12. Results (filled in at execution)

| Reading | Before (S0) | After |
|---|---|---|
| Homepage | empty (RUN 2026-09-27, again after 3.31.9, and 2026-09-28 at 21:29 UTC) | `https://lemonlion.github.io/BreakfastProvider/`, set 2026-09-28 at 21:39:51 UTC (RUN: the API returns it, and the page shows it) |
| Description | `A mechanism for tracking the request-responses in your tests and converting them into PlantUML diagrams.` (RUN 2026-09-27) | `See what your .NET tests actually did: HTTP calls, queries and messages as sequence diagrams in an HTML report.`, 111 characters, set at 21:59:05 UTC. Draft C, set at 21:39:57, came back on the card cut to "…in an HTML…" (F35). §5.3's check: no tells, no words to prove. The page title, the card's tags and its image carry it whole (RUN) |
| Unique visitors and views, 14 days | 31 unique on 2026-09-12 (PLAN, the direction report). **2026-09-28 at 21:29 UTC, before S1** (RUN, the traffic API as the owner): 17 unique visitors and 154 views over 2026-09-10 to 2026-09-23, the newest day the API held; 4,858 clones by 401 | The owner's, from 2026-10-12, as context only (§8) |
| Referring sites | GitHub 7, NuGet 2, Google 1, DuckDuckGo 1, ChatGPT 1 on 2026-09-12 (PLAN). **2026-09-28** (RUN): Google 7 views (1 unique), nuget.org 6 (3), github.com 4 (3), DuckDuckGo 3 (1), ya.ru 2 (1). Popular content: the overview 29 views (10 unique), the wiki 20 (5), the issues 17 (5) | The owner's, with the visitors. The About link sends no referrer (F23), so the demo cannot count what it sends |
| Search positions (S0 step 3) | The web (F17): "Kronikol" second, behind Kronikol4J; the five phrasings, absent; the demo, not found by name. nuget.org (F24): the three questions find nothing; "plantuml" second, "sequence diagram" sixth, "reqnroll report" ninth, and the old name ahead of the new on every query they share. **GitHub's search** (RUN 2026-09-28 at 21:30, the search API as the owner): "Kronikol" first, Kronikol4J second; absent from all five phrasings. Two of them find no repository at all ("see what my integration test actually did", "sequence diagram from EF Core queries"), and "what HTTP calls did my test make" finds 4, "sequence diagram tests" 70 and "reqnroll report" 28, none of them Kronikol | **GitHub's search at 22:14 UTC, 35 minutes after S2** (RUN): "sequence diagram tests" lists Kronikol 4th of 71 and "reqnroll report" 15th of 29; the three questions still find nothing. The web and nuget.org re-index on their own schedule: the owner's, with the 14-day reading |
| Social preview | GitHub's generated card, no custom image; it prints the whole description on three lines (RUN, F15) | The setting is unchanged. The regenerated card prints the new description whole on three lines, after cutting C at 116 characters (RUN, F35). Its counts now read 3 contributors, 18 issues, 17 stars and 1 fork |
| Public counts | 17 stars, 1 fork, 2 watchers, 2 contributors, 1 "used by" (RUN). nuget.org: `Kronikol` 127,720 downloads, the 62 Kronikol packages 893,458, the 56 TestTrackingDiagrams packages 2,520,149 (RUN, F16, F25) | Not this plan's to move: 17 stars, 1 fork, 2 watchers on 2026-09-28 (RUN) |
| The demo (S0 step 4) | 3.31.4 since 11:57 UTC, 3.29.0 before (RUN, F5). The root and all 18 reports answer 200; the six linked reports are green (F20); nothing scrolls sideways at either width; no diagram on a report's first screen, the first three taps from the root (F21). Without the network, the first diagram 2.1 to 3.5 s after a deep link at 1280 px, 3.5 to 3.7 s at 390 px with the CPU slowed, and 0.1 to 0.5 s after the last tap (RUN, F31). On a real phone and network, and the glance: the owner's | **3.32.4 since 22:09 UTC on 2026-09-28** (BreakfastProvider `4630c87`, the pins, and `377c270`, the landing page; CI: Main `36489694372`, 58 of 58 jobs). The six linked reports are green (xUnit 203 scenarios, ReqNRoll 205). Over the real network, F30's deep link lands with the scenario's title 274 px from the top at 1280 × 800 and 347 px at 390 × 844, its diagram drawn 1.11 s and 0.89 s after the link is opened; from the landing page's link at 390 px the same, drawn after 1.47 s (RUN). Nothing private (§13) |
| What the demo draws | The xUnit lane: 540 HTTP calls, 255 SQL queries, 214 other database queries and 258 messages; 53 of its 203 diagrams hold all three of B's nouns (RUN, F29). Q5's link: `#sid-fb8c79cbd78d82b0`, landing on 3.31.10 and not on the demo's 3.31.4 (RUN, F30) | Unchanged by the move. The landing page now links F30's scenario, one tap from the root |
| nuget.org | 3.31.9 latest; the project URL is the repository; the description is mechanism-first (RUN, F16); the README it shows links no demo (READ, F33) | Unchanged until the next release, which carries `nuget-readme.md`'s new first line |
| S3 and S4 | S4's check linted and dry-run in six cases, the deep-link step among them (RUN, §7) | S3 in `07d0a18`: both READMEs open with the line to the demo, and the example image opens it (`DemoLinkTests`). S4 in `07d0a18`: `.github/workflows/doorstep.yml`. Dispatched on 2026-09-28, it passed against the field (`36488512444`), failed against a missing path with "answered 404" (`36488524503`), and passed again once the landing page carried F30's link, finding it (`36491040249`) |
| BreakfastProvider (Q5) | The landing page titled `Breakfast Provider — Kronikol`, with no line for a visitor from Kronikol and no deep link; the description `An example API showcasing technologies I've created (TTD/InMemoryEmulators)` (RUN) | The title `Breakfast Provider: A Component Testing Approach Using Kronikol`. The first paragraph under the hero: "These are the reports Kronikol draws from Breakfast Provider's component tests: each scenario's HTTP calls, database queries and messages as a sequence diagram. See one.", its last two words linking F30's scenario. The description `An example API showcasing technologies I've created (Kronikol/InMemoryEmulators)`, set at 21:44:22 UTC (RUN) |

## 13. Execution (2026-09-28)

A local session (`kronikol-1b`), in a worktree of `main` at `1b74a56`, with the owner's `gh` (admin, the `repo` and
`workflow` scopes). Times are UTC.

1. **S0, what the cloud sessions could not reach.** At 21:29, before S1: the traffic and GitHub's search (§12). From
   21:37 to 22:03, a background session with Playwright and Chromium 149: the About block at 390 px (F36), the leak
   glance, and the first diagram over the real network on 3.31.9 (F37). The glance read the live xUnit report's data,
   its page, and the 622 compressed blocks inside the page, decoded, for credentials, keys, tokens, cookies,
   connection strings and private addresses, and found none. Its 16 email addresses are all `@breakfast.test`, its
   hosts are `localhost`, broker URIs with no host, BigQuery's public API and two public pages, and the first
   scenario's notes hold trace ids, `Date`, `Server: Kestrel` and made-up bodies.
2. **S1** at 21:39:51 and **S2** at 21:39:57, with `gh repo edit`; the API returned both. The regenerated card cut C
   (F35), and the owner chose the trim, set at 21:59:05, which the card prints whole.
3. **S3 and S4**, commit `07d0a18` on `main`, pushed from this machine as the owner, so the workflow's failures reach
   the owner (F34). `DemoLinkTests` (three facts) was red on the old READMEs and is green; `Kronikol.Tests` on net10.0
   passed 6,080 with 1 skipped. The workflow is §7's sketch plus a dispatch input that checks another address instead
   of the field, handed to the script through `env`. actionlint 1.7.12 with shellcheck 0.11.0 passes it, and flags
   SC2013 on a copy with the first sketch's loop. The step's script, run locally against the live site in §7's six
   cases, gave §7's six answers. On GitHub, `36488512444` passed against the field, `36488524503` failed against
   `.../BreakfastProvider/nonexistent/` ("answered 404"), and after Q5 `36491040249` passed and found F30's link. CI
   (28 jobs), CodeQL and CI Summary Preview passed on `07d0a18`.
4. **Q5, in BreakfastProvider**, by a background session in a scratch clone. `4630c87` moves the 27 package references
   and the three `Kronikol.Tool --version` lines from 3.31.9 to 3.32.4: restore clean with `--no-cache`, and the xUnit
   lane in memory, under CI's `KRONIKOL_SUITE=xunit-in-memory`, passed 203 of 203 with the scenario's id unchanged.
   `377c270` sets the landing page's title and adds the owner's line under the hero, in the page's paragraph colour
   and size. Pushed together, CI: Main `36489694372` passed all 58 jobs (the docker lanes' gates read the three known
   Orders failures as "3 still failing") and deployed at 22:09. The description was set at 21:44:22. Live, the deep
   link lands as §12 says; on 3.31.9, just before, the same link had ended 2,677 px and 2,031 px past the scenario (F28).
5. **S5:** this section, §12, `ROADMAP.md` (row 2.1, D24 and the register row) and `PLANS_STATUS.md`.

**Done** (§8). The API returns both fields, and a logged-out window shows both at 1280 px. At 390 px GitHub shows
neither to a visitor who is logged out (F36), which no setting changes. The homepage answers 200, and the xUnit
report it links draws F30's diagram at both widths, from the deep link and from a tap on the landing page. The final
text passed §5.3, and its nouns have lines in §5.4: it keeps C's "queries" and "messages" and drops only "their" and
"drawn". §12 holds the before-readings.

**Left for the owner:** the after-reading of traffic and search from 2026-10-12, as context rather than a target
(§8), and a look on a real phone, logged out and signed in (F36).

## 14. Audit (2026-09-29)

The owner asked whether anything was missed or could be better. Five things were, all fixed at the owner's word
("do it all"); the package changes shipped in 3.33.0.

1. **The templates package's README had no demo line.** `Kronikol.Templates` (9,106 downloads) ships
   `templates/README.md` (`templates/Kronikol.Templates.csproj:11`), not `nuget-readme.md`, so F33's "every package"
   was wrong, and S3 left nuget.org's page for it without the line. It opens with S3's line now, and `DemoLinkTests`
   holds it (red before the line).
2. **Its twelve templates still carried the old name.** `dotnet new list`, an IDE's new-project dialog and that
   README's table named each "TTD Component Tests (...)", the initials of TestTrackingDiagrams, which Q5 had taken out
   of BreakfastProvider's description. Each is "Kronikol Component Tests (...)" in 3.33.0. `identity` and `shortName`
   are unchanged, so an installed template updates in place and every `dotnet new kronikol-*` command works as before.
   `TemplateNameTests` (new) holds the names and the README's table to them: red on 3.32.4, and a README row renamed
   away from its template fails it.
3. **The guard could pass with nothing checked.** §7 let a page that links no report pass, for H2 and H3, which were
   not taken. With the root as the homepage, a landing page reworded so that no link reads
   `href="reports/<lane>/TestRunReport.html"` left both loops empty and the check green. It now fails when the page
   links no report or no deep link, tries a failed fetch three more times 10 s apart (`--retry-all-errors`,
   `--max-time 60`), and names an address that did not answer. Run locally, as GitHub's `bash` runs it, against
   fixture sites and the live demo, ten cases each gave the intended answer: the root with F30's link passed; a page
   without the deep link, an id no scenario has, a red digest, a page linking no report, a 404 and a refused
   connection (after 38 s of retries) failed; live, the root passed, a missing path failed with 404, and a lane
   report failed as a page linking no report. actionlint 1.7.12 with shellcheck 0.11.0 passes it, and flags SC2013 on
   a copy with the first sketch's loop.
4. **BreakfastProvider's README linked Kronikol at a dead address**, `lemonlion.github.io/Kronikol`, which answers
   404 (F1). `8550978` points it at the repository. **Its homepage field was empty** although it has a Pages site;
   it is the demo's root now (set 2026-09-28, about 23:30 UTC).
5. **The record had not caught up.** `ROADMAP.md` Appendix C had no row for this plan's leftovers, and has one. F33,
   Appendix B's step 4, §2's phone note and the header's phone and card sentences are corrected against §13, F35
   and F36.

## Appendix A. The Kronikol4J ledger entries 3.31.10 owes

For `../Kronikol4J/docs/REMAINING_PARITY.md`'s divergence ledger. The session that made the fixes could not
reach that repository, so the entries wait for whoever works there next (the owner's choice, 2026-09-27). The
first is F28's; the second is a fault CI found in 3.31.9 while this release was being checked
(`INTERNAL_FLOW_BLOB_PLAN.md` §11.3).

> - **Long jumps land on their scenario (.NET 3.31.10, 2026-09-27).** .NET's `reveal_url_anchor`
>   (`report-url-hash-function.js`), `jump_to_next_failure` (`report-jump-to-failure-function.js`) and the
>   failure-cluster links' `onclick`, which `ReportGenerator` writes, no longer call
>   `scrollIntoView({ behavior: 'smooth' })`. They call a new `jump_into_view(target, block)`, defined in
>   `report-url-hash-function.js`, which jumps (`behavior: 'instant'`) and re-aims for up to ten frames
>   while the target's top moves: a smooth scroll aimed across features `content-visibility: auto` had not
>   drawn yet ran past its target to the bottom of the page. The cluster link's `onclick` fragment
>   `el.scrollIntoView({behavior:'smooth',block:'start'});` is now `jump_into_view(el,'start');`. And
>   `report-init-script.js` calls `parse_url_hash()` last in its `DOMContentLoaded` handler, after the two
>   phone-width blocks, where it was first. Copy the three scripts verbatim and move the generator's
>   `onclick` string with them. Whether the port's scripts are .NET's byte for byte today was not checked.
>
> - **The internal-flow element's list is counted over the page's own diagrams (.NET 3.31.10, 2026-09-27).**
>   .NET's run report chooses the `iflow-segments` element's list (`has` or `hidden`, whichever is shorter)
>   over the diagrams of the scenarios the page lists: `ReportGenerator` passes `WrapSegmentData` only the
>   diagrams whose test id is one of the features' scenario ids, with the component diagram, where it passed
>   every diagram the fetcher made. The element differs only for a run that logged tests its features do not
>   name. If the port counts over every diagram, count the same way; `InternalFlowSegmentMapReportTests` has
>   the fact.

## Appendix B. What 3.31.10 left for the owner (2026-09-27)

1. **Publish it.** The tag starts `release.yml`, which builds, runs `Kronikol.Tests`, pushes the packages to NuGet
   and makes the GitHub release with generated notes. Tag `28e4460`, the last commit 3.31.10 needs. It adds to
   `571a98d` only a test fix and its changelog line (2026-09-28: the code-cache fact lost a race to parallel renders
   in CI's Core Tests on a plan-only commit), so it builds the same packages, and its CI passed all 34 checks,
   Core Tests and CodeQL included (2026-09-28):
   `git fetch origin && git tag v3.31.10 28e4460 && git push origin v3.31.10`.
2. **The wiki note.** In `Generated-Reports.md`, section "Deep links (`#scenario-` and `#sid-`)", after the
   paragraph that ends "resolves to the first match in document order.", add:

   > Following a link opens every section around the scenario and puts the scenario in the middle of the screen at
   > once. Before 3.31.10 the report scrolled there smoothly, and in a report longer than a few features the scroll
   > ended past the scenario, at the bottom of the page: a feature the browser has not drawn yet counts as a 500 px
   > placeholder (`content-visibility: auto`), and the placeholders shrank to their true size as the scroll passed
   > them. On a phone it also ended above a scenario near the top, because the filter panel folds away as the
   > report loads. *Next Failure* and the failure-cluster links jump the same way since 3.31.10.

3. **Kronikol4J.** Copy the two ledger entries of Appendix A.
4. **The demo.** BreakfastProvider gets the fix with its next pin move (to 3.31.10 or later), which Q5's deep
   link waits for.

**Done 2026-09-28, steps 1 to 3** (a local session, at the owner's word). The tag `v3.31.10` on `28e4460` ran
Release `36404942578`: it passed, NuGet lists all 62 packages at 3.31.10, and GitHub has the release. The wiki
note is `5bc9589`, and the Kronikol4J ledger has both entries of Appendix A (`8001eba`, each marked "Not
mirrored, a ledger entry only" like its neighbours). Step 4 was done on 2026-09-28 as well: the demo moved to 3.32.4, where F30's deep link lands (§13).
