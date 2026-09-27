# The doorstep: the repository's homepage and description

**Date:** 2026-09-27 · **Repo version:** 3.31.3 (`fa866f8`) when written, 3.31.5 (`4aa1e1e`) at the first
same-day update, 3.31.9 (`e4c9e36`) at the second · **Status: plan written, NOT green-lit; S0 run as far as
a session can reach; the defect it found (F28) fixed in 3.31.10.** 3.31.10 is on `main` with CI green
(`571a98d`, 28 of 28 jobs) but not published: this environment refused to push the tag `v3.31.10` and the wiki
commit (HTTP 403, where branch pushes worked), so both are the owner's (Appendix B). It needs D24, and most of D24 is the owner's own words: the roadmap gives this text to
"the owner's hand". Roadmap item **2.1** (stage 2, track C). No package changes, so **no version bump**, and
nothing in it is outreach (rule 9).

**Updated twice the same day.** Once GitHub was reconnected, a session ran the parts of S0 it could reach
through GitHub's API and BreakfastProvider's source. Once the environment had full network access, a session
reached the live demo, the card image and nuget.org's search (§3, §12). Between the two, BreakfastProvider
moved to 3.31.4 (`INTERNAL_FLOW_BLOB_PLAN.md` S5, deployed 11:57 UTC), so S1 now waits only on D24 and the
owner's traffic reading.

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
  survive a search result's cut (F26), so the outcome has to lead (§2).
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
  started (RUN, F28).
- **The draft over-claims by one word.** "Every HTTP call" holds only for calls through a tracked client
  that are attributed to a test, and section 0 of the roadmap rules out a document that states a
  falsehood (F9). "Self-contained", the direction report's word for the file, is not true of the default
  report either: the page fetches the PlantUML engine from jsDelivr when it opens, and without it shows a
  render error where each diagram would be (RUN, F10).
- **The old name is a larger doorstep on nuget.org than the new one.** The TestTrackingDiagrams packages
  have 2.5 million downloads to Kronikol's 0.9 million, rank above Kronikol's on every search they share,
  are not deprecated, and send their project link to the old repository address (RUN, F24, F25).
  Deprecating them is already 13.4's; this plan only records it.
- **Fixed in this plan's commit:** the README's link to the BreakfastProvider repository had a doubled
  slash, and both links in that sentence spelt the name "BreakFastProvider" (F11).
- **What a session can check, and what only the owner can.** A session read the repository's fields, page
  and card, BreakfastProvider's source, nuget.org's records and search, and the live demo, and drew copies
  of two live reports in Chromium at both widths (§12). Left for the owner: traffic (the API refuses the
  session's token), GitHub's own search (outside the session's repository scope), where GitHub puts the
  About block on a phone (its script draws it, and the browser here cannot load GitHub's pages, F23), the
  time to the first diagram (the same browser cannot fetch the engine from jsDelivr), and the glance for
  anything private (not run by a session). The session's GitHub tools have no call that edits a
  repository's settings, so S1 and S2 are the owner's hands whatever D24 says.

---

## 0. Summary

Two repository fields change. Nothing in `src/`, no package, no report byte, no Kronikol4J ledger entry.

| Slice | What | Who | Time | Needs |
|---|---|---|---|---|
| S0 | Look first: the demo at two widths and the version that wrote it, the repository page as a logged-out visitor sees it, and the numbers only the owner can read. **Run by a session on 2026-09-27 as far as it reaches** (§3, §12) | the owner, for what is left | 10 min left | a browser and a phone; push access |
| S1 | Set the homepage | owner | 2 min | Q1, and S0's traffic reading first (F12). The demo's version is no longer a condition: it is 3.31.4 (F5) |
| S2 | Write and set the description | owner | 30 min | Q2, and S0 step 5's look at a phone first (F23) |
| S3 | *Optional.* The README's first line points at the demo | the owner's words, a session's commit | 10 min | Q3 |
| S4 | *Optional.* A weekly check that the homepage answers and the reports it links are green | a session | 45 min | Q4, after S1 |
| S5 | Record the before and after, strike the roadmap row, update the index | a session | 10 min | S1, S2 |

The roadmap's hour is S0, S1, S2 and S5. S3 and S4 are proposals, each a yes or no in D24. The demo's
upgrade is in neither the hour nor this plan, and it is done: S0 first found the demo on 3.29.0, and
`INTERNAL_FLOW_BLOB_PLAN.md` S5 moved its pins to 3.31.4 the same day (F5). S2 never waited: the words
change nothing the demo shows. The defect of F28 was Kronikol's, not this plan's, and it shipped as a patch
of its own, 3.31.10, the same day. The demo gets it with its next pin move, and Q5's deep link waits for that.

## 1. What was checked

Basis marks as in the roadmap, plus **REFERENCE**: GitHub's documented or well-known behaviour, not
checked today. Every REFERENCE row that the plan leans on is checked in S0.

Pages over https were read with `curl`, which trusts this environment's proxy. Chromium does not, so the
reports were drawn from copies fetched with `curl` and opened from `file://`. That draws the page as a
visitor gets it, except that the engine from jsDelivr cannot load, so no diagram is drawn and no timing is
a network's.

| # | Finding | Basis |
|---|---|---|
| F1 | The homepage field is empty. `has_pages` is false: Kronikol has no Pages site of its own, so the homepage can only point elsewhere. The dashboard plan's `lemonlion.github.io/Kronikol/` is not built (roadmap 9.3; it answers 404) | RUN: `api.github.com/repos/lemonlion/Kronikol`, 2026-09-27, again after 3.31.9 |
| F2 | The description is `A mechanism for tracking the request-responses in your tests and converting them into PlantUML diagrams.`, 104 characters. It names a mechanism, uses a word nobody searches for ("request-responses") and gives the drawing tool as the outcome. It does not say .NET, that the result is an interactive report, that capture is automatic, or that databases and message brokers are drawn as well as HTTP | RUN, same call |
| F3 | Twenty topics, which is GitHub's maximum (REFERENCE). The direction report calls them "done right", and adding one means removing one, so this plan changes none. `plantuml`, `plantuml-diagrams`, `plantuml-generator` and `sequence-diagrams` keep those words findable if the description drops them | RUN, same call |
| F4 | The demo is `https://lemonlion.github.io/BreakfastProvider/`, a Pages site built by the `deploy-pages` job of BreakfastProvider's `ci-main.yml` on every push to `main` and daily at 03:00 UTC. Its root is a landing page the job writes inline, titled `Breakfast Provider — Kronikol`: a hero ("Breakfast Provider", "Component Test Reports & API Documentation", a "Powered by Kronikol" badge), six cards linking `reports/<framework>/TestRunReport.html` (the in-memory lanes of ReqNRoll, LightBDD, BDDfy, xUnit, TUnit and NUnit), the OpenAPI and AsyncAPI viewers under `api/`, and the two source repositories. The twelve docker and external-SUT reports (`reports/docker/<framework>`, `reports/docker-sut/<framework>`) are published and linked from nowhere. The live root is the job's text byte for byte, bar a final newline | READ: BreakfastProvider's `ci-main.yml` at `9f242d4` (a clone). RUN: the live root |
| F5 | The demo is on 3.31.4. On the morning of 2026-09-27 BreakfastProvider's `main` was `0d29537` (2026-09-23), with all 30 pins at 3.29.0 (27 package references and three `Kronikol.Tool --version` steps), seventeen releases back. `INTERNAL_FLOW_BLOB_PLAN.md` S5 moved all 30 to 3.31.4 in `9f242d4`, `CI: Main` went green twice, and the site redeployed at 11:57 UTC: each of the 18 reports carries `Kronikol v3.31.4`, and each digest names `Kronikol 3.31.4+81f77b58` (F20). So the fixes a visitor would meet on a first look are live: the toolbar at every width (3.29.2), parameter tables and detail panels at tablet and phone widths (3.29.3), long tokens scrolling the page sideways (3.29.4), the note header's contrast and a link's hover colour (3.30.0), and a report 37% smaller (3.31.4). The five releases since are fixes (3.31.5 to 3.31.8; 3.31.6 was tagged and never published) and a smaller file again (3.31.9: the xUnit lane's download 1.16 to 0.90 MB). Moving the demo to them is the next consumer release's business, not a condition of S1 | RUN: the clone, a `git grep` of the pins at both commits, the live site. PLAN: `INTERNAL_FLOW_BLOB_PLAN.md` §11.1. READ: CHANGELOG 3.31.5 to 3.31.9 |
| F6 | Nothing watches the link. None of this repository's four workflows mentions `github.io`, and none of BreakfastProvider's ten does either: its `post-deployment-tests.yml` tests the API server, not the Pages site. `deploy-pages` needs all 18 lane jobs and both unit-test jobs, with no `always()`, so a lane job that fails holds the deploy back and the last site stays up (READ; that Pages keeps serving it is REFERENCE). A lane job fails only on a failure its history ledger has not seen before (`kronikol history gate` in `_tests.yml`): a failure that persists is known on its second run, and from then on it is published, as the docker lanes' three are (F20). A renamed lane, a moved path, a renamed repository or Pages switched off would send every visitor to a 404 | READ: both repositories' workflows, BreakfastProvider's `ci-main.yml` and `_tests.yml` at `9f242d4`; INFERRED for the failure modes |
| F7 | The README links the demo twice, both inline: the first paragraph's words "rich interactive HTML reports" (`README.md:12`), and a bold sentence under Example Output (`:33`) saying the picture beneath it is "just a very simple static example". That picture is an image uploaded to GitHub, and it links to a plantuml.com server URL, so the README's one picture of the product sends a click to the site the ad audit measured (roadmap D.3) | READ |
| F8 | Tells are already at the door. The direction report's own draft has an em dash (`see what your integration test actually did — every HTTP call, SQL query and message, as a sequence diagram`; the roadmap's row already turned it into a colon). The README's opening paragraphs have three (`README.md:10`, `:14`, `:16`), and the demo's landing page title one (F4) | READ; RUN for the live title |
| F9 | "Every" cannot be proved. A call is drawn when it goes through a client that has its extension and is attributed to a test. A client without one is not drawn, and identity-less background work that names no document still lands nowhere (roadmap Appendix C). The nouns can be proved (§5.4) | READ: `src/` (62 packages), `README.md:14`, roadmap Appendix C |
| F10 | "Self-contained" is not true of the default report. The default rendering is `BrowserJs` (`ReportConfigurationOptions.cs:133`), and since 3.31.1 that page fetches the PlantUML engine from `cdn.jsdelivr.net` when it opens: `@plantuml/core@1.2026.8`'s `plantuml.js` (3,947,570 bytes, 1,078,370 as jsDelivr sends it) and `viz-global.js` (1,445,436 bytes, about 0.6 MB sent), which a browser then keeps for a year (`max-age=31536000, immutable`). A page that cannot fetch them shows, where each diagram would be, "Render error: PlantUML engine unavailable: failed to load https://cdn.jsdelivr.net/npm/@plantuml/core@1.2026.8/plantuml.js". Keep the word, and "offline", out of the description | READ: `ReportConfigurationOptions.cs:133`, `TrackingDefaults.PlantUmlJsCdnBase`. RUN: the engine's sizes and headers; the render error, drawn by a copy of a live report in a browser that cannot reach jsDelivr |
| F11 | `README.md:33` linked the BreakfastProvider repository as `https://github.com/lemonlion//BreakfastProvider/`, with a doubled slash, and spelt the name "BreakFastProvider" in both of its links. **Fixed in this plan's commit** (documentation only, no bump). Whether GitHub resolved the doubled slash is still unknown: this environment's proxy refuses to forward such a path (`Request path could not be canonicalized`), with full network access too. The corrected form is right either way | READ; RUN for the proxy's answer |
| F12 | Traffic (views, unique visitors, referring sites, popular content) is shown only to people with push access, and only for the last 14 days (REFERENCE). The traffic API refuses the session's token (403, `Resource not accessible by integration`) and an anonymous call (403). The direction report's baseline, read 2026-09-12: 31 unique visitors in 14 days; referrers GitHub 7, NuGet 2, Google 1, DuckDuckGo 1, ChatGPT 1. A before-reading has to be taken by the owner on the day of S1, or it is gone | PLAN (the report); REFERENCE; RUN for the refusals |
| F13 | A search across all of GitHub is outside this session's repository scope, so the GitHub half of the search baseline is the owner's. The web half was taken with the session's search tool (F17). A second engine, DuckDuckGo's HTML page, answered with a bot challenge, and a session does not go round one. nuget.org's half is F24 | RUN |
| F14 | A GitHub release is published for each tag (the latest, v3.31.9, at 13:46 UTC on 2026-09-27), so the About column's release box is current. Recorded so nobody re-checks it | RUN: `api.github.com/repos/lemonlion/Kronikol/releases/latest` |
| F15 | The repository page's title is `GitHub - lemonlion/Kronikol: <description> · GitHub`; `og:title` and `twitter:title` are the same without ` · GitHub`, and `og:description`, `twitter:description`, `og:image:alt` and the meta description are `<description> - lemonlion/Kronikol`. `og:image` is GitHub's generated card (`opengraph.githubassets.com/<hash>/lemonlion/Kronikol`, `twitter:card` `summary_large_image`), so no custom social preview is set, which answers S0 step 2. The card itself is a 1200 × 600 PNG: the name, the whole description on three lines of 44 to 49 characters, the owner's avatar, and four counts (2 contributors, 1 "used by", 17 stars, 1 fork) | RUN: the page's HTML and the card image, 2026-09-27 |
| F16 | nuget.org's latest Kronikol is 3.31.9. Its project URL is `https://github.com/lemonlion/Kronikol` (`Directory.Build.props:10`), so a NuGet visitor's project link lands on this doorstep too. Its description (`src/Kronikol/Kronikol.csproj:9`) is mechanism-first as well, and opens with the old name: "Kronikol, formerly TestTrackingDiagrams. Autogenerate PlantUML sequence diagrams from your component and acceptance tests. Tracks HTTP calls, database operations (Cosmos DB, SQL via EF Core), Redis commands, events/messages, and arbitrary method calls, then converts them into searchable HTML reports and structured data files." Package metadata, so out of scope (§10). The `Kronikol` package has 127,720 downloads, and the 62 Kronikol packages 893,458 | RUN: nuget.org's registration index and search API; READ |
| F17 | Web search, before (the session's search tool, US results, 2026-09-27). For "Kronikol", the Java port's repository comes first, titled "A Java port of the .Net Kronikol project that tracks your test flow at run time and turns it into interactive diagrams", then this repository under today's title, then release pages and issue #78. For the five phrasings of S0 step 3, Kronikol is in none of the results: "reqnroll report" returns ReportPortal, Allure and Reqnroll's own reporting pages, and "sequence diagram tests .NET" returns papers. The demo is not found by name: "BreakfastProvider Kronikol test reports" returns BreakfastProvider's repository first, described as "An example API showcasing technologies I've created (TTD/InMemoryEmulators)", then this repository's issues and the repository itself, and no page of the Pages site. "TestTrackingDiagrams" returns the old address, `github.com/lemonlion/TestTrackingDiagrams`, first, under today's description | RUN |
| F18 | The landing page's first look, drawn from its source (the text the job writes, which is the live page, F4) in Chromium through Playwright: nothing scrolls sideways at 1280 × 800 or at 390 × 844, and the first report card's top sits at 367 px and 468 px, inside the first screen at both. Kronikol appears only in the "Powered by Kronikol" badge, the source card and the footer, and the first paragraph is insider language (the history ledger, a sparkline, `$flaky`, `Failures.md`, `ctrf-report.json`) | RUN |
| F19 | A trap for whoever measures at phone width: headless Chromium's `--window-size` lays a page out no narrower than 500 px and crops the screenshot, so a 390 px check needs a real viewport (Playwright's, or DevTools' device mode). The first attempt here showed the landing page clipped at 390 px; measured with a real viewport, nothing overflows | RUN |
| F20 | The live site, 2026-09-27, as `CI: Main` run `36316716498` left it (11:57 UTC). The root (6,206 bytes, `max-age=600`) and all 18 reports answer 200 over https with no cookies. The root without its final slash answers 301 to the slash form; `lemonlion.github.io/` and `.../BreakfastProvider/demo/` answer 404. The six linked in-memory reports are 3.9 to 5.5 MB, 0.91 to 1.23 MB as Pages sends them (xUnit 4,962,030 and 1,179,176 bytes); the docker ones 3.5 to 6.2 MB (1.0 to 2.1 MB), the external-SUT ones 1.7 to 2.4 MB (0.40 to 0.48 MB). Beside each report sit its `Failures.md`, `Failures.jsonl`, `CLAUDE.md`, `AGENTS.md`, `TestRunReport.json` (6.4 MB for xUnit) and `ctrf-report.json`. The digests open `# No failures` on the six in-memory and the six external-SUT lanes, and `# Failures` with 3 of 178 to 205 scenarios on each docker lane: the three Orders scenarios the consumer's Azure emulator has failed since `7a0fb9b` (PLAN: `INTERNAL_FLOW_BLOB_PLAN.md` §11.1). The API viewers, the icon and the Kronikol link on the root answer 200; its two BreakfastProvider links are outside what this environment's proxy serves | RUN |
| F21 | A report's first look, drawn from copies of the live xUnit and ReqNRoll reports in Chromium through Playwright (1280 × 800; and 390 × 844 as a phone, with the CPU slowed four times): nothing scrolls sideways at either width (a wide parameter table scrolls inside its own box). The first screen is the title, the folded Features Summary and the execution summary ("Passed", 67 features, 203 scenarios) at 390 px, with the CI box and the filters beside them at 1280 px. All 67 features start collapsed, and so does each scenario, so no diagram is on a report's first screen at either width. The first diagram is three taps from the landing page (a report, a feature, a scenario), and at 390 px the first feature is a screen below the top. How long it takes to draw is the owner's to time: the browser here cannot fetch the engine (F10) | RUN |
| F22 | Deep links. A report opened at `TestRunReport.html#sid-<stableId>` (or `#scenario-<slug>`) opens every section around that scenario and scrolls it to the middle of the screen, the scenario opened (`report-url-hash-function.js`). On a copy of the live xUnit report at 1280 × 800, `#sid-83cb467c01ca1187` landed with the scenario's title and diagram box in the first screen. The id hashes the suite, the feature and scenario names, the outline and the example values (`ScenarioStableId.Compute`), so it survives reruns and releases and moves on a rename; an id that matches nothing leaves the visitor at the top of the report | READ; RUN for the landing. At 390 px, and far down a long report, see F28 |
| F23 | The About block on a phone. The repository page's HTML holds one visible copy of the description, in the sidebar's About section, which carries `hide-sm hide-md`: GitHub's stylesheet hides those at 543.98 px and below and from 544 to 767.98 px, so below 768 px. The HTML served to a phone's user agent is the same and holds no other copy, so whatever a phone shows is drawn by the page's script, which the browser here cannot load. §2's "above the file list" is therefore unconfirmed, and S0 step 5's phone look is the owner's | RUN: the page's HTML for a desktop and a phone user agent, GitHub's stylesheets |
| F24 | nuget.org's search, before (its search API, stable versions, 2026-09-27). The three problem phrasings find nothing at all: its search matches words, not questions. "integration test diagram" and "sequence diagram tests" return the old name's package first and Kronikol's second; "plantuml" has `TestTrackingDiagrams.PlantUml.Ikvm` second; the first Kronikol or TestTrackingDiagrams package is sixth for "sequence diagram", ninth for "reqnroll report" and fortieth for "test report html"; "living documentation" has neither in its first hundred | RUN |
| F25 | The old name on nuget.org. The 56 `TestTrackingDiagrams` packages have 2,520,149 downloads against the 62 Kronikol packages' 893,458 (the main packages 463,560 and 127,720). Their last version is 2.37.4 (2026-05-17), none is deprecated, and their project URL is `github.com/lemonlion/TestTrackingDiagrams`, which reaches this repository only through GitHub's redirect for a renamed repository (REFERENCE: this environment's proxy answers only for the session's own repositories, so the redirect was not checked). Deprecating them is 13.4's (`LLM_FIRST_PLAN.md` §14.3's outward steps) | RUN: nuget.org's search API and registration index |
| F26 | How much of the description a search result keeps. Measured with Arial's metrics at 20 px (the metric-compatible Liberation Sans here), a title cut at 600 px (REFERENCE: the width a desktop result is commonly cut at) holds the 29-character prefix (255 px) and 34 to 38 characters of each draft, then an ellipsis. Today's keeps "A mechanism for tracking the reques", and draft B "See what your .NET integration tests". A phone result wraps the title onto a second line instead (REFERENCE) | RUN for the measurement; REFERENCE for the cut |
| F27 | The README's links, 2026-09-27: 119 of its 152 answered 200 (59 badges, none drawing an error; 57 nuget.org pages; ctrf.io; the plantuml.com link; the demo). The 29 wiki links name a page and a heading that exist in the wiki at `86a77c1`. The CI badge and workflow link are refused by this environment's proxy (the workflow is `.github/workflows/ci.yml`), and the image and the BreakfastProvider link are outside what it serves. Nothing is broken | RUN |
| F28 | **A defect, fixed in 3.31.10: a `#sid-` link, Next Failure and a failure-cluster link landed past their scenario.** Two causes. Every feature and scenario is drawn with `content-visibility: auto`, whose placeholder is 500 px for a feature (about 532 px with its padding, where a closed feature is about 60) and 150 px for a scenario, until the browser first draws it. The three jumps scrolled smoothly, and a smooth scroll is aimed once, across those placeholders: as it passed them they were drawn at their true size, and it ran on to the bottom of the page. On the live xUnit report a link into the 40th of 67 features ended with the scenario 1,766 px above the screen at 1280 px and 1,267 px at 390 px. And at 768 px and below, `report-init-script.js` called `parse_url_hash()` first and only then folded the filter panel (559 px there) away, so even a link into the first feature ended 220 px above the screen at 390 × 844; `parse_url_hash` itself reveals its anchor last so that "the scroll has to land on the layout the filters produced", and the init script broke that rule. 3.31.10 routes the three jumps through `jump_into_view(target, block)` in `report-url-hash-function.js`, which jumps (`behavior: 'instant'`) and re-aims for up to ten frames while the target's top moves (the features it lands among are drawn in the frames after it, and Safari has no scroll anchoring to absorb that), and calls `parse_url_hash()` last in the init handler. On a copy of the live report with the change, links into the first, 40th and last features land with the scenario's title on screen, -15 to 85 px from the middle, at both widths. The keyboard and table-reference jumps move a short way and stay smooth. Tests: four Playwright facts, each red on 3.31.9 (a link into the 30th of 60 features; a link into the first feature at 390 px; Next Failure and a cluster link into the 30th feature). Report output changes, so the Kronikol4J ledger owes the entry of Appendix A | RUN: copies of the live report, as found and with the change; the new facts on 3.31.9 and 3.31.10. READ: `stylesheets.css`, `report-init-script.js`, `report-url-hash-function.js` |

## 2. Where the two fields show

The case for writing the description carefully is that it is read in five places, and in two of them it is
cut short. The title, the card's tags and the card's image were read on 2026-09-27 (F15), and the cut was
measured (F26). The rest is REFERENCE, which S0 step 5 checks before S2.

| Where | Homepage | Description | Note |
|---|---|---|---|
| The About column (desktop) or its phone form | yes, as the bare URL with a link icon | yes, whole | The desktop copy is in the sidebar, which GitHub hides below 768 px; where a phone shows it is drawn by GitHub's script and unconfirmed (F23) |
| The page title: browser tab, bookmarks, search engine result | no | yes: `GitHub - lemonlion/Kronikol: <description> · GitHub` (RUN, F15) | 29 characters go to the prefix, and about 35 of the description survive a desktop result's cut (F26) |
| The link card GitHub draws for a shared repository link (Slack, Teams, X, LinkedIn) | no | yes, unless a custom social preview is set, and none is (RUN, F15) | The tags carry it, and the image prints it whole on three lines beside the owner's avatar and four counts (RUN, F15). Unfurlers cache cards for days, so a change shows late |
| GitHub's repository search results and profile lists | the direction report says the homepage shows in search results (PLAN) | yes | Default repository search matches the name, the description and the topics |
| The REST API | `homepage` | `description` | RUN. How S1, S2 and S4 are verified |

What the homepage display means for the URL: the About column shows the address itself, so
`lemonlion.github.io/BreakfastProvider` reads as a project's site and a lane report's path reads as a file
path. Nothing in the About column says "demo", which is one argument for S3.

nuget.org shows neither field. A package page shows the package's own description and project URL, which
ship in a package and so in a release (§10). The old name's packages send theirs to the old address (F25).

## 3. S0: look first (the owner, about 20 minutes)

In a logged-out browser window unless the step says otherwise. Each step writes one row of §12.

**Run by a session on 2026-09-27, as far as it could reach:** step 2 whole; step 3's web and nuget.org
halves; step 4 but for the first diagram's time and the glance for anything private; step 5's title, card
tags and card image. **Left for the owner:** step 1, step 3's GitHub half, step 4's time and glance, and
step 5's About block on a phone.

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
   | It reaches a diagram | a report in one click from the root; a drawn diagram on a report's first screen, with the time it took on the owner's connection noted | Half. A report is one click away (F18), but its first screen holds no diagram at either width, and the first is two taps further in, a feature and a scenario (F21). A `#sid-` link lands on one (F22), at a phone's width and far down a long report only from 3.31.10 (F28), which the demo does not have yet. The time is the owner's: the first diagram costs the report (1.18 MB for xUnit) and, once, the engine (about 1.7 MB) |
   | Nothing is broken | no failing scenario, no render-error picture, nothing clipped or scrolling sideways at 390 px | The six linked reports are green, and the two drawn scroll nothing sideways at either width (F20, F21). `popup-smoke.js` drew every popup, with no console error, on the six in-memory lanes and the xUnit docker lane of the published 3.31.4 site (PLAN: `INTERNAL_FLOW_BLOB_PLAN.md` §11.1). A drawn diagram needs a browser that reaches jsDelivr: the owner's |
   | It is current | the version the lane's `Failures.md` names (`Kronikol <version>.` on a green run, `Written by Kronikol <version>.` when something failed), which sits beside the report | **Yes, since 11:57 UTC: 3.31.4** (F5, F20) |
   | Nothing leaks | open two notes and the headers of one request. The site is already public and linked from the README, so this is a glance, not an audit | Not run by a session. The owner's glance |

5. **Where the description shows** (§2): the tab title, the About column at both widths, and the card
   (paste the repository URL into any unfurling chat box, or read the page's `og:image`). If any row of
   §2 is wrong, correct §2 before S2, because the brief leans on it. **Done 2026-09-27:** the title, the
   card's tags and image (F15), and where the desktop copy sits (F23). **Left:** the About block on a
   phone, which GitHub's script draws (F23).

## 4. S1: the homepage (the owner, about 2 minutes)

| Option | For | Against |
|---|---|---|
| **H1. The site's root**, `https://lemonlion.github.io/BreakfastProvider/` | The URL that already exists and is linked from the README. It survives lanes being renamed or added. It reads well in the About column. It names the frameworks, so it shows breadth before the first click, which is section 0's worry (F18) | One click short of the artifact, and three taps short of a diagram (F21). Its title carries an em dash, and its hero names Kronikol only as "Powered by" (F4, F18, Q5) |
| **H2. One lane's report** | Lands inside the artifact: the direction report's "thirty seconds inside a real report and it is obvious". It lands on the summary, though, not a diagram, unless the URL carries a `#sid-` fragment (F21, F22) | A long URL that reads as a file path, longer with a fragment. It is tied to a lane path that BreakfastProvider's CI owns, so it is the first thing to break (F6). About 1.2 MB before the page shows and 1.7 MB more for the first diagram, on a phone's network (F10, F20). One framework, when the visitor may use another |
| **H3. A stable alias**, such as `https://lemonlion.github.io/BreakfastProvider/demo/`, written by BreakfastProvider's deploy job as a page that forwards to the chosen lane | H2's landing with H1's stability. The lane can change without the homepage changing | A change in another repository's workflow; Pages serves no redirects of its own (REFERENCE), so the alias is a page. `/demo/` answers 404 today (F20) |
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
the dialog's option to use one does not apply. Keep the final slash (F20).

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
6. **Short.** GitHub's own cap (350 characters, REFERENCE, not checked) is far above every draft. The limit
   that matters is the card and the phone block; the drafts below run 107 to 124 characters, and the card
   prints today's 104 whole on three lines (F15).

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

- **A** is the roadmap's form of the direction report's draft. It fails brief 2 and brief 4 ("every").
- **B** keeps the report's framing and fixes both. **Recommended as the starting point** for the owner's
  rewrite. It is also the only draft whose kept part ends on a whole phrase.
- **C** names the report as well as the diagram (brief 3), at the cost of ten characters, and says
  "queries" so databases other than SQL ones are covered.
- **D** is plain rather than outcome-first. It leads with the words people search for ("sequence
  diagrams") but gives up the contrast the direction report and 13.1 build on: your tests pass, and here
  is what they did.

A choice inside every draft: "SQL queries" is concrete and is what people type; "queries" or "database
queries" is true of Cosmos DB, MongoDB, DynamoDB, Redis and the rest as well (§5.4).

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

Every noun a draft uses, and what makes it true today.

| Word | True because | Basis |
|---|---|---|
| .NET | Every package in `src/` is .NET. The Java port is Kronikol4J, another repository | READ |
| tests, integration tests | The core package and the adapters for xUnit 2 and 3, NUnit 4, MSTest, TUnit, ReqNRoll, LightBDD and BDDfy. The README says "integration/component test" | READ: `src/`, `README.md:12` |
| HTTP calls | The first thing the README lists among what is tracked | READ: `README.md:14` |
| SQL queries | `SqlClient`, `Npgsql`, `MySqlConnector`, `Oracle`, `Sqlite`, `EfCore.Relational`, `Dapper`, three ClickHouse drivers, `BigQuery`, `Spanner` | READ: `src/` |
| queries, database queries | The above, and `CosmosDB`, `MongoDB` (two), `DynamoDB`, `Redis`, `Elasticsearch`, `Bigtable`, `AtlasDataApi` | READ: `src/` |
| messages | `ServiceBus`, `EventHubs`, `Kafka`, `SQS`, `SNS`, `EventBridge`, `PubSub`, `MassTransit`, `StorageQueues` | READ: `src/` |
| sequence diagrams | Each scenario's diagram is a PlantUML sequence diagram; activity and component diagrams are drawn as well | READ: `README.md:12` |
| HTML report, interactive | "rich interactive HTML reports" | READ: `README.md:12` |
| every, each, all | Not provable (F9) | |
| self-contained, offline | False by default (F10) | |

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

The line points at the root, not at a scenario. A `#sid-` link (F22) would land the reader on a diagram,
but the root survives a renamed scenario and a renamed lane, and before 3.31.10 the deep link landed past
its scenario (F28). The landing page is where a deep link belongs (Q5).

S3 and S4 are the only parts of 2.1 that touch a file (the roadmap's §5 counts 2.1 as settings alone). S3
edits `README.md`, which 2.2's PR #73 edits as well; the roadmap found that branch merging clean with
`main` on 2026-09-27, so whichever lands second takes a one-line merge.

## 7. S4, optional: the guard (Q4)

Rule 3: a net goes up before the work it protects. What F6 lists can break the homepage without anyone in
this repository changing anything.

| Where | Catches | Cost |
|---|---|---|
| **A scheduled workflow in this repository** | Everything in F6; an emptied or mistyped homepage field, because it reads the field from the API each time; and a red report on the page the homepage links (F20) | One file, about 30 lines. A failed scheduled run is emailed to whoever last edited the schedule (REFERENCE) |
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
      - name: The repository's homepage answers, and the reports it links are green
        shell: bash
        env:
          GH_TOKEN: ${{ github.token }}
        run: |
          url=$(gh api "repos/${{ github.repository }}" --jq .homepage)
          test -n "$url" || { echo "::error::The homepage field is empty"; exit 1; }
          code=$(curl -sS -L -o page.html -w '%{http_code}' "$url")
          test "$code" = 200 || { echo "::error::$url answered $code"; exit 1; }
          grep -qi kronikol page.html || { echo "::error::$url answered, but not with a Kronikol page"; exit 1; }
          for report in $(grep -oE 'href="reports/[^"]+/TestRunReport\.html"' page.html | sed -E 's/^href="//; s/"$//'); do
            digest="${url%/}/${report%TestRunReport.html}Failures.md"
            curl -sS -f -o digest.md "$digest" || { echo "::error::$digest did not answer"; exit 1; }
            first=$(head -n 1 digest.md)
            test "$first" = "# No failures" || { echo "::error::$digest reads: $first"; exit 1; }
          done
```

**Dry run, 2026-09-27** (RUN: the step's commands under `set -eo pipefail`, with the URL passed in, since the
field is still empty). Against the live root it passed, reading `# No failures` from all six linked
digests. Against `.../BreakfastProvider/demo/` it failed with 404. Given a page that links the docker xUnit
lane, it failed on that digest's `# Failures` line. Given a page that links no report, as a lane URL under
H2 or H3 would be, it passed on the page alone.

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

**What cannot be measured, said plainly.** GitHub does not count clicks on the homepage link, and a Pages
site has no analytics (REFERENCE), so whether visitors follow the link is not observable from either
repository. The item's success is binary: the two fields are set, true, free of tells, and the link is
alive.

## 9. Risks

| Risk | Answer |
|---|---|
| The demo shows fixed defects to every visitor (F5) | It did until 11:57 UTC on 2026-09-27; since then it is 3.31.4. The releases since change the file's size, not the first look |
| The link breaks later (F6) | S4, or at worst the next time someone opens the About column |
| A bad night: the demo shows a failing run | A new failure holds the deploy back, so the last site stays up; a failure that persists is published from its second run, as the docker lanes' three are (F6, F20). The six linked reports are green today, and S4's digest step would catch one that is not within a week |
| The description over-claims (F9, F10) | §5.4, and the check's words to prove |
| The description reads as generated | §5.3, and the owner's hand |
| It becomes outreach | Nothing here announces anything. Editing a repository's fields notifies nobody (REFERENCE). The topics stay as they are |
| A phone visitor waits on a large download | Measured by size: a linked report is 0.91 to 1.23 MB as sent, and the first diagram adds the engine, about 1.7 MB once and then kept for a year (F10, F20). Under H1 the visitor picks a report before paying for one. The time is the owner's |
| A visitor's network blocks jsDelivr | Every diagram becomes a render error naming the engine's address (F10). Accepted for the homepage: it is the product's default, and the page says what failed. Drawing the demo without the CDN is BreakfastProvider's configuration, not this plan |
| A visitor finds no diagram on the first screen | Three taps to the first one (F21). Q5's deep link on the landing page makes it one, once the demo is on 3.31.10 or later (F28) |
| Something private in the demo | It is public today and linked from the README, so pointing the homepage at it adds visitors, not exposure. The owner glances anyway (S0 step 4) |

## 10. Out of scope, and where it lives

| Item | Where |
|---|---|
| The README as a landing page: twenty-five words, one screenshot of the new look, depth below the fold, and the em dashes of its opening | 13.1 |
| A custom social preview image. A screenshot now would show the look that stage 6 and 12.1 replace; until then GitHub's own card carries the new description | 13.1 |
| The topics | Unchanged: at the maximum, and "done right" (F3) |
| nuget.org's package description and project URL | Package metadata, so it ships in a release. The project URL already points here, and the description is mechanism-first and opens with the old name (F16). If the owner wants the new words there too, they ride along with the next release; they are not a reason for one |
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
| Q4 | A guard on the homepage? | Yes, the weekly workflow in this repository, reading the reports the homepage links as well as the page (§7). Dry-run on 2026-09-27; proved by one failing run before it is kept, and committed after S1 |
| Q5 | If H1: the landing page's title (`Breakfast Provider — Kronikol`), a line on that page saying what it is to someone arriving from Kronikol, and a link to one scenario by `#sid-` so a diagram is one tap from the root rather than three (F21, F22). And BreakfastProvider's own description, which still names Kronikol by its old initials (F17) | All of it, in BreakfastProvider (its workflow and its settings), in a commit of its own, in the owner's words as for S2; the deep link once the demo is on 3.31.10 or later (F28). The title is the browser tab's text for every visitor the homepage sends (F8), the hero names Kronikol only as "Powered by" (F18), and the landing page's source card leads to that description |

## 12. Results (filled in at execution)

| Reading | Before (S0) | After |
|---|---|---|
| Homepage | empty (RUN 2026-09-27, again after 3.31.9) | |
| Description | `A mechanism for tracking the request-responses in your tests and converting them into PlantUML diagrams.` (RUN 2026-09-27) | |
| Unique visitors and views, 14 days | 31 unique on 2026-09-12 (PLAN, the direction report). Today's is the owner's: the traffic API refuses the session's token and an anonymous call (F12) | |
| Referring sites | GitHub 7, NuGet 2, Google 1, DuckDuckGo 1, ChatGPT 1 on 2026-09-12 (PLAN). Today's is the owner's | |
| Search positions (S0 step 3) | The web (F17): "Kronikol" second, behind Kronikol4J; the five phrasings, absent; the demo, not found by name. nuget.org (F24): the three questions find nothing; "plantuml" second, "sequence diagram" sixth, "reqnroll report" ninth, and the old name ahead of the new on every query they share. GitHub's search: the owner's | |
| Social preview | GitHub's generated card, no custom image; it prints the whole description on three lines (RUN, F15) | |
| Public counts | 17 stars, 1 fork, 2 watchers, 2 contributors, 1 "used by" (RUN). nuget.org: `Kronikol` 127,720 downloads, the 62 Kronikol packages 893,458, the 56 TestTrackingDiagrams packages 2,520,149 (RUN, F16, F25) | |
| The demo (S0 step 4) | 3.31.4 since 11:57 UTC, 3.29.0 before (RUN, F5). The root and all 18 reports answer 200; the six linked reports are green (F20); nothing scrolls sideways at either width; no diagram on a report's first screen, the first three taps from the root (F21). The first diagram's time and the glance: the owner's | |
| nuget.org | 3.31.9 latest; the project URL is the repository; the description is mechanism-first (RUN, F16) | |
| S3 and S4 | S4's check dry-run on the live site (RUN, §7) | |

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
   and makes the GitHub release. Tag the commit CI passed, not a later one:
   `git fetch origin && git tag v3.31.10 571a98d && git push origin v3.31.10`.
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
