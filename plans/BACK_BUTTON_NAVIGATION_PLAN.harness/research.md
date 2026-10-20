# Back button as primary navigation in a single-file HTML report: research

Researched 2026-10-19. Reading only: no browser or build was run. Every support claim carries a date.

Current releases on 2026-10-19:

| Browser | Version | Released |
|---|---|---|
| Chrome | 155 | 2026-10-06 (156 due 2026-10-20) |
| Firefox | 158 | 2026-10-13 |
| Firefox ESR | 153.5 and 115.43 | 2026-10-13 |
| Safari | 27.0 | 2026-09-14 |

Sources for that table: chromiumdash, https://product-details.mozilla.org/1.0/firefox_versions.json and
https://product-details.mozilla.org/1.0/firefox_history_major_releases.json (Firefox has shipped every
two weeks since 155), and the Safari 27 release notes,
https://developer.apple.com/documentation/safari-release-notes/safari-27-release-notes.

Conventions:
- "Source read" means the engine's own code on its main branch on 2026-10-18 or 2026-10-19, read on GitHub
  (chromium/chromium, mozilla-firefox/firefox, WebKit/WebKit). It says what the code does, not what a
  shipped browser was seen to do.
- **UNCONFIRMED** marks anything no primary source settled.
- "Inference" marks my own reasoning from the sources.

---

## Headlines

1. **Build on the History API, not the Navigation API.**
   - The Navigation API has been Baseline only since 2026-01-13, so Firefox ESR 115 and Safari before 26.2
     lack it.
   - It is switched off in opaque-origin (sandboxed) iframes.
   - On file:// pages in Safari, the WebKit source suggests `canIntercept` is false for every navigation.
   - `pushState`/`replaceState` with a hash or query change works on file:// in all three engines and in
     sandboxed iframes.
2. **Every engine rate-limits history writes, and they do not agree.**
   - Safari: 100 calls per 10 s, and it **throws**.
   - Chrome: 200 per 10 s, silently dropped.
   - Firefox 157+: 200 per 10 s, silently dropped.
   - Firefox 149-156 and ESR 153: 1000 per 10 s, and they throw.
   - Playwright's Chromium and WebDriver's Firefox switch these limits off, so E2E tests cannot catch a
     regression.
3. **Each tab holds a capped number of entries:** 50 in Chrome and Firefox, 100 in Safari. A report that
   pushes on every small change pushes the CI page, the way back out, off the end of the list.
4. **The Back button skips entries made without user activation** in Chrome (since 75), Firefox (since 135)
   and Safari (on Apple SDKs). Push only from a click or key handler. Replace in every other case.
5. **Chrome never puts a file:// page in the back/forward cache.** Coming Back into the report from another
   page always reloads it, so the report must rebuild its view from `location.hash` and `history.state` on
   every load.
6. **Inside an iframe, the report's pushes join the whole tab's history.** That covers Jenkins with a
   resource root URL, the srcdoc viewers for Azure DevOps and VS Code's Simple Browser. `history.back()`
   from inside the report can navigate the host page.
   - Jenkins's default CSP (`sandbox allow-same-origin; default-src 'none'; ...`) blocks every inline
     script, so the report does not run there at all.
7. **Under `content-visibility: auto`, restore scroll by anchor element plus offset, not by pixel.**
   - Safari 27.0 (2026-09-14) shipped scroll anchoring on by default. The report's code comment "Safari
     has no scroll anchoring" now holds only for Safari 26 and older.
8. **Tabs: precedent is split.**
   - Push: GOV.UK Frontend tabs (via `location.hash`), GitHub PR tabs, Allure, Playwright.
   - Do not push: the old Android rule, Smashing, Primer's panels.
   - Baymard's test is whether the view reads as a new page.
   - For filters, the tools mostly replace: Allure 3, Cucumber, Grafana Scenes.
   - Baymard found that users expect Back to undo a filter.
9. **Back-to-close for overlays is expected, especially on phones.**
   - iOS Safari has no close-request path: an edge swipe is a history traversal.
   - Android's Back is a close request first: in Chrome for a modal `<dialog>` or popover (since 120) and
     for `CloseWatcher` (since 126), and in Firefox for `CloseWatcher` (since 149).

---

## 1. Navigation API

### Support, October 2026

| Browser | First version | Date |
|---|---|---|
| Chrome / Chrome Android | 102 | 2022-05-24 |
| Edge | 102 | 2022-05-31 |
| Firefox / Firefox Android | 147 | 2026-01-13 |
| Safari / Safari iOS | 26.2 | 2025-12-12 |

- **Baseline status.**
  - webstatus.dev, fetched 2026-10-19 (https://api.webstatus.dev/v1/features/navigation): Baseline
    **newly available**, low date **2026-01-13**.
  - MDN's banner (https://developer.mozilla.org/en-US/docs/Web/API/Navigation_API): "Baseline 2026, Newly
    available. Since January 2026".
  - web.dev announced it on 2026-02-17 (https://web.dev/blog/baseline-navigation-api).
  - By the Baseline rule (30 months after the low date) it should become "widely available" around July
    2028. That date is my calculation, not a published one.
- **Release notes.**
  - Firefox 147, 2026-01-13: "The Navigation API is now supported" (bug 1997962,
    https://developer.mozilla.org/en-US/docs/Mozilla/Firefox/Releases/147).
  - Safari 26.2, 2025-12-12 (https://webkit.org/blog/17640/webkit-features-for-safari-26-2/).
  - Safari 26.3, 2026-02-11, added `NavigateEvent.signal`
    (https://webkit.org/blog/17798/webkit-features-for-safari-26-3/). MDN's BCD lists it as 26.2, so the
    two sources conflict.
- **Sub-features** (MDN BCD, https://raw.githubusercontent.com/mdn/browser-compat-data/main/api/NavigateEvent.json,
  read 2026-10-19).
  - Every member is in all three engines except `intercept({ precommitHandler })`: Chrome 141, Firefox
    147, **Safari: not supported**.
  - `sourceElement` arrived in Chrome 135 and `activation` in Chrome 123.
- **Browsers still supported that lack it.**
  - **Firefox ESR 115**, kept for Windows 7 to 8.1 and macOS 10.12 to 10.14 until 2027-04-06
    (https://whattrainisitnow.com/release/?version=esr). Its rate limit throws (section 2).
  - Safari before 26.2: any iPhone, iPad or Mac that cannot move to the 26 releases.
  - ESR 153 (current) has the API.

### Interop quality caveats

- Mozilla, 2026-02-12 (https://hacks.mozilla.org/2026/02/launching-interop-2026/): for "CSS Anchor
  Positioning and the Navigation API, we noticed that it was possible to achieve a good score on the tests
  while having inconsistent behavior compared to other browsers", and "in some cases the tests
  contradicted the spec". Interop 2026 keeps the Navigation API as a reliability focus area and adds
  `precommitHandler`.
- WebKit's implementation bug 258384 (https://bugs.webkit.org/show_bug.cgi?id=258384) is closed. On
  2025-12-25 a WebKit engineer wrote "There are still small differences".
  - Open follow-ups listed on that bug include 282244 (max limit of traversals), 282689 (remaining problems
    with traversals), 282610 (remaining scroll-behavior failures), 279321 (scroll API behaves differently on
    iOS) and 282603 (bfcache tests not run).
- Safari 27.0 (beta notes 2026-06-08, https://webkit.org/blog/17967/news-from-wwdc26-webkit-in-safari-27-beta/;
  release notes, link above) fixed these bugs, which means Safari 26.x has them:
  - pages using the Navigation API "could have offset hit test locations, making elements unclickable";
  - `canIntercept` was true for a different port;
  - `navigationType` was not "replace" for the document's own URL;
  - `currentEntry.key` did not change in private windows after `pushState()`;
  - `replaceState()` on a traversed entry gave it a new key;
  - "the parent window's history.state" was "set to null when history.pushState is called from a child
    iframe".

### file:// pages

The spec switches the API off for opaque origins (https://html.spec.whatwg.org/multipage/nav-history-apis.html).

- A Navigation "has entries and events disabled" when the document is not fully active, when it is the
  initial about:blank, or when "document's origin is opaque".
- In that state `navigation.entries()` is empty, `currentEntry` is null and no `navigate` event fires.
- The URL Standard leaves the origin of a file: URL to each engine: "Unfortunate as it is, this is left as
  an exercise to the reader. When in doubt, return a new opaque origin."
  (https://url.spec.whatwg.org/#concept-url-origin)

What each engine does (source read):

- **Chrome:**
  - A file: origin is not opaque in Blink. `ShouldTreatAsOpaqueOrigin` returns false for local schemes, and
    the origin only *serialises* as "null", through `SerializesAsNull`: `IsLocal() &&
    block_local_access_from_local_origin_`
    (https://github.com/chromium/chromium/blob/main/third_party/blink/renderer/platform/weborigin/security_origin.cc).
  - `NavigationApi::HasEntriesAndEventsDisabled()` tests `GetSecurityOrigin()->IsOpaque()`, so the API is
    live on file://
    (https://github.com/chromium/chromium/blob/main/third_party/blink/renderer/core/navigation_api/navigation_api.cc).
  - `canIntercept` is `CanChangeToUrlForHistoryApi(url, origin, window URL)`, provided the navigation is not
    a cross-document traversal. On file: that allows query and fragment changes (section 2.1).
- **Firefox:**
  - `Navigation::HasEntriesAndEventsDisabled()` tests `GetIsNullPrincipal()`, among other conditions
    (https://github.com/mozilla-firefox/firefox/blob/main/dom/navigation/Navigation.cpp).
  - A file: page has a content principal (caps/ContentPrincipal.cpp), so the API is live.
  - `canIntercept` uses `Document::CanRewriteURL`. A change to the fragment alone is always allowed. On
    file: the target must otherwise be the same file.
  - The same function switches the API off in three other cases:
    - documents embedded by `<object>`/`<embed>` (bug 1996216, 2025-10-24);
    - multipart and `javascript:` documents;
    - documents whose *principal* is an about: one other than about:blank (bug 2042379 part 2, landed
      2026-05-29; open bug 2043508).
  - An `<iframe srcdoc>` inherits its parent's principal, so the about: rule does not reach it, because
    `IsAboutPage()` tests `NodePrincipal()->SchemeIs("about")`.
- **Safari:**
  - `Navigation::hasEntriesAndEventsDisabled()` tests `isOpaque()`
    (https://github.com/WebKit/WebKit/blob/main/Source/WebCore/page/Navigation.cpp).
  - file: is a special scheme, so its origin is not opaque (`SecurityOriginData::shouldTreatAsOpaqueOrigin`).
    The API is live.
  - **But** `Navigation::documentCanHaveURLRewritten` starts with
    `if (!documentOrigin->isSameOriginAs(SecurityOrigin::create(targetURL))) return false;`.
  - WebKit2 defaults `AllowFileAccessFromFileURLs` to false (Source/WTF/Scripts/Preferences/UnifiedWebPreferences.yaml),
    so `Document` calls `setEnforcesFilePathSeparation()` on a file: origin.
  - `isSameOriginAs` then fails against any other origin object, because `hasLocalUnseparatedPath` needs
    both sides unseparated.
  - **Inference: on a file:// page in Safari, `canIntercept` is false for every navigation, hash-only ones
    included, so `intercept()` throws SecurityError.**
  - Navigate events still fire, and a fragment navigation still goes ahead natively.
  - UNCONFIRMED: nothing was run. One test settles it: log `e.canIntercept` in a navigate listener and
    click a `#x` link in a local file in Safari.
  - Safari's History API is not affected on file:// (section 2.1).

### Other gaps

- **Frames.** MDN: "The Navigation API operates only within a single frame — the top-level page, or a
  single specific `<iframe>`". `entries()` holds "all session history entries for this navigable that are
  same origin and contiguous to the current session history entry" (spec).
  - An iframe sees only its own entries.
  - `navigation.back()` in an iframe moves only that frame, and `canGoBack` is false at the frame's first
    entry.
  - `history.back()` moves the whole tab (section 3).
- **No navigate event on first load.** "The current Navigation API specification doesn't trigger
  "navigate" on a page's first load" (https://developer.chrome.com/docs/web-platform/navigation-api; the
  page says last updated 2021-08-25).
- **Traversals.** A cross-document traversal cannot be intercepted. A same-document traversal can be
  cancelled only in the top frame, and from the browser UI only with history-action user activation.
  - Chromium `should_allow_traversal_cancellation` = `IsMainFrame()` and (`!is_browser_initiated ||
    IsHistoryUserActivationActive()`).
  - Firefox `traverseCanBeCanceled` = `IsTop() && SameDocument() && (not BrowserUI ||
    HasHistoryActionActivation)`.
  - WebKit checks top frame and same document, and leaves a FIXME for the activation rule.
  - MDN still says "cancellation of traverse navigations is not yet implemented", which is out of date for
    the Chrome and Firefox code.
- **Default focus and scroll after `intercept()`.** Defaults per MDN
  (https://developer.mozilla.org/en-US/docs/Web/API/NavigateEvent/intercept):
  - `focusReset: 'after-transition'` moves focus to the first `[autofocus]` element "or the `<body>`
    element".
  - `scroll: 'after-transition'` scrolls to the fragment or the top on push/replace. On traverse/reload it
    restores scroll once the handler fulfils, or not at all if the handler rejects or the user scrolled.
- **`history.pushState()` fires `navigate` too.** The spec's "shared history push/replace state steps"
  fire it, and WebKit's `History::stateObjectAdded` calls `dispatchPushReplaceReloadNavigateEvent`.
  - A navigate listener that intercepts with the defaults would scroll to the top and reset focus on every
    filter write.
- **WebKit has a second limiter** on navigate dispatch: 200 per 10 s per window. Over the limit it logs
  "Excessive navigation attempts blocked." and rejects with `QuotaExceededError` "Navigation rate limit
  exceeded" (Source/WebCore/page/Navigation.h; commit of 2025-11-13).

**What this means for the report:**
- Make `pushState`/`replaceState` plus `popstate` the only way the report routes.
- Do not route through `intercept()`. It is probably unusable on Safari file://, off in sandboxed iframes,
  missing from ESR 115 and older Safari, and still uneven across engines.
- Where `navigation?.currentEntry` is non-null, read `currentEntry.index` and `canGoBack` as optional
  hints. Never depend on them.

---

## 2. History API details

### 2.1 What may change on file://, and the error

- **Spec** ("can have its URL rewritten", https://html.spec.whatwg.org/multipage/nav-history-apis.html#can-have-its-url-rewritten):
  - Scheme, username, password, host and port must match. HTTP(S) may then change anything.
  - "If targetURL's scheme is "file": If targetURL and documentURL differ in their path component, then
    return false." The note: "Differences in query and fragment are allowed for file: URLs."
  - Every other scheme may change only the fragment.
  - "Only the URL of the Document matters, and not its origin."
  - The steps throw SecurityError when the document is not fully active, when the URL does not parse, or
    when it cannot be rewritten. The state is structured-serialised first, so `DataCloneError` comes first.
  - Relative URLs resolve against the **base URL**, so a `<base href>` turns `?x` into a path change.
- **Chrome** (`CanChangeToUrlForHistoryApi`, which now lives in
  https://github.com/chromium/chromium/blob/main/third_party/blink/renderer/core/frame/history_util.cc):
  - On file:, a path change returns false. Query and fragment may change.
  - An opaque-origin document gets a "Non-standard" allowance (crbug 528681) to change query and fragment.
  - Error text: "Failed to execute 'pushState' on 'History': A history state object with URL '…' cannot be
    created in a document with origin 'null' and URL '…'."
- **Firefox** (`Document::CanRewriteURL`, dom/base/Document.cpp; `NS_SecurityCompareURIs`,
  netwerk/base/nsNetUtil.cpp):
  - A change to the fragment alone is always allowed.
  - Otherwise file: must be the same file ("For file scheme, reject unless the files are identical"), so
    the query may change but the path may not.
  - Error: SecurityError "The operation is insecure.", with no URL in it.
- **Safari** (Source/WebCore/page/History.cpp):
  - "Blocked attempt to use history.pushState() to change session history URL from … to …. Only
    differences in query and fragment are allowed for file: URLs." The path rule landed 2022-11-17
    (256785@main).
  - Local and opaque origins may change query and fragment when the path is unchanged (bug 183028).
- **The origin a file: page sees.**
  - Chrome: `self.origin` is "null" and `location.origin` is "file://".
  - Firefox: both are "null". Each file has been its own origin since Firefox 68 (2019-07-09,
    CVE-2019-11730, pref `privacy.file_unique_origin`). That pref was removed in Firefox 95, and the rule
    now sits under `security.fileuri.strict_origin_policy`, default true.
  - Safari: `self.origin` is "null" (`setEnforcesFilePathSeparation`) and `location.origin` is "file://".
  - The Safari pair and the 95 removal are source read only.
- **Precedent.** Allure 3 passes a relative URL (`fileName + search + hash`) to `pushState`, after a
  single-file report on a UNC path (`\\filer\report.html`) would not open test details: issue #607
  (2026-04-23), PRs #614 and #786
  (https://github.com/allure-framework/allure3/blob/41e9cf422f3a2cd8687c268b4411e6d4599d58c5/packages/web-commons/src/stores/url/helpers.ts#L52-L56).

### 2.2 Rate limits (current values)

| Engine | Limit | Over the limit | Counts |
|---|---|---|---|
| Chrome (numbers unchanged since 2018-10-31) | 200 per 10 s per frame; the window resets on the first call after 10 s | silently dropped; console: "Throttling navigation to prevent the browser from hanging. See https://crbug.com/1038223. Command line switch --disable-ipc-flooding-protection can be used to bypass the protection" | pushState, replaceState, history.go/back/forward, every frame navigation including `location.hash =` |
| Firefox 157 (2026-09-29) and 158 | 200 per 10 s per browsing context (`dom.navigation.navigationRateLimit.count` / `.timespan`) | silently ignored; console: "Too many attempts to navigate or modify history within a short timeframe." | non-traversal navigations (location, links, fragments), push/replaceState, reload, go/back/forward, the `scrollRestoration` setter |
| Firefox 149-156, ESR 153.5 | 1000 per 10 s | throws SecurityError | as above |
| Firefox 83 (2020-11-17) to 148 | 200 per 10 s | throws SecurityError "The operation is insecure." | |
| Firefox ESR 115.43 | 1000 per 10 s (`dom.navigation.locationChangeRateLimit.count`) | throws | |
| Safari 27.0 (WebKit main) | 100 per 10 s, counted on the **main frame**, so iframes share the page's budget | throws SecurityError "Attempt to use history.pushState() more than 100 times per 10 seconds" (or "replaceState()") | pushState and replaceState |
| Safari, Navigation API dispatch | 200 per 10 s per window | silently aborted; promises reject `QuotaExceededError` | location and fragment navigations, `navigation.*` |

Sources:
- Chromium navigation_rate_limiter.cc and history.cc on GitHub main.
- Firefox StaticPrefList.yaml on main and on the esr153 and esr115 branches. Bug 1995636 ("Increase
  navigation rate limiting threshold", target 149, 2026-01-30).
- Bug 1922677 ("Rate limit every navigation", landed 2026-09-09 as 73a7bc0a7aca, target 157). It was
  REOPENED on 2026-10-18, so it could still be backed out. UNCONFIRMED.
- WebKit History.cpp and Navigation.h. The window has been 10 s since 2023-05-25 (264499@main); it was
  30 s before that. Bug 317129 ("Rate limit navigations per spec") is still NEW.
- **Spec:** WHATWG PR #12492, merged 2026-06-16: "This returns instead of throws for web compat"
  (https://github.com/whatwg/html/pull/12492).
- **MDN** still lists SecurityError "if the method is called too frequently"
  (https://developer.mozilla.org/en-US/docs/Web/API/History/pushState).
- **Tests cannot see any of this.**
  - Playwright starts Chromium with `--disable-ipc-flooding-protection`
    (packages/playwright-core/src/server/chromium/chromiumSwitches.ts line 65).
  - Firefox's WebDriver preferences set `dom.navigation.navigationRateLimit.count` to 0
    (remote/shared/RecommendedPreferences.sys.mjs).
  - So neither E2E stack can hit the limit. Whether Playwright's WebKit lifts the History limit is
    UNCONFIRMED.

### 2.3 State and URL size

- **Firefox:** `browser.history.maxStateObjectSize` = 16,777,216 bytes, counted on the serialised state.
  - History: 640 KB, then 2 MiB from Firefox 68, then 16 MiB from Firefox 96 (2022-01-11, bug 1742168).
  - Over the limit it throws `NS_ERROR_ILLEGAL_VALUE`, which is not a DOMException.
- **Safari:** no limit per object. Each main-frame document may store 64 MB in total: URL length × 2 plus
  the serialised bytes, summed over every call. A replace subtracts what it replaces, and iframes count.
  Over the limit it throws `QuotaExceededError` "Attempt to store more data than allowed using
  history.pushState()".
- **Chrome:** no limit in the history code; Mojo's message cap is 256 MiB. Session restore keeps at most
  about 63 KB of page state per entry (`max_state_size`, components/sessions/core/base_session_service_commands.cc).
  A larger state is lost when the browser restores tabs after a restart.
- **URL length:**
  - Chrome: 2 MiB (`url::kMaxURLChars`). A longer commit URL is replaced by `about:blank#blocked`.
  - Firefox: `network.standard-url.max-length` is 1 MiB; a longer URL throws SecurityError.
  - Safari: a 2 MiB fragment cap applies on loads.
- **MDN's note:** "Some browsers save `state` objects to the user's disk ... and impose a size limit ... you're
  encouraged to use sessionStorage and/or localStorage." On file:// and in sandboxed frames, storage may
  throw (section 3.7).

### 2.4 Entries per tab

- **Spec:** "User agents may limit the number of state objects added to the session history per page".
  The entry after the document's first one is evicted ("a FIFO buffer for eviction, but as a LIFO buffer
  for navigation").
- **Engine caps:**

| Engine | Cap | Source |
|---|---|---|
| Chrome | 50 entries per tab (`kMaxSessionHistoryEntries = 50`) | third_party/blink/public/common/history/session_history_constants.h |
| Firefox | 50 (`browser.sessionhistory.max_entries`) | modules/libpref/init/all.js |
| WebKit | 100 (`DefaultCapacity = 100`) | Source/WebKit/UIProcess/WebBackForwardList.cpp |

- When Chrome's list is full, it drops the oldest *skippable* entry first, and otherwise the oldest entry
  (`PruneOldestSkippableEntryIfFull`, content/browser/renderer_host/navigation_controller_impl.cc).
- The same caps (WebKit 100, Chromium 50, Gecko 50) are recorded in https://github.com/whatwg/html/issues/8620
  (2022-12-14).

### 2.5 popstate and hashchange

- **Spec** (https://html.spec.whatwg.org/multipage/browsing-the-web.html, "update document for history step
  application"): for a same-document change it fires popstate synchronously, then runs "restore persisted
  state" (scroll), then *queues* hashchange if the fragment changed.
- **What fires when:**
  - A fragment navigation (`location.hash = …`, a click on `#x`) fires popstate with state null, and
    queues hashchange.
  - `pushState`/`replaceState` fire neither. Spec note: "popstate events fire for fragment navigations, but
    not for history.pushState() calls".
  - Back/Forward between any two same-document entries fires popstate, however each entry was made.
    hashchange fires as well when their fragments differ, so two pushState entries `#a` and `#b` fire both.
- **Chrome:** popstate has been synchronous since Chrome 103 (2022-06-21; CL 3580022, matching
  whatwg/html#7815, merged 2022-04-27).
- **Firefox:** before 150 (2026-04-21), a `setTimeout(0)` set inside popstate ran before hashchange
  (bug 2015811, target 150). Firefox now enqueues hashchange before it dispatches popstate. Its code
  comment: "We enqueue the hashchange event first before dispatching popstate, differing from the spec".
  Script still sees popstate, then hashchange.
- **Safari:** popstate synchronous since 2022-09-15 (254519@main, bug 245153).
- **Open spec question:** where hashchange falls relative to other tasks (https://github.com/whatwg/html/issues/12190,
  opened 2026-02-24).
- **Before `load`:** popstate can now fire before `load` in all three engines, so register the listener
  early.

### 2.6 popstate on initial load

- **Old behaviour:** Chrome before 34 (stable 2014-04-01) and Safari before 10 fired popstate on load
  (MDN BCD notes on `popstate_event`). Firefox never did. WebKit fixed it on 2015-10-02 (bug 94265).
- **Current behaviour:** popstate fires only when `documentIsNew` is false. So it does not fire on first
  load, on reload, or on a Back that misses the bfcache.
- **What the report must do:** initialise from `location.hash` and `history.state` on every load.

### 2.7 history.scrollRestoration

- **Per entry.** The mode belongs to each entry, and `pushState`/`replaceState` copy the active entry's
  mode to the new entry (spec, "URL and history update steps"; Chrome, Firefox and WebKit code agree).
- **Fragment navigations** copy it too in the spec, Chrome and Firefox. WebKit gives such an entry the
  default "auto". That comes from an unmerged WPT PR (#63184, opened 2026-10-01) and the code, so it is
  UNCONFIRMED.
- **When the restore runs.** It runs after popstate:
  - Chrome reads the mode before popstate and applies the offset at the next frame.
  - Firefox restores synchronously after popstate.
  - WebKit restores after popstate and reads the mode after it.
- **The browser may keep trying.** It may restore again "periodically", and skips the restore once the
  user has scrolled. Not restoring "does not imply that scroll positions will be left at any particular
  value" (spec, "restore persisted state").
- **With "manual",** Chrome and WebKit also skip the fragment scroll on Back/Forward.
- **Support:** Baseline widely available since January 2020 (Chrome 46, Firefox 46, Safari 11; MDN).
- **Firefox only:** the setter counts against the rate limit.

### 2.8 bfcache

- **file:// in Chrome.** Chrome never caches a file:// page: "Do not store main document with non HTTP/HTTPS
  URL scheme", reason `SchemeNotHTTPOrHTTPS` (content/browser/back_forward_cache/back_forward_cache_impl.cc).
  So Back into a file:// report from another page always reloads it, with no popstate and no persisted
  `pageshow`.
- **file:// in Firefox and Safari.** The cache code has no scheme check, so file:// pages look eligible.
  Source read, UNCONFIRMED.
- **`Cache-Control: no-store`** on CI hosts blocks the cache in Chrome (by default) and in WebKit.
- **web.dev** (updated 2026-10-16, https://web.dev/articles/bfcache): "it's good to always update the page
  after a `pageshow` event if `event.persisted` is `true`". Iframes "are not separately eligible".
- **Same-document entries** never involve the bfcache.

### 2.9 Back-button skipping (entries made without user activation)

- **Chrome** (on by default since 75, stable 2019-06-04;
  https://github.com/chromium/chromium/blob/main/docs/history_manipulation_intervention.md):
  - The rule: the Back button skips "pages that added history entries or redirected the user without ever
    getting a user activation".
  - "When a document adds a history entry without having a user activation, the `should_skip_on_back_forward_ui_`
    for all same-document history entries will be set to `true`".
  - `replaceState` never marks an entry.
  - Activation is sticky activation from any frame of the page. Subframe entries are covered too.
  - After a same-document Back or Forward from the browser UI, earlier activation stops counting until the
    user interacts again (crbug 1248529).
  - `history.back()` is not affected.
  - Activation comes from trusted keydown (not Escape), mousedown, mouse pointerdown, non-mouse pointerup
    and touchend. Scrolling does not count (spec, "activation-triggering input event").
- **Firefox** (`browser.navigation.requireUserInteraction` = true; on by default since **Firefox 135,
  2025-02-04**: "such entries, unless interacted with by the user, are skipped"; bug 1939691):
  - Each entry counts only if the user interacted while it was current (keyup, mouseup, wheel, touchend,
    pointerup, drop, or activation).
  - Back skips entries without interaction, except the first and the last.
  - `history.back()` is not affected.
  - **Two pushes from one click leave the first one skippable** (bug 1981019).
  - ESR 115 has the pref off.
- **Safari** (source read):
  - `pushState` and fragment items created without a gesture, or more than 10 s after the last activation,
    are marked `wasCreatedByJSWithoutUserInteraction`. This is gated on Apple SDK behaviour.
  - When the current item is marked, Back skips the run of marked items and the unmarked item before them.
  - Script navigation is not affected.
  - The Safari release that first shipped this is UNCONFIRMED; WebKit landed it in June 2022.
- **Navigation API:** `navigation.back()` never skips.

**What this means for the report:**
- **Pushes.** Call `pushState` only inside a click or keydown handler (not Escape), and push once per
  gesture. Firefox makes the extra pushes skippable.
- **Everything else replaces:** load-time normalisation, popstate and hashchange handlers, filter typing,
  and `<details>` bookkeeping.
- **Wrap every history call** in try/catch. Safari, Firefox 149-156 and the ESRs throw at the rate limit.
- **Budget** about one write per 100 ms at most, counted together with the host page when framed.
- **Keep `history.state` small,** under 63 KB, or Chrome's session restore drops it.
- **One router.** Make popstate the single input and rebuild the view from `location.hash` plus
  `history.state`, from it and from load.
  - The existing hashchange listener (report-init-script.js) would also run on Back between entries whose
    fragments differ. Once Back works, it must ignore traversals.
- **Prefer `pushState` with `preventDefault()` over fragment links.** A `#scenario-…` fragment matches an
  element id, so the native jump fights `jump_into_view`, and Safari gives fragment entries the "auto"
  scroll mode.
- **Plan for the 50-entry cap.** Push only for "places", so the CI page behind the report stays reachable.

---

## 3. Iframes, joint session history and the hosts

### 3.1 Joint session history

- **One list per tab.** Navigations in child navigables "need to be linearized into a single session
  history list since the user only has a single back/forward interface for the entire traversable
  navigable (e.g., browser tab)" (spec §7.4, https://html.spec.whatwg.org/multipage/browsing-the-web.html).
  - A `pushState` or fragment navigation in an iframe therefore adds an entry that the browser's Back
    button traverses.
  - It also clears the tab's forward list.
- **The address bar keeps the top-level URL.** No spec sentence says so outright; it follows from the
  above.
- **`history.back()` from an iframe** is a delta traverse over the whole tab. If the parent pushed last, it
  undoes the parent's entry. If the report has no entries of its own, it can take the host page back.
  - Exception: a sandboxed caller without `allow-top-navigation` gets "initiator-disallowed", and
    Chromium discards the traversal (`kSandboxViolation`).
- **`history.length` counts the whole tab.** `navigation.entries()` lists only that frame's same-origin
  entries.
- **A page's iframe entries can become skippable.** Chrome's skip rule covers subframe entries, and
  activation in any frame clears it. Inference from the code: a framed report that pushes without
  activation makes the host page's entries skippable too.
- **Safari before 27.0** set "the parent window's history.state" to null whenever a child iframe called
  `pushState` (fixed in 27.0, 2026-09-14). A framed report can break a single-page host, Azure DevOps for
  example, on older Safari. That is my inference.
- **`location.hash` during load.** Assigning it before the document has finished loading, without
  transient activation, becomes a *replace* (spec, "Location-object navigate"). `pushState` has no such
  rule.
- **Precedent.**
  - Grafana #5593 (2016-07-15): panels embedded in iframes added "5 more histories" to the host.
  - Playwright #28389 (2023-11-29), a report inside an Azure DevOps iframe. Maintainer: "Back button in
    the browser works in the iframe… We don't want to be mimicking browser functionality in-page."

### 3.2 Jenkins HTML Publisher

- **The plugin's wrapper** (HTML Publisher 429, 2026-08-14):
  - It shows the report in `<iframe id="myframe"></iframe>` with **no sandbox attribute** (src/main/resources/htmlpublisher/HtmlPublisher/footer.html).
  - `htmlpublisher.js` sets `iframe.src` on DOMContentLoaded and on every tab click, so each tab click is a
    new entry in the tab's history.
  - The `useWrapperFileDirectly` option serves the archived wrapper as it is; it uses the same iframe.
- **Jenkins core's default CSP for user content** (`DirectoryBrowserSupport.DEFAULT_CSP_VALUE`, verified
  2026-10-19): `"sandbox allow-same-origin; default-src 'none'; img-src 'self'; style-src 'self';"`.
  - `allow-same-origin` was added in Jenkins 2.417 (2023-08-01, PR #8273).
  - The `sandbox` directive without `allow-scripts`, plus `default-src 'none'`, means **no script runs**.
    `style-src 'self'` blocks inline `<style>`.
  - So a single-file report with inline JS and CSS does not work under the defaults, and its use of
    `pushState` never comes into play.
- **What Jenkins recommends** (https://www.jenkins.io/doc/book/security/configuring-content-security-policy/):
  "It is strongly recommended to set up the Resource Root URL instead of customizing
  Content-Security-Policy".
  - Resource Root URLs "will not set the CSP header, allowing JavaScript and similar features to work".
  - Those URLs "expire after 30 minutes by default" (https://www.jenkins.io/doc/book/security/user-content/).
  - The fallback is the system property `hudson.model.DirectoryBrowserSupport.CSP`.
- **With a resource root,** the report runs as a cross-origin child of the wrapper. Its entries join the
  Jenkins tab's history, the address bar keeps showing the wrapper, and a deep link to a report state
  expires with the resource URL. That is derived.
- **Precedent:** Playwright #10750 (2021-12-07) reports that inside HTML Publisher, Back "means moving away
  from the current Jenkins job run".

### 3.3 Azure DevOps

- **No built-in viewer.** Microsoft documents no way to view artifact HTML in the browser: "download your
  Artifact and explore its content" (pipeline artifacts docs, updated 2026-09-22).
- **The Code Coverage tab's HTML view** was sandboxed without `allow-scripts` in 2020 (ReportGenerator
  #358). Whether that still holds is UNCONFIRMED.
- **Extensions** "run in a sandboxed browser iframe" (https://learn.microsoft.com/en-us/azure/devops/extend/overview,
  2026-04-13). The sandbox tokens are not documented.
  - A community answer from 2023, re-confirmed 2025-11-02, says the iframe on Azure DevOps Server 2022
    lacks `allow-same-origin`.
  - For Services this is UNCONFIRMED.
- **Marketplace viewers** (sources on GitHub, read 2026-10-19):

| Extension | Version (date) | How it renders the report |
|---|---|---|
| JakubRumpca Html Viewer | 1.0.8 (2021) | `<iframe srcdoc>`, scripts run |
| awardedsolutions fork | 1.3.0 (2025-06-07) | `<iframe srcdoc>`, scripts run |
| blakyaks Html Reports | 1.3.0 (2025-01-23) | `<iframe srcdoc>`, scripts run |
| Cucumber HTML Report | 1.0.18 (2026-07-31) | `<iframe srcdoc>`, scripts run |
| LakshayKaushik PublishHTMLReports | 1.2.1 | `innerHTML`, so **scripts do not run** |

- **What srcdoc means for the report:**
  - Its URL is `about:srcdoc`, which may change only the fragment, and it has no shareable link.
  - Its entries mix with Azure DevOps' own entries in the tab.
  - The srcdoc child inherits the extension frame's sandbox flags. If that frame lacks
    `allow-same-origin`, the report has an opaque origin: storage throws and the Navigation API is off.

### 3.4 GitLab

- **When HTML artifacts can be previewed:** when GitLab Pages is enabled on the instance (on GitLab.com,
  and on Linux packages with the built-in NGINX). Private projects also need Pages access control
  (https://docs.gitlab.com/ci/jobs/job_artifacts/).
- **How:** the `file` action redirects to a top-level page on the Pages domain,
  `…/-/jobs/<id>/artifacts/<path>`, for `.html .htm .txt .json .xml .log`.
- **No CSP from the proxy.** The Pages artifact proxy copies only Content-Type, Content-Encoding,
  Content-Length, Vary and Cache-Control (gitlab-pages internal/artifact/artifact.go).
- **For the report:** it is a top-level HTTPS page, and every History API feature works. Storage is shared
  with every artifact of the group's Pages host.

### 3.5 GitHub

- **Pages** serves a top-level HTTPS page with no CSP.
- **Actions artifacts.** Since 2026-02-26, an artifact uploaded with `actions/upload-artifact` v7 and
  `archive: false` (one file) can be viewed in the browser. The announcement says this is "great for simple
  HTML files (without links to CSS or JS)"
  (https://github.blog/changelog/2026-02-26-github-actions-now-supports-uploading-and-downloading-non-zipped-artifacts/).
  - The file is served from short-lived blob-storage SAS URLs, about 10 minutes in one report
    (actions/upload-artifact #787), so deep links expire.
  - Whether inline scripts run there is UNCONFIRMED.
  - Zipped artifacts, still the default, must be downloaded.

### 3.6 VS Code

- **Simple Browser:**
  - It puts the page in `<iframe sandbox="allow-scripts allow-forms allow-same-origin allow-downloads">`
    inside a webview (extensions/simple-browser/src/simpleBrowserView.ts).
  - Its Back and Forward buttons call `history.back()` and `history.forward()` on the webview document
    (preview-src/index.ts).
  - It cannot reach file:// (derived from its CSP `frame-src *` and its http/https opener).
- **Integrated Browser** (VS Code 1.109, 2026-02-04):
  - It is an Electron `WebContentsView`, so the page is a real top-level page.
  - It can open a local file:// page, and its Back and Forward use the real history.
  - On desktop, Simple Browser now hands off to it.
- **Live Preview:** it keeps its own page list, so it never sees `pushState` entries.

### 3.7 Sandboxed iframes without allow-same-origin

- **`pushState`/`replaceState`:** a hash-only change never throws, in the spec or in any of the three
  engines. Beyond that:
  - Chrome and Safari also allow query changes in opaque non-HTTP documents.
  - Firefox allows only the fragment on a sandboxed file: page.
- **Storage:** `localStorage` and `sessionStorage` throw SecurityError.
  - Chromium: "The document is sandboxed and lacks the 'allow-same-origin' flag."
  - Spec: https://html.spec.whatwg.org/multipage/webstorage.html.
  - MDN adds that `sessionStorage` can throw on `file:` and `data:` origins.
- **Navigation API:** off in all three engines (section 1).

**What this means for the report:**
- **Call `history.back()` only for an entry the report knows it pushed,** marked in `history.state`.
  - Framed, an unguarded back can leave Jenkins or Azure DevOps.
  - On file://, an unguarded back can leave for whatever page opened the report.
- **Pass relative URLs** (`'#' + …`) to `pushState`.
- **Keep scroll state and other per-entry state in `history.state`, not in storage,** and wrap every
  storage access in try/catch.
- **Document Jenkins's resource root URL** as the way to see the report in Jenkins at all.
- **Expect no useful address-bar link when framed,** so a "copy link" control needs the top-level URL,
  which a framed report cannot know.

---

## 4. Scroll restoration

### 4.1 Router patterns (docs and source, read 2026-10-19)

- **React Router** (`<ScrollRestoration>`, 8.5.0):
  - Positions live in `sessionStorage` under "react-router-scroll-positions", keyed by `location.key` (or
    a custom `getKey`).
  - It sets `scrollRestoration = "manual"` on mount and saves on `pagehide`.
  - It restores in a layout effect after the loaders, in this order: saved position, then hash target,
    then top
    (https://github.com/remix-run/react-router/blob/main/packages/react-router/lib/dom/lib.tsx).
- **TanStack Router:**
  - Positions live in `sessionStorage` under `tsr-scroll-restoration-v1_3`, keyed per entry.
  - It restores "after successful navigations before DOM paint", and covers nested scrollers via
    `data-scroll-restoration-id`.
  - Source: router-core/src/scroll-restoration.ts.
- **Vue Router:**
  - `push()` first writes `scroll` into the current `history.state` with `replaceState`, then pushes. It
    also writes on `pagehide`.
  - `savedPosition` is "only available if this is a `popstate` navigation".
  - `{ el, top }` treats `top` as an offset from the element.
  - Returning a Promise delays the scroll (https://router.vuejs.org/guide/advanced/scroll-behavior.html).
- **Angular** (`scrollPositionRestoration: 'enabled'`):
  - It keeps an in-memory store keyed by navigation id.
  - It waits for `setTimeout` and then `requestAnimationFrame` before scrolling: "cannot rely solely on
    setTimeout because a frame may paint before the timeout" (packages/router/src/router_scroller.ts).
- **Turbo:** a `restorationIdentifier` in `history.state` plus an in-memory map, with mode "manual".
- **No router writes history on every scroll event.** They save on leaving: before a push, in popstate,
  or on `pagehide`.

### 4.2 Anchor plus offset, not pixels

- **The scroll-anchoring explainer** (WICG, archived 2019) proposed that the history entry store
  "something analogous to a CSS selector for the scroll anchor node, instead of storing an absolute scroll
  offset" (https://github.com/WICG/ScrollAnchoring/blob/master/explainer.md).
- **CSSWG #6225** (2021-04-19, still open) describes facebook.com's feed under `content-visibility: auto`:
  restoring an absolute offset means "the browser will fail to scroll to a proper position". It proposes
  storing anchor plus offset. A Chrome engineer suggested saving the anchor on each entry with the History
  API (https://github.com/w3c/csswg-drafts/issues/6225).
- **Baymard** (2020-11-17, https://baymard.com/blog/return-same-place): sending users back to the top of
  a list instead of their previous place caused abandonment.

### 4.3 Navigation API scroll handling

- **`intercept({ scroll })`:** `'after-transition'` (the default) or `'manual'`.
- **`NavigateEvent.scroll()`** works only once the interception is "committed". It throws
  `InvalidStateError` before commit, on a second call, or after the automatic scroll.
- **The browser may re-aim.** It "may try to set the scroll position multiple times asynchronously", so it
  can still land "even if the content arrives slightly later"
  (https://developer.chrome.com/docs/web-platform/navigation-api).
- Section 1 explains why the report should not rely on this.

### 4.4 content-visibility: auto and contain-intrinsic-size

- **CSS Containment 2** (ED 2026-10-15, https://drafts.csswg.org/css-contain-2/):
  - It warns that the layout can "'jump around' as elements go off-screen and start skipping", and
    recommends `contain-intrinsic-size: auto`.
  - `scrollIntoView` on a skipped element works out its position "with size containment still active",
    then the element renders, so it may not land exactly.
- **Remembered sizes** (CSS Sizing 4 §5.2.1) are per element and recorded when the element renders.
  - Inference: after a reload, or a Back that misses the bfcache (every file:// Back in Chrome), every
    section not yet rendered in the new document is back at its placeholder size. A saved pixel offset
    then lands on different content.
- **CSSWG decisions:**
  - #8407 (resolved 2023-02-22): `content-visibility: auto` forces `contain-intrinsic-size` to gain an
    `auto` value (Chrome 117).
  - #9337 (resolved 2024-01-17): the target of `scrollIntoView` is "relevant to the user", and so are its
    ancestors.
  - #9833 (open): a smooth `scrollIntoView` stops short when the placeholders underestimate.
- **Bugs:** WebKit bug 324202 (fixed 2026-09-21, PR #74017); Mozilla bug 1979805 (NEW).
- **Support:**
  - `content-visibility`: Chrome 85, Firefox 125 (2024-04-16), Safari 18 (2024-09-16, partial until 26).
    Baseline newly available since 2025-09-15.
  - `contain-intrinsic-size`: Baseline widely available since 2026-03-18 (webstatus.dev).
- **Fragment navigation opens `<details>`; `scrollIntoView` does not.** Fragment navigation runs the
  ancestor-revealing algorithm, which opens closed `<details>`. My reading of the spec is that a plain
  `scrollIntoView()` does not, so the report must open them itself, as `reveal_url_anchor` already does.

### 4.5 Scroll anchoring (overflow-anchor)

- **Spec:** CSS Scroll Anchoring Level 1 (ED 2026-09-30).
- **Chrome and Firefox:** Chrome 56 (2017-01-25), Firefox 66 (2019-03-19).
- **Safari: shipped, on by default, in Safari 27.0 on 2026-09-14.** The release notes say "Enabled scroll
  anchoring, which prevents visible jumps in scroll position when content is inserted or removed above the
  viewport". WebKit's post of 2026-09-17 (https://webkit.org/blog/18325/webkit-features-for-safari-27-0/)
  says "Scroll anchoring is controlled by the `overflow-anchor` CSS property, which defaults to `auto`".
  - Earlier steps: Technology Preview 185 (2023-12-20) "Added support for scroll anchoring", then
    "[Scroll anchoring] Enable in stable" (bug 307734, landed 2026-02-13), then TP 238 (2026-02-26,
    "Enabled scroll anchoring") and TP 241 (2026-04-08).
- **Baseline:** newly available since 2026-09-14 (webstatus.dev).
- **caniuse is stale.** It still marks Safari as unsupported.
- **Safari 26 and older,** which means every iPhone not yet on iOS 27, have no scroll anchoring.

**What this means for the report:**
- **Saving.** Before each `pushState`, and on `pagehide`, write a scroll record into the *current* entry
  with `replaceState`: the stable id of the topmost visible scenario or feature, the offset of its top from
  the viewport top, and `scrollY` as a fallback.
- **Mode.** Set `history.scrollRestoration = 'manual'` once at startup. The browser's pixel restore runs
  straight after popstate, before the report has switched tab and filters, and across placeholders whose
  heights have changed.
- **Restoring, after popstate or on load:**
  1. rebuild the tab and filters;
  2. open the enclosing `<details>`;
  3. `scrollIntoView` the anchor with `behavior: 'instant'`, then apply the offset;
  4. keep the existing re-aim loop for a few frames, as `jump_into_view` does. Safari 26 and older need it
     because they have no anchoring; the other engines need it because the re-aim covers the placeholders
     as they render.
- **The code comment** saying Safari has no scroll anchoring should now say "Safari 26 and older".

---

## 5. Accessibility of view changes, and what deserves a history entry

### 5.1 Focus and announcements

- **Marcy Sutton with Fable Tech Labs** (Gatsby blog, 2019-07-11,
  https://www.gatsbyjs.com/blog/2019-07-11-user-testing-accessible-client-routing/):
  - Five sessions in June 2019, with NVDA, JAWS, magnification, Dragon and switch users.
  - **Screen reader users rated focusing the new content's heading best.** It "typically requires
    `tabindex="-1"`". Resetting focus to the top of the app was "very overwhelming", and focusing a wrapper
    was "okay".
  - **Magnifier users** were badly served by focusing wide wrappers or headings: mobile Chrome scrolled to
    the middle of the text. Dragon and switch users needed a visible focus outline.
  - **Revised advice** after a re-test in October 2019: "Provide a skip link that takes focus on a route
    change within the site", and "Include an ARIA Live Region on page load". "On a route change, append
    text to it indicating the current page".
- **Deque** (2020-09-24, https://www.deque.com/blog/accessible-routing-in-javascript-frameworks/) ranks
  three options:
  1. the top-level heading with `tabindex="-1"`;
  2. the main container with `tabindex="-1"` and a visible indicator;
  3. a skip link plus a live announcement.
- **GOV.UK Service Manual** (updated 2024-09-27, https://www.gov.uk/service-manual/technology/using-progressive-enhancement):
  "Do not build your service as a single-page application". Its reasons: assistive-technology users
  "would be unaware of changes in context", the app "would fail to handle focus", and users "would be
  unable to navigate using the back or forward buttons".
- **WCAG 2.2 Understanding docs:**
  - 2.4.2: in single-page apps "the title of the page should also be changed dynamically"
    (https://www.w3.org/WAI/WCAG22/Understanding/page-titled.html, updated 2026-03-09).
  - 3.2.2: "A tab control" does "not necessarily change the context".
  - 4.1.3: changes that are not a change of context belong in status messages.
- **What frameworks do:**
  - Next.js announces `document.title`, else the h1, through a visually hidden `aria-live="assertive"`
    region (docs 2024-11-06; app-router-announcer.tsx). It does not move focus.
  - SvelteKit announces the title and focuses `<body>`.
  - Astro and Gatsby announce, as Next.js does.
- **The Navigation API explainer** (https://github.com/WICG/navigation-api) says parity on Back and
  Forward "requires restoring focus to the same element that was previously focused when that history
  entry was active". It suggests storing an id in the entry's state.
- **When not to move focus:**
  - Tab switches: focus stays on the tab, following the APG keyboard model. GOV.UK focuses the tab when
    Back changes the hash.
  - In-place filter results: use a status message.
  - Closing a dialog: return focus to the element that opened it (https://www.w3.org/WAI/ARIA/apg/patterns/dialog-modal/).
  - Focus already moved during the transition: leave it.

### 5.2 Tabs and history

- **The APG Tabs pattern** says nothing about the URL or history (https://www.w3.org/WAI/ARIA/apg/patterns/tabs/).
- **GOV.UK tabs** (https://design-system.service.gov.uk/components/tabs/; that text was added in March
  2023):
  - "When moving between tabs, the URL gets updated with a fragment", so "pressing the browser's 'back'
    button should navigate back to the previous tab".
  - It also says "do not use the tabs component as a form of page navigation".
  - And: "This component has not yet been tried in research with users".
- **Android's old "Navigation with Back and Up"** (archived 2016) is the one explicit "no": "Changing view
  options for a screen does not change the behavior of Up or Back … no new navigation history is created".
  Its examples are switching tabs, filtering, sorting and zooming.
- **NN/g, "Tabs, Used Right"** (2024-08-02, reviewed 2026-09-02): in-page tabs "are not for navigation",
  while "Navigation tabs enable users to navigate to different pages". "Mixing in-page and navigation tabs
  within one tab control will disorient users".
- **Heydon Pickering** (2017-10-05): "Single-page applications should not present or behave as tabbed
  interfaces"; views should be same-page links using "standard hash fragments".
- **Smashing** (2022-08-16): "As long as the page doesn't change significantly, we should avoid adding
  states to the user's history stack". That covers checkboxes, drop-downs, view switchers and toggles.
- **Baymard** (2020-07-20, https://baymard.com/blog/back-button-expectations):
  - Make a new history entry for any view users perceive as new: "if users click a link and 70% of the
    view changes to something new, most will perceive this to be a new page".
  - "Truncated content should in general never be implemented as a new 'page'".
  - 59% of sites get at least one of its four patterns wrong.

### 5.3 What design systems do

- **GOV.UK Frontend tabs** (packages/govuk-frontend/src/govuk/components/tabs/tabs.mjs, main):
  - **Push:** `createHistoryEntry()` assigns `window.location.hash = panelId`. The file has no `pushState`
    or `replaceState`.
  - **No jump:** it blanks the panel's id around the assignment.
  - **Back switches tabs:** a `hashchange` listener shows the tab for the new hash and focuses that tab.
    A `changingHash` flag skips the events the component caused itself.
  - **Tablet and up only:** below that width the tabs are a list of sections.
  - **Possible bug (inference, untested):** re-clicking the selected tab sets `changingHash` but fires no
    event, so the next real Back is swallowed.
- **github/tab-container-element** has no history code.
- **Primer:**
  - UnderlineNav is navigation: links with `aria-current="page"`.
  - UnderlinePanels switch "without changing the URL", and Primer says: "If you want to use this pattern
    for tabs that change the URL when activated, use the UnderlineNav component instead".
- **Angular Material:** `mat-tab-group` switches views "within a single route", while `mat-tab-nav-bar` "provides
  a tab-like UI for navigating between routes".
- **Carbon and Polaris:** tabs are not for primary navigation. USWDS has no tabs component.
- **GitHub PR tabs** are separate URLs pushed through Turbo or React Router. That is inferred from the
  markup (section 7).

### 5.4 Guidance on push versus replace

- **developer.chrome.com:**
  - "If you would expect the state to be retained when the user shares the URL with another user, store it
    in the URL. Otherwise, the state object is the better option".
  - A `<details>` toggle goes in `updateCurrentEntry()`, the replace-style call.
  - "You should not be able to trap your users on your site".
- **The Navigation API explainer:** "Most developers will be fine either omitting the `history` option …
  or using `"replace"`".
- **Baymard, NN/g and Smashing** set the rules quoted in 5.2. NN/g calls the Back button "the lifeline of
  the web user" (1999).

**What this means for the report:**
- **What pushes, judged by Baymard's "70% of the view" test:**
  - the five top-level tabs;
  - a cross-tab link to a scenario;
  - opening a large overlay (section 6).
- **What replaces:**
  - the Full/Summary switch, a view option of the kind the Android rule names;
  - `<details>` expand and collapse, which is truncated content (Baymard). Record it in the current
    entry's state at most once per gesture.
  - filters (section 8).
- **Tabs that push:**
  - mark them up as navigation (links with `aria-current`), not `role=tablist`, as NN/g, Primer and Heydon
    advise; or, if they stay ARIA tabs, follow GOV.UK and keep focus on the tab;
  - update `document.title` per tab. Set the title *after* `pushState`. Chromium gives a title change to
    the frame's current entry (`WebContentsImpl::UpdateTitle`, which looks the entry up by `nav_entry_id`),
    so the Back menu labels each entry correctly. The ordering effect is my inference.
- **Cross-tab jump to a scenario:** move focus to the scenario's heading (`tabindex="-1"`) and announce the
  change through one polite live region.
- **Back and Forward:** restore the focus id saved in `history.state`, else focus the view's heading.

---

## 6. Back to close overlays

### 6.1 Guidance

- **For:**
  - NN/g (2015-05-25): users "click the browser's Back button in an attempt to revert to the underlying
    screen"; "Consider adjusting the Back button so that when an overlay is displayed, the Back button
    closes the lightbox" (https://www.nngroup.com/articles/overuse-of-overlays/).
  - NN/g (2020-11-29): "ensure the Back button has the same effect as a Close link".
  - NN/g (2023-06-11) supports Back for dismissing bottom sheets.
  - Baymard (2020): "users should always be exited from an overlay when clicking 'Back'"; 37% of sites
    fail this.
  - Smashing (2022): Back should close large overlays, but "we probably shouldn't add every single image
    to the user's history".
  - Grafana PR #130121 (merged 2026-08-12): "users read Back as 'close this'".
- **Against pushing:**
  - The WICG close-watcher explainer (archived 2025-08-19, https://github.com/WICG/close-watcher) calls
    history manipulation "a poor fit". Its reasons: the URL "shouldn't change", it is "very hard to
    determine when the component's state is popped out of history", "History navigation is not
    cancelable", and it "can easily corrupt a web application's router".
  - MUI leaves it to the app (#12759).
  - Angular CDK closes dialogs on popstate *without* pushing (`closeOnNavigation`, default true).

### 6.2 APIs, October 2026 (MDN BCD read 2026-10-19)

| Feature | Chrome / Edge | Firefox | Safari |
|---|---|---|---|
| `CloseWatcher` | 126 (2024-06-11); first shipped in 120 (2023-12-05), then disabled | 149 (2026-03-24) | Technology Preview only |
| `<dialog closedby>` | 134 (2025-03-04) | 141 (2025-07-22) | Technology Preview only |
| `dialog.requestClose()` | 134 | 139 (2025-05-27) | 18.4 (2025-03-31) |
| Popover (light dismiss) | Baseline since 2025-01-27, when Safari 18.3 fixed iOS light dismiss (https://web.dev/blog/popover-baseline) | | |
| `PopStateEvent.hasUAVisualTransition` | 118 | 149 | 18 |

- Interop 2026 includes `dialog closedby` (Mozilla post above).
- Safari 27.0's feature post mentions neither CloseWatcher nor `closedby`.

### 6.3 Close requests by platform (HTML §6.10, https://html.spec.whatwg.org/multipage/interaction.html#close-requests-and-close-watchers)

- **What counts as a close request:** "The Esc key on desktop platforms", "the back button or gesture on
  certain mobile platforms such as Android", assistive-technology dismiss gestures, and a gamepad's back
  button.
- **Desktop:** the browser's Back button is always a history traversal.
- **Android:** Back goes to the close watchers first. Only if none is active does it traverse history.
  - Chrome: "New in Chrome 120" (2023-12-05, https://developer.chrome.com/blog/new-in-chrome-120) says a
    modal `<dialog>` and a popover now "respond to the Android back button".
  - The `CloseWatcher` API shipped in 120, was then disabled "due to an unexpected interaction with
    `<dialog>`", and came back in 126 (2024-06-11). So the current behaviour is certain from 126 on.
    Whether dialogs kept it in 121 to 125 is UNCONFIRMED.
  - Firefox: `CloseWatcher` since 149. How Firefox for Android routes its Back button to dialogs is
    UNCONFIRMED.
- **iOS Safari:** the edge swipe is a history traversal with a preview of the previous entry, "including
  same-document traversals" (Jake Archibald, csswg-drafts #8333, 2023-01-19).
  - Stable Safari has no CloseWatcher, so only a pushed entry lets the swipe close an overlay.
  - `hasUAVisualTransition` (Safari 18) tells the page the browser already animated the swipe.
- **Anti-abuse rules:**
  - Watchers are grouped. A new group needs a user activation.
  - `cancel` is cancelable only with history-action activation.

### 6.4 Pitfalls and what libraries did

- **Orphan entries.** Closing with the X, Escape or a click on the backdrop leaves the pushed entry behind,
  so the next Back seems to do nothing.
  - Fancybox's docs admit "Browser history can end up with several empty entries" and that "it is not
    possible to make this plugin work perfectly in every scenario" (https://fancyapps.com/fancybox/plugins/hash/).
- **Android's Back is spent on the close request** (inference from §6.10). With a modal `<dialog>` or a
  popover open in Chrome on Android (120+), Back closes it without traversing. Unless the close handler
  pops the pushed entry, the entry is orphaned.
- **Forward reopens the overlay,** and so does a reload that keeps the state.
- **Pushing in a close handler is a trap.**
  - Allure 2 pushes `attachment: null` when its overlay closes, so Back probably reopens it (read from
    the code, not run).
  - Grafana's Workspace had the same bug before #130121.
- **PhotoSwipe:** v4 had a `history` option, on by default ("back button to close gallery"). v5 dropped it
  as "History API (#hash-based navigation is outdated)" (release v5.2.2, 2022-03-28).
- **Grafana #130121's fix:** push once on open, turn later pushes into replaces, and close on POP.

### 6.5 A pattern that avoids the pitfalls

This is the research agent's design, put together from the APIs above, not taken from one source.

- **Open:** from the opening click, `history.pushState({...history.state, overlay: token}, '')`, with no
  URL change.
- **Close by X, Escape or a close request:** if `history.state?.overlay === token`, call
  `history.back()`; otherwise just close.
- **On popstate:** if an overlay is open and the new state has no token, close it and return focus to the
  element that opened it.
- **On load, or Forward into a marked entry:** do not reopen. `replaceState` the marker away.

**What this means for the report:**
- **The image lightbox and the internal-flow popup** are large overlays. Give each one entry, and close it
  on Back (popstate), on the X and on Escape, through the pattern above.
  - At least on phone layouts this is needed, because iOS gives no other way for a swipe to close them.
  - Doing the same on desktop keeps behaviour uniform.
- **The context menu** gets no entry. Make it a popover, which light-dismisses and closes on Android's
  Back in Chrome.
- **Never push again when an overlay closes.**
- **Return focus to the element that opened the overlay.**

---

## 7. Precedents in test-report and dashboard tools

The code was read on each tool's main branch on 2026-10-19. The research agent's links are permalinks to
the commit read.

| Tool | State lives in | Open a test | Switch tab | Typing in search | Toggle a filter | Popup | Back inside the report |
|---|---|---|---|---|---|---|---|
| Playwright HTML (v1.64.1) | hash `#?q=…&testId=…` | push | status nav push; retry tabs not in the URL | not written; Enter pushes | push | none in the URL | yes |
| Allure 2 (2.46.1) | hash path | push (keyboard nav: replace) | push | not in the URL | localStorage | push on open, *push on close* | yes |
| Allure 3 (v3.21.0) | hash plus query | push | top level push; inner replace | replace, 150 ms debounce | replace | not in the URL (UNCONFIRMED) | yes, except filters |
| Cucumber html-formatter (v24.2.0) | query | plain `#id` anchors push | none | replace, 500 ms debounce | replace | none | anchors only |
| ReportPortal | path plus query | push | push | UNCONFIRMED | push (redux-first-router) | UNCONFIRMED | yes |
| Mochawesome, jest-html-reporters | none | none | none | none | none | none | no |
| Vitest UI | hash `#?file=…` | replace (VueUse default) | replace | n/a | n/a | n/a | no |
| Grafana Scenes / Explore | query | n/a | n/a | Explore: pushes on run, 200 ms debounce | user change push (opt in); ad hoc filters replace | panel view push; Workspace one entry | yes |
| GitHub PR / GitLab MR | path | n/a | push (GitLab replaced until 2020) | n/a | n/a | n/a | yes |

**Playwright** (https://github.com/microsoft/playwright/blob/58b93301d906b77a3854bc4d8ff4130f00a808ae/packages/html-reporter/src/links.tsx):
- This is the closest precedent: one file, often opened from file://, with state in the hash.
- Every navigation runs `window.history.pushState({}, '', href); const navEvent = new PopStateEvent('popstate');
  window.dispatchEvent(navEvent);` (lines 30-34). One `popstate` listener re-reads `location.hash` (lines
  163-173), so the report's own navigations and real Back share one code path.
- **Complaints:**
  - #18378 (2022-10-27): "I cannot go back".
  - #25015 and #33681: the search input and the URL drift apart.
  - PR #33689 (closed unmerged). Maintainer: "we don't want typing (and clearing) to affect the history",
    and "'Submit', 'Enter' or 'Click' should navigate".
  - PR #33754 (merged 2024-12-11) removed the clear button, because the team "couldn't come up with
    behaviour that works well with the browser history".
  - PR #38841 (2026-01-19, closed). Reviewer: "make sure that this doesn't spam the browser history".

**Allure 2** (allure-generator/src/main/javascript/core/routing/router.mts L122-133):
- `window.history[options.replace ? "replaceState" : "pushState"](...)`.
- #444 (2017-06-29): "I will need to press back button 100+ times", which led to `replace` for keyboard
  navigation (PR #482).

**Allure 3** (packages/web-commons/src/stores/url/helpers.ts):
- It pushes or replaces, then dispatches a custom "pushState"/"replaceState" event that subscribers hear,
  together with `popstate` and `hashchange`.
- #56 (2025-01-09): "History navigation doesn't work properly". The cause was a leading `/` that changed
  the path.
- #607 (2026-04-23): a UNC file path (section 2.1).

**Cucumber** (react-components src/components/app/UrlSearchProvider.tsx):
- `window.history.replaceState({}, '', url)`. It reads the URL once and has no popstate listener.

**Grafana and Scenes:**
- Scenes pushes only when the change opts in (`performBrowserHistoryAction`): "For state actions that
  should add browser history wrap them in this callback". Writing state on init replaces.
- #18053 (2019-07-10): a default value written into the URL on load "takes two clicks on the back button".
- Scenes PR #760's debate:
  - Torkel: GitHub's PR list pushes per filter.
  - dprokop: filters "should not create new history entries".
  - The outcome was opt-in per object (#878), with only user changes pushing (#882).

**GitLab merge-request tabs** used `replaceState` until gitlab#16019 ("Using the browser's previous an
next buttons then doesn't work", 2016) was closed on 2020-06-02 by switching to `pushState` plus a popstate
handler.

**What this means for the report:**
- Adopt Playwright's shape: one `navigate()` that pushes and then runs the same handler popstate runs.
- Add the rules the others learned:
  - typing never pushes;
  - state written at init replaces;
  - a popup owns one entry, and closing it never pushes;
  - pass relative URLs on file://.
- Users of every tool that replaced instead of pushing for tabs or views complained until it pushed: GitLab
  merge-request tabs, Allure 2 before #482, Playwright #18378.

---

## 8. Filters and search in the URL

- **React Router's tutorial** (8.5.0, https://reactrouter.com/tutorials/address-book): `submit(form,
  { replace: !isFirstSearch })`. The first keystroke of a search pushes and each later one replaces.
  "Instead of clicking back 7 times to remove the search, users only have to click back once".
- **nuqs 2.10.2** (https://nuqs.dev/docs/options):
  - Replace is the default. Push "only if the search params to update contribute to a navigation-like
    experience (eg: tabs, modals)".
  - "Updates to the URL are queued and throttled to a default of 50ms … Safari's rate limits are much
    stricter and use a default throttle of 120ms (320ms for older versions of Safari)". Inference: 320 ms
    fits WebKit's old window of 100 calls per 30 s, and 120 ms fits today's 100 per 10 s.
  - Debounce the search box (its example uses 500 ms), but flush at once on Enter or when the box is
    cleared.
- **Next.js Learn:** `replace()` with a 300 ms debounce.
- **Grafana:** Scenes replaces unless the change opts in. Explore debounces 200 ms "to avoid creating
  history entries when rapid changes are committed".
- **Allure 3** replaces with a 150 ms debounce; **Cucumber** replaces with 500 ms.
- **Playwright** never writes typing to the URL, and pushes on Enter.
- **Baymard:**
  - 2020-07-20: "users generally perceive each discrete iteration of their filtering as a new view", and
    "Tapping 'Back' after applying filters should begin removing the applied filters". 27% of sites fail
    this, and broken Back behaviour caused abandonment in testing.
  - 2026-04-17: after Back from a product page, "their filters should still be applied", so keep filter
    state in the URL (https://baymard.com/blog/ecommerce-filter-ui).
- **NN/g** has no article on Back and filters (UNCONFIRMED after a search). It warns that jumping to the
  top can make users "lose their place" (https://www.nngroup.com/articles/applying-filters/, 2016).
- **Budget arithmetic:**
  - Safari's 100 calls per 10 s allows one write per 100 ms on average.
  - A "select all" or "expand all" that writes once per item can exceed it in one gesture, and Safari
    throws.
  - Count writes per gesture, not per element.

**What this means for the report:**
- **Search box:** replace, debounced about 300 ms. Flush on Enter, blur and clear, and never push per
  keystroke.
- **Filter chips, the duration threshold and dependency or category modes:** coalesce.
  - The first change after a navigation entry pushes one "filtered" entry, so Back returns to the
    unfiltered view.
  - Later changes replace that entry until the next push-worthy navigation. This is React Router's search
    pattern applied to chips.
  - It meets Baymard's expectation that Back undoes filtering, without one entry per chip.
  - If the owner prefers the Allure 3, Cucumber and Scenes rule, replace always. That is the cheaper
    choice, but Back then never undoes a filter.
- **Keep the `#sid-`/`#scenario-` anchor at the front of the hash on every rewrite,** as
  `update_url_hash` already does.
- **Write the URL once per gesture,** and wrap the write in try/catch.

---

## Not confirmed (collected)

1. **Safari, file://, `canIntercept`.** That it is false for every navigation on file:// in Safari is
   inferred from WebKit source; no browser was run.
2. **Firefox's no-throw limit.** Whether bug 1922677 (non-throwing 200 per 10 s, target 157) stays in
   release. The bug was reopened on 2026-10-18.
3. **Safari's 100-per-10-s throw.** That it holds in shipped Safari 27.0 rests on main plus open bug
   317129. The first Safari releases with the 10 s window (2023) and the file: path rule (2022) are
   unknown.
4. **bfcache.** That Firefox and Safari put file:// pages in the bfcache. The code has no exclusion; not
   tested.
5. **Safari's scroll mode on fragment entries.** That entries made by a fragment navigation get "auto"
   rests on an unmerged WPT PR (#63184) and the code.
6. **Safari's skipping.** Which Safari release first skipped script-made entries on Back, and whether the
   toolbar button uses it.
7. **Azure DevOps.** The sandbox tokens on Services extension iframes, and whether the Code Coverage
   tab's HTML view still blocks scripts.
8. **GitHub artifacts.** Whether inline scripts run in non-zipped HTML artifacts.
9. **Firefox ESR 140.** Its end of life on 2026-09-29 comes only from third-party trackers.
10. **GitHub PR tabs.** That they push comes from markup and Turbo's docs; GitHub documents none of it.
11. **GOV.UK's swallowed Back.** The re-click bug in GOV.UK Frontend tabs is inferred from code.
12. **Allure 2's overlay.** That Back reopens a closed attachment overlay is read from code.
13. **Screen readers.** Whether any browser announces intercepted Navigation API navigations to assistive
    technology.
14. **Playwright's WebKit.** Whether it lifts WebKit's History limit as its Chromium lifts Chrome's.
