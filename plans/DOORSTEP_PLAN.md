# The doorstep: the repository's homepage and description

**Date:** 2026-09-27 · **Repo version:** 3.31.3 (`fa866f8`) when written, 3.31.5 (`4aa1e1e`) at the
same-day update · **Status: plan written, NOT green-lit; S0 partly run.** It needs D24, and most of D24
is the owner's own words: the roadmap gives this text to "the owner's hand". Roadmap item **2.1** (stage
2, track C). No package changes, so **no version bump**, and nothing in it is outreach (rule 9).
**Updated the same day**, once GitHub was reconnected: a session ran the parts of S0 it can reach (§3,
§12). The demo is on 3.29.0, seventeen releases back, and moving it is already S5 of
`INTERNAL_FLOW_BLOB_PLAN.md`, so S1 waits for that.

The roadmap's row, whole: "**The doorstep.** The repository's homepage field is empty (RUN): point it at
the live BreakfastProvider report. Rewrite the description from mechanism to outcome; the direction
report's draft is 'see what your integration test actually did: every HTTP call, SQL query and message,
as a sequence diagram'. About an hour. Passive: it changes what the fortnight's 31 visitors see and
invites no more. Outward-facing text: the owner's hand, and no em-dashes or other tells."

It is the first rung of the distribution ladder in "Where Kronikol Goes Next" (roadmap D.1), headed "Put
the demo where people land". That rung had three parts: the homepage field, the description, and the demo
hoisted to the README's first line with the real report as its first image. The roadmap kept the first
two here and put the third into 13.1, the README as a landing page. What this plan adds to the row:

- **The premises hold** (RUN, 2026-09-27). The homepage is empty, the description is word for word the one
  the direction report quoted on 2026-09-12, and the repository has twenty topics.
- **The description is read in more places than the About box.** GitHub puts it in the repository page's
  title, which is the line a search engine shows, and in the link card's tags (RUN, F15; a web search
  returns that title word for word, F17). Roughly the first 25 to 30 characters survive in a search
  result, so the outcome has to lead (§2).
- **The demo is another repository's output.** BreakfastProvider's CI rebuilds the site on every push and
  every night: a landing page, 18 reports and two API viewers (READ). Nothing in either repository checks
  the URL (READ), so choosing it is also choosing what can break (F6). S4 offers a guard.
- **The demo is seventeen releases behind, and moving it is already planned.** BreakfastProvider's `main`
  (`0d29537`, 2026-09-23) pins all 30 of its Kronikol references at 3.29.0 (RUN), and the nightly of
  2026-09-27 served 3.29.0 (PLAN). A visitor at a tablet or phone width would meet defects that 3.29.2 to
  3.29.4 fixed. Moving the pins is S5 of `INTERNAL_FLOW_BLOB_PLAN.md`, green-lit on 2026-09-27, so S1
  waits for it and this plan bumps nothing itself (F5).
- **The draft over-claims by one word.** "Every HTTP call" holds only for calls through a tracked client
  that are attributed to a test, and section 0 of the roadmap rules out a document that states a
  falsehood (F9). "Self-contained", the direction report's word for the file, is not true of the default
  report either: the page fetches the PlantUML engine from jsDelivr when it opens (F10).
- **Fixed in this plan's commit:** the README's link to the BreakfastProvider repository had a doubled
  slash, and both links in that sentence spelt the name "BreakFastProvider" (F11).
- **What a session can check, and what only the owner can.** With GitHub reconnected, a session read the
  repository page's title and card tags, BreakfastProvider's source and nuget.org's record, and drew the
  demo's landing page from its source at both widths (§12). The live demo stays out of reach: this
  environment's network policy refuses `lemonlion.github.io`, which is not a GitHub setting, and GitHub's
  card images and nuget.org's search with it. Traffic needs the owner's own access (the session's token is
  refused), and a search across all of GitHub is outside the session's repository scope. The session's
  GitHub tools have no call that edits a repository's settings, so S1 and S2 are the owner's hands
  whatever D24 says.

---

## 0. Summary

Two repository fields change. Nothing in `src/`, no package, no report byte, no Kronikol4J ledger entry.

| Slice | What | Who | Time | Needs |
|---|---|---|---|---|
| S0 | Look first: the demo at two widths and the version that wrote it, the repository page as a logged-out visitor sees it, and the numbers only the owner can read. **Partly run by a session on 2026-09-27** (§3, §12) | owner, for what is left | 10 min left | a browser; push access |
| S1 | Set the homepage | owner | 2 min | S0, Q1, and the demo on the latest release (`INTERNAL_FLOW_BLOB_PLAN.md` S5) |
| S2 | Write and set the description | owner | 30 min | S0, Q2 |
| S3 | *Optional.* The README's first line points at the demo | the owner's words, a session's commit | 10 min | Q3 |
| S4 | *Optional.* A weekly check that the homepage still answers | a session | 45 min | Q4 |
| S5 | Record the before and after, strike the roadmap row, update the index | a session | 10 min | S1, S2 |

The roadmap's hour is S0, S1, S2 and S5. S3 and S4 are proposals, each a yes or no in D24. The demo's
upgrade is in neither the hour nor this plan. S0 found the demo on 3.29.0 (F5), and moving its pins is S5
of `INTERNAL_FLOW_BLOB_PLAN.md`, green-lit on 2026-09-27: its §8.6 moves all 30 sites in one commit,
restores clean, runs the xUnit lane locally, and checks `CI: Main` and the published site. S1 waits for
that. S2 does not: the words change nothing the demo shows.

## 1. What was checked

Basis marks as in the roadmap, plus **REFERENCE**: GitHub's documented or well-known behaviour, not
checked today. Every REFERENCE row that the plan leans on is checked in S0.

| # | Finding | Basis |
|---|---|---|
| F1 | The homepage field is empty. `has_pages` is false: Kronikol has no Pages site of its own, so the homepage can only point elsewhere. The dashboard plan's `lemonlion.github.io/Kronikol/` is not built (roadmap 9.3) | RUN: `api.github.com/repos/lemonlion/Kronikol`, 2026-09-27 |
| F2 | The description is `A mechanism for tracking the request-responses in your tests and converting them into PlantUML diagrams.`, 104 characters. It names a mechanism, uses a word nobody searches for ("request-responses") and gives the drawing tool as the outcome. It does not say .NET, that the result is an interactive report, that capture is automatic, or that databases and message brokers are drawn as well as HTTP | RUN, same call |
| F3 | Twenty topics, which is GitHub's maximum (REFERENCE). The direction report calls them "done right", and adding one means removing one, so this plan changes none. `plantuml`, `plantuml-diagrams`, `plantuml-generator` and `sequence-diagrams` keep those words findable if the description drops them | RUN, same call |
| F4 | The demo is `https://lemonlion.github.io/BreakfastProvider/`, a Pages site built by the `deploy-pages` job of BreakfastProvider's `ci-main.yml` on every push to `main` and daily at 03:00 UTC. Its root is a landing page the job writes inline, titled `Breakfast Provider — Kronikol`: a hero ("Breakfast Provider", "Component Test Reports & API Documentation", a "Powered by Kronikol" badge), six cards linking `reports/<framework>/TestRunReport.html` (the in-memory lanes of ReqNRoll, LightBDD, BDDfy, xUnit, TUnit and NUnit), the OpenAPI and AsyncAPI viewers under `api/`, and the two source repositories. The twelve docker and external-SUT reports (`reports/docker/<framework>`, `reports/docker-sut/<framework>`) are published and linked from nowhere. The nightly of 2026-09-27 served the xUnit report at 8.0 MB, ReqNRoll at 9.2 MB and LightBDD at 9.1 MB; Pages serves gzip, about 1.3 MB for the xUnit report | READ: BreakfastProvider's `ci-main.yml` at `0d29537` (a clone). PLAN for the sizes: `INTERNAL_FLOW_BLOB_PLAN.md` §1.5, §11.1 |
| F5 | The demo is on 3.29.0. BreakfastProvider's `main` is `0d29537` (2026-09-23, its subject opening "Kronikol 3.29.0"), and all 30 of its pins are 3.29.0: 27 package references and three `Kronikol.Tool --version` steps. Every rebuild since has used 3.29.0, the nightly of 2026-09-27 included. Seventeen releases have shipped since (3.29.1 to 3.31.5), with fixes a visitor sees on a first look: the toolbar at every width (3.29.2), parameter tables and detail panels clipped at tablet and phone widths (3.29.3), long tokens scrolling the page sideways (3.29.4), the grey note header below AA contrast and a link whose text stayed black after a hover (3.30.0), and a report 37% smaller that loads faster (3.31.4: the xUnit lane 7.6 to 4.8 MB, `domContentLoaded` 135 to 104 ms). Moving the pins is `INTERNAL_FLOW_BLOB_PLAN.md` S5, whose dry run at 3.31.4 restored clean and passed 203 of 203 | RUN: the clone, a `grep` of the pins, `git log`. PLAN: `INTERNAL_FLOW_BLOB_PLAN.md` §8.6, §11.1. READ: roadmap stage 1 for what each release fixed |
| F6 | Nothing watches the link. None of this repository's four workflows mentions `github.io`, and none of BreakfastProvider's ten does either: its `post-deployment-tests.yml` tests the API server, not the Pages site. A failed deploy leaves the last good site up (REFERENCE), but a renamed lane, a moved path, a renamed repository or Pages switched off would send every visitor to a 404 | READ, both repositories; INFERRED for the failure modes |
| F7 | The README links the demo twice, both inline: the first paragraph's words "rich interactive HTML reports" (`README.md:12`), and a bold sentence under Example Output (`:33`) saying the picture beneath it is "just a very simple static example". That picture links to a plantuml.com server URL, so the README's one picture of the product sends a click to the site the ad audit measured (roadmap D.3) | READ |
| F8 | Tells are already at the door. The direction report's own draft has an em dash (`see what your integration test actually did — every HTTP call, SQL query and message, as a sequence diagram`; the roadmap's row already turned it into a colon). The README's opening paragraphs have three (`README.md:10`, `:14`, `:16`), and the demo's landing page title one (F4) | READ; PLAN for the title |
| F9 | "Every" cannot be proved. A call is drawn when it goes through a client that has its extension and is attributed to a test. A client without one is not drawn, and identity-less background work that names no document still lands nowhere (roadmap Appendix C). The nouns can be proved (§5.4) | READ: `src/` (62 packages), `README.md:14`, roadmap Appendix C |
| F10 | "Self-contained" is not true of the default report. The default rendering is `BrowserJs` (`ReportConfigurationOptions.cs:133`), and since 3.31.1 that page fetches the PlantUML engine from `cdn.jsdelivr.net` when it opens. A report opened with no network draws no diagram unless the browser already holds the engine. Keep the word, and "offline", out of the description | READ: `ReportConfigurationOptions.cs:133`, the 3.31.1 release commit; INFERRED for the offline case |
| F11 | `README.md:33` linked the BreakfastProvider repository as `https://github.com/lemonlion//BreakfastProvider/`, with a doubled slash, and spelt the name "BreakFastProvider" in both of its links. **Fixed in this plan's commit** (documentation only, no bump). Whether GitHub resolved the doubled slash is still unknown: this environment's proxy refuses to forward such a path (`Request path could not be canonicalized`). The corrected form is right either way | READ; RUN for the proxy's answer |
| F12 | Traffic (views, unique visitors, referring sites, popular content) is shown only to people with push access, and only for the last 14 days (REFERENCE). The traffic API refuses the session's token (403, `Resource not accessible by integration`). The direction report's baseline, read 2026-09-12: 31 unique visitors in 14 days; referrers GitHub 7, NuGet 2, Google 1, DuckDuckGo 1, ChatGPT 1. A before-reading has to be taken by the owner on the day of S1, or it is gone | PLAN (the report); REFERENCE; RUN for the refusal |
| F13 | GitHub's search API refused this container anonymously (403), and a search across all of GitHub is outside this session's repository scope, so the GitHub half of the search baseline is the owner's. The web half was taken with the session's search tool (F17) | RUN |
| F14 | A GitHub release is published for each tag (the latest, v3.31.3, today), so the About column's release box is current. Recorded so nobody re-checks it | RUN: `api.github.com/repos/lemonlion/Kronikol/releases` |
| F15 | The repository page's title is `GitHub - lemonlion/Kronikol: <description> · GitHub`; `og:title` is the same without ` · GitHub`, and `og:description` and the meta description are `<description> - lemonlion/Kronikol`. `og:image` is GitHub's generated card (`opengraph.githubassets.com/<hash>/lemonlion/Kronikol`), so no custom social preview is set, which answers S0 step 2. The card image could not be fetched (the proxy refuses `opengraph.githubassets.com`), so that it prints the description stays REFERENCE | RUN: the page's HTML, 2026-09-27 |
| F16 | nuget.org's latest Kronikol is 3.31.4 (published 2026-09-27 11:01 UTC). Its project URL is `https://github.com/lemonlion/Kronikol` (`Directory.Build.props:10`), so a NuGet visitor's project link lands on this doorstep too. Its description (`src/Kronikol/Kronikol.csproj:9`) is mechanism-first as well, and opens with the old name: "Kronikol, formerly TestTrackingDiagrams. Autogenerate PlantUML sequence diagrams from your component and acceptance tests. Tracks HTTP calls, database operations (Cosmos DB, SQL via EF Core), Redis commands, events/messages, and arbitrary method calls, then converts them into searchable HTML reports and structured data files." Package metadata, so out of scope (§10) | RUN: `api.nuget.org`'s registration index (its search endpoint is refused by policy); READ |
| F17 | Web search, before (the session's search tool, US results, 2026-09-27). For "Kronikol", the Java port's repository comes first, titled "A Java port of the .Net Kronikol project that tracks your test flow at run time and turns it into interactive diagrams", then this repository under today's title, then release pages and issue #78. For the three problem phrasings of S0 step 3, Kronikol is in none of the results | RUN |
| F18 | The landing page's first look, drawn from its source (the text the job writes) in Chromium through Playwright: nothing scrolls sideways at 1280 × 800 or at 390 × 844, and the first report card's top sits at 367 px and 468 px, inside the first screen at both. Kronikol appears only in the "Powered by Kronikol" badge, the source card and the footer, and the first paragraph is insider language (the history ledger, a sparkline, `$flaky`, `Failures.md`, `ctrf-report.json`) | RUN |
| F19 | A trap for whoever measures at phone width: headless Chromium's `--window-size` lays a page out no narrower than 500 px and crops the screenshot, so a 390 px check needs a real viewport (Playwright's, or DevTools' device mode). The first attempt here showed the landing page clipped at 390 px; measured with a real viewport, nothing overflows | RUN |

## 2. Where the two fields show

The case for writing the description carefully is that it is read in five places, and in two of them it is
cut short. The title and the card's tags were read on 2026-09-27 (F15). The rest is REFERENCE, which S0
step 5 checks before S2.

| Where | Homepage | Description | Note |
|---|---|---|---|
| The About column (desktop) or the block under the name (phone) | yes, as the bare URL with a link icon | yes, whole | On a phone it sits above the file list, so it is the first text a mobile visitor reads |
| The page title: browser tab, bookmarks, search engine result | no | yes: `GitHub - lemonlion/Kronikol: <description> · GitHub` (RUN, F15) | 29 characters go to the prefix, so roughly the first 25 to 30 characters of the description survive a search engine's cut (INFERRED from a cut near 55 to 60 characters) |
| The link card GitHub draws for a shared repository link (Slack, Teams, X, LinkedIn) | no | yes, unless a custom social preview is set, and none is (RUN, F15) | The tags carry it (RUN); the image is GitHub's generated card, which prints it (REFERENCE). Unfurlers cache cards for days, so a change shows late |
| GitHub's repository search results and profile lists | the direction report says the homepage shows in search results (PLAN) | yes | Default repository search matches the name, the description and the topics |
| The REST API | `homepage` | `description` | RUN. How S1, S2 and S4 are verified |

What the homepage display means for the URL: the About column shows the address itself, so
`lemonlion.github.io/BreakfastProvider` reads as a project's site and a lane report's path reads as a file
path. Nothing in the About column says "demo", which is one argument for S3.

nuget.org shows neither field. A package page shows the package's own description and project URL, which
ship in a package and so in a release (§10).

## 3. S0: look first (the owner, about 20 minutes)

In a logged-out browser window unless the step says otherwise. Each step writes one row of §12.

**Run by a session on 2026-09-27, as far as it could reach:** step 2 whole, step 3's web half, step 4's
version and the root's first look from its source, step 5's title and card tags. **Left for the owner:**
step 1, step 3's GitHub half, step 4's live answer and the reports' own first look at 390 px, and step
5's About block and card image.

1. **Traffic, before** (logged in, Insights → Traffic). The 14-day unique visitors and views, the
   referring sites, and the popular content. F12: this is the only day it can be read.
2. **The social preview** (logged in, Settings → General → Social preview). Is a custom image set? If not,
   the card GitHub draws carries the description, and S2 changes the card too. **Answered 2026-09-27:**
   none is set (F15).
3. **Search, before.** On GitHub's repository search and on one web search engine, note Kronikol's
   position for the phrasings people search with. The direction report's: *see what my integration test
   actually did*, *sequence diagram from EF Core queries*, *what HTTP calls did my test make*. Add
   *sequence diagram tests* and *reqnroll report*. "Not in the first page" is a valid entry. **The web half, 2026-09-27:** F17.
4. **The demo's first look**, at 1280 × 800 and 390 × 844, for the root and for the two or three lanes that
   are candidates (an in-memory lane carries the internal-flow popups, PLAN: `INTERNAL_FLOW_BLOB_PLAN.md`
   §1.5):

   | Check | Pass | Found 2026-09-27 |
   |---|---|---|
   | It answers | 200 over https, logged out | Not reachable from a session (network policy). The owner's |
   | It says what it is | the first screen names Kronikol and says what the page holds | In part. The hero reads "Breakfast Provider", "Component Test Reports & API Documentation" and "Powered by Kronikol": Kronikol is named, but not as what the page shows, and the first paragraph is insider language (F18) |
   | It shows breadth | the root names the test frameworks, so a visitor finds their own (roadmap §0: a visitor who finds their case missing does not return) | Yes: six framework cards. MSTest, which Kronikol supports, has no lane, and the twelve docker and external-SUT reports are linked from nowhere (F4) |
   | It reaches a diagram | a report in one click from the root; a drawn diagram on a report's first screen, with the time it took on the owner's connection noted | One click: the first report card is inside the first screen at both widths (F18). The reports themselves were not reached |
   | Nothing is broken | no failing scenario, no render-error picture, nothing clipped or scrolling sideways at 390 px | The landing page: nothing scrolls sideways at either width (F18). The reports: every popup of eight diagrams drew, with no console error, on three lanes of the 2026-09-27 nightly (PLAN, `INTERNAL_FLOW_BLOB_PLAN.md` §11.1). Nothing was checked at 390 px |
   | It is current | the version the lane's `Failures.md` names (`Kronikol <version>.` on a green run, `Written by Kronikol <version>.` when something failed). The site copies each lane's whole reports directory (PLAN: `EVIDENCE_SURVIVES_A_RERUN_PLAN.md` §1), so the file should sit beside the report (INFERRED). If the version predates 3.29.4, move BreakfastProvider's pins to the latest release and let its CI redeploy before S1 (F5) | **No: 3.29.0** (F5). S1 waits for `INTERNAL_FLOW_BLOB_PLAN.md` S5 |
   | Nothing leaks | open two notes and the headers of one request. The site is already public and linked from the README, so this is a glance, not an audit | Not checked |

5. **Where the description shows** (§2): the tab title, the About column at both widths, and the card
   (paste the repository URL into any unfurling chat box, or read the page's `og:image`). If any row of
   §2 is wrong, correct §2 before S2, because the brief leans on it. **Done 2026-09-27:** the title
   and the card's tags (F15). **Left:** the About block at both widths, and the card image.

## 4. S1: the homepage (the owner, about 2 minutes)

| Option | For | Against |
|---|---|---|
| **H1. The site's root**, `https://lemonlion.github.io/BreakfastProvider/` | The URL that already exists and is linked from the README. It survives lanes being renamed or added. It reads well in the About column. If the landing page names the frameworks, it shows breadth before the first click, which is section 0's worry | One click short of the artifact. The landing page is generated by CI and its look is unknown here. Its title carries an em dash (F4, Q5) |
| **H2. One lane's report** | Lands inside the artifact: the direction report's "thirty seconds inside a real report and it is obvious" | A long URL that reads as a file path. It is tied to a lane path that BreakfastProvider's CI owns, so it is the first thing to break (F6). It is a 1.3 MB download before anything draws, which a phone on a slow network feels. One framework, when the visitor may use another |
| **H3. A stable alias**, such as `https://lemonlion.github.io/BreakfastProvider/demo/`, written by BreakfastProvider's deploy job as a redirect to the chosen lane | H2's landing with H1's stability. The lane can change without the homepage changing | A change in another repository's workflow |
| H4. The wiki, or nuget.org | | The direction report's whole point is the artifact, not more prose. Rejected |

**Recommended: H1, if the root passes S0's first look** (it answers, it says what it is, it names the
frameworks, a report is one click away at both widths). If it fails, **H2 with the lane S0 rates best**,
and then H3 as the form to settle on, so that the homepage never holds a lane path.

**What S0 found (2026-09-27).** Drawn from its source, the root passes breadth, the one click and both
widths (F18), and says what it is only in part (Q5). What it fails is currency: every report behind it is
3.29.0 (F5). So H1 stands, set once `INTERNAL_FLOW_BLOB_PLAN.md` S5 has moved the demo and the owner has
seen the root answer.

```bash
gh repo edit lemonlion/Kronikol --homepage "https://lemonlion.github.io/BreakfastProvider/"
curl -s https://api.github.com/repos/lemonlion/Kronikol | python3 -c "import json,sys; print(json.load(sys.stdin)['homepage'])"
```

Or the gear beside About on the repository page → Website. Kronikol has no Pages site of its own (F1), so
the dialog's option to use one does not apply.

**Order.** S1 need not wait for S2. The link changes no words, and the 31 visitors are better off with a link
than without one while the owner writes the description.

## 5. S2: the description (the owner, about 30 minutes)

### 5.1 The brief

The row says "from mechanism to outcome". Turned into tests a draft can pass or fail:

1. **The outcome leads.** Something a visitor wants (to see what their test did) comes before anything
   about how, inside the first 25 to 30 characters that a search result keeps (§2).
2. **The platform is named early.** ".NET" is the first thing a visitor needs to know ("does this work for
   me?"), and the launch is a .NET launch (D18). Nothing claims Java: Kronikol4J is a separate repository
   with its own front door (D10).
3. **The artifact is named.** A sequence diagram in an interactive HTML report is what the demo shows.
   Naming it makes the homepage link the obvious next click.
4. **Every word is true of the product today** (§5.4). No universal quantifier without a proof (F9), no
   "self-contained" or "offline" (F10).
5. **No tells** (§5.3). The owner's list governs; §5.3 is a starting list and a check for it.
6. **Short.** GitHub's own cap (350 characters, REFERENCE, not checked) is far above every draft. The limit
   that matters is the card and the phone block; the drafts below run 107 to 124 characters.

### 5.2 Raw material

Drafts to cut from, not text to paste: the row asks for the owner's hand. Each was measured by the check
in §5.3 (RUN).

| # | Draft | Chars | Search title, first 60 characters | Tells | Words to prove |
|---|---|---:|---|---|---|
| today | A mechanism for tracking the request-responses in your tests and converting them into PlantUML diagrams. | 104 | `GitHub - lemonlion/Kronikol: A mechanism for tracking the re` | none | none |
| A | See what your integration test actually did: every HTTP call, SQL query and message, as a sequence diagram. | 107 | `GitHub - lemonlion/Kronikol: See what your integration test ` | none | every |
| B | See what your .NET integration tests actually did: the HTTP calls, SQL queries and messages, as sequence diagrams. | 114 | `GitHub - lemonlion/Kronikol: See what your .NET integration ` | none | none |
| C | See what your .NET tests actually did: their HTTP calls, queries and messages, drawn as sequence diagrams in an HTML report. | 124 | `GitHub - lemonlion/Kronikol: See what your .NET tests actual` | none | none |
| D | Sequence diagrams of the HTTP calls, database queries and messages your .NET tests make, in one interactive HTML report. | 120 | `GitHub - lemonlion/Kronikol: Sequence diagrams of the HTTP c` | none | none |

- **A** is the roadmap's form of the direction report's draft. It fails brief 2 and brief 4 ("every").
- **B** keeps the report's framing and fixes both. **Recommended as the starting point** for the owner's
  rewrite.
- **C** names the report as well as the diagram (brief 3), at the cost of ten characters, and says
  "queries" so databases other than SQL ones are covered.
- **D** is plain rather than outcome-first. It leads with the words people search for ("sequence
  diagrams") but gives up the contrast the direction report and 13.1 build on: your tests pass, and here
  is what they did.

A choice inside every draft: "SQL queries" is concrete and is what people type; "queries" or "database
queries" is true of Cosmos DB, MongoDB, DynamoDB, Redis and the rest as well (§5.4).

### 5.3 The tells check

The check behind the table. It prints the length, the search-title preview, anything from the starting
list of tells (dashes used as punctuation, the typographic ellipsis and quotes, an exclamation mark, and
the words that mark generated copy), and every word that makes a claim about all cases, which then needs
a line in §5.4. Run it on the final text before S2 sets it.

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
    print(f"  search title, first 60: {title[:60]}")
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

## 7. S4, optional: the guard (Q4)

Rule 3: a net goes up before the work it protects. What F6 lists can break the homepage without anyone in
this repository changing anything.

| Where | Catches | Cost |
|---|---|---|
| **A scheduled workflow in this repository** | Everything in F6, and an emptied or mistyped homepage field, because it reads the field from the API each time | One file, about 20 lines. A failed scheduled run is emailed to whoever last edited the schedule (REFERENCE) |
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
      - name: The repository's homepage answers
        env:
          GH_TOKEN: ${{ github.token }}
        run: |
          url=$(gh api "repos/${{ github.repository }}" --jq .homepage)
          test -n "$url" || { echo "::error::The homepage field is empty"; exit 1; }
          code=$(curl -sS -L -o page.html -w '%{http_code}' "$url")
          test "$code" = 200 || { echo "::error::$url answered $code"; exit 1; }
          grep -qi kronikol page.html || { echo "::error::$url answered, but not with a Kronikol page"; exit 1; }
```

Before it is committed: run it once by `workflow_dispatch` against the real field, then once with the
URL swapped for a path that does not exist, and see it fail. A check that has never failed has not been
shown to work. It adds no test project and no package, so no bump. It is a new file under
`.github/workflows/`, so it collides with no other stage (roadmap §5, track C).

## 8. S5: record (a session, about 10 minutes)

- §12 of this plan: the before-readings of S0 and the text and URL set by S1 and S2.
- `ROADMAP.md`: strike 2.1 with the date and what was set, move D24 into the row, and note S3 and S4's
  answers.
- `PLANS_STATUS.md`: this plan's row.
- The after-reading, 14 days after S1, is the owner's (F12). It is context, not a target: the item invites
  nobody, so no rise in visitors is expected. The number the direction report set, 31 a fortnight becoming
  300, belongs to stage 13, and 13.0 re-reads the usage figures anyway (roadmap §8).

**Done** when: the API returns the chosen homepage and description; a logged-out window shows both at 1280
and 390 px; the homepage answers 200, and a report it leads to (one click in at most) draws its first
diagram at both widths; the final text has passed §5.3 and every noun in it has a line in §5.4;
and §12 holds the before-readings.

**What cannot be measured, said plainly.** GitHub does not count clicks on the homepage link, and a Pages
site has no analytics (REFERENCE), so whether visitors follow the link is not observable from either
repository. The item's success is binary: the two fields are set, true, free of tells, and the link is
alive.

## 9. Risks

| Risk | Answer |
|---|---|
| The demo shows fixed defects to every visitor (F5) | It would today: the demo is 3.29.0. S1 waits for `INTERNAL_FLOW_BLOB_PLAN.md` S5 |
| The link breaks later (F6) | S4, or at worst the next time someone opens the About column |
| A bad night: the demo shows a failing run | S0 looks at the run the demo holds. The dashboard plan found all 108 recorded runs green (PLAN, 2026-09-14) |
| The description over-claims (F9, F10) | §5.4, and the check's words to prove |
| The description reads as generated | §5.3, and the owner's hand |
| It becomes outreach | Nothing here announces anything. Editing a repository's fields notifies nobody (REFERENCE). The topics stay as they are |
| A phone visitor waits on a large download | S0 times the first diagram at 390 px. Under H1 the visitor picks a report before paying for one |
| Something private in the demo | It is public today and linked from the README, so pointing the homepage at it adds visitors, not exposure. S0 glances anyway |

## 10. Out of scope, and where it lives

| Item | Where |
|---|---|
| The README as a landing page: twenty-five words, one screenshot of the new look, depth below the fold, and the em dashes of its opening | 13.1 |
| A custom social preview image. A screenshot now would show the look that stage 6 and 12.1 replace; until then GitHub's own card carries the new description | 13.1 |
| The topics | Unchanged: at the maximum, and "done right" (F3) |
| nuget.org's package description and project URL | Package metadata, so it ships in a release. The project URL already points here, and the description is mechanism-first and opens with the old name (F16). If the owner wants the new words there too, they ride along with the next release; they are not a reason for one |
| The Kronikol4J repository's front door | D10, taken 2026-09-22 in its README |
| A Pages site for Kronikol itself | The dashboard plan (9.3), not green-lit |
| Anything that brings people in: a listing, a post, a newsletter | Stage 13 (rule 9) |
| A rename | Not funded (roadmap stage 13) |

## 11. Open questions (D24)

| # | Question | Recommendation |
|---|---|---|
| Q1 | Which URL? | H1, the root, once `INTERNAL_FLOW_BLOB_PLAN.md` S5 has moved the demo off 3.29.0. From its source it passes the rest of the first look but the live answer, which is the owner's to see (§4, F18). Otherwise H2 with the best lane, then H3 so the homepage never holds a lane path |
| Q2 | Which words? | The owner's. Start from B (".NET" early, no claim about every call) and rewrite it by hand; run §5.3 on the result (§5) |
| Q3 | Does the README's first line point at the demo now, or wait for 13.1? | Now, as one line, with the example image linking to the demo. It is as passive as the homepage, and the rewrite and screenshot stay in 13.1 (§6) |
| Q4 | A guard on the homepage? | Yes, the weekly workflow in this repository, proved by one failing run before it is kept (§7) |
| Q5 | If H1: the landing page title's em dash (`Breakfast Provider — Kronikol`), and a line on that page saying what it is to someone arriving from Kronikol | Both, in BreakfastProvider's workflow, in a commit of their own after the pins move (the internal-flow plan's commit changes only version strings). The title is the browser tab's text for every visitor the homepage sends (F8), and the hero names Kronikol only as "Powered by" (F18). The words are the owner's, as for S2 |

## 12. Results (filled in at execution)

| Reading | Before (S0) | After |
|---|---|---|
| Homepage | empty (RUN 2026-09-27) | |
| Description | `A mechanism for tracking the request-responses in your tests and converting them into PlantUML diagrams.` (RUN 2026-09-27) | |
| Unique visitors and views, 14 days | 31 unique on 2026-09-12 (PLAN, the direction report). Today's is the owner's: the traffic API refuses the session's token (F12) | |
| Referring sites | GitHub 7, NuGet 2, Google 1, DuckDuckGo 1, ChatGPT 1 on 2026-09-12 (PLAN). Today's is the owner's | |
| Search positions (S0 step 3) | Web, 2026-09-27 (F17): "Kronikol" second, behind Kronikol4J; the three problem phrasings, absent. GitHub's search: the owner's | |
| Social preview | GitHub's generated card, no custom image (RUN, F15) | |
| The demo (S0 step 4) | 3.29.0 (RUN, F5). The root from its source: breadth, one click, both widths (RUN, F18). The live answer and the reports at 390 px: the owner's | |
| nuget.org | 3.31.4 latest; the project URL is the repository; the description is mechanism-first (RUN, F16) | |
| S3 and S4 | | |
