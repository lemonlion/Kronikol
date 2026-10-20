# History API and Navigation API probe: results

Measured 2026-10-19 on Windows 11 (10.0.26200), headless, viewport 1280x720, Node v25.9.0, playwright-core 1.59.1 (the copy the .NET driver ships, whose browsers.json pins the three installed revisions). Only Playwright's bundled browsers were launched.

| Configuration | browser.version() | Binary | Launch options |
|---|---|---|---|
| Chromium | 147.0.7727.15 | ms-playwright\chromium_headless_shell-1217\chrome-headless-shell-win64\chrome-headless-shell.exe | headless, Playwright defaults |
| Chromium (bfcache + rate limiter on) | 147.0.7727.15 | ms-playwright\chromium_headless_shell-1217\chrome-headless-shell-win64\chrome-headless-shell.exe | headless, ignoreDefaultArgs ['--disable-back-forward-cache', '--disable-ipc-flooding-protection'] |
| Firefox | 148.0.2 | ms-playwright\firefox-1511\firefox\firefox.exe | headless, Playwright defaults |
| WebKit | 26.4 | ms-playwright\webkit-2272\Playwright.exe | headless, Playwright defaults |
| Firefox (stock file:// origin pref) (Q2b, Q10b, Q11c only) | 148.0.2 | ms-playwright\firefox-1511\firefox\firefox.exe | headless, firefoxUserPrefs {'security.fileuri.strict_origin_policy': true} |
| Chrome for Testing (Q12c only) | 147.0.7727.15 | ms-playwright\chromium-1217\chrome-win64\chrome.exe | channel 'chromium' (new headless), launchPersistentContext on a seeded profile, ignoreDefaultArgs ['--disable-back-forward-cache'] |

## Findings

1. pushState/replaceState with a fragment or query change work on file:// and http in every engine. A path change works on
   http and throws SecurityError on file:// in Chromium, WebKit and stock Firefox (Q2, Q2b).
2. Back and Forward between same-document entries fire popstate, then hashchange when the fragment differs, in every engine
   (Q1, Q3, Q5). popstate also fires for fragment navigations (link click, `location.hash = …`, `navigation.navigate('#x')`),
   with state null (Q3, Q5, Q9).
3. With `scrollRestoration = 'auto'` every engine restores the scroll of same-document entries, after popstate and before
   hashchange (Q6). With 'manual' none does, except WebKit for entries made by a fragment navigation, because WebKit gives those
   entries 'auto' again (Q5b; small-checks.js).
4. Scroll restoration by pixel offset is not reliable on a content-visibility:auto page. scrollIntoView of the saved section plus
   the saved offset landed on the saved section in 58 of 58 trials (every engine, Back, Back after a detour, reload).
   `scrollTo(savedY)` landed in 34 of 58 and the browser's own 'auto' restore in 14 of 58 (Q7, Q7b).
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
11. Firefox: `page.goBack()` does nothing on a page that has had no user interaction, though `history.back()` works. One real
    click is enough (X1, X1b, X2). Chromium and WebKit stepped back normally.


### How to read this

- Columns: engine and protocol. file = `file:///…/pages/report.html`; http = `http://127.0.0.1:<port 0>/report.html` from a Node
  `http` server with no Cache-Control header. Every case runs in a fresh browser context, twice (run 1 and run 2, same browser
  launch). A cell shows run 1; where run 2 differed it adds **[run 2: …]**, and the cell is listed under "Flaky cells". Rows
  marked "run 1 / run 2" show both runs on purpose (timings).
- "Back"/"Forward" are Playwright's `page.goBack()`/`page.goForward()` unless a row says `history.back()`. What they drive is not
  the same in every engine (read from playwright-core 1.59.1 and the Firefox build's juggler):
  - Chromium: CDP `Page.getNavigationHistory` then `Page.navigateToHistoryEntry(entries[currentIndex - 1])`, a browser-initiated
    traversal to the previous entry by index (the navigate event reports userInitiated: true).
  - Firefox: juggler's `Page.goBack` returns without navigating unless the browser element's `canGoBack` is true, then calls
    `browsingContext.goBack()` (the navigate event reports userInitiated: false).
  - WebKit: the `Page.goBack` protocol command, `WebPageProxy::goBack`.
- `page.evaluate` runs with a user gesture in all three engines: Chromium (CDP `userGesture: true`), WebKit (`emulateUserGesture:
  true`) and Firefox (juggler's Runtime.js wraps every evaluation in `windowUtils.setHandlingUserInput(true)` and
  `document.notifyUserGestureActivation()`). So every harness-driven pushState counts as user-activated. X1, X1b and X2 push
  from page timers with no evaluate before Back; Q2b, Q8b and Q8c repeat their calls from page-owned timer callbacks.
- Two Playwright artifacts in Firefox, found during the run and measured around:
  - `playwright.cfg` sets `security.fileuri.strict_origin_policy` to false (stock Firefox: true). Every file:// cell for Firefox
    in the main tables runs with that pref; Q2b, Q10b and Q11c add "Firefox (stock file:// origin pref)" columns.
  - Over http, `page.reload()` in Firefox (juggler runs the browser's `Browser:Reload` command) does not reload: it adds a
    session history entry (history.length +1), the navigation type is `navigate`, `history.state` is null and
    `scrollRestoration` is back to auto (Q11c). The page's own `location.reload()` is a real reload there. So the Firefox http
    cells of Q7's and Q11's reload rows are not reload measurements; Q7b and Q11b repeat them with `location.reload()` in every
    engine. Over file://, `page.reload()` is a real reload in Firefox too.
- A Playwright page starts on about:blank. Chromium and WebKit keep it as entry 1 (history.length is 2 at load); Firefox replaces it
  (1). A user's tab has whatever came before the report, so read history.length as a delta.
- Playwright switches that change what is measured: Chromium is launched with `--disable-back-forward-cache` and
  `--disable-ipc-flooding-protection` (the second turns off Blink's navigation rate limiter). The "Chromium (bfcache + rate limiter
  on)" runs drop both (`ignoreDefaultArgs`); the main tables say whether that changed anything. Firefox: `playwright.cfg` sets
  `fission.bfcacheInParent=false` and juggler sets `docShell.disallowBFCache = true` on every page
  (omni.ja `chrome/juggler/content/content/main.js`), so Firefox's bfcache cannot be measured under Playwright. WebKit: no switch.
- Chromium is the headless shell, not the full browser: no toolbar, no Alt+Left, and its embedder turns bfcache off (Q12b). For
  Q12c only, Playwright's bundled full Chromium (Chrome for Testing 147, `channel: 'chromium'`) ran once, on a profile seeded with
  the repository's `seedProfile()` so that it skips Windows' blank-password sign-in; the account's BadPasswordAttempts counter
  read 0 before and 0 after a 16 s session.
- WebKit here is Playwright's Windows build of WebKit 26.4 (the WinCairo port), not Safari. Its feature flags and back-forward
  cache policy need not match Safari on macOS or iOS.


## Q1. pushState('#a'), pushState('#b'), then Back and Forward

| Measure | Chromium file | Chromium http | Firefox file | Firefox http | WebKit file | WebKit http |
|---|---|---|---|---|---|---|
| pushState({k:1}, "", "#a") | ok, hash #a, history.length 2 > 3 | ok, hash #a, history.length 2 > 3 | ok, hash #a, history.length 1 > 2 | ok, hash #a, history.length 1 > 2 | ok, hash #a, history.length 2 > 3 | ok, hash #a, history.length 2 > 3 |
| pushState({k:2}, "", "#b") | ok, hash #b, history.length 4 | ok, hash #b, history.length 4 | ok, hash #b, history.length 3 | ok, hash #b, history.length 3 | ok, hash #b, history.length 4 | ok, hash #b, history.length 4 |
| Events fired by the two pushes (popstate/hashchange) | none | none | none | none | none | none |
| page.goBack() | resolved | resolved | resolved | resolved | resolved | resolved |
| Back: popstate/hashchange, in order | popstate(state={k:1}) then hashchange(#b>#a) | popstate(state={k:1}) then hashchange(#b>#a) | popstate(state={k:1}) then hashchange(#b>#a) | popstate(state={k:1}) then hashchange(#b>#a) | popstate(state={k:1}) then hashchange(#b>#a) | popstate(state={k:1}) then hashchange(#b>#a) |
| Back: every event, in order | navigate(traverse, hashChange, userInitiated) then currententrychange then navigatesuccess then popstate(state={k:1}) then hashchange(#b>#a) | navigate(traverse, hashChange, userInitiated) then currententrychange then navigatesuccess then popstate(state={k:1}) then hashchange(#b>#a) | navigate(traverse, hashChange) then navigatesuccess then currententrychange then popstate(state={k:1}) then hashchange(#b>#a) | navigate(traverse, hashChange) then navigatesuccess then currententrychange then popstate(state={k:1}) then hashchange(#b>#a) | popstate(state={k:1}) then hashchange(#b>#a) | popstate(state={k:1}) then hashchange(#b>#a) |
| After Back: history.state, location.hash | {k:1}, #a | {k:1}, #a | {k:1}, #a | {k:1}, #a | {k:1}, #a | {k:1}, #a |
| Forward: popstate/hashchange, in order | popstate(state={k:2}) then hashchange(#a>#b) | popstate(state={k:2}) then hashchange(#a>#b) | popstate(state={k:2}) then hashchange(#a>#b) | popstate(state={k:2}) then hashchange(#a>#b) | popstate(state={k:2}) then hashchange(#a>#b) | popstate(state={k:2}) then hashchange(#a>#b) |
| Forward: every event, in order | navigate(traverse, hashChange, userInitiated) then currententrychange then navigatesuccess then popstate(state={k:2}) then hashchange(#a>#b) | navigate(traverse, hashChange, userInitiated) then currententrychange then navigatesuccess then popstate(state={k:2}) then hashchange(#a>#b) | navigate(traverse, hashChange) then navigatesuccess then currententrychange then popstate(state={k:2}) then hashchange(#a>#b) | navigate(traverse, hashChange) then navigatesuccess then currententrychange then popstate(state={k:2}) then hashchange(#a>#b) | popstate(state={k:2}) then hashchange(#a>#b) | popstate(state={k:2}) then hashchange(#a>#b) |
| After Forward: history.state, location.hash | {k:2}, #b | {k:2}, #b | {k:2}, #b | {k:2}, #b | {k:2}, #b | {k:2}, #b |

With bfcache and the rate limiter on (chromium-real), Chromium gives the same values in every row.

Every engine, both protocols: pushState succeeds and `location.hash` updates at once; pushing fires no event. Back fires
popstate (state {k:1}) then hashchange (#b>#a); Forward fires popstate ({k:2}) then hashchange (#a>#b). Where the Navigation API
exists its `navigate` (traverse) event comes first. Chromium then fires currententrychange and navigatesuccess, Firefox
navigatesuccess and currententrychange, and both fire popstate and hashchange after those.

## Q2. pushState that changes the query or the path

| Measure | Chromium file | Chromium http | Firefox file | Firefox http | WebKit file | WebKit http |
|---|---|---|---|---|---|---|
| '#a' (control) | ok, hash #a | ok, hash #a | ok, hash #a | ok, hash #a | ok, hash #a | ok, hash #a |
| '?x=1#a' (query) | allowed: file:///…/report.html?x=1#a | allowed: http://127.0.0.1:port/report.html?x=1#a | allowed: file:///…/report.html?x=1#a | allowed: http://127.0.0.1:port/report.html?x=1#a | allowed: file:///…/report.html?x=1#a | allowed: http://127.0.0.1:port/report.html?x=1#a |
| 'report.html?y=2' (same path, query) | allowed: file:///…/report.html?y=2 | allowed: http://127.0.0.1:port/report.html?y=2 | allowed: file:///…/report.html?y=2 | allowed: http://127.0.0.1:port/report.html?y=2 | allowed: file:///…/report.html?y=2 | allowed: http://127.0.0.1:port/report.html?y=2 |
| 'other.html' (path, same directory) | SecurityError: Failed to execute 'pushState' on 'History': A history state object with URL 'file:///…/other.html' cannot be created in a document with origin 'null' and URL 'file:///…/report.html'. | allowed: http://127.0.0.1:port/other.html | allowed: file:///…/other.html | allowed: http://127.0.0.1:port/other.html | SecurityError: Blocked attempt to use history.pushState() to change session history URL from file:///…/report.html to file:///…/other.html. Only differences in query and fragment are allowed for file: URLs. | allowed: http://127.0.0.1:port/other.html |
| 'sub/other.html' (path, subdirectory) | SecurityError: Failed to execute 'pushState' on 'History': A history state object with URL 'file:///…/sub/other.html' cannot be created in a document with origin 'null' and URL 'file:///…/report.html'. | allowed: http://127.0.0.1:port/sub/other.html | allowed: file:///…/sub/other.html | allowed: http://127.0.0.1:port/sub/other.html | SecurityError: Blocked attempt to use history.pushState() to change session history URL from file:///…/report.html to file:///…/sub/other.html. Only differences in query and fragment are allowed for file: URLs. | allowed: http://127.0.0.1:port/sub/other.html |

With bfcache and the rate limiter on (chromium-real), Chromium gives the same values in every row.

On file://, a query change is allowed in every engine and a path change throws SecurityError in Chromium and WebKit. Playwright's
Firefox allows it only because its profile turns `security.fileuri.strict_origin_policy` off; stock Firefox throws
"SecurityError: The operation is insecure." (Q2b). On http every change within the origin is allowed.

## Q2b. Q2 on file:// again: stock Firefox origin pref, and the same push from a page-owned timer (extras.js file-origin)

| Measure | Chromium file | Firefox file | Firefox (stock file:// origin pref) file | WebKit file |
|---|---|---|---|---|
| '?x=1#a' by page.evaluate | allowed: …/report.html?x=1#a | allowed: …/report.html?x=1#a | allowed: …/report.html?x=1#a | allowed: …/report.html?x=1#a |
| '?x=1#a' by a page timer | allowed: …/report.html?x=1#a | allowed: …/report.html?x=1#a | allowed: …/report.html?x=1#a | allowed: …/report.html?x=1#a |
| 'other.html' by page.evaluate | SecurityError: Failed to execute 'pushState' on 'History': A history state object with URL 'file:///…/other.html' cannot be created in a document with origin 'null' and URL 'file:///…/report.html'. | allowed: …/other.html | SecurityError: The operation is insecure. | SecurityError: Blocked attempt to use history.pushState() to change session history URL from file:///…/report.html to file:///…/other.html. Only differences in query and fragment are allowed for file: URLs. |
| 'other.html' by a page timer | SecurityError: Failed to execute 'pushState' on 'History': A history state object with URL 'file:///…/other.html' cannot be created in a document with origin 'null' and URL 'file:///…/report.html'. | allowed: …/other.html | SecurityError: The operation is insecure. | SecurityError: Blocked attempt to use history.pushState() to change session history URL from file:///…/report.html to file:///…/other.html. Only differences in query and fragment are allowed for file: URLs. |
| 'sub/other.html' by page.evaluate | SecurityError: Failed to execute 'pushState' on 'History': A history state object with URL 'file:///…/sub/other.html' cannot be created in a document with origin 'null' and URL 'file:///…/report.html'. | allowed: …/sub/other.html | SecurityError: The operation is insecure. | SecurityError: Blocked attempt to use history.pushState() to change session history URL from file:///…/report.html to file:///…/sub/other.html. Only differences in query and fragment are allowed for file: URLs. |
| 'sub/other.html' by a page timer | SecurityError: Failed to execute 'pushState' on 'History': A history state object with URL 'file:///…/sub/other.html' cannot be created in a document with origin 'null' and URL 'file:///…/report.html'. | allowed: …/sub/other.html | SecurityError: The operation is insecure. | SecurityError: Blocked attempt to use history.pushState() to change session history URL from file:///…/report.html to file:///…/sub/other.html. Only differences in query and fragment are allowed for file: URLs. |

A call made by a page-owned timer gets the same answer as one made by page.evaluate in every engine.

## Q3. location.hash = 'c' (from an entry whose state is {k:'base'}; #c is at y=3000)

| Measure | Chromium file | Chromium http | Firefox file | Firefox http | WebKit file | WebKit http |
|---|---|---|---|---|---|---|
| New entry? (history.length before > after) | 2 > 3 (yes) | 2 > 3 (yes) | 1 > 2 (yes) | 1 > 2 (yes) | 2 > 3 (yes) | 2 > 3 (yes) |
| history.state right after the assignment | null | null | null | null | null | null |
| scrollY right after / after settle | 3000 / 3000 | 3000 / 3000 | 3000 / 3000 | 3000 / 3000 | 3000 / 3000 | 3000 / 3000 |
| Events of the assignment, in order | navigate(push, hashChange) then currententrychange then popstate(state=null) then navigatesuccess then hashchange((no hash)>#c) | navigate(push, hashChange) then currententrychange then popstate(state=null) then navigatesuccess then hashchange((no hash)>#c) | navigate(push, hashChange) then currententrychange then popstate(state=null) then navigatesuccess then hashchange((no hash)>#c) | navigate(push, hashChange) then currententrychange then popstate(state=null) then navigatesuccess then hashchange((no hash)>#c) | popstate(state=null) then hashchange((no hash)>#c) | popstate(state=null) then hashchange((no hash)>#c) |
| Back: popstate/hashchange, in order | popstate(state={k:"base"}) then hashchange(#c>(no hash)) | popstate(state={k:"base"}) then hashchange(#c>(no hash)) | popstate(state={k:"base"}) then hashchange(#c>(no hash)) | popstate(state={k:"base"}) then hashchange(#c>(no hash)) | popstate(state={k:"base"}) then hashchange(#c>(no hash)) | popstate(state={k:"base"}) then hashchange(#c>(no hash)) |
| Back: every event, in order | navigate(traverse, hashChange, userInitiated) then currententrychange then navigatesuccess then popstate(state={k:"base"}) then hashchange(#c>(no hash)) | navigate(traverse, hashChange, userInitiated) then currententrychange then navigatesuccess then popstate(state={k:"base"}) then hashchange(#c>(no hash)) | navigate(traverse, hashChange) then navigatesuccess then currententrychange then popstate(state={k:"base"}) then hashchange(#c>(no hash)) | navigate(traverse, hashChange) then navigatesuccess then currententrychange then popstate(state={k:"base"}) then hashchange(#c>(no hash)) | popstate(state={k:"base"}) then hashchange(#c>(no hash)) | popstate(state={k:"base"}) then hashchange(#c>(no hash)) |
| After Back: history.state, hash, scrollY | {k:"base"}, (no hash), 0 | {k:"base"}, (no hash), 0 | {k:"base"}, (no hash), 0 | {k:"base"}, (no hash), 0 | {k:"base"}, (no hash), 0 | {k:"base"}, (no hash), 0 |
| Forward: popstate/hashchange, in order | popstate(state=null) then hashchange((no hash)>#c) | popstate(state=null) then hashchange((no hash)>#c) | popstate(state=null) then hashchange((no hash)>#c) | popstate(state=null) then hashchange((no hash)>#c) | popstate(state=null) then hashchange((no hash)>#c) | popstate(state=null) then hashchange((no hash)>#c) |
| After Forward: history.state, hash, scrollY | null, #c, 3000 | null, #c, 3000 | null, #c, 3000 | null, #c, 3000 | null, #c, 3000 | null, #c, 3000 |

With bfcache and the rate limiter on (chromium-real), Chromium gives the same values in every row.

`location.hash = 'c'` adds one entry with history.state null and scrolls to #c before the assignment returns. It fires popstate
(state null) then hashchange in every engine, so a popstate handler also sees fragment navigations, not only traversals. Back
brings back the previous entry's state ({k:'base'}), with popstate then hashchange.

## Q4. Assigning the same hash again (at #c, scrolled away to y=500 first)

| Measure | Chromium file | Chromium http | Firefox file | Firefox http | WebKit file | WebKit http |
|---|---|---|---|---|---|---|
| location.hash = location.hash | no new entry (3); no re-scroll (y 500); events: none | no new entry (3); no re-scroll (y 500); events: none | no new entry (2); re-scrolled (y 500 > 3000); events: navigate(replace) then currententrychange then navigatesuccess | no new entry (2); re-scrolled (y 500 > 3000); events: navigate(replace) then currententrychange then navigatesuccess | no new entry (3); no re-scroll (y 500); events: none | no new entry (3); no re-scroll (y 500); events: none |
| location.hash = '#c' | no new entry (3); no re-scroll (y 500); events: none | no new entry (3); no re-scroll (y 500); events: none | no new entry (2); re-scrolled (y 500 > 3000); events: navigate(replace) then currententrychange then navigatesuccess | no new entry (2); re-scrolled (y 500 > 3000); events: navigate(replace) then currententrychange then navigatesuccess | no new entry (3); no re-scroll (y 500); events: none | no new entry (3); no re-scroll (y 500); events: none |
| location.href = location.href (for comparison) | no new entry (3); re-scrolled (y 500 > 3000); events: navigate(replace) then currententrychange then popstate(state=null) then navigatesuccess | no new entry (3); re-scrolled (y 500 > 3000); events: navigate(replace) then currententrychange then popstate(state=null) then navigatesuccess | no new entry (2); re-scrolled (y 500 > 3000); events: navigate(replace) then currententrychange then navigatesuccess | no new entry (2); re-scrolled (y 500 > 3000); events: navigate(replace) then currententrychange then navigatesuccess | no new entry (3); re-scrolled (y 500 > 3000); events: popstate(state=null) | no new entry (3); re-scrolled (y 500 > 3000); events: popstate(state=null) |

With bfcache and the rate limiter on (chromium-real), Chromium gives the same values in every row.

Assigning the same hash never adds an entry. Chromium and WebKit do nothing at all: no scroll and no event. Firefox scrolls back
to #c and fires navigate(replace), currententrychange and navigatesuccess, but neither popstate nor hashchange.
`location.href = location.href` re-scrolls in every engine without adding an entry. Chromium and WebKit also fire popstate
(state null); Firefox does not.

## Q5. Clicking a real <a href="#target"> (link at y=1000, #target at y=6000, page at y=800 before the click)

| Measure | Chromium file | Chromium http | Firefox file | Firefox http | WebKit file | WebKit http |
|---|---|---|---|---|---|---|
| [auto] click: new entry, scrollY 800 > | new entry, y 6000, hash #target | new entry, y 6000, hash #target | new entry, y 6000, hash #target | new entry, y 6000, hash #target | new entry, y 6000, hash #target | new entry, y 6000, hash #target |
| [auto] click: events | navigate(push, hashChange, userInitiated) then currententrychange then navigatesuccess then popstate(state=null) then hashchange((no hash)>#target) | navigate(push, hashChange, userInitiated) then currententrychange then navigatesuccess then popstate(state=null) then hashchange((no hash)>#target) | navigate(push, hashChange, userInitiated) then navigatesuccess then currententrychange then popstate(state=null) then hashchange((no hash)>#target) | navigate(push, hashChange, userInitiated) then navigatesuccess then currententrychange then popstate(state=null) then hashchange((no hash)>#target) | popstate(state=null) then hashchange((no hash)>#target) | popstate(state=null) then hashchange((no hash)>#target) |
| [auto] Back: events | navigate(traverse, hashChange, userInitiated) then currententrychange then navigatesuccess then popstate(state=null) then hashchange(#target>(no hash)) | navigate(traverse, hashChange, userInitiated) then currententrychange then navigatesuccess then popstate(state=null) then hashchange(#target>(no hash)) | navigate(traverse, hashChange) then navigatesuccess then currententrychange then popstate(state=null) then hashchange(#target>(no hash)) | navigate(traverse, hashChange) then navigatesuccess then currententrychange then popstate(state=null) then hashchange(#target>(no hash)) | popstate(state=null) then hashchange(#target>(no hash)) | popstate(state=null) then hashchange(#target>(no hash)) |
| [auto] Back: scrollY (800 = restored), state | 800, state null, hash (no hash) | 800, state null, hash (no hash) | 800, state null, hash (no hash) | 800, state null, hash (no hash) | 800, state null, hash (no hash) | 800, state null, hash (no hash) |
| [auto] scroll to 300, Forward: scrollY (6000 = restored) | 6000, hash #target; events: popstate(state=null) then hashchange((no hash)>#target) | 6000, hash #target; events: popstate(state=null) then hashchange((no hash)>#target) | 6000, hash #target; events: popstate(state=null) then hashchange((no hash)>#target) | 6000, hash #target; events: popstate(state=null) then hashchange((no hash)>#target) | 6000, hash #target; events: popstate(state=null) then hashchange((no hash)>#target) | 6000, hash #target; events: popstate(state=null) then hashchange((no hash)>#target) |
| [manual] click: new entry, scrollY 800 > | new entry, y 6000, hash #target | new entry, y 6000, hash #target | new entry, y 6000, hash #target | new entry, y 6000, hash #target | new entry, y 6000, hash #target | new entry, y 6000, hash #target |
| [manual] click: events | navigate(push, hashChange, userInitiated) then currententrychange then navigatesuccess then popstate(state=null) then hashchange((no hash)>#target) | navigate(push, hashChange, userInitiated) then currententrychange then navigatesuccess then popstate(state=null) then hashchange((no hash)>#target) | navigate(push, hashChange, userInitiated) then navigatesuccess then currententrychange then popstate(state=null) then hashchange((no hash)>#target) | navigate(push, hashChange, userInitiated) then navigatesuccess then currententrychange then popstate(state=null) then hashchange((no hash)>#target) | popstate(state=null) then hashchange((no hash)>#target) | popstate(state=null) then hashchange((no hash)>#target) |
| [manual] Back: events | navigate(traverse, hashChange, userInitiated) then currententrychange then navigatesuccess then popstate(state=null) then hashchange(#target>(no hash)) | navigate(traverse, hashChange, userInitiated) then currententrychange then navigatesuccess then popstate(state=null) then hashchange(#target>(no hash)) | navigate(traverse, hashChange) then navigatesuccess then currententrychange then popstate(state=null) then hashchange(#target>(no hash)) | navigate(traverse, hashChange) then navigatesuccess then currententrychange then popstate(state=null) then hashchange(#target>(no hash)) | popstate(state=null) then hashchange(#target>(no hash)) | popstate(state=null) then hashchange(#target>(no hash)) |
| [manual] Back: scrollY (800 = restored), state | 6000, state null, hash (no hash) | 6000, state null, hash (no hash) | 6000, state null, hash (no hash) | 6000, state null, hash (no hash) | 6000, state null, hash (no hash) | 6000, state null, hash (no hash) |
| [manual] scroll to 300, Forward: scrollY (6000 = restored) | 300, hash #target; events: popstate(state=null) then hashchange((no hash)>#target) | 300, hash #target; events: popstate(state=null) then hashchange((no hash)>#target) | 300, hash #target; events: popstate(state=null) then hashchange((no hash)>#target) | 300, hash #target; events: popstate(state=null) then hashchange((no hash)>#target) | 6000, hash #target; events: popstate(state=null) then hashchange((no hash)>#target) | 6000, hash #target; events: popstate(state=null) then hashchange((no hash)>#target) |

With bfcache and the rate limiter on (chromium-real), Chromium gives the same values in every row.

A real click adds an entry and scrolls to #target. It fires popstate (state null) then hashchange, plus navigate with
userInitiated in Chromium and Firefox. On Back, 'auto' restores y=800 in every engine; with 'manual' the page stays at #target.
The Forward rows cannot tell a scroll restore from a fragment scroll, since both are 6000; Q5b separates them.

## Q5b. Q5 disambiguated: the #target entry is left at y=6500 (its own scroll), #target sits at 6000; Back; scroll to 300; Forward

| Measure | Chromium file | Chromium http | Firefox file | Firefox http | WebKit file | WebKit http |
|---|---|---|---|---|---|---|
| [auto] y after Back (800 = restored) | 800 | 800 | 800 | 800 | 800 | 800 |
| [auto] y after Forward | 6500 (restored the entry scroll) | 6500 (restored the entry scroll) | 6500 (restored the entry scroll) | 6500 (restored the entry scroll) | 6500 (restored the entry scroll) | 6500 (restored the entry scroll) |
| [manual] y after Back (800 = restored) | 6500 | 6500 | 6500 | 6500 | 6500 | 6500 |
| [manual] y after Forward | 300 (did not scroll) | 300 (did not scroll) | 300 (did not scroll) | 300 (did not scroll) | 6500 (restored the entry scroll) | 6500 (restored the entry scroll) |

Forward under 'auto' restores the entry's own scroll (6500), not the fragment position, in every engine. Under 'manual',
Chromium and Firefox do not scroll at all. WebKit restores 6500 because a fragment navigation's entry does not inherit 'manual'
there. small-checks.js, run from a 'manual' entry: after a link click or `location.hash = 'c'`, `history.scrollRestoration` reads
'auto' in WebKit and 'manual' in Chromium and Firefox. After pushState it reads 'manual' everywhere. A page that wants 'manual'
in WebKit has to set it again after every fragment navigation.

## Q6. Scroll restoration for a pushState entry (scroll to 3000, pushState #p, scroll to 0, Back)

| Measure | Chromium file | Chromium http | Firefox file | Firefox http | WebKit file | WebKit http |
|---|---|---|---|---|---|---|
| [auto] Back: scrollY at popstate, at hashchange, after | 0, 3000, 3000 | 0, 3000, 3000 | 0, 3000, 3000 | 0, 3000, 3000 | 0, 3000, 3000 | 0, 3000, 3000 |
| [auto] scroll to 1500, Forward: scrollY after (0 = restored) | 0 | 0 | 0 | 0 | 0 | 0 |
| [manual] Back: scrollY at popstate, at hashchange, after | 0, 0, 0 | 0, 0, 0 | 0, 0, 0 | 0, 0, 0 | 0, 0, 0 | 0, 0, 0 |
| [manual] scroll to 1500, Forward: scrollY after (0 = restored) | 1500 | 1500 | 1500 | 1500 | 1500 | 1500 |

With bfcache and the rate limiter on (chromium-real), Chromium gives the same values in every row.

Under 'auto' every engine restores 3000 on Back and the pushed entry's 0 on Forward. Inside the popstate handler the scroll is
still 0; by hashchange it is restored, in all three engines. So code in popstate runs before the browser's own restore and can
be overwritten by it. Under 'manual' nothing is restored.

## Q7. content-visibility:auto (300 sections, 60 px rendered, 500 px placeholder): save at s250, leave, come back, restore

| Measure | Chromium file | Chromium http | Firefox file | Firefox http | WebKit file | WebKit http |
|---|---|---|---|---|---|---|
| [Back, top, auto]: saved (y, top section) | 104320, s250 **[run 2: 103880, s250]** | 103880, s250 | 113560, s250 | 113560, s250 **[run 2: 114000, s250]** | 119280, s257+20px | 119280, s257+20px |
| [Back, top, auto]: browser's own restore (y, section) | y 104320 (at event: [0]), s257+20px, scrollRestoration auto **[run 2: y 103880 (at event: [0]), s250, scrollRestoration auto]** | y 103880 (at event: [0]), s250, scrollRestoration auto | y 113560 (at event: [0]), s250, scrollRestoration auto | y 113560 (at event: [0]), s250, scrollRestoration auto **[run 2: y 114000 (at event: [0]), s257+20px, scrollRestoration auto]** | y 119280 (at event: [0]), s257+20px, scrollRestoration auto | y 119280 (at event: [0]), s257+20px, scrollRestoration auto |
| [Back, top, manual]: saved (y, top section) | 103880, s250 **[run 2: 104320, s250]** | 103880, s250 | 113560, s250 | 113560, s250 | 119280, s257+20px | 119280, s257+20px |
| [Back, top, manual] (a) scrollTo(savedY) | now s250; after 2 frames s250; +600 ms s250 => SAME section **[run 2: now s257+20px; after 2 frames s257+20px; +600 ms s257+20px => WRONG section]** | now s250; after 2 frames s250; +600 ms s250 => SAME section | now s250; after 2 frames s250; +600 ms s250 => SAME section | now s250; after 2 frames s250; +600 ms s250 => SAME section | now s257+20px; after 2 frames s257+20px; +600 ms s257+20px => SAME section | now s257+20px; after 2 frames s257+20px; +600 ms s257+20px => SAME section |
| [Back, top, manual] (b) scrollIntoView(section) + offset | now s250; after 2 frames s250; +600 ms s250 => SAME section | now s250; after 2 frames s250; +600 ms s250 => SAME section | now s250; after 2 frames s250; +600 ms s250 => SAME section | now s250; after 2 frames s250; +600 ms s250 => SAME section | now s257+20px; after 2 frames s257+20px; +600 ms s257+20px => SAME section | now s257+20px; after 2 frames s257+20px; +600 ms s257+20px => SAME section |
| [Back, via s120 then top, auto]: saved (y, top section) | 104320, s250 | 103880, s250 | 113560, s250 | 113560, s250 | 119280, s257+20px | 119280, s257+20px |
| [Back, via s120 then top, auto]: browser's own restore (y, section) | y 85040 (at event: [0]), s288, scrollRestoration auto | y 85040 (at event: [0]), s288, scrollRestoration auto | y 109680 (at event: [0]), s288, scrollRestoration auto | y 109680 (at event: [0]), s288, scrollRestoration auto | y 118920 (at event: [0]), s288, scrollRestoration auto | y 118920 (at event: [0]), s288, scrollRestoration auto |
| [Back, via s120 then top, manual] (a) scrollTo(savedY) | now s298+280px; after 2 frames s288; +600 ms s288 => WRONG section **[run 2: now s295+20px; after 2 frames s288; +600 ms s288 => WRONG section]** | now s298+280px; after 2 frames s288; +600 ms s288 => WRONG section | now s289+300px; after 2 frames s288; +600 ms s288 => WRONG section | now s289+300px; after 2 frames s288; +600 ms s288 => WRONG section | now s289+300px; after 2 frames s288; +600 ms s288 => WRONG section | now s289+300px; after 2 frames s288; +600 ms s288 => WRONG section |
| [Back, via s120 then top, manual] (b) scrollIntoView + offset | now s250; after 2 frames s250; +600 ms s250 => SAME section | now s250; after 2 frames s250; +600 ms s250 => SAME section | now s250; after 2 frames s250; +600 ms s250 => SAME section | now s250; after 2 frames s250; +600 ms s250 => SAME section | now s257+20px; after 2 frames s257+20px; +600 ms s257+20px => SAME section | now s257+20px; after 2 frames s257+20px; +600 ms s257+20px => SAME section |
| [reload, auto]: saved (y, top section) | 104320, s250 **[run 2: 103880, s250]** | 104320, s250 **[run 2: 103880, s250]** | 113560, s250 | 114440, s250 **[run 2: 113560, s250]** | 119280, s257+20px | 119280, s257+20px |
| [reload, auto]: browser's own restore (y, section) | y 125000 (at event: [125000]), s272, scrollRestoration auto | y 125000 (at event: [125000]), s272, scrollRestoration auto | y 113560 (at event: [113560]), s235+20px, scrollRestoration auto | not a reload: Firefox's page.reload() over http adds an entry instead (Q11c); see Q7b | y 119280 (at event: [0]), s242+40px, scrollRestoration auto | y 119280 (at event: [0]), s242+40px, scrollRestoration auto |
| [reload, manual] browser scrollY after reload, scrollRestoration | 0, manual, state kept: yes | 0, manual, state kept: yes | 0, manual, state kept: yes | not a reload: Firefox's page.reload() over http adds an entry instead (Q11c); see Q7b | 0, manual, state kept: yes | 0, manual, state kept: yes |
| [reload, manual] (a) scrollTo(history.state.savedY) | now s234+80px; after 2 frames s250; +600 ms s250 => SAME section **[run 2: now s235+20px; after 2 frames s257+20px; +600 ms s257+20px => WRONG section]** | now s239+220px; after 2 frames s250; +600 ms s250 => SAME section **[run 2: now s234+80px; after 2 frames s250; +600 ms s250 => SAME section]** | now s243+420px; after 2 frames s250; +600 ms s250 => SAME section | not a reload: Firefox's page.reload() over http adds an entry instead (Q11c); see Q7b | now s252+320px; after 2 frames s257+20px; +600 ms s257+20px => SAME section | now s252+320px; after 2 frames s257+20px; +600 ms s257+20px => SAME section |
| [reload, manual] (b) scrollIntoView + offset from history.state | now s250; after 2 frames s250; +600 ms s250 => SAME section | now s250; after 2 frames s250; +600 ms s250 => SAME section | now s250; after 2 frames s250; +600 ms s250 => SAME section | not a reload: Firefox's page.reload() over http adds an entry instead (Q11c); see Q7b | now s257+20px; after 2 frames s257+20px; +600 ms s257+20px => SAME section | now s257+20px; after 2 frames s257+20px; +600 ms s257+20px => SAME section |

With bfcache and the rate limiter on, Chromium differs here: [Back, top, auto]: saved (y, top section) (file): 103880, s250; [Back, top, auto]: browser's own restore (y, section) (file): y 103880 (at event: [0]), s250, scrollRestoration auto; [Back, top, manual]: saved (y, top section) (file): 104320, s250; [Back, top, manual] (a) scrollTo(savedY) (file): now s257+20px; after 2 frames s257+20px; +600 ms s257+20px => WRONG section; [Back, via s120 then top, auto]: saved (y, top section) (file): 103880, s250; [Back, via s120 then top, manual] (a) scrollTo(savedY) (file): now s298+280px; after 2 frames s288; +600 ms s288 => WRONG section; [reload, auto]: saved (y, top section) (file): 103880, s250 **[run 2: 104320, s250]**; [reload, auto]: saved (y, top section) (http): 103880, s250; [reload, auto]: browser's own restore (y, section) (file): y 103880 (at event: [125000]), s228, scrollRestoration auto **[run 2: y 104320 (at event: [125000]), s228, scrollRestoration auto]**; [reload, auto]: browser's own restore (y, section) (http): y 125000 (at event: [125000]), s272, scrollRestoration auto **[run 2: y 103880 (at event: [125000]), s228, scrollRestoration auto]**; [reload, manual] (a) scrollTo(history.state.savedY) (file): now s234+80px; after 2 frames s250; +600 ms s250 => SAME section; [reload, manual] (a) scrollTo(history.state.savedY) (http): now s234+80px; after 2 frames s250; +600 ms s250 => SAME section.

Outcome counts over every valid trial (q7-counts.js; Q7 and Q7b, 2 runs x 2 protocols, Firefox's http page.reload() trials
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
- WebKit has no scroll anchoring (`CSS.supports('overflow-anchor', 'none')` is false; true in Chromium and Firefox). Its
  `scrollIntoView(s250)` itself settles on s257+20px: a section above renders after the jump and nothing compensates for the
  440 px. WebKit's saved position is therefore s257+20px in every run, and (b) restores that. Chromium and Firefox keep s250 at
  the top.
- The Chromium differences listed under the table come from this same timing. They do not follow the bfcache or rate-limiter
  switch.

## Q7b. Q7's reload variants again, reloading by location.reload() (extras.js q7reload)

| Measure | Chromium file | Chromium http | Firefox file | Firefox http | WebKit file | WebKit http |
|---|---|---|---|---|---|---|
| [reload, auto] saved (y, top section) | 103880, s250 | 103880, s250 | 113560, s250 | 113560, s250 | 119280, s257+20px | 119280, s257+20px |
| [reload, auto] browser's own restore | y 125000 (at pageshow: [125000]), s272, navigation type reload => WRONG section | y 125000 (at pageshow: [125000]), s272, navigation type reload => WRONG section | y 113560 (at pageshow: [113560]), s235+20px, navigation type reload => WRONG section | y 113560 (at pageshow: [113560]), s235+20px, navigation type reload => WRONG section | y 119280 (at pageshow: [0]), s242+40px, navigation type reload => WRONG section | y 119280 (at pageshow: [0]), s242+40px, navigation type reload => WRONG section |
| [reload, manual] saved; browser y after reload; state kept | 104320, s250; 0, scrollRestoration manual; yes | 104320, s250; 0, scrollRestoration manual; yes | 113560, s250; 0, scrollRestoration manual; yes | 114000, s250; 0, scrollRestoration manual; yes | 119280, s257+20px; 0, scrollRestoration manual; yes | 119280, s257+20px; 0, scrollRestoration manual; yes |
| [reload, manual] (a) scrollTo(history.state.savedY) | now s235+20px; after 2 frames s257+20px; +600 ms s257+20px => WRONG section | now s235+20px; after 2 frames s257+20px; +600 ms s257+20px => WRONG section | now s243+420px; after 2 frames s250; +600 ms s250 => SAME section | now s244+360px; after 2 frames s250; +600 ms s250 => SAME section | now s252+320px; after 2 frames s257+20px; +600 ms s257+20px => SAME section | now s252+320px; after 2 frames s257+20px; +600 ms s257+20px => SAME section |
| [reload, manual] (b) scrollIntoView + offset from history.state | now s250; after 2 frames s250; +600 ms s250 => SAME section | now s250; after 2 frames s250; +600 ms s250 => SAME section | now s250; after 2 frames s250; +600 ms s250 => SAME section | now s250; after 2 frames s250; +600 ms s250 => SAME section | now s257+20px; after 2 frames s257+20px; +600 ms s257+20px => SAME section | now s257+20px; after 2 frames s257+20px; +600 ms s257+20px => SAME section |

location.reload() gives the same picture as Q7's page.reload() rows, except in Chromium, where (a) failed in all four trials.

## Q8. 500 calls in a tight loop, each kind on a fresh page

| Measure | Chromium file | Chromium http | Firefox file | Firefox http | WebKit file | WebKit http | Chromium (bfcache + rate limiter on) file | Chromium (bfcache + rate limiter on) http |
|---|---|---|---|---|---|---|---|---|
| replaceState(null, '', '#n'+i) x500 | no exception; final hash #n499, history.length 2; no console message; one more call 300 ms later: ok, hash #more | no exception; final hash #n499, history.length 2; no console message; one more call 300 ms later: ok, hash #more | no exception; final hash #n499, history.length 1; no console message; one more call 300 ms later: ok, hash #more | no exception; final hash #n499, history.length 1; no console message; one more call 300 ms later: ok, hash #more | throws from call 101 (400 of 500 threw): SecurityError: Attempt to use history.replaceState() more than 100 times per 10 seconds; final hash #n99, history.length 2; no console message; one more call 300 ms later: SecurityError: Attempt to use history.replaceState() more than 100 times per 10 seconds | throws from call 101 (400 of 500 threw): SecurityError: Attempt to use history.replaceState() more than 100 times per 10 seconds; final hash #n99, history.length 2; no console message; one more call 300 ms later: SecurityError: Attempt to use history.replaceState() more than 100 times per 10 seconds | no exception; 300 calls silently ignored from call 201; final hash #n199, history.length 2; console: warning: Throttling navigation to prevent the browser from hanging. See https://crbug.com/1038223. Command line switch --disable-ipc-flooding-protection can be used to bypass the protection; one more call 300 ms later: ok, hash #n199 | no exception; 300 calls silently ignored from call 201; final hash #n199, history.length 2; console: warning: Throttling navigation to prevent the browser from hanging. See https://crbug.com/1038223. Command line switch --disable-ipc-flooding-protection can be used to bypass the protection; one more call 300 ms later: ok, hash #n199 |
| pushState(null, '', '#n'+i) x500 | no exception; final hash #n499, history.length 50; no console message; one more call 300 ms later: ok, hash #more | no exception; final hash #n499, history.length 50; no console message; one more call 300 ms later: ok, hash #more | no exception; final hash #n499, history.length 501; no console message; one more call 300 ms later: ok, hash #more | no exception; final hash #n499, history.length 501; no console message; one more call 300 ms later: ok, hash #more | throws from call 101 (400 of 500 threw): SecurityError: Attempt to use history.pushState() more than 100 times per 10 seconds; final hash #n99, history.length 100; no console message; one more call 300 ms later: SecurityError: Attempt to use history.pushState() more than 100 times per 10 seconds | throws from call 101 (400 of 500 threw): SecurityError: Attempt to use history.pushState() more than 100 times per 10 seconds; final hash #n99, history.length 100; no console message; one more call 300 ms later: SecurityError: Attempt to use history.pushState() more than 100 times per 10 seconds | no exception; 300 calls silently ignored from call 201; final hash #n199, history.length 50; console: warning: Throttling navigation to prevent the browser from hanging. See https://crbug.com/1038223. Command line switch --disable-ipc-flooding-protection can be used to bypass the protection; one more call 300 ms later: ok, hash #n199 | no exception; 300 calls silently ignored from call 201; final hash #n199, history.length 50; console: warning: Throttling navigation to prevent the browser from hanging. See https://crbug.com/1038223. Command line switch --disable-ipc-flooding-protection can be used to bypass the protection; one more call 300 ms later: ok, hash #n199 |
| Time for the 500 replaceState calls, ms (run 1 / run 2) | 52 / 62 | 98 / 66 | 318 / 657 | 229 / 200 | 2 / 3 | 2 / 5 | 24 / 24 | 19 / 49 |
| Time for the 500 pushState calls, ms (run 1 / run 2) | 58 / 52 | 92 / 49 | 313 / 337 | 336 / 209 | 3 / 3 | 2 / 3 | 33 / 29 | 19 / 35 |

Under Playwright's defaults Chromium has no rate limit at all; the "rate limiter on" columns are Chromium's real behaviour.
Chromium's ignored calls return normally but change nothing: URL, state and history.length stay as they were. One console
warning appears per window. Calls work again 10.0 s after the window opened (Q8b). WebKit throws from the 101st call for
pushState and replaceState alike, with the message in the table, until 10.1 s after the burst. Firefox let 500 calls of either
kind through, from page.evaluate and from page timers alike (Q8b), with no exception and no console message.

## Q8b. Q8 again from page-owned timer callbacks (extras.js ratelimit-page, ratelimit), and how long the limit lasts

| Measure | Chromium (bfcache + rate limiter on) file | Chromium (bfcache + rate limiter on) http | Firefox file | Firefox http | WebKit file | WebKit http |
|---|---|---|---|---|---|---|
| replaceState x500 from a page timer | no exception; calls 201 to 500 silently ignored (300); hash #n199; history.length 2, 1 s later 2; console: warning: Throttling navigation to prevent the browser from hanging. See https://crbug.com/1038223. Command line switch --disable-ipc-flooding-protection can be used to bypass the protection | no exception; calls 201 to 500 silently ignored (300); hash #n199; history.length 2, 1 s later 2; console: warning: Throttling navigation to prevent the browser from hanging. See https://crbug.com/1038223. Command line switch --disable-ipc-flooding-protection can be used to bypass the protection | no exception; hash #n499; history.length 1, 1 s later 1 | no exception; hash #n499; history.length 1, 1 s later 1 | throws from call 101 (400 of 500): SecurityError: Attempt to use history.replaceState() more than 100 times per 10 seconds; hash #n99; history.length 2, 1 s later 2 | throws from call 101 (400 of 500): SecurityError: Attempt to use history.replaceState() more than 100 times per 10 seconds; hash #n99; history.length 2, 1 s later 2 |
| pushState x500 from a page timer | no exception; calls 201 to 500 silently ignored (300); hash #n199; history.length 50, 1 s later 50; console: warning: Throttling navigation to prevent the browser from hanging. See https://crbug.com/1038223. Command line switch --disable-ipc-flooding-protection can be used to bypass the protection | no exception; calls 201 to 500 silently ignored (300); hash #n199; history.length 50, 1 s later 50; console: warning: Throttling navigation to prevent the browser from hanging. See https://crbug.com/1038223. Command line switch --disable-ipc-flooding-protection can be used to bypass the protection | no exception; hash #n499; history.length 501, 1 s later 142 **[run 2: no exception; hash #n499; history.length 501, 1 s later 148]** | no exception; hash #n499; history.length 501, 1 s later 196 **[run 2: no exception; hash #n499; history.length 501, 1 s later 203]** | throws from call 101 (400 of 500): SecurityError: Attempt to use history.pushState() more than 100 times per 10 seconds; hash #n99; history.length 100, 1 s later 100 | throws from call 101 (400 of 500): SecurityError: Attempt to use history.pushState() more than 100 times per 10 seconds; hash #n99; history.length 100, 1 s later 100 |
| After a 500-call replaceState burst, the first once-a-second retry that takes effect | 10011 ms after the burst / 10013 ms after the burst | 10009 ms after the burst / 10004 ms after the burst | 1011 ms after the burst / 1007 ms after the burst | 1011 ms after the burst / 1007 ms after the burst | 10113 ms after the burst / 10085 ms after the burst | 10103 ms after the burst / 10105 ms after the burst |

## Q8c. history.length after 500 pushState calls from a page timer, over time (extras.js histlen; Chromium without its rate limiter)

| Measure | Chromium file | Chromium http | Firefox file | Firefox http | WebKit file | WebKit http |
|---|---|---|---|---|---|---|
| history.length at load | 2 | 2 | 1 | 1 | 2 | 2 |
| right after / 1 s / 3 s / 6 s | 50 / 50 / 50 / 50 | 50 / 50 / 50 / 50 | 501 / 140 / 50 / 50 **[run 2: 501 / 112 / 50 / 50]** | 501 / 244 / 50 / 50 **[run 2: 501 / 139 / 50 / 50]** | 100 / 100 / 100 / 100 | 100 / 100 / 100 / 100 |
| calls that threw | 0 | 0 | 0 | 0 | 400 | 400 |

Session history caps: 50 entries in Chromium and 100 in WebKit (2 + 100 successful pushes reads 100). In Firefox,
history.length reads 501 right after the loop and falls to 50 within 3 s as the parent process trims; the 1 s sample varies by run.

## Q9. Navigation API

| Measure | Chromium file | Chromium http | Firefox file | Firefox http | WebKit file | WebKit http |
|---|---|---|---|---|---|---|
| 'navigation' in window; self.origin | yes; origin null | yes; origin http://127.0.0.1:port | yes; origin null | yes; origin http://127.0.0.1:port | no; origin null | no; origin http://127.0.0.1:port |
| At load: currentEntry, entries().length | currentEntry index 0, entries 1 | currentEntry index 0, entries 1 | currentEntry index 0, entries 1 | currentEntry index 0, entries 1 | n/a | n/a |
| navigation.navigate('#x'): committed / finished / hash | resolved / resolved / #x | resolved / resolved / #x | resolved / resolved / #x | resolved / resolved / #x | n/a | n/a |
| navigate('#x'): events | navigate(push, hashChange) then currententrychange then popstate(state=null) then navigatesuccess then hashchange((no hash)>#x) | navigate(push, hashChange) then currententrychange then popstate(state=null) then navigatesuccess then hashchange((no hash)>#x) | navigate(push, hashChange) then currententrychange then popstate(state=null) then navigatesuccess then hashchange((no hash)>#x) | navigate(push, hashChange) then currententrychange then popstate(state=null) then navigatesuccess then hashchange((no hash)>#x) | n/a | n/a |
| navigate('#y', {state:{a:1}}): getState() after | hash #y, getState() {a:1}, history.state null, entries 3 | hash #y, getState() {a:1}, history.state null, entries 3 | hash #y, getState() {a:1}, history.state null, entries 3 | hash #y, getState() {a:1}, history.state null, entries 3 | n/a | n/a |
| Back (page.goBack): events | navigate(traverse, hashChange, userInitiated) then currententrychange then navigatesuccess then popstate(state=null) then hashchange(#y>#x) | navigate(traverse, hashChange, userInitiated) then currententrychange then navigatesuccess then popstate(state=null) then hashchange(#y>#x) | navigate(traverse, hashChange) then navigatesuccess then currententrychange then popstate(state=null) then hashchange(#y>#x) | navigate(traverse, hashChange) then navigatesuccess then currententrychange then popstate(state=null) then hashchange(#y>#x) | n/a | n/a |
| After Back: getState() | hash #x, getState() undefined, history.state null, entries 3 | hash #x, getState() undefined, history.state null, entries 3 | hash #x, getState() undefined, history.state null, entries 3 | hash #x, getState() undefined, history.state null, entries 3 | n/a | n/a |
| After Forward: getState() | hash #y, getState() {a:1}, history.state null, entries 3 | hash #y, getState() {a:1}, history.state null, entries 3 | hash #y, getState() {a:1}, history.state null, entries 3 | hash #y, getState() {a:1}, history.state null, entries 3 | n/a | n/a |
| intercept() on location.hash = 'z': events | navigate(push, hashChange, intercepted) then currententrychange then popstate(state=null) then navigatesuccess; hash after #z | navigate(push, hashChange, intercepted) then currententrychange then popstate(state=null) then navigatesuccess; hash after #z | navigate(push, hashChange, intercepted) then currententrychange then navigatesuccess; hash after #z | navigate(push, hashChange, intercepted) then currententrychange then navigatesuccess; hash after #z | n/a | n/a |
| intercept() on a Back (traverse): events | navigate(traverse, hashChange, userInitiated, intercepted) then currententrychange then popstate(state=null) then navigatesuccess; hash after #y | navigate(traverse, hashChange, userInitiated, intercepted) then currententrychange then popstate(state=null) then navigatesuccess; hash after #y | navigate(traverse, hashChange, intercepted) then navigatesuccess then currententrychange then popstate(state=null) then hashchange(#z>#y); hash after #y | navigate(traverse, hashChange, intercepted) then navigatesuccess then currententrychange then popstate(state=null) then hashchange(#z>#y); hash after #y | n/a | n/a |

With bfcache and the rate limiter on (chromium-real), Chromium gives the same values in every row.

Chromium 147 and Firefox 148 expose the Navigation API on file:// as well as http. On file://, where self.origin is "null" in
both, currentEntry and entries() work and the events fire. Playwright's WebKit 26.4 does not expose it ('navigation' in window is
false), so every WebKit row is n/a. Facts that matter:
- `navigation.navigate('#x')` commits and finishes, and fires popstate (state null) and hashchange like any fragment navigation.
- Navigation API state and history.state are separate: after `navigate('#y', {state: {a:1}})`, getState() is {a:1} and history.state
  is null.
- A Back to a same-document entry fires navigate with navigationType 'traverse' and canIntercept true; getState() follows the entry.
- intercept() is allowed both for a same-document hash navigation and for a same-document traverse, and the URL still changes.
  An intercepted push fires no hashchange in either engine, and no popstate in Firefox. An intercepted traverse fires popstate in
  both, and hashchange only in Firefox.

## Q10. The report in an iframe (parent.html embeds report.html)

| Measure | Chromium file | Chromium http | Firefox file | Firefox http | WebKit file | WebKit http |
|---|---|---|---|---|---|---|
| <iframe>: origins (top / child); child Navigation API | null / null; currentEntry set | http://127.0.0.1:port / http://127.0.0.1:port; currentEntry set | null / null; currentEntry set | http://127.0.0.1:port / http://127.0.0.1:port; currentEntry set | null / null; absent | http://127.0.0.1:port / http://127.0.0.1:port; absent |
| <iframe>: child pushState #f1, #f2 | ok, hash #f1; ok, hash #f2 | ok, hash #f1; ok, hash #f2 | ok, hash #f1; ok, hash #f2 | ok, hash #f1; ok, hash #f2 | ok, hash #f1; ok, hash #f2 | ok, hash #f1; ok, hash #f2 |
| <iframe>: top history.length before > after the pushes | 2 > 4 (child reads 4) | 2 > 4 (child reads 4) | 1 > 3 (child reads 3) | 1 > 3 (child reads 3) | 2 > 4 (child reads 4) | 2 > 4 (child reads 4) |
| <iframe>: page.goBack() at top | Timeout 3000ms exceeded.; child now #f1, top file:///…/parent.html; child events: popstate(state={f:1}) then hashchange(#f2>#f1) | Timeout 3000ms exceeded.; child now #f1, top http://127.0.0.1:port/parent.html; child events: popstate(state={f:1}) then hashchange(#f2>#f1) | Timeout 3000ms exceeded.; child now #f1, top file:///…/parent.html; child events: popstate(state={f:1}) then hashchange(#f2>#f1) | Timeout 3000ms exceeded.; child now #f1, top http://127.0.0.1:port/parent.html; child events: popstate(state={f:1}) then hashchange(#f2>#f1) | Timeout 3000ms exceeded.; child now #f1, top file:///…/parent.html; child events: popstate(state={f:1}) then hashchange(#f2>#f1) | Timeout 3000ms exceeded.; child now #f1, top http://127.0.0.1:port/parent.html; child events: popstate(state={f:1}) then hashchange(#f2>#f1) |
| <iframe>: then top history.back() | child now (no hash), top file:///…/parent.html; top events: none | child now (no hash), top http://127.0.0.1:port/parent.html; top events: none | child now (no hash), top file:///…/parent.html; top events: none | child now (no hash), top http://127.0.0.1:port/parent.html; top events: none | child now (no hash), top file:///…/parent.html; top events: none | child now (no hash), top http://127.0.0.1:port/parent.html; top events: none |
| <iframe>: then page.goForward() | Timeout 3000ms exceeded.; child now #f1 | Timeout 3000ms exceeded.; child now #f1 | Timeout 3000ms exceeded.; child now #f1 | Timeout 3000ms exceeded.; child now #f1 | Timeout 3000ms exceeded.; child now #f1 | Timeout 3000ms exceeded.; child now #f1 |
| sandbox="allow-scripts": origins (top / child); child Navigation API | null / null; currentEntry null, entries 0 | http://127.0.0.1:port / null; currentEntry null, entries 0 | null / null; currentEntry null, entries 0 | http://127.0.0.1:port / null; currentEntry null, entries 0 | null / null; absent | http://127.0.0.1:port / null; absent |
| sandbox="allow-scripts": child pushState #f1, #f2 | ok, hash #f1; ok, hash #f2 | ok, hash #f1; ok, hash #f2 | SecurityError: The operation is insecure.; SecurityError: The operation is insecure. | ok, hash #f1; ok, hash #f2 | ok, hash #f1; ok, hash #f2 | ok, hash #f1; ok, hash #f2 |
| sandbox="allow-scripts": top history.length before > after the pushes | 2 > 4 (child reads 4) | 2 > 4 (child reads 4) | 1 > 1 (child reads 1) | 1 > 3 (child reads 3) | 2 > 4 (child reads 4) | 2 > 4 (child reads 4) |
| sandbox="allow-scripts": page.goBack() at top | Timeout 3000ms exceeded.; child now #f1, top file:///…/parent-sb.html; child events: popstate(state={f:1}) then hashchange(#f2>#f1) | Timeout 3000ms exceeded.; child now #f1, top http://127.0.0.1:port/parent-sb.html; child events: popstate(state={f:1}) then hashchange(#f2>#f1) | resolved; child now (no hash), top file:///…/parent-sb.html; child events: none | Timeout 3000ms exceeded.; child now #f1, top http://127.0.0.1:port/parent-sb.html; child events: popstate(state={f:1}) then hashchange(#f2>#f1) | Timeout 3000ms exceeded.; child now #f1, top file:///…/parent-sb.html; child events: popstate(state={f:1}) then hashchange(#f2>#f1) | Timeout 3000ms exceeded.; child now #f1, top http://127.0.0.1:port/parent-sb.html; child events: popstate(state={f:1}) then hashchange(#f2>#f1) |
| sandbox="allow-scripts": then top history.back() | child now (no hash), top file:///…/parent-sb.html; top events: none | child now (no hash), top http://127.0.0.1:port/parent-sb.html; top events: none | child now (no hash), top file:///…/parent-sb.html; top events: none | child now (no hash), top http://127.0.0.1:port/parent-sb.html; top events: none | child now (no hash), top file:///…/parent-sb.html; top events: none | child now (no hash), top http://127.0.0.1:port/parent-sb.html; top events: none |
| sandbox="allow-scripts": then page.goForward() | Timeout 3000ms exceeded.; child now #f1 | Timeout 3000ms exceeded.; child now #f1 | resolved; child now (no hash) | Timeout 3000ms exceeded.; child now #f1 | Timeout 3000ms exceeded.; child now #f1 | Timeout 3000ms exceeded.; child now #f1 |
| sandbox="allow-scripts allow-same-origin": origins (top / child); child Navigation API | null / null; currentEntry set | http://127.0.0.1:port / http://127.0.0.1:port; currentEntry set | null / null; currentEntry set | http://127.0.0.1:port / http://127.0.0.1:port; currentEntry set | null / null; absent | http://127.0.0.1:port / http://127.0.0.1:port; absent |
| sandbox="allow-scripts allow-same-origin": child pushState #f1, #f2 | ok, hash #f1; ok, hash #f2 | ok, hash #f1; ok, hash #f2 | ok, hash #f1; ok, hash #f2 | ok, hash #f1; ok, hash #f2 | ok, hash #f1; ok, hash #f2 | ok, hash #f1; ok, hash #f2 |
| sandbox="allow-scripts allow-same-origin": top history.length before > after the pushes | 2 > 4 (child reads 4) | 2 > 4 (child reads 4) | 1 > 3 (child reads 3) | 1 > 3 (child reads 3) | 2 > 4 (child reads 4) | 2 > 4 (child reads 4) |
| sandbox="allow-scripts allow-same-origin": page.goBack() at top | Timeout 3000ms exceeded.; child now #f1, top file:///…/parent-sbso.html; child events: popstate(state={f:1}) then hashchange(#f2>#f1) | Timeout 3000ms exceeded.; child now #f1, top http://127.0.0.1:port/parent-sbso.html; child events: popstate(state={f:1}) then hashchange(#f2>#f1) | Timeout 3000ms exceeded.; child now #f1, top file:///…/parent-sbso.html; child events: popstate(state={f:1}) then hashchange(#f2>#f1) | Timeout 3000ms exceeded.; child now #f1, top http://127.0.0.1:port/parent-sbso.html; child events: popstate(state={f:1}) then hashchange(#f2>#f1) | Timeout 3000ms exceeded.; child now #f1, top file:///…/parent-sbso.html; child events: popstate(state={f:1}) then hashchange(#f2>#f1) | Timeout 3000ms exceeded.; child now #f1, top http://127.0.0.1:port/parent-sbso.html; child events: popstate(state={f:1}) then hashchange(#f2>#f1) |
| sandbox="allow-scripts allow-same-origin": then top history.back() | child now (no hash), top file:///…/parent-sbso.html; top events: none | child now (no hash), top http://127.0.0.1:port/parent-sbso.html; top events: none | child now (no hash), top file:///…/parent-sbso.html; top events: none | child now (no hash), top http://127.0.0.1:port/parent-sbso.html; top events: none | child now (no hash), top file:///…/parent-sbso.html; top events: none | child now (no hash), top http://127.0.0.1:port/parent-sbso.html; top events: none |
| sandbox="allow-scripts allow-same-origin": then page.goForward() | Timeout 3000ms exceeded.; child now #f1 | Timeout 3000ms exceeded.; child now #f1 | Timeout 3000ms exceeded.; child now #f1 | Timeout 3000ms exceeded.; child now #f1 | Timeout 3000ms exceeded.; child now #f1 | Timeout 3000ms exceeded.; child now #f1 |

With bfcache and the rate limiter on (chromium-real), Chromium gives the same values in every row.

Every engine joins the session history. The child's pushes add to the top-level history.length. A top-level Back, by
page.goBack() or by the top's history.back(), walks the child's entries, firing popstate then hashchange in the child and
nothing in the top. Playwright's page.goBack() and goForward() then time out because they wait for a main-frame navigation, but
the traversal did happen. In a sandbox="allow-scripts" frame (opaque origin), pushState('#f1') works in Chromium and WebKit on
both protocols and in Firefox over http. On file:// Firefox throws "SecurityError: The operation is insecure." with either pref.
The Navigation API is switched off inside an opaque-origin frame in Chromium and Firefox: currentEntry is null and entries() is
empty. With allow-same-origin every engine behaves as with a plain iframe.

## Q10b. file:// iframes with the stock Firefox origin pref (extras.js file-origin)

| Measure | Chromium file | Firefox file | Firefox (stock file:// origin pref) file | WebKit file |
|---|---|---|---|---|
| <iframe>: parent reads iframe.contentDocument | contentDocument is null | readable | contentDocument is null | contentDocument is null |
| <iframe>: child origin; child pushState #f1 | null; ok, hash #f1 | null; ok, hash #f1 | null; ok, hash #f1 | null; ok, hash #f1 |
| sandbox="allow-scripts": parent reads iframe.contentDocument | contentDocument is null | contentDocument is null | contentDocument is null | contentDocument is null |
| sandbox="allow-scripts": child origin; child pushState #f1 | null; ok, hash #f1 | null; SecurityError: The operation is insecure. | null; SecurityError: The operation is insecure. | null; ok, hash #f1 |

With the stock pref, a file:// parent cannot read a file:// child's document in any engine; the child's own pushState still works.

## Q11. Reload after pushState (pushState at y=2000, then scroll to 2500, reload)

| Measure | Chromium file | Chromium http | Firefox file | Firefox http | WebKit file | WebKit http |
|---|---|---|---|---|---|---|
| [auto] pushState #r (no element), scroll 2500, reload | state {k:"reload"}, hash #r, scrollRestoration auto; y at pageshow [2500], after load 2500, +700 ms 2500 | state {k:"reload"}, hash #r, scrollRestoration auto; y at pageshow [2500], after load 2500, +700 ms 2500 | state {k:"reload"}, hash #r, scrollRestoration auto; y at pageshow [2500], after load 2500, +700 ms 2500 | not a reload: Firefox's page.reload() over http adds an entry instead (Q11c); see Q11b | state {k:"reload"}, hash #r, scrollRestoration auto; y at pageshow [0], after load 2500, +700 ms 2500 | state {k:"reload"}, hash #r, scrollRestoration auto; y at pageshow [0], after load 2500, +700 ms 2500 |
| [auto] pushState #c (element at y=3000), scroll 2500, reload | state {k:"reload"}, hash #c, scrollRestoration auto; y at pageshow [3000], after load 2500, +700 ms 2500 | state {k:"reload"}, hash #c, scrollRestoration auto; y at pageshow [3000], after load 2500, +700 ms 2500 | state {k:"reload"}, hash #c, scrollRestoration auto; y at pageshow [2500], after load 2500, +700 ms 2500 | not a reload: Firefox's page.reload() over http adds an entry instead (Q11c); see Q11b | state {k:"reload"}, hash #c, scrollRestoration auto; y at pageshow [3000], after load 2500, +700 ms 2500 | state {k:"reload"}, hash #c, scrollRestoration auto; y at pageshow [3000], after load 2500, +700 ms 2500 |
| [manual] pushState #r (no element), scroll 2500, reload | state {k:"reload"}, hash #r, scrollRestoration manual; y at pageshow [0], after load 0, +700 ms 0 | state {k:"reload"}, hash #r, scrollRestoration manual; y at pageshow [0], after load 0, +700 ms 0 | state {k:"reload"}, hash #r, scrollRestoration manual; y at pageshow [0], after load 0, +700 ms 0 | not a reload: Firefox's page.reload() over http adds an entry instead (Q11c); see Q11b | state {k:"reload"}, hash #r, scrollRestoration manual; y at pageshow [0], after load 0, +700 ms 0 | state {k:"reload"}, hash #r, scrollRestoration manual; y at pageshow [0], after load 0, +700 ms 0 |
| [manual] pushState #c (element at y=3000), scroll 2500, reload | state {k:"reload"}, hash #c, scrollRestoration manual; y at pageshow [3000], after load 3000, +700 ms 3000 | state {k:"reload"}, hash #c, scrollRestoration manual; y at pageshow [3000], after load 3000, +700 ms 3000 | state {k:"reload"}, hash #c, scrollRestoration manual; y at pageshow [0], after load 0, +700 ms 0 | not a reload: Firefox's page.reload() over http adds an entry instead (Q11c); see Q11b | state {k:"reload"}, hash #c, scrollRestoration manual; y at pageshow [3000], after load 3000, +700 ms 3000 | state {k:"reload"}, hash #c, scrollRestoration manual; y at pageshow [3000], after load 3000, +700 ms 3000 |

With bfcache and the rate limiter on (chromium-real), Chromium gives the same values in every row.

The Firefox http cells are page.reload() artifacts (see How to read this); Q11b has the real figures. In every real reload,
history.state, the fragment and the entry's scrollRestoration survive. Under 'auto' the position comes back (2500), and it wins
over a fragment that names an element (#c). Under 'manual' nothing is restored. When the fragment names an element, Chromium
and WebKit scroll to it on reload and Firefox stays at 0. Timing under 'auto': with #r, Chromium and Firefox have restored by
pageshow and WebKit restores after it (y 0 at pageshow). With #c, Chromium and WebKit are at the fragment (3000) at pageshow and
reach 2500 after load, while Firefox is at 2500 already.

## Q11b. Q11 again, reloading by location.reload() (extras.js q11b)

| Measure | Chromium file | Chromium http | Firefox file | Firefox http | WebKit file | WebKit http |
|---|---|---|---|---|---|---|
| [auto] pushState #r (no element), scroll 2500, location.reload() | state {k:"reload"}, hash #r, scrollRestoration auto, type reload; y at pageshow [2500], after load 2500, +700 ms 2500 | state {k:"reload"}, hash #r, scrollRestoration auto, type reload; y at pageshow [2500], after load 2500, +700 ms 2500 | state {k:"reload"}, hash #r, scrollRestoration auto, type reload; y at pageshow [2500], after load 2500, +700 ms 2500 | state {k:"reload"}, hash #r, scrollRestoration auto, type reload; y at pageshow [2500], after load 2500, +700 ms 2500 | state {k:"reload"}, hash #r, scrollRestoration auto, type reload; y at pageshow [0], after load 2500, +700 ms 2500 | state {k:"reload"}, hash #r, scrollRestoration auto, type reload; y at pageshow [0], after load 2500, +700 ms 2500 |
| [auto] pushState #c (element at y=3000), scroll 2500, location.reload() | state {k:"reload"}, hash #c, scrollRestoration auto, type reload; y at pageshow [3000], after load 2500, +700 ms 2500 | state {k:"reload"}, hash #c, scrollRestoration auto, type reload; y at pageshow [3000], after load 2500, +700 ms 2500 **[run 2: state {k:"reload"}, hash #c, scrollRestoration auto, type reload; y at pageshow [2500], after load 2500, +700 ms 2500]** | state {k:"reload"}, hash #c, scrollRestoration auto, type reload; y at pageshow [2500], after load 2500, +700 ms 2500 | state {k:"reload"}, hash #c, scrollRestoration auto, type reload; y at pageshow [2500], after load 2500, +700 ms 2500 | state {k:"reload"}, hash #c, scrollRestoration auto, type reload; y at pageshow [3000], after load 2500, +700 ms 2500 | state {k:"reload"}, hash #c, scrollRestoration auto, type reload; y at pageshow [3000], after load 2500, +700 ms 2500 |
| [manual] pushState #r (no element), scroll 2500, location.reload() | state {k:"reload"}, hash #r, scrollRestoration manual, type reload; y at pageshow [0], after load 0, +700 ms 0 | state {k:"reload"}, hash #r, scrollRestoration manual, type reload; y at pageshow [0], after load 0, +700 ms 0 | state {k:"reload"}, hash #r, scrollRestoration manual, type reload; y at pageshow [0], after load 0, +700 ms 0 | state {k:"reload"}, hash #r, scrollRestoration manual, type reload; y at pageshow [0], after load 0, +700 ms 0 | state {k:"reload"}, hash #r, scrollRestoration manual, type reload; y at pageshow [0], after load 0, +700 ms 0 | state {k:"reload"}, hash #r, scrollRestoration manual, type reload; y at pageshow [0], after load 0, +700 ms 0 |
| [manual] pushState #c (element at y=3000), scroll 2500, location.reload() | state {k:"reload"}, hash #c, scrollRestoration manual, type reload; y at pageshow [3000], after load 3000, +700 ms 3000 | state {k:"reload"}, hash #c, scrollRestoration manual, type reload; y at pageshow [3000], after load 3000, +700 ms 3000 | state {k:"reload"}, hash #c, scrollRestoration manual, type reload; y at pageshow [0], after load 0, +700 ms 0 | state {k:"reload"}, hash #c, scrollRestoration manual, type reload; y at pageshow [0], after load 0, +700 ms 0 | state {k:"reload"}, hash #c, scrollRestoration manual, type reload; y at pageshow [3000], after load 3000, +700 ms 3000 | state {k:"reload"}, hash #c, scrollRestoration manual, type reload; y at pageshow [3000], after load 3000, +700 ms 3000 |

One timing cell differs between runs: Chromium http, 'auto' with #c, where y at pageshow was 3000 or 2500. The final
position was 2500 in both runs.

## Q11c. Which reload keeps history.state (extras.js reload-state; scrollRestoration set to manual first)

| Measure | Chromium file | Chromium http | Firefox file | Firefox http | WebKit file | WebKit http | Firefox (stock file:// origin pref) file | Firefox (stock file:// origin pref) http |
|---|---|---|---|---|---|---|---|---|
| replaceState({k}), then page.reload() | state {k:"replaced"}, history.length 2 > 2, scrollRestoration manual, type reload | state {k:"replaced"}, history.length 2 > 2, scrollRestoration manual, type reload | state {k:"replaced"}, history.length 1 > 1, scrollRestoration manual, type reload | state null, history.length 1 > 2, scrollRestoration auto, type navigate | state {k:"replaced"}, history.length 2 > 2, scrollRestoration manual, type reload | state {k:"replaced"}, history.length 2 > 2, scrollRestoration manual, type reload | state {k:"replaced"}, history.length 1 > 1, scrollRestoration manual, type reload | state null, history.length 1 > 2, scrollRestoration auto, type navigate |
| pushState({k}, "#r"), then page.reload() | state {k:"pushed"}, history.length 3 > 3, scrollRestoration manual, type reload | state {k:"pushed"}, history.length 3 > 3, scrollRestoration manual, type reload | state {k:"pushed"}, history.length 2 > 2, scrollRestoration manual, type reload | state null, history.length 2 > 3, scrollRestoration auto, type navigate | state {k:"pushed"}, history.length 3 > 3, scrollRestoration manual, type reload | state {k:"pushed"}, history.length 3 > 3, scrollRestoration manual, type reload | state {k:"pushed"}, history.length 2 > 2, scrollRestoration manual, type reload | state null, history.length 2 > 3, scrollRestoration auto, type navigate |
| pushState({k}, "#r"), then location.reload() | state {k:"pushed"}, history.length 3 > 3, scrollRestoration manual, type reload | state {k:"pushed"}, history.length 3 > 3, scrollRestoration manual, type reload | state {k:"pushed"}, history.length 2 > 2, scrollRestoration manual, type reload | state {k:"pushed"}, history.length 2 > 2, scrollRestoration manual, type reload | state {k:"pushed"}, history.length 3 > 3, scrollRestoration manual, type reload | state {k:"pushed"}, history.length 3 > 3, scrollRestoration manual, type reload | state {k:"pushed"}, history.length 2 > 2, scrollRestoration manual, type reload | state {k:"pushed"}, history.length 2 > 2, scrollRestoration manual, type reload |

## Q12. bfcache: pushState #bf, follow a link to other.html, Back

| Measure | Chromium file | Chromium http | Firefox file | Firefox http | WebKit file | WebKit http | Chromium (bfcache + rate limiter on) file | Chromium (bfcache + rate limiter on) http |
|---|---|---|---|---|---|---|---|---|
| pageshow after Back | pageshow(persisted=false) | pageshow(persisted=false) | pageshow(persisted=false) | pageshow(persisted=false) | pageshow(persisted=false) | pageshow(persisted=false) | pageshow(persisted=false) | pageshow(persisted=false) |
| JS state intact (counter 42 survives) | no (counter 0) | no (counter 0) | no (counter 0) | no (counter 0) | no (counter 0) | no (counter 0) | no (counter 0) | no (counter 0) |
| Navigation timing type; notRestoredReasons | back_forward; null | back_forward; null | back_forward; "unsupported" | back_forward; "unsupported" | back_forward; "unsupported" | back_forward; "unsupported" | back_forward; {src:"",id:"",url:"file:///…/report.html#bf",name:"",reasons:[{reason:"masked"}],children:[]} | back_forward; {src:null,id:null,url:"http://127.0.0.1:port/report.html#bf",name:null,reasons:[{reason:"masked"}],children:[]} |
| After Back: hash, state, history.length | #bf, {k:"bf"}, 4 | #bf, {k:"bf"}, 4 | #bf, {k:"bf"}, 3 | #bf, {k:"bf"}, 3 | #bf, {k:"bf"}, 4 | #bf, {k:"bf"}, 4 | #bf, {k:"bf"}, 4 | #bf, {k:"bf"}, 4 |

None of the three engines restored from bfcache under Playwright. Chromium's headless shell switches bfcache off in its embedder
(Q12b: BackForwardCacheDisabledForDelegate), Firefox's juggler switches it off on every page, and Playwright's WebKit build
reloaded the page. WebKit has no notRestoredReasons API, so the build cannot say why. Q12c is the measurement that means
something for Chromium.

## Q12b. Why Chromium did not restore: CDP Page.backForwardCacheNotUsed, headless shell vs Chrome for Testing

| Measure | Chromium (bfcache + rate limiter on) file | Chromium (bfcache + rate limiter on) http |
|---|---|---|
| Headless shell, bfcache switch removed: notRestoredExplanations | Circumstantial: BrowsingInstanceNotSwapped | Circumstantial: BrowsingInstanceNotSwapped, Circumstantial: BackForwardCacheDisabledForDelegate |
| Headless shell: pageshow persisted, counter | [false], 0 | [false], 0 |

## Q12c. Q12 in Chrome for Testing 147 (full Chromium, new headless, bfcache switch removed; cft-bfcache.js)

| Measure | Chrome for Testing file | Chrome for Testing http |
|---|---|---|
| pagehide/pageshow around the Back | pageshow(persisted=false) | pagehide(persisted=true) then pageshow(persisted=true) |
| JS state intact (counter 42 survives) | no (counter 0) | yes (counter 42) |
| Navigation timing type; notRestoredReasons | back_forward; {src:"",id:"",url:"file:///…/report.html#bf",name:"",reasons:[{reason:"masked"}],children:[]} | navigate; null |
| CDP notRestoredExplanations | Circumstantial: BrowsingInstanceNotSwapped | none (restored) |
| After Back: hash, history.state | #bf, {k:"bf"} | #bf, {k:"bf"} |

In Chrome for Testing 147 the http report comes back from bfcache: pagehide and pageshow with persisted=true, JS state intact,
hash and history.state as left, and the navigation type stays 'navigate'. The pushState before leaving does not block restore.
The file:// report does not come back (CDP: BrowsingInstanceNotSwapped; notRestoredReasons only says 'masked'). It reloads and
loses JS state, but keeps its hash and history.state.

## Q13. history.length: load, three pushStates, two replaceStates

| Measure | Chromium file | Chromium http | Firefox file | Firefox http | WebKit file | WebKit http |
|---|---|---|---|---|---|---|
| At load | 2 | 2 | 1 | 1 | 2 | 2 |
| After 3 pushState | 5 (+3) | 5 (+3) | 4 (+3) | 4 (+3) | 5 (+3) | 5 (+3) |
| After 2 replaceState | 5 (+3 since load) | 5 (+3 since load) | 4 (+3 since load) | 4 (+3 since load) | 5 (+3 since load) | 5 (+3 since load) |

With bfcache and the rate limiter on (chromium-real), Chromium gives the same values in every row.

pushState adds one entry and replaceState none, in every engine.

## X1. Entries added from timers with no user activation (noact.html: pushState #na1, #na2 after load)

| Measure | Chromium file | Chromium http | Firefox file | Firefox http | WebKit file | WebKit http |
|---|---|---|---|---|---|---|
| page.goBack() lands on | #na1 (resolved) | #na1 (resolved) | #na2 (resolved) | #na2 (resolved) | #na1 (resolved) | #na1 (resolved) |
| the page's own history.back() from a timer lands on | #na1 | #na1 | #na1 | #na1 | #na1 | #na1 |

With bfcache and the rate limiter on (chromium-real), Chromium gives the same values in every row.

Chromium's and WebKit's page.goBack() step to #na1. The page's own history.back(), called from a timer with no activation,
steps to #na1 in every engine. Firefox's page.goBack() does nothing here; X1b shows that is not a race.

## X1b. X1 again with a 1 s pause between the second push and page.goBack() (extras.js x1b)

| Measure | Chromium file | Chromium http | Firefox file | Firefox http | WebKit file | WebKit http |
|---|---|---|---|---|---|---|
| page.goBack() lands on | #na1 (resolved), history.length 4 | #na1 (resolved), history.length 4 | #na2 (resolved), history.length 3 | #na2 (resolved), history.length 3 | #na1 (resolved), history.length 4 | #na1 (resolved), history.length 4 |

With a 1 s pause, Firefox's page.goBack() still does nothing (history.length 3, still #na2). Juggler returns without
navigating when the browser element's canGoBack is false, so on a page with no user interaction Firefox's browser-level Back is
unavailable, while history.back() from the page works (X1).

## X2. One real click on the first entry, then #na1 and #na2 pushed from timers 6 s later (noact-click.html; extras.js x2)

| Measure | Chromium file | Chromium http | Firefox file | Firefox http | WebKit file | WebKit http |
|---|---|---|---|---|---|---|
| page.goBack() lands on | #na1 (resolved); events: clicked(no hash), pushed1(#na1), pushed2(#na2), popstate(#na1), hashchange(#na1) | #na1 (resolved); events: clicked(no hash), pushed1(#na1), pushed2(#na2), popstate(#na1), hashchange(#na1) | #na1 (resolved); events: clicked(no hash), pushed1(#na1), pushed2(#na2), popstate(#na1), hashchange(#na1) | #na1 (resolved); events: clicked(no hash), pushed1(#na1), pushed2(#na2), popstate(#na1), hashchange(#na1) | #na1 (resolved); events: clicked(no hash), pushed1(#na1), pushed2(#na2), popstate(#na1), hashchange(#na1) | #na1 (resolved); events: clicked(no hash), pushed1(#na1), pushed2(#na2), popstate(#na1), hashchange(#na1) |

After one real click on the first entry, Firefox's page.goBack() works and steps to #na1. No engine skipped #na1, although it
was pushed without activation. This does not tell whether a real toolbar Back skips it; see "Not measured" below.

## Flaky cells

All 17 are timing, and none is a harness fault. The Q7 "saved" cells differ by exactly 440 px, that is whether one more
section had rendered when the position was saved, and the Q7 outcome cells next to them follow from that. The
"[Back, via s120 then top, manual] (a)" cell differs only in its first, immediate sample; both runs end on s288. Q8b and Q8c are
Firefox trimming session history asynchronously (the 1 s sample). Q11b is whether Chromium had restored by pageshow; both runs
end at 2500.

- Q7 / [Back, top, auto]: saved (y, top section) / Chromium file
- Q7 / [Back, top, auto]: saved (y, top section) / Firefox http
- Q7 / [Back, top, auto]: browser's own restore (y, section) / Chromium file
- Q7 / [Back, top, auto]: browser's own restore (y, section) / Firefox http
- Q7 / [Back, top, manual]: saved (y, top section) / Chromium file
- Q7 / [Back, top, manual] (a) scrollTo(savedY) / Chromium file
- Q7 / [Back, via s120 then top, manual] (a) scrollTo(savedY) / Chromium file
- Q7 / [reload, auto]: saved (y, top section) / Chromium file
- Q7 / [reload, auto]: saved (y, top section) / Chromium http
- Q7 / [reload, auto]: saved (y, top section) / Firefox http
- Q7 / [reload, manual] (a) scrollTo(history.state.savedY) / Chromium file
- Q7 / [reload, manual] (a) scrollTo(history.state.savedY) / Chromium http
- Q8b / pushState x500 from a page timer / Firefox file
- Q8b / pushState x500 from a page timer / Firefox http
- Q8c / right after / 1 s / 3 s / 6 s / Firefox file
- Q8c / right after / 1 s / 3 s / 6 s / Firefox http
- Q11b / [auto] pushState #c (element at y=3000), scroll 2500, location.reload() / Chromium http

## Not measured, and why

- **The toolbar Back button.** Chrome and Edge skip entries added without user activation (the history manipulation
  intervention), and Firefox has a similar user-interaction rule. Neither can be driven here. Chromium's page.goBack() is a CDP
  traversal by index, and the headless shell has no toolbar or Alt+Left. Firefox's juggler calls `browsingContext.goBack()`
  without the user-interaction flag; only its `canGoBack` gate shows (X1, X1b, X2). Installed Chrome and Edge were off limits on
  this machine.
- **Firefox bfcache.** Juggler sets `docShell.disallowBFCache = true` on every page; no pref turns it back on.
- **Safari and iOS.** Only Playwright's Windows WebKit 26.4 ran. It has no Navigation API and did not restore from bfcache;
  Safari may differ on both.
- **Mobile.** No Android back gesture, no swipe navigation (`hasUAVisualTransition`), no mobile browsers.
- **Headed mode.** Every run was headless.
- **pushState and replaceState sharing one rate-limit budget.** Each kind was measured on its own fresh page.

## Files

All in this folder. `make-pages.js` writes `pages/*.html`. `run.js <engine>` runs Q1 to Q13 and X1, twice, into
`results/<engine>.json`. `extras.js <engine> <cases>` runs the follow-ups into `results/extras-<engine>-<case>.json`.
`cft-bfcache.js` runs Q12c into `results/cft-bfcache.json`, and `small-checks.js` writes `results/small-checks.json`.
`q7-counts.js` prints the Q7 outcome counts. `make-results.js` builds this file, with prose from `notes.js`. `peek.js` and
`cols.js` are viewers. Every script requires playwright-core 1.59.1 by absolute path from the .NET driver's copy
(`%USERPROFILE%\.nuget\packages\microsoft.playwright\1.59.0\.playwright\package`), because its browsers.json pins the three
installed browser revisions. The Kronikol4J copy (1.61.0) pins Firefox and WebKit revisions that are not installed.

