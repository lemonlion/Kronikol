'use strict';
// Prose that make-results.js puts around the generated tables. Keys: summary, header, footer, '<Q>:before', '<Q>'.
module.exports = {
  summary: `## Findings

1. pushState/replaceState with a fragment or query change work on file:// and http in every engine. A path change works on
   http and throws SecurityError on file:// in Chromium, WebKit and stock Firefox (Q2, Q2b).
2. Back and Forward between same-document entries fire popstate, then hashchange when the fragment differs, in every engine
   (Q1, Q3, Q5). popstate also fires for fragment navigations (link click, \`location.hash = …\`, \`navigation.navigate('#x')\`),
   with state null (Q3, Q5, Q9).
3. With \`scrollRestoration = 'auto'\` every engine restores the scroll of same-document entries, after popstate and before
   hashchange (Q6). With 'manual' none does, except WebKit for entries made by a fragment navigation, because WebKit gives those
   entries 'auto' again (Q5b; small-checks.js).
4. Scroll restoration by pixel offset is not reliable on a content-visibility:auto page. scrollIntoView of the saved section plus
   the saved offset landed on the saved section in 58 of 58 trials (every engine, Back, Back after a detour, reload).
   \`scrollTo(savedY)\` landed in 34 of 58 and the browser's own 'auto' restore in 14 of 58 (Q7, Q7b).
5. Rate limits: WebKit throws SecurityError from the 101st pushState or replaceState in 10 s. Chromium silently ignores calls from
   the 201st in 10 s, with one console warning; Playwright's default switches hide this. Firefox has no limit at 500 calls. Session
   history caps at 50 entries in Chromium and Firefox and 100 in WebKit (Q8, Q8b, Q8c).
6. Navigation API: in Chromium 147 and Firefox 148, on file:// too; not in Playwright's WebKit 26.4 (Q9).
7. An iframe shares the top-level session history in every engine: the child's pushes grow top.history.length, and a top-level
   Back walks the child's entries (Q10). In a sandbox="allow-scripts" frame, pushState('#…') works except in Firefox on file://
   (SecurityError).
8. A real reload keeps history.state and the entry's scrollRestoration mode in every engine (Q11, Q11b, Q11c).
9. bfcache: Chrome for Testing restores the http report (persisted=true, JS state intact) but not the file:// one
   (BrowsingInstanceNotSwapped). Nothing could be measured for Firefox, and WebKit's build did not restore (Q12, Q12b, Q12c).
10. history.length: +1 per pushState, +0 per replaceState (Q13).
11. Firefox: \`page.goBack()\` does nothing on a page that has had no user interaction, though \`history.back()\` works. One real
    click is enough (X1, X1b, X2). Chromium and WebKit stepped back normally.
`,
  header: `### How to read this

- Columns: engine and protocol. file = \`file:///…/pages/report.html\`; http = \`http://127.0.0.1:<port 0>/report.html\` from a Node
  \`http\` server with no Cache-Control header. Every case runs in a fresh browser context, twice (run 1 and run 2, same browser
  launch). A cell shows run 1; where run 2 differed it adds **[run 2: …]**, and the cell is listed under "Flaky cells". Rows
  marked "run 1 / run 2" show both runs on purpose (timings).
- "Back"/"Forward" are Playwright's \`page.goBack()\`/\`page.goForward()\` unless a row says \`history.back()\`. What they drive is not
  the same in every engine (read from playwright-core 1.59.1 and the Firefox build's juggler):
  - Chromium: CDP \`Page.getNavigationHistory\` then \`Page.navigateToHistoryEntry(entries[currentIndex - 1])\`, a browser-initiated
    traversal to the previous entry by index (the navigate event reports userInitiated: true).
  - Firefox: juggler's \`Page.goBack\` returns without navigating unless the browser element's \`canGoBack\` is true, then calls
    \`browsingContext.goBack()\` (the navigate event reports userInitiated: false).
  - WebKit: the \`Page.goBack\` protocol command, \`WebPageProxy::goBack\`.
- \`page.evaluate\` runs with a user gesture in all three engines: Chromium (CDP \`userGesture: true\`), WebKit (\`emulateUserGesture:
  true\`) and Firefox (juggler's Runtime.js wraps every evaluation in \`windowUtils.setHandlingUserInput(true)\` and
  \`document.notifyUserGestureActivation()\`). So every harness-driven pushState counts as user-activated. X1, X1b and X2 push
  from page timers with no evaluate before Back; Q2b, Q8b and Q8c repeat their calls from page-owned timer callbacks.
- Two Playwright artifacts in Firefox, found during the run and measured around:
  - \`playwright.cfg\` sets \`security.fileuri.strict_origin_policy\` to false (stock Firefox: true). Every file:// cell for Firefox
    in the main tables runs with that pref; Q2b, Q10b and Q11c add "Firefox (stock file:// origin pref)" columns.
  - Over http, \`page.reload()\` in Firefox (juggler runs the browser's \`Browser:Reload\` command) does not reload: it adds a
    session history entry (history.length +1), the navigation type is \`navigate\`, \`history.state\` is null and
    \`scrollRestoration\` is back to auto (Q11c). The page's own \`location.reload()\` is a real reload there. So the Firefox http
    cells of Q7's and Q11's reload rows are not reload measurements; Q7b and Q11b repeat them with \`location.reload()\` in every
    engine. Over file://, \`page.reload()\` is a real reload in Firefox too.
- A Playwright page starts on about:blank. Chromium and WebKit keep it as entry 1 (history.length is 2 at load); Firefox replaces it
  (1). A user's tab has whatever came before the report, so read history.length as a delta.
- Playwright switches that change what is measured: Chromium is launched with \`--disable-back-forward-cache\` and
  \`--disable-ipc-flooding-protection\` (the second turns off Blink's navigation rate limiter). The "Chromium (bfcache + rate limiter
  on)" runs drop both (\`ignoreDefaultArgs\`); the main tables say whether that changed anything. Firefox: \`playwright.cfg\` sets
  \`fission.bfcacheInParent=false\` and juggler sets \`docShell.disallowBFCache = true\` on every page
  (omni.ja \`chrome/juggler/content/content/main.js\`), so Firefox's bfcache cannot be measured under Playwright. WebKit: no switch.
- Chromium is the headless shell, not the full browser: no toolbar, no Alt+Left, and its embedder turns bfcache off (Q12b). For
  Q12c only, Playwright's bundled full Chromium (Chrome for Testing 147, \`channel: 'chromium'\`) ran once, on a profile seeded with
  the repository's \`seedProfile()\` so that it skips Windows' blank-password sign-in; the account's BadPasswordAttempts counter
  read 0 before and 0 after a 16 s session.
- WebKit here is Playwright's Windows build of WebKit 26.4 (the WinCairo port), not Safari. Its feature flags and back-forward
  cache policy need not match Safari on macOS or iOS.
`,
  Q1: `Every engine, both protocols: pushState succeeds and \`location.hash\` updates at once; pushing fires no event. Back fires
popstate (state {k:1}) then hashchange (#b>#a); Forward fires popstate ({k:2}) then hashchange (#a>#b). Where the Navigation API
exists its \`navigate\` (traverse) event comes first. Chromium then fires currententrychange and navigatesuccess, Firefox
navigatesuccess and currententrychange, and both fire popstate and hashchange after those.`,
  Q2: `On file://, a query change is allowed in every engine and a path change throws SecurityError in Chromium and WebKit. Playwright's
Firefox allows it only because its profile turns \`security.fileuri.strict_origin_policy\` off; stock Firefox throws
"SecurityError: The operation is insecure." (Q2b). On http every change within the origin is allowed.`,
  Q2b: 'A call made by a page-owned timer gets the same answer as one made by page.evaluate in every engine.',
  Q3: `\`location.hash = 'c'\` adds one entry with history.state null and scrolls to #c before the assignment returns. It fires popstate
(state null) then hashchange in every engine, so a popstate handler also sees fragment navigations, not only traversals. Back
brings back the previous entry's state ({k:'base'}), with popstate then hashchange.`,
  Q4: `Assigning the same hash never adds an entry. Chromium and WebKit do nothing at all: no scroll and no event. Firefox scrolls back
to #c and fires navigate(replace), currententrychange and navigatesuccess, but neither popstate nor hashchange.
\`location.href = location.href\` re-scrolls in every engine without adding an entry. Chromium and WebKit also fire popstate
(state null); Firefox does not.`,
  Q5: `A real click adds an entry and scrolls to #target. It fires popstate (state null) then hashchange, plus navigate with
userInitiated in Chromium and Firefox. On Back, 'auto' restores y=800 in every engine; with 'manual' the page stays at #target.
The Forward rows cannot tell a scroll restore from a fragment scroll, since both are 6000; Q5b separates them.`,
  Q5b: `Forward under 'auto' restores the entry's own scroll (6500), not the fragment position, in every engine. Under 'manual',
Chromium and Firefox do not scroll at all. WebKit restores 6500 because a fragment navigation's entry does not inherit 'manual'
there. small-checks.js, run from a 'manual' entry: after a link click or \`location.hash = 'c'\`, \`history.scrollRestoration\` reads
'auto' in WebKit and 'manual' in Chromium and Firefox. After pushState it reads 'manual' everywhere. A page that wants 'manual'
in WebKit has to set it again after every fragment navigation.`,
  Q6: `Under 'auto' every engine restores 3000 on Back and the pushed entry's 0 on Forward. Inside the popstate handler the scroll is
still 0; by hashchange it is restored, in all three engines. So code in popstate runs before the browser's own restore and can
be overwritten by it. Under 'manual' nothing is restored.`,
  Q7: `Outcome counts over every valid trial (q7-counts.js; Q7 and Q7b, 2 runs x 2 protocols, Firefox's http page.reload() trials
excluded):

| Restore | Chromium | Chromium (switches off) | Firefox | WebKit | All |
|---|---|---|---|---|---|
| (b) scrollIntoView(saved section) + saved offset | 16/16 | 12/12 | 14/14 | 16/16 | **58/58** |
| (a) scrollTo(savedY) | 6/16 | 6/12 | 10/14 | 12/16 | 34/58 |
| browser's own 'auto' restore | 3/16 | 4/12 | 3/14 | 4/16 | 14/58 |

- Back after a detour through s120: s120's neighbours render, shrink from 500 px to 60 px and keep that size. (a) and the
  browser's restore then land on s288 in every engine, every run. (b) lands on the saved section.
- Back after a plain jump to the top: (a) and the browser's restore are usually right. They are wrong (s257+20px) whenever one
  more top section rendered after the save. Then savedY is 104320 instead of 103880: 440 px, one 500 px placeholder minus its
  60 px rendered height. That happened in some Chromium runs and one Firefox run.
- Reload: the browser's own restore is wrong in every engine and run (Chromium s272 or s228, Firefox s235, WebKit s242).
  Chromium is at y 125000 at pageshow, exactly where s250 starts while the 250 sections above it are still 500 px placeholders.
  So it restores to the saved element, not the saved pixel offset. Then the sections around it render and the page ends on s272.
  In three of the four chromium-real trials it later re-applied the saved pixel offset (the final y equals savedY) and ended
  on s228. (a) after reload is right in Firefox and WebKit. In Chromium it is right after page.reload() in 7 of 8 trials and
  wrong after location.reload() in 4 of 4: it is right only when the same sections end up rendered as at save time.
- WebKit has no scroll anchoring (\`CSS.supports('overflow-anchor', 'none')\` is false; true in Chromium and Firefox). Its
  \`scrollIntoView(s250)\` itself settles on s257+20px: a section above renders after the jump and nothing compensates for the
  440 px. WebKit's saved position is therefore s257+20px in every run, and (b) restores that. Chromium and Firefox keep s250 at
  the top.
- The Chromium differences listed under the table come from this same timing. They do not follow the bfcache or rate-limiter
  switch.`,
  Q7b: "location.reload() gives the same picture as Q7's page.reload() rows, except in Chromium, where (a) failed in all four trials.",
  Q8: `Under Playwright's defaults Chromium has no rate limit at all; the "rate limiter on" columns are Chromium's real behaviour.
Chromium's ignored calls return normally but change nothing: URL, state and history.length stay as they were. One console
warning appears per window. Calls work again 10.0 s after the window opened (Q8b). WebKit throws from the 101st call for
pushState and replaceState alike, with the message in the table, until 10.1 s after the burst. Firefox let 500 calls of either
kind through, from page.evaluate and from page timers alike (Q8b), with no exception and no console message.`,
  Q8c: `Session history caps: 50 entries in Chromium and 100 in WebKit (2 + 100 successful pushes reads 100). In Firefox,
history.length reads 501 right after the loop and falls to 50 within 3 s as the parent process trims; the 1 s sample varies by run.`,
  Q9: `Chromium 147 and Firefox 148 expose the Navigation API on file:// as well as http. On file://, where self.origin is "null" in
both, currentEntry and entries() work and the events fire. Playwright's WebKit 26.4 does not expose it ('navigation' in window is
false), so every WebKit row is n/a. Facts that matter:
- \`navigation.navigate('#x')\` commits and finishes, and fires popstate (state null) and hashchange like any fragment navigation.
- Navigation API state and history.state are separate: after \`navigate('#y', {state: {a:1}})\`, getState() is {a:1} and history.state
  is null.
- A Back to a same-document entry fires navigate with navigationType 'traverse' and canIntercept true; getState() follows the entry.
- intercept() is allowed both for a same-document hash navigation and for a same-document traverse, and the URL still changes.
  An intercepted push fires no hashchange in either engine, and no popstate in Firefox. An intercepted traverse fires popstate in
  both, and hashchange only in Firefox.`,
  Q10: `Every engine joins the session history. The child's pushes add to the top-level history.length. A top-level Back, by
page.goBack() or by the top's history.back(), walks the child's entries, firing popstate then hashchange in the child and
nothing in the top. Playwright's page.goBack() and goForward() then time out because they wait for a main-frame navigation, but
the traversal did happen. In a sandbox="allow-scripts" frame (opaque origin), pushState('#f1') works in Chromium and WebKit on
both protocols and in Firefox over http. On file:// Firefox throws "SecurityError: The operation is insecure." with either pref.
The Navigation API is switched off inside an opaque-origin frame in Chromium and Firefox: currentEntry is null and entries() is
empty. With allow-same-origin every engine behaves as with a plain iframe.`,
  Q10b: "With the stock pref, a file:// parent cannot read a file:// child's document in any engine; the child's own pushState still works.",
  Q11: `The Firefox http cells are page.reload() artifacts (see How to read this); Q11b has the real figures. In every real reload,
history.state, the fragment and the entry's scrollRestoration survive. Under 'auto' the position comes back (2500), and it wins
over a fragment that names an element (#c). Under 'manual' nothing is restored. When the fragment names an element, Chromium
and WebKit scroll to it on reload and Firefox stays at 0. Timing under 'auto': with #r, Chromium and Firefox have restored by
pageshow and WebKit restores after it (y 0 at pageshow). With #c, Chromium and WebKit are at the fragment (3000) at pageshow and
reach 2500 after load, while Firefox is at 2500 already.`,
  Q11b: `One timing cell differs between runs: Chromium http, 'auto' with #c, where y at pageshow was 3000 or 2500. The final
position was 2500 in both runs.`,
  Q12: `None of the three engines restored from bfcache under Playwright. Chromium's headless shell switches bfcache off in its embedder
(Q12b: BackForwardCacheDisabledForDelegate), Firefox's juggler switches it off on every page, and Playwright's WebKit build
reloaded the page. WebKit has no notRestoredReasons API, so the build cannot say why. Q12c is the measurement that means
something for Chromium.`,
  Q12c: `In Chrome for Testing 147 the http report comes back from bfcache: pagehide and pageshow with persisted=true, JS state intact,
hash and history.state as left, and the navigation type stays 'navigate'. The pushState before leaving does not block restore.
The file:// report does not come back (CDP: BrowsingInstanceNotSwapped; notRestoredReasons only says 'masked'). It reloads and
loses JS state, but keeps its hash and history.state.`,
  Q13: 'pushState adds one entry and replaceState none, in every engine.',
  X1: `Chromium's and WebKit's page.goBack() step to #na1. The page's own history.back(), called from a timer with no activation,
steps to #na1 in every engine. Firefox's page.goBack() does nothing here; X1b shows that is not a race.`,
  X1b: `With a 1 s pause, Firefox's page.goBack() still does nothing (history.length 3, still #na2). Juggler returns without
navigating when the browser element's canGoBack is false, so on a page with no user interaction Firefox's browser-level Back is
unavailable, while history.back() from the page works (X1).`,
  X2: `After one real click on the first entry, Firefox's page.goBack() works and steps to #na1. No engine skipped #na1, although it
was pushed without activation. This does not tell whether a real toolbar Back skips it; see "Not measured" below.`,
  flaky: `All 17 are timing, and none is a harness fault. The Q7 "saved" cells differ by exactly 440 px, that is whether one more
section had rendered when the position was saved, and the Q7 outcome cells next to them follow from that. The
"[Back, via s120 then top, manual] (a)" cell differs only in its first, immediate sample; both runs end on s288. Q8b and Q8c are
Firefox trimming session history asynchronously (the 1 s sample). Q11b is whether Chromium had restored by pageshow; both runs
end at 2500.`,
  footer: `## Not measured, and why

- **The toolbar Back button.** Chrome and Edge skip entries added without user activation (the history manipulation
  intervention), and Firefox has a similar user-interaction rule. Neither can be driven here. Chromium's page.goBack() is a CDP
  traversal by index, and the headless shell has no toolbar or Alt+Left. Firefox's juggler calls \`browsingContext.goBack()\`
  without the user-interaction flag; only its \`canGoBack\` gate shows (X1, X1b, X2). Installed Chrome and Edge were off limits on
  this machine.
- **Firefox bfcache.** Juggler sets \`docShell.disallowBFCache = true\` on every page; no pref turns it back on.
- **Safari and iOS.** Only Playwright's Windows WebKit 26.4 ran. It has no Navigation API and did not restore from bfcache;
  Safari may differ on both.
- **Mobile.** No Android back gesture, no swipe navigation (\`hasUAVisualTransition\`), no mobile browsers.
- **Headed mode.** Every run was headless.
- **pushState and replaceState sharing one rate-limit budget.** Each kind was measured on its own fresh page.

## Files

All in this folder. \`make-pages.js\` writes \`pages/*.html\`. \`run.js <engine>\` runs Q1 to Q13 and X1, twice, into
\`results/<engine>.json\`. \`extras.js <engine> <cases>\` runs the follow-ups into \`results/extras-<engine>-<case>.json\`.
\`cft-bfcache.js\` runs Q12c into \`results/cft-bfcache.json\`, and \`small-checks.js\` writes \`results/small-checks.json\`.
\`q7-counts.js\` prints the Q7 outcome counts. \`make-results.js\` builds this file, with prose from \`notes.js\`. \`peek.js\` and
\`cols.js\` are viewers. Every script requires playwright-core 1.59.1 by absolute path from the .NET driver's copy
(\`%USERPROFILE%\\.nuget\\packages\\microsoft.playwright\\1.59.0\\.playwright\\package\`), because its browsers.json pins the three
installed browser revisions. The Kronikol4J copy (1.61.0) pins Firefox and WebKit revisions that are not installed.
`,
};
