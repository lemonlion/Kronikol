# BACK_BUTTON_NAVIGATION_PLAN: Back and Forward as the report's navigation

**Status: executed and released as 5.2.0 (2026-10-20)**, at the owner's word of that day ("implement the plan in full");
section 10's log says what was decided while building and what was found. Written 2026-10-19 against the local branch
`v5` at `d385c715` (`OPTION_C_PLAN.md` S12 in progress, worktree `C:/Code/Kronikol-v5`; `0eeb9644` since, which touches
none of the code cited here) and `origin/main` at `17914701` (4.32.0). Every `file:line` is on `v5` unless it says 4.x;
paths are under `src/Kronikol/` unless they say otherwise. The harness is `BACK_BUTTON_NAVIGATION_PLAN.harness/`
(Appendix A): the code inventory every line here was taken from, the research notes with their sources, and a probe that
measured the browsers.

## 0. In short

- **The ask.** Back and Forward move between the places a reader has been in the report, as they do between the pages
  of a site: from a scenario back to the History tab entry that led to it, and the same everywhere in the report.
- **Why it fails today.** The report never adds an entry to the session history. Every write to the address is a
  `replaceState` (four call sites), the History tab's links and the failure clusters' links cancel their own
  navigation, and the tab and the Features view are in no address. From almost anywhere in the report, Back leaves it.
  The one in-page action Back undoes is the link button on a scenario's title.
- **What changes.** One new script owns the session history. It adds an entry for every move to another place (a tab,
  the Full or Summary view, a scenario reached by any link, a row of the Summary table, Next Failure), for a layer while
  it is open (the image lightbox and the internal-flow popup), and for each filter control used, a run of changes to one
  control being one entry (typing in the search is one entry, not one per letter). Going back or forward to an entry
  puts back its tab, view, filters and layer, then its scroll position and the control that had the focus. The address
  gains `tab=` and `view=`, so a reload or a copied address opens the same place; every address written before keeps
  its meaning.
- **Found on the way** (section 6): sixteen defects in the same code, fixed here as CLAUDE.md asks. Among them: in an
  Azure DevOps viewer every write to the address throws, which stops the search before it opens its one match (4.x
  too); a link to a scenario the filters hide does nothing; clearing the search closes every scenario, the one a link
  opened too; the in-page links drop the filters from the address; an address pasted into an open report has its
  filters ignored; and Ctrl+click on a History or failure cluster link cannot open the scenario in a new tab.
- **Where.** On a branch made from `v5`, `v5-back-button`, merged into `v5` before 5.0.0 (recommended, Q1: 5.0.0 moves
  the History tab into the report, and on 4.x the same links are on the labs page, where Back already works), or
  released after 5.0.0 as 5.1.0, a minor.

## 1. The ask

The owner, on 2026-10-19, while the Option C session ("Kronikol new UI report preview", `kronikol-0b`) was at S12:

> I have another session working on a full UI revamp (the session as named to me in vscode is 'Kronikol new UI report
> preview'). The one thing that it's missing is that in the html report isn't acting like a SPA because it if you clink
> on a link to another part of the report (eg going from the History tab, you click on a link to a scenario) when you
> press the back button in the browser it doesn't take you back to where you were. Similarly in the entire UI the Back
> button needs to be a primary navigation device and behave intuitively like the user would expect. Can you create a
> detailed well researched plan to implement that. It will most likely go into v5.1, but possible v5.0.

Two requirements follow, and every decision below is held to them:

1. **Where you were.** Back returns to the place the reader left: the tab, the view, the position on the page and the
   control they had just used, not only the right tab.
2. **What a reader expects.** Back undoes what a reader takes for going somewhere, and nothing they take for adjusting
   what is in front of them (section 4.1). NN/g calls the Back button "the lifeline of the web user", and the tools
   whose Back did nothing for their tabs and views were asked until it did (research 5.4 and 7).

## 2. What Back does today

### 2.1 The mechanism

1. **Nothing adds an entry.** No `pushState`, no assignment to `location.hash`, no `popstate` listener, no
   `history.scrollRestoration`, no `pageshow` or `pagehide` handler, in any of the report's scripts.
2. **Four `replaceState` calls:** `update_url_hash` (`Reports/report-url-hash-function.js:101`), which every filter
   reaches; `clear_all_filters` (`Reports/report-export-function.js:35`); the failure cluster link's inline `onclick`
   (`Reports/ReportGenerator.cs:2111`); the History tab link's inline `onclick` (`History/HistoryHtml.cs:143`).
3. **One element adds an entry:** the link button on every scenario and parameterized group title,
   `a.scenario-link href="#scenario-<slug>"` (`ReportGenerator.cs:2417`, `:3489`), a plain fragment link. An address
   typed into the address bar, and the cross-document links (the labs page, a non-image attachment), add entries too.
4. **Three `hashchange` listeners** (`Reports/report-features-tools-script.js:178`, `Reports/report-tabs-script.js:41`,
   `Reports/report-init-script.js:6-9`), each testing on its own whether the first segment names a scenario. None
   applies the filters a new address carries (`report-init-script.js:2-5` says so on purpose).
5. **The address holds the filters and at most one scenario** (`report-url-hash-function.js:1-9`, `:71-102`). The tab,
   the Features view, the sort, the open sections, the chosen example row, an open layer and the scroll position are in
   no address and in no entry.

### 2.2 The owner's case, step by step

1. The reader is on the History tab, scrolled down to a regression.
2. They click its link, `a.history-link href="#sid-<id>"`. Two capture handlers run first: the features tools switch to
   the Full view (`report-features-tools-script.js:150`) and the tabs script shows the Features tab
   (`report-tabs-script.js:49`). Then the link's own `onclick` cancels the navigation, opens and centres the scenario
   (`reveal_url_anchor`, `report-url-hash-function.js:25-46`) and rewrites the address with `replaceState`
   (`HistoryHtml.cs:143`).
3. The session history still holds one entry for the report. Its address is now `#sid-<id>`.
4. Back goes to the entry before the report: the labs page, a CI artifact listing, a file listing, or nothing in a
   fresh tab. The History tab and the place on it are gone. Forward loads the report again at `#sid-<id>`.

### 2.3 Every interaction, today and after this plan

"Skipped" means the action adds no entry, so Back goes to the entry before it, which is usually the page the report
was opened from. The rule column names section 4.1's rule that decides the new behaviour. Inventory section 1 has
every row with its handler and markup.

| Interaction | Handler today | Back right after it, today | After this plan | Rule |
|---|---|---|---|---|
| A report tab, clicked | `report-tabs-script.js:45-46`, `activate` `:10-25` | skipped | an entry; Back shows the tab before, where the reader was in it | 1 |
| The tabs' arrow keys, Home and End | `report-tabs-script.js:53-66` | skipped | one entry for the run of keys | 1 |
| Full or Summary | `report-features-tools-script.js:138-139`, `setView` `:10-29` | skipped | an entry | 1 (Q14) |
| The views' arrow keys | `report-features-tools-script.js:152-165` | skipped | one entry for the run of keys | 1 (Q14) |
| A Summary row opening its card | `openRow`, `report-features-tools-script.js:80-92` | skipped | an entry; Back shows the table where it was | 1 |
| A History tab link | inline `onclick`, `HistoryHtml.cs:143` | skipped (the owner's case) | an entry; Back shows the History tab where it was, focus on the link | 1 |
| A failure cluster link | inline `onclick`, `ReportGenerator.cs:2111` | skipped | an entry | 1 |
| A scenario's link button | native fragment link, `ReportGenerator.cs:2417`, `:3489` | the entry before, but the filters drop out of the address | an entry; the filters stay in the address | 1, 5 |
| Next Failure | `jump_to_next_failure`, `Reports/report-jump-to-failure-function.js:2-16` | skipped | an entry per press: Back is the previous failure, then where the reader started | 1 (Q3) |
| An address typed or pasted into an open report | the three `hashchange` listeners | the entry before; the filters in the address are ignored | an entry, filters applied exactly | 5 |
| The image lightbox | `Reports/report-lightbox-function.js:7-13` | skipped, with the lightbox still open; on a phone Back leaves the report | an entry while it is open: Back closes it | 2 |
| The internal-flow popup | `showPopup`, `Reports/internal-flow-popup-script.js:51-93` | skipped, with the popup still open | an entry while it is open: Back closes it | 2 |
| The search, typed | `report-search-function.js`, through `update_url_hash` | skipped; the text stays | one entry for the run of typing; Back puts the old text back | 3 |
| A status, chip, mode, Happy paths or duration control | the filter functions, through `update_url_hash` | skipped | one entry per control used; a run of clicks on one control is one entry | 3 |
| Clear All, a row's Clear | `clear_all_filters`, `report-export-function.js:1-36` | skipped; cannot be undone | an entry: Back puts the filters back | 3 |
| Opening or closing a section, folding all, sorting, a column sort | native `<details>`; `report-features-tools-script.js:55-77`, `:118-128` | skipped | unchanged: no entry | 4 |
| Display and payload settings, a scenario's settings and gears, the theme | `Reports/collapsible-notes-script.js`, `report-scenario-settings-script.js`, `report-theme-switch-script.js` | skipped | unchanged | 4 |
| A scenario's Sequence, Activity and Flame views, an example row, the flat or grouped table, a step's table reference, flame zoom, diagram zoom | `toggle-script.js`, `report-select-row-function.js`, `flame-chart-render-script.js`, `context-menu-script.js` | skipped | unchanged | 4 |
| More filters, the Filters box in the bar, the search help, the phone's Filters toggle | `report-filter-bar-script.js`, `report-search-function.js:3-9`, `report-init-script.js:19-31` | skipped | unchanged | 4 |
| The timeline's and components' controls | `report-timeline-script.js:101-112`, `report-components-script.js:184-195` | skipped | unchanged | 4 |
| The arrow keys between scenarios, Back to top | `report-keyboard-navigation-function.js:14-29`, inline `scrollTo` | skipped | unchanged | 4 |
| The diagram context menu | `context-menu-script.js:492-893` | skipped | unchanged; its "in new tab" items open a tab as before | 4 |
| A non-image attachment, the CI run link, the labs page | cross-document or a new tab | the browser's own | unchanged, and the report puts its place back when the reader returns (4.9) | 6 |

### 2.4 4.x against 5.0.0

In 4.x the History section is off by default, and history is read on the labs page (`TestRunReport.labs.html`, 4.6.0),
whose scenario links are ordinary links into the report (`HistoryHtml.cs:134-137`). Back from the report returns to the
labs page, scrolled where it was. 5.0.0 turns the History tab on by default (`OPTION_C_PLAN.md` R18.1, its S3) with the
in-page links of `HistoryHtml.cs:143`, so the owner's flow works in 4.x and breaks in 5.0.0. The failure cluster links,
the link button and Next Failure behave the same in both lines (inventory section 5); the tabs, the views and the
filter bar exist only in 5.0.0.

## 3. What the platform gives

Measured on 2026-10-19 with the browsers Playwright 1.59.1 bundles (Chromium 147, Firefox 148, and WebKit 26.4 in its
Windows build, which is not Safari), on `file://` and on `http://127.0.0.1`, twice each (harness `probe/RESULTS.md`,
`s20/`, `s21/`). Researched the same day from the specifications and the engines' own sources (harness `research.md`,
which carries every source and date). The browsers current that day: Chrome 155, Firefox 158 (ESR 153.5 and 115.43),
Safari 27.0.

| # | Fact | Source | For this plan |
|---|---|---|---|
| 1 | `pushState` and `replaceState` that change the fragment or the query work on `file://` and over http in all three engines. A changed path throws on `file://` in Chromium, WebKit and Firefox as it ships (Playwright's Firefox profile relaxes the `file://` rule, so the probe measured both) | probe Q1, Q2, Q2b | the address stays a fragment, as today |
| 2 | Back and Forward between two same-document entries fire `popstate` with the entry's state, then `hashchange` where the fragments differ, in all three. A fragment navigation (a link, a typed address, `location.hash =`) fires `popstate` with a null state, then `hashchange`. `page.goBack()` works for such entries in all three, in Firefox once the page has had one real user interaction (before that it does nothing there, while the page's own `history.back()` works) | probe Q1, Q3, Q5, X1, X2; research 2.5 | one `popstate` handler sees every traversal and every fragment navigation (4.5); the Playwright tests can press Back (7) |
| 3 | With `history.scrollRestoration = 'auto'` every engine restores a same-document entry's raw position after `popstate` and before `hashchange`; with `'manual'` none does, except that WebKit gives an entry made by a fragment navigation (a link click, `location.hash =`) `'auto'` again, and restores it | probe Q5, Q5b, Q6; research 2.7 | the module restores the position itself, and sets `'manual'` again after a fragment navigation |
| 4 | Under `content-visibility: auto`, scrolling the saved element into view and applying the saved offset landed on the saved section in 58 of 58 trials (every engine; after a Back, a Back after a detour, a reload); a saved pixel position in 34 of 58, and the browser's own restore in 14 of 58 | probe Q7, Q7b | 4.3's element and offset |
| 5 | A real reload keeps the entry's state and its `scrollRestoration` mode in every engine (Playwright's `page.reload()` in Firefox over http adds an entry instead, which the probe found and measured around) | probe Q11, Q11b, Q11c | a reload puts the place back from the state (4.9) |
| 6 | Chrome for Testing keeps an http report in its back/forward cache (`persisted`, script state intact) but never a `file://` one, which it loads again; Playwright's WebKit restored neither, and Firefox's cache cannot be measured under Playwright | probe Q12, Q12b, Q12c; research 2.8 | the load path rebuilds the place from the address and the state (4.9) |
| 7 | Rate limits: WebKit throws `SecurityError` from the 101st write within 10 s, and goes on throwing until the window has passed; Chrome drops writes past 200 per 10 s with a console warning; Firefox 157 and 158 drop past 200, Firefox 149 to 156 and ESR 153 throw past 1,000. Playwright switches Chromium's and Firefox's limits off, so only its WebKit can catch a breach | probe Q8, Q8b; research 2.2 | a write budget, every write caught, and a WebKit test (7.5) |
| 8 | A tab keeps 50 entries in Chromium and Firefox and 100 in WebKit (measured), dropping the oldest | probe Q8, Q8c; research 2.4 | entries for places, layers and runs of filter changes only, so the page the report was opened from stays within reach |
| 9 | The Back button skips entries a page added without a user action: Chrome since 75, Firefox since 135, Safari. Firefox also skips the first of two entries one click pushed | research 2.9; probe X1, X2 | an entry only from a click or a key press, and one per action |
| 10 | Inside an iframe, the report's entries join the tab's one history: Back at the top goes through them first. (Playwright's `page.goBack()` at the top level times out there, though the frame goes back) | probe Q10; research 3.1 | Back works in an embedding page too; the module calls `history.back()` only for an entry it made; the iframe test presses Back with `history.back()` |
| 11 | In an `<iframe srcdoc>`, how the Azure DevOps viewer extensions show an artifact, a relative address resolves against the embedding page, so every write the report makes today throws `SecurityError` in Chromium and WebKit; an address built from `location.href` works there. Under an http parent, Firefox throws for any address | `s21/` (measured), research 3.3 | S21: the module builds absolute addresses and catches every throw |
| 12 | Ctrl+click and Shift+click on a link whose click handler calls `preventDefault()` open nothing in all three, and the page jumps in place; on a plain link they open a new page. A middle click opens a tab in Chromium and Firefox either way (it fires `auxclick`) | `s20/` (measured) | S20 |
| 13 | The Navigation API is Baseline only since 2026-01-13. It is off in sandboxed frames, missing from Firefox ESR 115 (supported until 2027-04-06) and from Safari before 26.2, probably unable to intercept anything on a `file://` page in Safari (WebKit's source; not run), and absent from Playwright's WebKit | research 1; probe Q9 | the History API only (Q8) |
| 14 | Jenkins' default policy for archived files lets no script run, so the report does not work there unless the site uses a resource root URL, as Jenkins recommends; the HTML Publisher then shows it in an iframe | research 3.2 | fact 10 applies there |
| 15 | Screen reader users did best with the focus moved to the new view's heading (Fable, 2019); Deque ranks the heading first; the Navigation API's explainer asks for the previous focus back on a Back; frameworks announce a new view in a live region; on a tab switch the focus stays on the tab (the APG's keyboard model, GOV.UK) | research 5.1 | 4.5 step 5 and a polite status line |
| 16 | Readers' expectations, from user testing: a change of about 70% of the view reads as a new page and deserves an entry (Baymard); Back should close an overlay (NN/g, Baymard); "Tapping 'Back' after applying filters should begin removing the applied filters" (Baymard, 2020). Tools that replaced for their tabs or views were asked until they pushed (GitLab's merge request tabs, Allure 2, Playwright #18378), and Playwright's HTML report, the closest precedent (one file, often on `file://`, its state in the fragment), pushes and then runs its one `popstate` handler for its own navigations too | research 5.2, 6.1, 7, 8 | rules 1 to 3, and 4.4's shape |

## 4. The navigation model

### 4.1 Rules

1. **A place is an entry.** Going to another tab, to the Full or Summary view, or to a scenario (by any link, by a row
   of the Summary table, by Next Failure) adds an entry. A run of arrow keys on the tabs or the views is one entry.
2. **A layer is an entry while it is open.** Opening the image lightbox or the internal-flow popup adds an entry with
   the same address; Back closes the layer and changes nothing else. Closing it any other way (its close button, Esc,
   a click on the backdrop) takes that entry back out, so it never lingers.
3. **A run of changes to one control is one entry.** A filter changes what a place shows, and readers expect Back to
   undo it as a site's faceted search does (research 8; Q2). Each control used adds an entry; a run of changes to
   the same control replaces it: typing in the search, cycling one chip through its three states, toggling one mode.
4. **Adjusting is never an entry.** Opening or closing a section, folding, sorting, the display and payload settings,
   a scenario's settings and diagram views, an example row, the theme, the timeline's and components' controls, More
   filters, the search help and the context menu change how something is shown, not where the reader is. They add no
   entry and Back does not undo them. They are not in the address either, as today.
5. **The address is the place.** Every entry's address opens the same place in a fresh page (a reload, a copied
   address, a bookmark): the tab, the view, the scenario, the filters. The entry keeps for itself what an address
   should not carry: the scroll position, the focused control and an open layer.
6. **An entry is put back exactly.** Going back or forward to an entry puts back its tab, view, filters and layer, then
   scrolls to where the reader was and focuses the control they had used. Leaving the report and coming back to it, by
   a link, a reload or the browser's own Back, puts back the same.
7. **Only the reader adds entries, and never twice for one place.** Loading the page, restoring filters from an
   address, deep search results arriving and anything else the page does on its own replace the current entry. An
   action that leaves the place unchanged (the tab already selected, the view already shown, a link to the scenario
   already in the address) replaces too.

### 4.2 The address, version 3

```
#[<anchor>&][tab=<key>&][view=<view>&]<the filters of version 2>
```

- `<anchor>` is `scenario-<slug>` or `sid-<stableId>`, first as before (the wiki's contract, Generated-Reports.md
  "Deep links").
- `tab=<key>` names a tab by its `data-tab` value (`features`, `timeline`, `components`, `history`, `diagnostics`;
  `ReportGenerator.cs:1975-1979`). The script reads the keys from the page and names none of them (5.3).
- `view=summary` or `view=full` names the Features tab's view.
- The filters are version 2's keys and values, unchanged (`report-url-hash-function.js:71-102`).

Reading an address: the tab is `tab=` when it names a tab of this page, else Features when there is an anchor, else
the tab the page was written to open on. The view is `view=` when it is `full` or `summary`, else Full when there is an
anchor, else the view the page was written to open on. The generator writes both starting points as
`data-start-tab` and `data-start-view` (new), since the scripts change the markup they would otherwise be read from
before the page settles (`report-features-tools-script.js:180-184` runs `setView` at load).

Writing an address: a key whose value is what reading would give without it is left out, so the address of the place
the page opens on is the bare file name, as today, and `#sid-<id>` stays `#sid-<id>`. The anchor is kept while the
reader is on the Features tab's Full view and dropped when they leave it (another tab, the Summary view): leaving the
scenario is leaving the place the anchor names. Clear All keeps the anchor and drops the filters, as the wiki says.

Every address written before reads as before: one without `tab=` or `view=` is a version 2 address (or 4.x's). A 4.x
report given a version 3 address ignores the two keys it does not know (4.x `report-url-hash-function.js:95-166` reads
only its own keys). Nothing outside the page changes: `kronikol query`, `Failures.md`, `Failures.jsonl`, the agent
files, the labs page, merges, the skill's `query.py` and the doorstep check all write `TestRunReport.html#sid-<id>`
(inventory 3.2), which reads as the Features tab, Full view, that scenario.

### 4.3 What an entry holds

The entry's state is one object under one key, so that a page embedding the report, or a later script, cannot collide
with it:

```
history.state = { kronikol: {
  v: 1,
  id: '<page id>.<n>',      // unique per entry, kept over a reload
  kind: 'load' | 'place' | 'filter' | 'layer',
  key: '<what made it>',    // for rule 3 and rule 1's runs of keys: 'tab-keys', 'view-keys', 'q', 'chip:tag:api', ...
  scroll: { at: '<address of an element>', dy: <px>, y: <px> },
  focus: '<address of an element>',
  layer: null | { kind: 'lightbox', src: '...', alt: '...' } | { kind: 'iflow', segment: '...' }
} }
```

- `scroll.at` is the address of the first element under the pinned bar that has one, `dy` its distance from the bar's
  bottom edge, and `y` the page's scroll position, kept for a page whose element is gone. An element's address is its
  `id` when it has one; else `[data-stable-id=...]` (a flat example row) or `[data-sid=...]` (a timeline row); else
  `a[href=...]` for a link; else the feature around the first scenario of a feature (`details.feature` has no id,
  inventory 3.1), written as that scenario's address and a step up to `details.feature`. Restoring by element and offset
  is what lands on the same scenario when features the browser has not drawn change height
  (`content-visibility: auto`, `report-url-hash-function.js:48-54`; the probe's question 7, section 3).
- `focus` is the address of the element that had the focus when the reader left the entry: the link they followed, the
  tab or row they pressed.
- An entry's scroll position is recorded in the page's memory as the reader scrolls (once scrolling stops, not on
  every event) and written into the entry when the reader leaves it by an action, on `pagehide` and when the page is
  hidden. The History API can only write the current entry, so an entry left by Back has its newest position in the
  page's memory only; after a reload it has the one written when it was last left by an action.

### 4.4 Going somewhere

Every action that adds an entry goes through one call, `kronikol_nav.go(change, how)`, and arrives the way a Back
arrives, through the one function `popstate` runs (4.5). That is the shape of Playwright's HTML report, which pushes and
then dispatches its own `popstate` so that its links and the browser's Back cannot drift apart (fact 16). In order:

1. **Write the current entry's scroll position and focus into its state** (`replaceState`, same address).
2. **Build the new address** from the place as it is and the change (4.2): a tab, a view, a scenario's anchor.
3. **Add the entry or replace the current one.** Replace when rule 7 says so, or when `how.key` equals the current
   entry's key and that entry is live (made by this page and not left since: rules 1 and 3). One `pushState` per
   action, and only in answer to the reader: in the click or key handler itself, or in the search's 300 ms wait after a
   key press (Chrome counts any activation since the load, Firefox the reader's input while the entry is current, Safari
   one within 10 s; research 2.9), never on the page's own initiative. An entry added without a user action is one the
   Back button skips, and Firefox skips the first of two pushed by one click (fact 9).
4. **Arrive** (4.5) with nothing saved for the new entry: the anchor is revealed, a tab opens where the reader left it
   (S13), a Summary row's card opens at its title, Next Failure's scenario is jumped to. The page's own functions do the
   work (`activate`, `setView`, `reveal_url_anchor`, `openRow`, `jump_to_next_failure`). No other handler runs before
   step 1: the capture handlers that switch the tab and the view for a link today (`report-tabs-script.js:49`,
   `report-features-tools-script.js:150`) are removed, as is the links' inline script.

Every write goes through one writer, which:

- builds an absolute address from `location.href` with its fragment replaced, never a relative one, so it holds in an
  `<iframe srcdoc>` (fact 11, S21) and on a UNC path;
- catches every exception and carries on without the entry (a sandboxed frame in Firefox, a viewer that forbids it, a
  rate limit): the report then works as it does today, with no entries;
- keeps to a budget under WebKit's 100 writes in 10 s (fact 7): a push is written at once, a replace waits until 120 ms
  after the last write (nuqs's figure for Safari, research 8) and only the newest is written, and a push folds any
  replace still waiting into its step 1;
- never writes on a scroll event, and at most twice for one action (step 1's replace, then the entry).

### 4.5 Coming back

One function, `arrive(how)`, runs for every arrival: from `go` (4.4 step 4), from `popstate` (Back, Forward, the
long-press history menu, `history.go(n)`, and a fragment typed into the address bar or reached by a native link), and
from the load (4.9). `popstate` comes first in all three engines and `hashchange` follows (fact 2), so `hashchange` does
nothing for an address `popstate` has just handled. It arrives only for an address `popstate` has not handled, which no
engine measured leaves; it is kept as a guard, and it replaces the three listeners of 2.1. In order:

1. **Layer.** If an open layer is not the entry's, close it. If the entry's layer is not open, open it (Forward to a
   lightbox entry shows the lightbox again, Q4). If only the layer changed, stop here.
2. **Tab and view** from the address (4.2), with `activate` and `setView`. A tab shown this way draws its diagrams as a
   click would (`report-tabs-script.js:23`).
3. **Filters**, only if the address's filters differ from the page's: every filter is reset without writing the address
   and without closing a scenario (S8), then the address's are applied, and the scenarios are shown or hidden once.
   Today's `parse_url_hash` cannot do this: it only turns on what an address names (`report-url-hash-function.js:103-174`,
   inventory S3).
4. **Scroll.** If the entry has a position (it was left before), scroll to it: the element `scroll.at`, `dy` below the
   bar, aimed again while the frames after it settle, as `jump_into_view` does (`report-url-hash-function.js:55-64`),
   until the reader scrolls; `y` if the element is gone. If it has none, do what 4.4 step 4 says for the kind of
   arrival; an address with nothing saved and no anchor opens at the top of its panel.
5. **Focus** the `focus` element if it shows, with `preventScroll`; else the selected tab when the tab changed (focus
   stays on a tab, fact 15), the scenario's title when an anchor was revealed; else leave the focus alone. A focus left
   inside a panel that is now hidden is never kept. On a traversal (not on a click, whose control already says what it
   did) a visually hidden status line, `role="status"`, names the place: the tab, the view, the scenario, or "Filters
   changed" with the readout (fact 15).
6. **Done.** The entry is no longer live, so the next filter change adds an entry rather than rewriting this one (4.4
   step 3); Next Failure's counter is set to the scenario shown when it is a failure; `data-kron-nav` rises (5.1).

`history.scrollRestoration` is set to `manual` as the page loads, and each pushed entry copies it (research 2.7). WebKit
gives an entry a fragment navigation made `auto` again (fact 3), so `arrive` sets it again after one, and only when it
is not already `manual`: Firefox counts the setter against its rate limit (research 2.2). The browser then leaves the
scroll alone, where it would put back a raw position that no longer means anything (facts 3 and 4).

### 4.6 Links

One click handler on the document takes every same-page link (`a[href^="#"]`), so the History tab's, the failure
clusters' and the link button need no script of their own. A click with Ctrl, Meta, Shift or Alt, a middle click, or
a link with a `target` is left to the browser: it opens the report at that scenario in a new tab or window. The inline
handlers cancel that today (S20). Everything else goes through `kronikol_nav.go`, which keeps the filters in the new
address (S2).

The link button's `href` stays `#scenario-<slug>`, so "Copy link address" keeps giving what it gives today. Whether it
should give `#sid-<id>`, the address every producer outside the page writes, is Q6.

### 4.7 Layers

The lightbox and the internal-flow popup call `kronikol_nav.layer(open)` when they open and when they close:

- Opening adds an entry with the same address and `layer` set, so Back closes the layer and leaves the place as it was.
  The entry below it keeps the opener (the image's link, the diagram's arrow) as its `focus`, so the focus goes back to
  it when the layer closes, as the APG's dialog pattern asks. The popup replacing another popup
  (`internal-flow-popup-script.js:52-53`) replaces the entry.
- Closing by the close button, Esc or the backdrop calls `history.back()` only when the current entry is that layer's
  own (its `kronikol.layer` names it), and the `popstate` handler then closes it (4.5 step 1). Otherwise it just
  closes. The check matters in a frame: a `history.back()` the report did not earn would take the embedding page back
  (fact 10). Closing never pushes, which is the trap Allure 2 and Grafana fell into (research 6.4).
- A reload with a layer entry current shows the place without the layer and takes `layer` out of the entry.
- On iOS a swipe back is a traversal and nothing else, so the entry is the only way a swipe can close a layer; on
  Android, Chrome sends Back to an open modal `<dialog>` or popover first (research 6.3). The two layers are plain
  elements, so Back reaches the history. If Option C or a later change makes either a `<dialog>`, its `close` event must
  take the entry out with the same check, or the entry is left behind.

The context menu, the search help and the gears are not layers: they close on the next click or Esc, and readers do not
press Back for a menu (research 6.1: "we probably shouldn't add every single image to the user's history"; NN/g's
overlays are the large ones).

### 4.8 Filters

- `update_url_hash` keeps building the version 2 parameters (`report-url-hash-function.js:71-95`), adds `tab=` and
  `view=`, and hands the address to `kronikol_nav`, which adds or replaces by rule 3. The control is read from the
  difference between the old and the new parameters, so no filter function needs to say which control called it: the
  search (`q`), one status (`status`), one chip in either of its states (`deps`/`depx`, `tags`/`tagx`, `cats`/`catx`,
  with the chip's value), a row's mode, Happy paths (`hp`/`hpx`), the duration (`dur`, `pctl`); a change to several at
  once (Clear All, a row's Clear) is its own key.
- While `kronikol_nav` applies filters itself (a load, a traversal), no filter function writes the address; one write
  follows, replacing.
- The search keeps its 300 ms wait (`report-search-function.js:26-33`), and writes at once on Enter, on leaving the
  field and when it is emptied (research 8). Its first write after another control, or after a traversal, adds an
  entry; every write after it replaces that entry, so a word typed is one entry, as React Router's own example does
  for a search box ("users only have to click back once", research 8). No keystroke ever adds an entry.
- A link to a scenario the filters hide (S7) shows a line where the scenario would be, "This scenario is hidden by the
  filters", with a button that clears them and shows it (Q5).

### 4.9 Load, reload, leaving and returning

- **Load.** One function puts the place in the address on the page, in order: the tab and the view, the filters, then
  the anchor (S5: today the anchor is revealed before the Features panel shows, and only `jump_into_view`'s re-aim
  saves it; `report-init-script.js:37-39` runs before `report-tabs-script.js:77-83`). Then it replaces the entry once,
  adding `kronikol` to its state. No entry is added.
- **Reload.** The entry keeps its state over a reload (fact 5), so the page scrolls back to where the reader was, not
  to the anchor: the reader may have moved on from the scenario a link took them to (Q11). An entry with no state (one
  a typed address made, or one an older report wrote) puts the place back from its address alone, at the anchor.
- **Leaving and returning.** A non-image attachment, a cross-document link or the labs page leaves the report. Coming
  back by Back loads it again from `file://` (fact 6), which reads the entry's state as a reload does. Over http,
  Chromium restores the live page from its back/forward cache, and `pageshow` with `persisted` has nothing to do. The
  page has no `unload` handler and adds none, so it stays eligible for that cache (research 2.8); `pagehide` writes the
  position.
- **The Specifications report and an exported Filtered HTML page** run the same scripts without tabs
  (`ReportTabsTests.cs:158`; `report-export-function.js:103-118`): the module works without a tab or a view, as
  `activate` already does (`report-tabs-script.js:12`).

### 4.10 Hosts

| Where the report is read | What it is | Back after this plan |
|---|---|---|
| A local file (`file://`) | a top-level page; the most common case and the one every E2E test uses | as designed; a Back into the report from another page reloads it (fact 6) |
| GitLab's artifact pages, GitHub Pages, any http server | a top-level https page | as designed |
| A GitHub Actions artifact uploaded unzipped (since 2026-02-26) | a top-level page on a short-lived storage URL; whether inline scripts run there is not confirmed (research 3.5) | as designed where the scripts run; a copied address expires with the URL |
| Jenkins with a resource root URL | the report in the HTML Publisher's iframe, no sandbox | entries join the Jenkins tab's history (fact 10): Back goes through the report's places, then to the job page |
| Jenkins with its default policy | no script runs (fact 14) | nothing changes: the report does not work there today |
| An Azure DevOps viewer extension | `<iframe srcdoc>`, usually able to run scripts (research 3.3) | entries join the tab's history once the writer builds absolute addresses (S21); where the frame forbids writing (Firefox under an http parent), no entries, and nothing else breaks |
| VS Code's Simple Browser, and its Integrated Browser | a sandboxed iframe with `allow-same-origin` over http; a real top-level page | as designed |
| A sandboxed frame without `allow-same-origin` | an opaque origin; Firefox throws on any write under a `file://` parent (probe Q10) | no entries where the write throws; the report works as today |

Wherever a write fails, the report keeps working as it does today: the filters, the tabs and the links work and the
address is not rewritten. The module never assumes that an entry it tried to add exists: `layer` and `live` are set only
after a write succeeds.

### 4.11 Left out, and why

- **Opening and closing sections.** An entry per section opened would make Back a slow undo of every click in a long
  report; the position Back restores already brings the reader to where they were reading.
- **A scenario's own views and its example rows.** They are part of the scenario, as a tab strip inside a card is part
  of the page that holds it: NN/g's in-page tabs, Primer's panels (research 5.2, 5.3).
- **Display settings, sort, the theme.** How the reader likes to look, not where they are.
- **Making the Timeline rows and the Diagnostics entries links to their scenarios.** Both name a scenario as text today
  (`ReportGenerator.cs:2178-2191`, `:4806-4807`). It would give Back more to do, but it is a new feature, so it is Q7.
- **A page title per place.** Q13.
- **The context menu as a popover**, which Chrome on Android would close on Back by itself (research 6.5): a change to
  Option C's menu, not to navigation.
- **The Navigation API.** Fact 13 and Q8.
- **Keeping the way out within reach whatever the reader does.** A tab keeps 50 entries in Chrome and Firefox (fact 8).
  The model adds entries only for places, layers and runs of filter changes, so a reader would have to visit about fifty
  places before the page the report was opened from fell off the list; the page cannot see how many entries come
  before it, so it does not try to count.

## 5. Code

### 5.1 `Reports/report-navigation-script.js` (new)

The only code that writes the session history: its writer (4.4) is the page's one caller of `pushState` and
`replaceState`. It exposes `window.kronikol_nav`:

| Member | Does |
|---|---|
| `go(change, how)` | 4.4: `change` is `{ tab, view, anchor, row, failure }`, `how` is `{ key, replace }` |
| `wrote(address)` | the filters' path (4.8): called by `update_url_hash` with the address it built |
| `layer(kind, data, open)` | 4.7 |
| `address(element)` / `find(address)` | the element addresses of 4.3 |
| `settled` | a counter on `<html data-kron-nav="n">`, raised when a traversal or a load has finished (tab, filters, scroll and focus), which the Playwright tests wait on |

It runs in the head with the report's other scripts (`ReportGenerator.cs:1625-1687`), registers its `popstate`,
`hashchange`, `pagehide` and `visibilitychange` listeners and its click handler as it runs, and starts from
`report-init-script.js`'s `DOMContentLoaded` handler where `parse_url_hash` runs today, so the timeline and components
scripts, which start after it and also follow `kron-filter-change` (`report-timeline-script.js:114`,
`report-components-script.js:198`), see the filters applied.

### 5.2 Changes to existing code

| File | Change |
|---|---|
| `Reports/report-url-hash-function.js` | one test for "the first segment names a scenario", used by every script (S15); `update_url_hash` writes `tab=` and `view=` and hands the address to `kronikol_nav.wrote` in place of its relative `replaceState` (S21); `parse_url_hash` becomes `apply_url_hash(hash)`, exact: reset, then apply (S3); `reveal_url_anchor` is the one way to a scenario, for the failure clusters too (S6, S16), and a hidden target gets 4.8's line (S7); `jump_into_view`'s comment on Safari (S22) |
| `Reports/report-init-script.js` | its `hashchange` listener goes (4.5 handles it); it calls `kronikol_nav`'s start where it calls `parse_url_hash` |
| `Reports/report-tabs-script.js` | a tab click and the arrow keys call `kronikol_nav.go`; the `hashchange` listener, the capture branch for links and Next Failure (`:41`, `:49`) and the load-time `toFeatures` (`:78`) go to the module; the arrow keys between scenarios do nothing while a tab has the focus (S11) |
| `Reports/report-features-tools-script.js` | the views, their arrow keys and a Summary row call `kronikol_nav.go`; its `hashchange` listener, capture branch and load-time `setView` (`:150`, `:178`, `:182`) go to the module |
| `Reports/report-jump-to-failure-function.js` | Next Failure calls `kronikol_nav.go` with the failure's `sid-` anchor; its counter can be set from outside (4.5 step 6) |
| `Reports/report-export-function.js` | `clear_all_filters` leaves the address to `update_url_hash` (`:34-35` go) |
| `Reports/report-search-function.js` | an empty search closes only the scenarios the search opened (S8, `:131-135`, `:217-221`) |
| `Reports/report-lightbox-function.js`, `Reports/internal-flow-popup-script.js` | open and close through `kronikol_nav.layer` (4.7) |
| `Reports/report-scenario-feature-map-helper.js` | `applyVisibility` can be held for one pass while the module applies several filters |
| `Reports/ReportGenerator.cs` | the new script's slot; `data-start-tab` on `.report-tabs` and `data-start-view` on `.view-switch`; the failure cluster links lose their inline `onclick` (`:2111`); parameterized groups' ids are made unique as scenarios' are (S9, `:3482` against `:2058-2074`); `autocomplete="off"` on `#features-sort` (S10, `:3085`) |
| `History/HistoryHtml.cs` | `InPageLink` is a plain link, `href="#sid-<id>"`, without the inline `onclick` (`:141-144`); the stale comment goes (S12) |

### 5.3 Constraints carried over

- **No `history-` anywhere in a report without history** (`DefaultReportCleanTests.cs:112-122`, `:125`). The module
  names no tab, no section and no class of one; its own words avoid the string, comments included (`window.history` and
  `history.pushState` do not contain it, but a comment saying "the history-link" would).
- **Nothing stored but the theme** (`PersistentFilterStateReportTests.cs:38`). The session history's own state is not
  storage in that sense: it lives with the entry, dies with the tab, and is what the platform keeps for this. No
  `sessionStorage`.
- **The labs page runs no script** (`LabsPageTests.cs:80`). It is unchanged: its links already work with Back.
- **D48, the focus helper still to come in Option C's S12** (structure inventory D48), scrolls a control that receives
  the keyboard's focus clear of the pinned bar and keeps doing so for a second. The module's own focus on a traversal
  (4.5 step 5) must not start it: the module marks its restore, and D48 leaves a focus alone while the mark is set. One
  rule, agreed with the Option C session before either lands (8.1).
- **Kronikol4J** keeps its own copies of the report scripts (`OPTION_C_PLAN.md` section 5): a divergence ledger entry,
  no port (Q10).

## 6. Bugs found on the way

Fixed by this plan, as CLAUDE.md asks; each change of behaviour gets a test that fails before its fix (S12, S15 and
S22 change none). S1 to S19 are the inventory's numbers; S20 to S22 were found writing this plan.

| # | Finding | Where | Fix |
|---|---|---|---|
| S1 | The owner's case: no entry for a History link, a failure cluster link, Next Failure or a Summary row, and no tab in any address | `HistoryHtml.cs:143`, `ReportGenerator.cs:2111`, `report-jump-to-failure-function.js:2-16`, `report-features-tools-script.js:80-92`, `report-tabs-script.js:10-25` | sections 4 and 5 |
| S2 | The in-page links drop the filters from the address while the page keeps them | `ReportGenerator.cs:2111`, `HistoryHtml.cs:143`, `:2417`, `:3489` | 4.6 |
| S3 | An address with filters, typed into an open report or reached by a traversal, has its filters ignored; `parse_url_hash` only adds | `report-init-script.js:2-9`, `report-url-hash-function.js:103-174` | 4.5 step 3 |
| S5 | On load the anchor is revealed before the Features panel shows; only the re-aim saves it | `report-init-script.js:37-39`, `report-tabs-script.js:77-84` | 4.9 |
| S6 | A failure cluster link to an example row of an outline shown flat selects the hidden grouped copy and scrolls nowhere | `ReportGenerator.cs:2111`, `:3493-3503`, `:3592-3595` | `reveal_url_anchor` for every link |
| S7 | A link to a scenario the filters hide does nothing visible | `report-url-hash-function.js:15-23`, `:45` | 4.8, Q5 |
| S8 | Clearing the search closes every scenario, the one a link opened too | `report-search-function.js:131-135`, `:217-221` (4.x too) | only what the search opened |
| S9 | Parameterized groups' ids are not made unique, so two groups of one name share an id and a link reaches the first | `ReportGenerator.cs:3482` against `:2058-2074` | the same numbering as scenarios |
| S10 | `#features-sort` alone among the page's controls lacks `autocomplete="off"`, so a browser that restores form values can show an order the cards are not in | `ReportGenerator.cs:3085` | the attribute |
| S11 | The arrow keys move the chosen scenario inside the hidden Features panel while a tab has the focus | `report-tabs-script.js:55-61`, `report-keyboard-navigation-function.js:14-29` | ignored while a tab has the focus |
| S12 | Stale comments: the History link "opens the scenario" through the hash handler, which it never reaches; the tabs script names "a diagnostic's" link, which does not exist | `HistoryHtml.cs:139`, `report-tabs-script.js:47-48` | corrected |
| S13 | Every tab shares the window's scroll position, so a tab opens wherever the last one was scrolled | `report-tabs-script.js:10-25` | 4.4: a tab opens where the reader was in it, or at its top |
| S15 | "The first segment names a scenario" is written three times | `report-url-hash-function.js:7-8`, `report-tabs-script.js:36-37`, `report-features-tools-script.js:175-176` | one function |
| S16 | Two ways to a scenario that differ: the clusters open and jump to `start` without the stable id's row resolution, the rest go through `reveal_url_anchor` and centre | `ReportGenerator.cs:2111`, `report-url-hash-function.js:25-46` | one way |
| S20 | Ctrl+click, Meta+click and Shift+click on a History or failure cluster link cannot open a new tab or window: the inline `onclick` cancels every click, and the page jumps in place instead (measured in all three engines, fact 12) | `HistoryHtml.cs:143`, `ReportGenerator.cs:2111` | 4.6 |
| S21 | In an `<iframe srcdoc>`, how the Azure DevOps viewer extensions show an artifact, every address write throws `SecurityError` in Chromium and WebKit (measured, fact 11): `location.pathname + location.search + hash` is relative, and resolves against the embedding page. The throw stops what follows the write: a search that finds one scenario no longer opens it, matching example rows are not marked or selected, and an emptied search does not reset the deep search. 4.x has the same code | `report-url-hash-function.js:101`, `report-export-function.js:35`, `report-search-function.js:137`, `:172`, `:223`, `:259-289` | 4.4's writer; Q9 asks for a 4.x patch |
| S22 | A stale comment: `jump_into_view` says Safari has no scroll anchoring; Safari 27.0 has it on by default since 2026-09-14 (research 4.5). The re-aim stays, for Safari 26 and older and for the placeholders | `report-url-hash-function.js:48-54` | the comment says "Safari 26 and older" |

S4 (the browser's own scroll restoration fighting the `hashchange` reveal) is answered by 4.5's `manual` restoration;
S14 (the link button writes the weak slug address, and there is no "Copy link") is Q6; S17 (layers without the session
history) is 4.7; S18 and S19 are the constraints of 5.3 and section 3.

## 7. Tests

Tests first (CLAUDE.md): every fact below is written before its code, and fails on `v5` as it is unless it is a guard
that says so (7.4); the UI facts are Playwright facts. The pages are local files opened from `file://`, as every E2E test opens them
(`ReportTestHelper.cs`), with no network mocking, no forced clicks, and `PollingInterval = 200` on every wait. Each test
first opens `about:blank`, then the report, so that "Back left the report" is a page at `about:blank` and not a test
that cannot go back.

Two helpers in `PlaywrightTestBase`: `BackAsync()` and `ForwardAsync()` call `Page.GoBackAsync()` and
`Page.GoForwardAsync()`, which work for same-document entries in all three engines (fact 2), and then wait until the
module's counter (`<html data-kron-nav>`, 5.1) has risen, so a test reads the page after the tab, the filters, the
scroll and the focus are back, never in between. A framed report is the exception: there Playwright's `GoBackAsync`
waits for a top-level navigation that never comes (fact 10), so its test calls `history.back()` at the top level. The
tests press real controls (`ClickAsync`, `Keyboard`), never `pushState` through an evaluation: Playwright's evaluations
count as a user action in every engine (probe, "How to read this"), so an entry pushed from one would pass a test that
the reader's page, pushing from a timer, would fail. And since Firefox's Back does nothing on a page that has had no
user interaction (fact 2), every Firefox fact makes a real click before its first Back, as any reader has.

### 7.1 Playwright: `BackForwardTests` (new, one class per group)

**Places**

| # | Fact | Red on `v5` because |
|---|---|---|
| P1 | The owner's case: the History tab scrolled to an entry, its link followed, Back shows the History tab with the link where it was (within 2 px) and focused; Forward shows the scenario open, where it was left | Back goes to `about:blank` |
| P2 | Timeline, then Components, then History: Back three times shows Components, Timeline and Features in turn, Forward the other way | as P1 |
| P3 | Clicking the selected tab, or the view already shown, adds no entry (`history.length` unchanged) | holds today; kept as a guard for rule 7 |
| P4 | A run of arrow keys across the tabs is one entry, as is one across the views | Back goes to `about:blank` |
| P5 | Full, Summary, a row: Back shows the Summary view with the table where it was; Back again the Full view | as P1 |
| P6 | A failure cluster link: Back returns to the cluster list where it was, the link focused | as P1 |
| P7 | The link button with Failed chosen: the address is `#scenario-<slug>&status=Failed`, and Back returns | the address drops the filter (S2) |
| P8 | Next Failure three times, pressed from the History tab: Back shows the second failure, the first, then the History tab | as P1 |
| P9 | A reload at each step of P1 and P5 opens the same tab, view, scenario and filters, scrolled where the reader was | the tab and view are lost |
| P10 | An address with `tab=history` opened in a fresh page opens the History tab | no `tab=` |
| P11 | Addresses of version 2 and of 4.x open as before (`#sid-<id>&status=Failed&deps=Payments`, `#scenario-<slug>`, `hp=1`) | holds today; guards compatibility |
| P12 | Ctrl+click on a History link and on a failure cluster link opens a new page at that scenario's address and leaves this one unchanged (S20) | the inline `onclick` cancels it |

**Filters**

| # | Fact | Red because |
|---|---|---|
| F1 | A chip chosen, then Back: the chip is off, the readout and the address as before; Forward: on again | Back goes to `about:blank` |
| F2 | A word typed in the search is one entry; Back empties the field and the whole list shows | as F1 |
| F3 | Cycling one tag chip through its three states is one entry; another chip adds a second | as F1 |
| F4 | Clear All, then Back: every filter back | as F1 |
| F5 | Back from an entry with Failed chosen to one without: Failed is off (applied exactly, not added) | S3 |
| F6 | An address with filters typed into an open report applies them | S3 |
| F7 | A History link to a scenario the filters hide shows the hidden line, and its button clears the filters and shows the scenario | S7 |
| F8 | Clearing the search leaves open a scenario the reader had opened | S8 |
| F9 | A filter changed after going Back adds an entry, so Forward then has nothing to go to | as F1 |

**Layers**

| # | Fact | Red because |
|---|---|---|
| L1 | The lightbox open, Back closes it and leaves the tab and the scroll position as they were; Back again goes to the entry before it | Back leaves the report with the lightbox open |
| L2 | The lightbox closed by Esc, and by its backdrop, leaves no entry behind: the next Back goes to the entry before it | holds today (no entry at all); guards 4.7's `history.back()` |
| L3 | Forward after L1 opens the lightbox again (Q4) | no entry |
| L4 | L1 to L3 for the internal-flow popup | as L1 |
| L5 | A reload with the lightbox open shows the place without it | holds today; guards 4.7 |

**Putting the place back**

| # | Fact | Red because |
|---|---|---|
| R1 | In a long report (StableIdDeepLinkTests' far-down fixture), Back lands on the same scenario under the bar, in Chromium, Firefox and WebKit | no entry |
| R2 | Each tab opens where the reader left it when its tab is clicked again (S13) | the window's one position |
| R3 | After any Back, the focus is never inside a hidden panel | the focus stays on the followed link, hidden |
| R4 | Leaving for a non-image attachment and coming back by Back puts the tab and the position back | the tab is lost on a reload |
| R5 | The report in an iframe: Back at the top level goes through the report's entries first, then leaves the embedding page's (fact 10) | no entry |
| R6 | The exported Filtered HTML and the Specifications report: links, filters and Back work without tabs | no entry |
| R7 | A configured Timeline start tab with a `#sid-` address: the scenario is under the bar on the first aim (S5) | revealed while hidden |
| R8 | Two parameterized groups of one name: each link reaches its own group (S9) | the second reaches the first |
| R9 | A failure cluster link to an example row of an outline shown flat selects the row shown and scrolls to it (S6) | the hidden copy |
| R10 | The arrow keys while a tab has the focus leave the hidden scenarios alone (S11) | they move |
| R11 | The report in an `<iframe srcdoc>` under an http page: a filter changes the address to `about:srcdoc#status=...`, a search for one scenario opens it, and Back undoes the filter (Chromium and WebKit); in Firefox no write throws out of the page (S21) | the write throws (fact 11) |
| R12 | After a Back to the History tab, the status line names the tab; after a click, it says nothing | no status line |
| R13 | WebKit: 150 clicks on one chip within a few seconds, and a held arrow key across the tabs, raise no exception, and the address ends on the last state (the write budget; Playwright's Chromium and Firefox have their limits switched off, fact 7) | `update_url_hash` throws from the 101st write |
| R14 | The search writes at once on Enter: Back right after Enter undoes the search, with no 300 ms wait | no entry |

### 7.2 Core (`tests/Kronikol.Tests`)

| # | Fact |
|---|---|
| C1 | The module is in every report written to be drawn in the page, in the Specifications report, and in the head an exported page copies |
| C2 | No `onclick` on `a.history-link` or `a.failure-cluster-scenario-link`; the History tab's links keep `href="#sid-<id>"` |
| C3 | `data-start-tab` and `data-start-view` follow `ScenarioTimelineVisible`, `ComponentDiagramVisible` and `FeaturesSummaryOpen` |
| C4 | The module's text names none of the five tab keys and contains no `history-` |
| C5 | Two parameterized groups of one display name get two ids (S9) |
| C6 | `#features-sort` carries `autocomplete="off"` (S10) |

`DefaultReportCleanTests` and `PersistentFilterStateReportTests` stay as they are and must pass with the module in the
page (5.3).

### 7.3 Pins that move

| Test | Pins today | After |
|---|---|---|
| `ClearAllFiltersReportTests.cs:66` | the exact `replaceState` call in `clear_all_filters` | `clear_all_filters` calls `update_url_hash` and writes no address itself |
| `FailureClusterReportTests.cs:135`, `:200`, `:239`, `:259` | the link's inline `onclick` (`el.click()`, `jump_into_view(el,'start')`, `event.preventDefault()`) | the link's `href` and no `onclick`; its behaviour stays pinned in Playwright (`FailureClusterLinkTests`) |
| `DeepLinkReportTests.cs:61` | `parse_url_hash` contains `reveal_url_anchor(anchor)` | `apply_url_hash` does |
| `UrlAnchoredFilterReportTests.cs:53` | `history.replaceState` somewhere in the page | the module's `replaceState` and `pushState` |
| `UrlHashFilterTests.cs:64-120` | lines in the bodies of `update_url_hash` and `parse_url_hash` | the same lines, in `update_url_hash` and `apply_url_hash` |

Kept as they are, and expected to pass unchanged: `StableIdDeepLinkTests` (both), `HistorySectionTests.cs:47`,
`ReportTabsTests.cs:95` and `:108`, `LabsPageTests`, `UrlHashFilterHighlightTests`, `FilterPipelineTests.cs:234-293`
(inventory 4.2).

### 7.4 Proofs

- **Red first.** Each fact runs on `v5` before its code and fails for the reason its table gives; a fact that passes
  there is a guard and says so (P3, P11, L2, L5).
- **Mutations.** `mutate-backnav.py`, modelled on `OPTION_C_PLAN.harness/checks/mutate-s8.py`, undoes one behaviour at a
  time and runs the fact meant to catch it: a tab click replacing instead of adding, the scroll restored from `y` only,
  the focus not restored, `scrollRestoration` left `auto`, a run of one control not coalesced and a different control
  coalesced, filters applied additively, the hidden line missing, a layer closed without `history.back()`, the modifier
  keys not passed through, the anchor revealed before the tab shows, a relative address written (S21), the write budget
  removed, a write's exception left uncaught. Every mutation must be killed, and a mutation is counted only when its
  target passes unmutated.

### 7.5 Browsers and load

Chromium for every fact. Firefox and WebKit (both installed with Playwright 1.59.1 on this machine) for P1, P5, R1, L1,
F5 and R11, where the engines differ (facts 3, 5, 11), and WebKit for R13, the only engine Playwright leaves
rate-limited. The E2E suite's other facts run in Chromium as they do today. The full Playwright suite runs after each
step; across sessions it is announced and serialized as the peers do. No step launches an installed Chrome or Edge
(CLAUDE.md): the bundled browsers only.

## 8. Releases and where the work happens

### 8.1 Branch and the Option C session

The work needs Option C's tabs, views and filter bar, so it is built on a branch made from `v5`, `v5-back-button`, in a
worktree of its own (`Kronikol-v5-back`, checked out with LF), as `v5-tags` and `v5-report-format` are. It edits twelve
files the Option C session also edits (5.2). Before the first commit:

1. The Option C session is told which files and functions change (5.2) and when, by `SendMessage`.
2. The rule for D48 and the module's focus (5.3) is agreed, whichever lands first.
3. The branch is rebased onto `v5` before each step, and merged into `v5` (for 5.0.0) or released after it (5.1.0) as Q1
   decides.

### 8.2 Steps

| Step | Content | Tests |
|---|---|---|
| N0 | This plan's harness: the inventory, the research, the probe | done |
| N1 | The module (5.1) and its writer (absolute addresses, the budget, every write caught) and the places: tabs, views, links, Summary rows, Next Failure; address version 3; scroll and focus put back, the status line; the load order; links with a modifier key; S1, S5, S6, S9 to S13, S15, S16, S20 to S22 | P1 to P12, R1 to R3, R6 to R13, C1 to C6 |
| N2 | Filters as entries; exact application on a traversal and on a typed address; the search's Enter; S2, S3, S7, S8 | F1 to F9, R14 |
| N3 | Layers | L1 to L5 |
| N4 | Leaving and returning, hosts | R4, R5 |
| N5 | Docs: the wiki's Generated-Reports.md "Deep links" section becomes "Addresses, Back and Forward" (version 3, the rules of 4.1, what Back does for each kind of action), the README where it shows a deep link, the changelog, the Kronikol4J divergence ledger entry, and "Migrating to v5" if 5.0.0 | the wiki link check (`tools/wiki-links`) |

### 8.3 Version

- **In 5.0.0** (Q1, recommended): no number of its own. The branch's changelog `[Unreleased]` section gains an Added entry
  (Back and Forward, `tab=` and `view=`) and Fixed entries for S2 to S22.
- **As 5.1.0**: one minor for N1 to N5 together, since `tab=` and `view=` are new keys of a documented address and Back's
  behaviour is new (CLAUDE.md: anything new is a minor). One release and not three: a report whose Back worked for
  places but not for filters would teach readers a rule the next release changes.

## 9. Questions for the owner

Work proceeds on each recommendation unless the owner says otherwise.

1. **5.0.0 or 5.1.0?** Recommendation: **5.0.0**. 5.0.0 puts the History tab in the report by default, and on 4.x the
   same links are on the labs page, where Back works (2.4): shipping 5.0.0 without this makes the owner's flow worse
   than 4.x's. 5.0.0 also changes the address already (version 2), so readers meet one change of addresses, not two.
   The cost: twelve files shared with Option C's last stages (8.1). 5.1.0 if 5.0.0's date should not move.
2. **Filters and Back.** (a) An entry per control used, a run of changes to one control being one entry (recommended:
   it is what Baymard's user testing found readers expect, "Tapping 'Back' after applying filters should begin removing
   the applied filters", research 8); (b) one entry per run of filter changes of any kind, so Back undoes a whole
   narrowing at once (the research notes' own suggestion, React Router's search pattern applied to chips; fewer entries
   against the 50 a tab keeps, fact 8); (c) no entries for filters, as Allure 3, Cucumber's report and Grafana do, with
   the filters still in the address: cheaper, but Back never undoes a filter.
3. **Next Failure.** (a) An entry per press (recommended: Back is then "the previous failure", which the report has no
   button for); (b) one entry per run of presses, so that stepping through thirty failures does not spend thirty of a
   tab's fifty entries (fact 8).
4. **Forward to a layer's entry.** (a) Opens the layer again (recommended: Forward undoes Back, as it does for the
   photo viewers whose overlays have addresses of their own); (b) shows the place without it and takes the layer out of
   the entry, as the research notes' pattern does (research 6.5). With (b) that Forward, and the Back after it, change
   nothing on the screen.
5. **A link to a scenario the filters hide.** (a) A line where the scenario would be, with a button that clears the
   filters (recommended: says what happened, changes nothing unasked); (b) clear the filters at once; (c) show the
   scenario despite the filters, marked as shown by the link.
6. **The link button's address.** (a) `#scenario-<slug>`, as today (recommended for this plan); (b) `#sid-<id>`, the
   stable form every producer outside the page writes (the wiki says to prefer it). A "Copy link" action beside the
   copy-name button is a new feature and not in this plan.
7. **Timeline rows and Diagnostics entries as links to their scenarios.** Not in this plan (a new feature, 4.11).
   Recommendation: yes, after it, since every link then gets Back for free.
   **Answered for the Timeline (owner, 2026-10-23), released as 5.5.0:** each row's name is an
   `<a class="timeline-label" href="#sid-<stableId>">`, which the navigation script takes as it takes the History tab's
   links, with no change to the script (`BackForwardPlacesTests` P13 to P15). The Diagnostics entries stay text.
8. **The Navigation API.** Recommendation: **the History API only**, for every browser (fact 13). The Navigation API
   would be cleaner (one `navigate` event for links, Back and Forward), but it would need the History API beside it for
   Firefox ESR 115, Safari before 26.2 and sandboxed frames, and probably for Safari on `file://`, the report's most
   common host; two paths would double the tests for no change a reader sees. 4.4's one writer and 4.5's one arrival
   keep a later move mechanical, for when the API is Baseline widely available (by the Baseline rule, about July 2028).
9. **4.x.** (a) S21 as a 4.x patch now (recommended): `update_url_hash` and `clear_all_filters` write absolute
   addresses and never throw, so the search works again in the Azure DevOps viewers. It is a few lines, it needs none
   of this plan, and 4.x is what readers have until 5.0.0. (b) No backport of the navigation model (recommended): the
   failure cluster links and the link button have the same flaw in 4.x, but 4.x's History links are on the labs page,
   and two implementations of the model would have to be kept in step.
10. **Kronikol4J.** A divergence ledger entry only (recommended); porting Option C to Kronikol4J is a separate decision
    (`OPTION_C_PLAN.md` section 5).
11. **A reload.** (a) Back to the exact position (recommended: the reader may have moved on from the scenario a link
    took them to); (b) to the anchor, as today.
12. **A tab clicked again.** (a) Opens where the reader left it (recommended, S13); (b) at its top.
13. **A page title per place.** WCAG's understanding of 2.4.2 asks a single-page app to change the page's title with
    its views, and the browser's long-press Back menu lists entries by title, so every entry this plan adds would read
    the same there (research 5.1 and the close of its section 5). (a) The title names the place, for example "History · <the report's
    title>", set after the entry is added; (b) the title stays as it is. Not in this plan unless the owner says (a): it
    changes what a browser tab and a bookmark show.
14. **Full and Summary.** (a) The switch adds an entry (recommended: it replaces the whole Features panel, which
    Baymard's test reads as a new page, and it is drawn as tabs); (b) it replaces, as a view option, which is what the
    research notes suggest after Android's old rule and Smashing (research 5.2 and the close of its section 5). Either way a Summary row opening its
    card adds an entry, so Back from the card returns to the table.

## 10. Log

- 2026-10-19: written at the owner's ask, against `v5` at `d385c715` and 4.32.0, by a session that touched neither
  (read through `git show v5:...`; the plan's worktree is `Kronikol-backnav`, branch `plan/back-button-navigation` from
  `origin/main`). The inventory's key citations were re-read by hand. Measured the same day with the bundled browsers
  only (no installed Chrome or Edge): the probe (section 3's facts 1 to 10, 13), the modifier clicks (S20) and the
  srcdoc writes (S21). S21 is a live 4.x bug, found by reasoning from the research's note on `srcdoc` viewers and then
  measured; Q9 proposes it as a 4.x patch. Not committed: the owner decides whether the plan is pushed (it changes no
  package, so it needs no version bump).
- 2026-10-20: executed at the owner's word ("implement the plan in full"), as 5.2.0, by the session that wrote it, in a
  worktree of its own (`Kronikol-back`, branch `feat/back-button-navigation` from `origin/main` at `0e0fdb4a`, 5.1.0).
  5.0.0 and 5.1.0 had shipped since the plan was written, so Q1 was settled by the calendar: a minor of its own, 5.2.0.
  The other questions took their recommendations (Q2 a, Q3 a, Q4 a, Q5 a, Q6 a, Q8, Q10, Q11 a, Q12 a, Q14 a); Q7 and
  Q13 stay out, as the plan says; Q9's 4.x patch is left to the owner, since 5.x is what readers have now. The line
  numbers of sections 2 to 6 are on `v5` at `d385c715`; on `0e0fdb4a` they moved by a few lines and no cited code
  changed but D48's focus helper, which landed as `report-focus-script.js` and acts only on focus Tab moves, so the
  module's restore needs no mark (5.3). Kronikol-0b (5.1.1, toggle performance) was told the files first and agreed the
  numbering; Playwright runs were held during its timing runs.
  Decided while building, beyond the plan's text: a link the reader follows is remembered as the focus to return to
  even where the browser does not focus a clicked link (WebKit); a scenario taller than the window is shown from its
  title rather than centred (the clusters went to the top and every other link centred, S16); the filters an address
  carries are compared control by control, so the same filters written in another order replace rather than add an
  entry; the wiki keeps its "Deep links" heading, which three links point at, and gains a "Back and Forward" section
  after it.
  Found while making the facts pass, and fixed: an address with no filters, reached by Back, left the scenarios the
  filters had hidden hidden (the visibility pass `apply_url_hash` holds ran only when a filter function did); a bare
  word in the address (`#top`, an internal-flow arrow's `#iflow-<id>` a click did not cancel) was read as a filter and
  closed an open layer; the position was written to the entry only as the page went away, which a reload in Chromium
  does not see (it is written whenever the reader stops scrolling now), and it was read at one point that could fall in
  the space beside the report's header (it tries further points now); the line for a scenario the filters hide sat
  inside its feature, closed on a long report. One 5.1.0 defect came up the same way: a search with one match opened it
  inside a closed feature, out of sight, and emptying the search could only close scenarios; the search now opens the
  feature and rule round its match and closes on emptying only what it opened (F10). Two facts changed with what they
  could ask: R6 for an exported page asks for its links, since an export has no filters, and R9 checks the row by its
  stable id, since every table starts with its first row selected. The pins of two fenced scripts moved
  (`VerdictFencesTests`: the helper and the search function, outside their fences). Proofs: on 5.1.0's code 50 of the
  57 browser cases written first failed and all 22 core facts written or moved; the 7 that passed are guards of what
  5.1.0 already did. Of the 22 mutations (the harness's `checks/mutate-backnav.py`), 18 were killed at the first run;
  three of the four that survived showed facts that could not tell (R1 draws nothing between leaving and returning, R3
  is met by the tab's focus, and R13's clicks never reached WebKit's limit), so R16 was written for the
  position, P1 and P6 now hold the focus mutation and R13 clicks 150 times in one task: 21 of 22 are killed. The
  survivor leaves the browser's own scroll restoration on (`auto`), which no fact can see, since the page puts the
  position back after the browser in all three engines; `manual` stays, as 4.5 says. Checks: the core suite passes
  8,516 with 2 skipped, the full Playwright suite 1,471 with 28 skipped, and the Back and Forward classes 62 of 62
  on 5.1.1's `1f190827`.

## Appendix A. The harness

`plans/BACK_BUTTON_NAVIGATION_PLAN.harness/`:

| Path | What it is |
|---|---|
| `inventory.md` | The code inventory this plan cites: every interaction that moves the reader or opens a layer, with its handler, what it changes, whether it writes the address or adds an entry, and what Back does after it (section 1, 1A to 1G); every `hashchange` listener and the load order (2); the ids and the addresses written outside the page (3); the tests that pin this behaviour (4); 4.x against 5.0.0 (5); S1 to S19 (6). `v5` at `d385c715`, 4.x at `17914701`, every line re-read when it was written |
| `research.md` | The research, one section per question, every claim with its source and date and what was not confirmed (listed at its end): the Navigation API, the History API and its limits, frames and the CI hosts, scroll restoration, accessibility and what deserves an entry, overlays, the precedents in other report tools, filters |
| `probe/` | The measurements of section 3: `make-pages.js` writes the pages, `run.js` drives them (`node run.js <chromium|chromium-real|firefox|webkit> [case,...] [--runs N]`, from that folder; `chromium-real` drops Playwright's two switches that turn off Chromium's back/forward cache and its rate limiter), `make-results.js` writes `RESULTS.md` from `results/`, the raw readings. Bundled browsers only, through `tools/playwright/installed-browser.js`; the Playwright package is the one the .NET driver ships (`microsoft.playwright` 1.59.0 in the NuGet cache) |
| `s20/` | `s20-probe.js` and its results: modifier clicks on a link whose click handler calls `preventDefault()`, against a plain link (fact 12, S20) |
| `s21/` | `s21-probe.js` and its results: the address writes of an `<iframe srcdoc>` under http and `file://` parents, with and without a sandbox, three ways of building the address (fact 11, S21) |
